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

/// <summary>Composed Frontier Defense Mortar Titan emplacement, firing cycle, and animation controller.</summary>
internal sealed class Titanfall2NpcMortarController
{
	GameObject _target;
	Titanfall2MortarPosition _position;
	Vector3 _origin;
	Vector3 _destination;
	bool _initialized;
	bool _breakArmed;
	double _spawnTime;
	double _nextScan;
	double _nextHealthCheck;
	double _waitUntil;
	float _moveHealth;
	float _waitHealth;
	float _breakHealth;
	float _quietSeconds;
	int _eventLoop;
	int _eventIndex;
	int _visualSerial = -1;

	internal bool Tick( Titanfall2Npc npc )
	{
		if ( npc.Role != Titanfall2Npc.FrontierRole.MortarTitan ) return false;
		if ( !_initialized )
		{
			_initialized = true;
			_origin = npc.WorldPosition;
			_spawnTime = Time.NowDouble;
		}
		if ( !npc.CanFight || !npc.TitanCanAct ) { npc.StopForAbility(); return true; }
		var now = Time.NowDouble;
		var harvester = npc.FindHarvester();
		if ( harvester.IsValid() )
		{
			_target = harvester;
		}
		else if ( !npc.CanTarget( _target ) )
		{
			// In Sandbox maps without a Harvester, fall back to bombarding long-range hostile targets.
			_target = npc.FindTargetInternal();
			if ( !_target.IsValid() )
			{
				npc.StopForAbility();
				if ( npc.Mortar.State is Titanfall2MortarState.Firing or Titanfall2MortarState.Deploying ) BeginRecovery( npc );
				else if ( npc.Mortar.State == Titanfall2MortarState.Recovering && now - npc.Mortar.StartedAt >= npc.Mortar.Duration ) SetState( npc, Titanfall2MortarState.Idle );
				return true;
			}
		}
		var targetIsHarvester = _target.GetComponent<Titanfall2Harvester>() is { Destroyed: false };
		if ( !targetIsHarvester && npc.Mortar.State is Titanfall2MortarState.Idle or Titanfall2MortarState.Moving
			&& _target.WorldPosition.Distance( npc.WorldPosition ) < 520f )
		{
			var deltaClose = (_target.WorldPosition - npc.WorldPosition).WithZ( 0 );
			if ( deltaClose.LengthSquared > 1 ) npc.WorldRotation = Rotation.Lerp( npc.WorldRotation, Rotation.LookAt( deltaClose ), Math.Clamp( Time.Delta * 6f, 0, 1 ) );
			if ( npc.HasLineOfSight( _target ) && npc.NextShotReady )
				npc.Fire( new Ray( npc.EyePositionInternal, (Titanfall2Npc.AimAtInternal( _target ) - npc.EyePositionInternal).Normal ), npc.GameObject, false );
			return true;
		}
		if ( npc.Mortar.State == Titanfall2MortarState.Idle )
		{
			if ( now < _nextScan ) return true;
			_nextScan = now + 5; // Retail waits for a free stationary position.
			if ( !ChoosePosition( npc ) ) return true;
			_moveHealth = npc.Health + npc.Shield;
			SetState( npc, Titanfall2MortarState.Moving );
		}
		if ( npc.Mortar.State == Titanfall2MortarState.Moving )
		{
			if ( npc.WorldPosition.Distance( _destination ) > 64 )
			{
				if ( npc.Agent.IsValid() && npc.Scene.NavMesh.IsEnabled ) { npc.Agent.MoveTo( _destination ); npc.SetMovingHost( npc.Agent.Velocity.LengthSquared > 25 ); }
				return true;
			}
			npc.StopForAbility();
			if ( _moveHealth > npc.Health + npc.Shield + 75 ) BeginWait( npc, 3 );
			else BeginDeployment( npc );
		}
		if ( npc.Mortar.State == Titanfall2MortarState.Waiting )
		{
			if ( now >= _nextHealthCheck )
			{
				_nextHealthCheck = now + 1;
				if ( _waitHealth > npc.Health + npc.Shield + 75 ) { _waitHealth = npc.Health + npc.Shield; _waitUntil = now + _quietSeconds; }
			}
			// The original falls back to ordinary combat while waiting to reengage.
			var threat = npc.FindTargetInternal();
			if ( threat.IsValid() )
			{
				var toThreat = (threat.WorldPosition - npc.WorldPosition).WithZ( 0 );
				if ( toThreat.LengthSquared > 1 ) npc.WorldRotation = Rotation.Lerp( npc.WorldRotation, Rotation.LookAt( toThreat ), Math.Clamp( Time.Delta * 6f, 0, 1 ) );
				if ( npc.HasLineOfSight( threat ) && npc.NextShotReady )
					npc.Fire( new Ray( npc.EyePositionInternal, (Titanfall2Npc.AimAtInternal( threat ) - npc.EyePositionInternal).Normal ), npc.GameObject, false );
			}
			if ( now >= _waitUntil ) { _moveHealth = npc.Health + npc.Shield; SetState( npc, Titanfall2MortarState.Moving ); }
		}
		if ( npc.Mortar.State == Titanfall2MortarState.Deploying && now - npc.Mortar.StartedAt >= npc.Mortar.Duration )
		{
			var attack = Clip( npc, ModelLoader.MortarAttackRole );
			if ( attack is not { Duration: > 0, Sequence.Events.Length: > 0 } ) return true;
			SetState( npc, Titanfall2MortarState.Firing, attack.Duration );
			_eventLoop = _eventIndex = 0;
			_breakArmed = false;
		}
		if ( npc.Mortar.State == Titanfall2MortarState.Firing )
		{
			DispatchEvents( npc );
			// Damage before the mandatory five-second opening is not part of the later
			// 90%-health break-off threshold (WaitForInteruption in the retail script).
			if ( now - npc.Mortar.StartedAt >= 5 )
			{
				if ( !_breakArmed ) { _breakArmed = true; _breakHealth = (npc.Health + npc.Shield) * 0.9f; _nextHealthCheck = now; }
				if ( now >= _nextHealthCheck )
				{
					_nextHealthCheck = now + 1;
					if ( npc.Health + npc.Shield < _breakHealth || EnemyPilotNear( npc ) ) BeginRecovery( npc );
				}
			}
		}
		if ( npc.Mortar.State == Titanfall2MortarState.Recovering && now - npc.Mortar.StartedAt >= npc.Mortar.Duration ) BeginWait( npc, 7 );
		return true;
	}

