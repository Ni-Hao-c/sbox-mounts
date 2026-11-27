using Sandbox.Navigation;
using Titanfall2.Formats;

/// <summary>An authored mortar emplacement; the host reserves it for one living mortar Titan.</summary>
[Title( "Titanfall 2 Mortar Position" ), Group( "Titanfall 2" )]
public sealed class Titanfall2MortarPosition : Component
{
	[Property] public string Faction { get; set; } = "imc";
	internal Titanfall2Npc Occupant;
	internal bool Available => !Occupant.IsValid() || Occupant.Dead || Occupant.Role != Titanfall2Npc.FrontierRole.MortarTitan;
}

public enum Titanfall2MortarState { Idle, Moving, Waiting, Deploying, Firing, Recovering }

public record struct Titanfall2MortarSnapshot
{
	public Titanfall2MortarState State { get; set; }
	public int Serial { get; set; }
	public double StartedAt { get; set; }
	public float Duration { get; set; }
}

public sealed partial class Titanfall2Npc
{
	[Property] public Titanfall2ProjectileSettings MortarProjectile { get; set; }
	[Property] public float MortarDamage { get; set; } = 50;
	[Property] public float MortarTitanDamage { get; set; } = 350;
	[Sync( SyncFlags.FromHost )] public Titanfall2MortarSnapshot Mortar { get; private set; }
	[Sync( SyncFlags.FromHost )] public int MortarShotsFired { get; private set; }
	GameObject _mortarTarget;
	Titanfall2MortarPosition _mortarPosition;
	Vector3 _mortarOrigin, _mortarDestination;
	bool _mortarInitialized, _mortarBreakArmed;
	double _mortarSpawnTime, _mortarNextScan, _mortarNextHealthCheck, _mortarWaitUntil;
	float _mortarMoveHealth, _mortarWaitHealth, _mortarBreakHealth, _mortarQuietSeconds;
	int _mortarEventLoop, _mortarEventIndex;
	int _mortarVisualSerial = -1;

	bool TickMortar()
	{
		if ( Role != FrontierRole.MortarTitan ) return false;
		if ( !_mortarInitialized )
		{
			_mortarInitialized = true;
			_mortarOrigin = WorldPosition;
			_mortarSpawnTime = Time.NowDouble;
		}
		if ( !CanFight || !TitanCanAct ) { StopForAbility(); return true; }
		var now = Time.NowDouble;
		var harvester = FindHarvester();
		if ( harvester.IsValid() )
		{
			_mortarTarget = harvester;
		}
		else if ( !CanTarget( _mortarTarget ) )
		{
			// In Sandbox maps without a Harvester, fall back to bombarding long-range hostile targets.
			_mortarTarget = FindTarget();
			if ( !_mortarTarget.IsValid() )
			{
				StopForAbility();
				if ( Mortar.State is Titanfall2MortarState.Firing or Titanfall2MortarState.Deploying ) BeginMortarRecovery();
				else if ( Mortar.State == Titanfall2MortarState.Recovering && now - Mortar.StartedAt >= Mortar.Duration ) SetMortarState( Titanfall2MortarState.Idle );
				return true;
			}
		}
		var targetIsHarvester = _mortarTarget.GetComponent<Titanfall2Harvester>() is { Destroyed: false };
		if ( !targetIsHarvester && Mortar.State is Titanfall2MortarState.Idle or Titanfall2MortarState.Moving
			&& _mortarTarget.WorldPosition.Distance( WorldPosition ) < 520f )
		{
			var deltaClose = (_mortarTarget.WorldPosition - WorldPosition).WithZ( 0 );
			if ( deltaClose.LengthSquared > 1 ) WorldRotation = Rotation.Lerp( WorldRotation, Rotation.LookAt( deltaClose ), Math.Clamp( Time.Delta * 6f, 0, 1 ) );
			if ( HasLineOfSight( _mortarTarget ) && _nextShot <= 0 ) Fire( new Ray( Eye, (AimAt( _mortarTarget ) - Eye).Normal ), GameObject, false );
			return true;
		}
		if ( Mortar.State == Titanfall2MortarState.Idle )
		{
			if ( now < _mortarNextScan ) return true;
			_mortarNextScan = now + 5; // Retail waits for a free stationary position.
			if ( !ChooseMortarPosition() ) return true;
			_mortarMoveHealth = Health + Shield;
			SetMortarState( Titanfall2MortarState.Moving );
		}
		if ( Mortar.State == Titanfall2MortarState.Moving )
		{
			if ( WorldPosition.Distance( _mortarDestination ) > 64 )
			{
				if ( Agent.IsValid() && Scene.NavMesh.IsEnabled ) { Agent.MoveTo( _mortarDestination ); Moving = Agent.Velocity.LengthSquared > 25; }
				return true;
			}
			StopForAbility();
			if ( _mortarMoveHealth > Health + Shield + 75 ) BeginMortarWait( 3 );
			else BeginMortarDeployment();
		}
		if ( Mortar.State == Titanfall2MortarState.Waiting )
		{
			if ( now >= _mortarNextHealthCheck )
			{
				_mortarNextHealthCheck = now + 1;
				if ( _mortarWaitHealth > Health + Shield + 75 ) { _mortarWaitHealth = Health + Shield; _mortarWaitUntil = now + _mortarQuietSeconds; }
			}
			// The original falls back to ordinary combat while waiting to reengage.
			var threat = FindTarget();
			if ( threat.IsValid() )
			{
				var toThreat = (threat.WorldPosition - WorldPosition).WithZ( 0 );
				if ( toThreat.LengthSquared > 1 ) WorldRotation = Rotation.Lerp( WorldRotation, Rotation.LookAt( toThreat ), Math.Clamp( Time.Delta * 6f, 0, 1 ) );
				if ( HasLineOfSight( threat ) && _nextShot <= 0 ) Fire( new Ray( Eye, (AimAt( threat ) - Eye).Normal ), GameObject, false );
			}
			if ( now >= _mortarWaitUntil ) { _mortarMoveHealth = Health + Shield; SetMortarState( Titanfall2MortarState.Moving ); }
		}
		if ( Mortar.State == Titanfall2MortarState.Deploying && now - Mortar.StartedAt >= Mortar.Duration )
		{
			var attack = MortarClip( ModelLoader.MortarAttackRole );
			if ( attack is not { Duration: > 0, Sequence.Events.Length: > 0 } ) return true;
			SetMortarState( Titanfall2MortarState.Firing, attack.Duration );
			_mortarEventLoop = _mortarEventIndex = 0;
			_mortarBreakArmed = false;
		}
		if ( Mortar.State == Titanfall2MortarState.Firing )
		{
			DispatchMortarEvents();
			// Damage before the mandatory five-second opening is not part of the later
			// 90%-health break-off threshold (WaitForInteruption in the retail script).
			if ( now - Mortar.StartedAt >= 5 )
			{
				if ( !_mortarBreakArmed ) { _mortarBreakArmed = true; _mortarBreakHealth = (Health + Shield) * 0.9f; _mortarNextHealthCheck = now; }
				if ( now >= _mortarNextHealthCheck )
				{
					_mortarNextHealthCheck = now + 1;
					if ( Health + Shield < _mortarBreakHealth || EnemyPilotNearMortar() ) BeginMortarRecovery();
				}
			}
		}
		if ( Mortar.State == Titanfall2MortarState.Recovering && now - Mortar.StartedAt >= Mortar.Duration ) BeginMortarWait( 7 );
		return true;
	}