	bool ChoosePosition( Titanfall2Npc npc )
	{
		var authored = npc.Scene.GetAllComponents<Titanfall2MortarPosition>()
			.Where( p => p.Active && npc.SameFaction( p.Faction ) && p.WorldPosition.Distance( _origin ) <= 1024 ).ToArray();
		if ( authored.Length > 0 )
		{
			if ( _position.IsValid() && _position.Occupant == npc ) { _destination = _position.WorldPosition; return true; }
			var available = authored.Where( p => p.Available ).ToArray();
			if ( available.Length == 0 ) return false;
			_position = available[Random.Shared.Next( available.Length )];
			_position.Occupant = npc;
			_destination = _position.WorldPosition;
		}
		else
		{
			// Sandbox maps need not contain the retail director's stationary-position
			// database. A user-spawned mortar uses its own spawn as the emplacement.
			_destination = _origin;
		}
		if ( npc.WorldPosition.Distance( _destination ) <= 64 ) return true;
		if ( !npc.Agent.IsValid() || !npc.Scene.NavMesh.IsEnabled ) return false;
		return npc.Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = npc.WorldPosition, Target = _destination, Agent = npc.Agent } ).Status == NavMeshPathStatus.Complete;
	}

	void BeginDeployment( Titanfall2Npc npc )
	{
		var clip = Clip( npc, ModelLoader.MortarDeployRole );
		if ( clip is not { Duration: > 0 } || !npc.MortarProjectile.Enabled ) return;
		var facing = (_target.WorldPosition - npc.WorldPosition).WithZ( 0 );
		if ( facing.LengthSquared > 1 ) npc.WorldRotation = Rotation.LookAt( facing );
		npc.StopForAbility();
		SetState( npc, Titanfall2MortarState.Deploying, clip.Duration );
	}

	void BeginRecovery( Titanfall2Npc npc ) => SetState( npc, Titanfall2MortarState.Recovering, Clip( npc, ModelLoader.MortarRecoverRole )?.Duration ?? 0 );

	void BeginWait( Titanfall2Npc npc, float seconds )
	{
		_quietSeconds = seconds;
		_waitUntil = Time.NowDouble + seconds;
		_waitHealth = npc.Health + npc.Shield;
		_nextHealthCheck = Time.NowDouble + 1;
		SetState( npc, Titanfall2MortarState.Waiting );
	}

	static void SetState( Titanfall2Npc npc, Titanfall2MortarState state, float duration = 0 ) => npc.SetMortarSnapshotHost( new()
	{
		State = state, Duration = duration, StartedAt = Time.NowDouble, Serial = npc.Mortar.Serial == int.MaxValue ? 1 : npc.Mortar.Serial + 1
	} );

	static Titanfall2ActorAnimationData.Clip Clip( Titanfall2Npc npc, string role ) => npc.AnimationData?.Clips?.FirstOrDefault( c => c.Role == role );

	static bool EnemyPilotNear( Titanfall2Npc npc ) => npc.Scene.GetAllComponents<PlayerController>().Any( player => player.Active
		&& player.WorldPosition.Distance( npc.WorldPosition ) < 256
		&& !npc.SameFaction( player.GetComponentInParent<Titanfall2Npc>()?.Faction ?? Sandbox.Npcs.Factions.Player ) );

	void DispatchEvents( Titanfall2Npc npc )
	{
		var events = Clip( npc, ModelLoader.MortarAttackRole )?.Sequence?.Events;
		if ( events is null || events.Length == 0 || npc.Mortar.Duration <= 0 ) return;
		var elapsed = Math.Max( 0, Time.NowDouble - npc.Mortar.StartedAt );
		var loop = (int)(elapsed / npc.Mortar.Duration);
		// Bound catch-up after a long pause; never manufacture minutes of delayed fire.
		if ( loop - _eventLoop > 1 ) { _eventLoop = loop; _eventIndex = 0; }
		while ( _eventLoop <= loop )
		{
			while ( _eventIndex < events.Length )
			{
				var evt = events[_eventIndex];
				if ( (_eventLoop + evt.Cycle) * npc.Mortar.Duration > elapsed ) return;
				_eventIndex++;
				if ( evt.Name == "AE_NPC_WEAPON_RELOAD" ) { npc.SetRoundsHost( npc.MagazineSize ); npc.SetReloadingHost( false ); }
				else if ( evt.Name == "AE_NPC_WEAPON_FIRE" ) LaunchMissile( npc, _eventIndex - 1 );
			}
			_eventLoop++; _eventIndex = 0;
		}
	}

	void LaunchMissile( Titanfall2Npc npc, int eventIndex )
	{
		if ( !_target.IsValid() || !npc.MortarProjectile.Enabled || npc.Rounds <= 0 ) return;
		var eventPose = Clip( npc, ModelLoader.MortarAttackRole )?.EventBonePoses?
			.FirstOrDefault( pose => pose.EventIndex == eventIndex && pose.Bone == npc.GunBone?.Name );
		var weaponModel = npc.GunObject.IsValid() ? npc.GunObject.GetComponent<BaseWeaponModel>()?.Renderer?.Model : null;
		var muzzleBind = weaponModel?.Attachments.GetTransform( string.IsNullOrEmpty( npc.WeaponPresentation.MuzzleAttachment ) ? "muzzle_flash" : npc.WeaponPresentation.MuzzleAttachment );
		if ( eventPose is null || !muzzleBind.HasValue ) return;
		// The event's gun bone is sampled from the original sequence while importing.
		// No previous rendered frame or guessed launch elevation enters host ballistics.
		var muzzle = npc.WorldTransform.ToWorld( eventPose.Transform ).ToWorld( npc.GunBindInverse ).ToWorld( muzzleBind.Value );
		var origin = muzzle.Position;
		var direction = muzzle.Rotation.Forward;
		var radius = float.Lerp( 220, 100, Math.Clamp( (float)(Time.NowDouble - _spawnTime - 60) / 120, 0, 1 ) );
		var targetOffset = new Vector3( (Random.Shared.NextSingle() * 2 - 1) * radius, (Random.Shared.NextSingle() * 2 - 1) * radius, 0 );
		var missile = Titanfall2Projectile.Launch( npc.Scene, new Ray( origin, direction ), npc.MortarProjectile,
			npc.MortarDamage, npc.MortarTitanDamage, npc.GameObject, npc.GameObject, npc.GameObject, npcFired: true );
		if ( !missile.IsValid() ) return;
		missile.GameObject.AddComponent<Titanfall2MortarMissile>().Initialize( missile, _target, targetOffset );
		npc.SetRoundsHost( npc.Rounds - 1 );
		npc.IncrementMortarShotsFiredHost();
		npc.PlayShotSoundInternal( origin );
		npc.BroadcastMortarIncoming( _target.WorldPosition );
	}

	internal bool TryPlayAnimation( Titanfall2Npc npc )
	{
		if ( npc.Role != Titanfall2Npc.FrontierRole.MortarTitan || npc.Dead ) return false;
		var role = npc.Mortar.State switch
		{
			Titanfall2MortarState.Deploying => ModelLoader.MortarDeployRole,
			Titanfall2MortarState.Firing => ModelLoader.MortarAttackRole,
			Titanfall2MortarState.Recovering => ModelLoader.MortarRecoverRole, _ => null
		};
		if ( role is null || npc.Mortar.Duration <= 0 ) return false;
		var loop = npc.Mortar.State == Titanfall2MortarState.Firing;
		var cycle = (float)Math.Max( 0, (Time.NowDouble - npc.Mortar.StartedAt) / npc.Mortar.Duration );
		var restart = _visualSerial != npc.Mortar.Serial;
		_visualSerial = npc.Mortar.Serial;
		var layered = npc.LayeredAnimation;
		if ( layered is not null && layered.Has( role ) )
		{
			layered.ClearOverlays();
			layered.Play( role, loop: loop, restart: restart );
			layered.SetCycle( loop ? cycle : MathF.Min( cycle, 1 ) );
			layered.Tick();
		}
		else if ( npc.HasActorClip( role ) )
		{
			npc.PlaySequence( role, looping: loop, restart: restart );
			npc.Renderer.Sequence.PlaybackRate = 0;
			npc.Renderer.Sequence.TimeNormalized = loop ? cycle % 1 : Math.Clamp( cycle, 0, 1 );
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