	bool ChooseMortarPosition()
	{
		var authored = Scene.GetAllComponents<Titanfall2MortarPosition>()
			.Where( p => p.Active && SameFaction( p.Faction ) && p.WorldPosition.Distance( _mortarOrigin ) <= 1024 ).ToArray();
		if ( authored.Length > 0 )
		{
			if ( _mortarPosition.IsValid() && _mortarPosition.Occupant == this ) { _mortarDestination = _mortarPosition.WorldPosition; return true; }
			var available = authored.Where( p => p.Available ).ToArray();
			if ( available.Length == 0 ) return false;
			_mortarPosition = available[Random.Shared.Next( available.Length )];
			_mortarPosition.Occupant = this;
			_mortarDestination = _mortarPosition.WorldPosition;
		}
		else
		{
			// Sandbox maps need not contain the retail director's stationary-position
			// database. A user-spawned mortar uses its own spawn as the emplacement.
			_mortarDestination = _mortarOrigin;
		}
		if ( WorldPosition.Distance( _mortarDestination ) <= 64 ) return true;
		if ( !Agent.IsValid() || !Scene.NavMesh.IsEnabled ) return false;
		return Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = WorldPosition, Target = _mortarDestination, Agent = Agent } ).Status == NavMeshPathStatus.Complete;
	}

	void BeginMortarDeployment()
	{
		var clip = MortarClip( ModelLoader.MortarDeployRole );
		if ( clip is not { Duration: > 0 } || !MortarProjectile.Enabled ) return;
		var facing = (_mortarTarget.WorldPosition - WorldPosition).WithZ( 0 );
		if ( facing.LengthSquared > 1 ) WorldRotation = Rotation.LookAt( facing );
		StopForAbility();
		SetMortarState( Titanfall2MortarState.Deploying, clip.Duration );
	}
	void BeginMortarRecovery() => SetMortarState( Titanfall2MortarState.Recovering, MortarClip( ModelLoader.MortarRecoverRole )?.Duration ?? 0 );
	void BeginMortarWait( float seconds )
	{
		_mortarQuietSeconds = seconds;
		_mortarWaitUntil = Time.NowDouble + seconds;
		_mortarWaitHealth = Health + Shield;
		_mortarNextHealthCheck = Time.NowDouble + 1;
		SetMortarState( Titanfall2MortarState.Waiting );
	}
	void SetMortarState( Titanfall2MortarState state, float duration = 0 ) => Mortar = new()
	{
		State = state, Duration = duration, StartedAt = Time.NowDouble, Serial = Mortar.Serial == int.MaxValue ? 1 : Mortar.Serial + 1
	};
	Titanfall2ActorAnimationData.Clip MortarClip( string role ) => _actorAnimationData?.Clips?.FirstOrDefault( c => c.Role == role );

	bool EnemyPilotNearMortar() => Scene.GetAllComponents<PlayerController>().Any( player => player.Active
		&& player.WorldPosition.Distance( WorldPosition ) < 256
		&& !SameFaction( player.GetComponentInParent<Titanfall2Npc>()?.Faction ?? Sandbox.Npcs.Factions.Player ) );

	void DispatchMortarEvents()
	{
		var events = MortarClip( ModelLoader.MortarAttackRole )?.Sequence?.Events;
		if ( events is null || events.Length == 0 || Mortar.Duration <= 0 ) return;
		var elapsed = Math.Max( 0, Time.NowDouble - Mortar.StartedAt );
		var loop = (int)(elapsed / Mortar.Duration);
		// Bound catch-up after a long pause; never manufacture minutes of delayed fire.
		if ( loop - _mortarEventLoop > 1 ) { _mortarEventLoop = loop; _mortarEventIndex = 0; }
		while ( _mortarEventLoop <= loop )
		{
			while ( _mortarEventIndex < events.Length )
			{
				var evt = events[_mortarEventIndex];
				if ( (_mortarEventLoop + evt.Cycle) * Mortar.Duration > elapsed ) return;
				_mortarEventIndex++;
				if ( evt.Name == "AE_NPC_WEAPON_RELOAD" ) { Rounds = MagazineSize; Reloading = false; }
				else if ( evt.Name == "AE_NPC_WEAPON_FIRE" ) LaunchMortarMissile( _mortarEventIndex - 1 );
			}
			_mortarEventLoop++; _mortarEventIndex = 0;
		}
	}

	void LaunchMortarMissile( int eventIndex )
	{
		if ( !_mortarTarget.IsValid() || !MortarProjectile.Enabled || Rounds <= 0 ) return;
		var eventPose = MortarClip( ModelLoader.MortarAttackRole )?.EventBonePoses?
			.FirstOrDefault( pose => pose.EventIndex == eventIndex && pose.Bone == _gunBone?.Name );
		var weaponModel = _gun.IsValid() ? _gun.GetComponent<BaseWeaponModel>()?.Renderer?.Model : null;
		var muzzleBind = weaponModel?.Attachments.GetTransform( string.IsNullOrEmpty( WeaponPresentation.MuzzleAttachment ) ? "muzzle_flash" : WeaponPresentation.MuzzleAttachment );
		if ( eventPose is null || !muzzleBind.HasValue ) return;
		// The event's gun bone is sampled from the original sequence while importing.
		// No previous rendered frame or guessed launch elevation enters host ballistics.
		var muzzle = WorldTransform.ToWorld( eventPose.Transform ).ToWorld( _gunBindInverse ).ToWorld( muzzleBind.Value );
		var origin = muzzle.Position;
		var direction = muzzle.Rotation.Forward;
		var radius = float.Lerp( 220, 100, Math.Clamp( (float)(Time.NowDouble - _mortarSpawnTime - 60) / 120, 0, 1 ) );
		var targetOffset = new Vector3( (Random.Shared.NextSingle() * 2 - 1) * radius, (Random.Shared.NextSingle() * 2 - 1) * radius, 0 );
		var missile = Titanfall2Projectile.Launch( Scene, new Ray( origin, direction ), MortarProjectile,
			MortarDamage, MortarTitanDamage, GameObject, GameObject, GameObject, npcFired: true );
		if ( !missile.IsValid() ) return;
		missile.GameObject.AddComponent<Titanfall2MortarMissile>().Initialize( missile, _mortarTarget, targetOffset );
		Rounds--; MortarShotsFired++;
		PlayShotSound( origin );
		PlayMortarIncoming( _mortarTarget.WorldPosition );
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMortarIncoming( Vector3 target ) => Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, "Weapon_FlightCore_Incoming_Projectile", target );

	bool TryPlayMortarAnimation()
	{
		if ( Role != FrontierRole.MortarTitan || Dead ) return false;
		var role = Mortar.State switch
		{
			Titanfall2MortarState.Deploying => ModelLoader.MortarDeployRole,
			Titanfall2MortarState.Firing => ModelLoader.MortarAttackRole,
			Titanfall2MortarState.Recovering => ModelLoader.MortarRecoverRole, _ => null
		};
		if ( role is null || Mortar.Duration <= 0 ) return false;
		var loop = Mortar.State == Titanfall2MortarState.Firing;
		var cycle = (float)Math.Max( 0, (Time.NowDouble - Mortar.StartedAt) / Mortar.Duration );
		var restart = _mortarVisualSerial != Mortar.Serial;
		_mortarVisualSerial = Mortar.Serial;
		if ( _layeredAnimation is not null && _layeredAnimation.Has( role ) )
		{
			_layeredAnimation.ClearOverlays();
			_layeredAnimation.Play( role, loop: loop, restart: restart );
			_layeredAnimation.SetCycle( loop ? cycle : MathF.Min( cycle, 1 ) );
			_layeredAnimation.Tick();
		}
		else if ( _actorClips?.Contains( role ) == true )
		{
			PlaySequence( role, looping: loop, restart: restart );
			Renderer.Sequence.PlaybackRate = 0;
			Renderer.Sequence.TimeNormalized = loop ? cycle % 1 : Math.Clamp( cycle, 0, 1 );
		}
		return true;
	}
}

/// <summary>Retail scripted homing-rate ramp; the shared projectile still owns collision and damage.</summary>
[Hide]
public sealed class Titanfall2MortarMissile : Component
{
	Titanfall2Projectile _projectile;
	GameObject _target;
	Vector3 _targetOffset;
	float _travelTime, _maximumTurn, _turnRate;
	double _startedAt, _nextParameterUpdate;
	internal void Initialize( Titanfall2Projectile projectile, GameObject target, Vector3 offset )
	{
		_projectile = projectile; _target = target; _targetOffset = offset; _startedAt = Time.NowDouble;
		// The retail callback derives the ramp from target origin before applying its
		// random missile target offset, and keeps the target entity moving underneath it.
		var distance = projectile.WorldPosition.Distance( target.WorldPosition );
		_travelTime = Math.Clamp( distance / 7000 * 5, 0.001f, 5 );
		_maximumTurn = MathF.Max( 1, 400 + (distance - 2500) / 4500 * -200 );
	}
	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy || !_projectile.IsValid() || !_target.IsValid() ) return;
		if ( Time.NowDouble >= _nextParameterUpdate )
		{
			var fraction = Math.Clamp( (float)(Time.NowDouble - _startedAt) / _travelTime, 0, 1 );
			_turnRate = float.Lerp( 10, _maximumTurn, fraction * fraction );
			_nextParameterUpdate = Time.NowDouble + 0.25;
		}
		var degrees = _turnRate * Time.Delta;
		var delta = _target.WorldPosition + _targetOffset - _projectile.WorldPosition;
		if ( delta.LengthSquared < 1 || _projectile.Velocity.LengthSquared < 1 ) return;
		var current = Rotation.LookAt( _projectile.Velocity );
		var desired = Rotation.LookAt( delta );
		var rotation = Rotation.Slerp( current, desired, Math.Clamp( degrees / MathF.Max( current.Distance( desired ), 0.001f ), 0, 1 ) );
		_projectile.SetHostVelocity( rotation.Forward * _projectile.Velocity.Length );
	}
}
