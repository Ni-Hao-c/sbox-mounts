using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;

/// <summary>Host-driven TF2 actor inheriting BaseCombatNpc, supporting ground, stationary, hover, Frontier Defense, and drivable Titans.</summary>
[Title( "Titanfall 2 Infantry" ), Group( "Titanfall 2" )]
public sealed partial class Titanfall2Npc : BaseCombatNpc, Component.IPressable
{
	public enum Navigation { Ground, Stationary, Hover }

	[Property] public Navigation NavigationMode { get; set; }
	[Property] public float MoveSpeed { get; set; } = 160;
	[Property] public float MeleeDamage { get; set; }
	[Property] public float MeleeRange { get; set; } = 80;
	[Property] public float MeleeWindup { get; set; } = 0.28f;
	[Property] public float MeleeInterval { get; set; } = 1.2f;
	[Property] public bool MeleeOnly { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2ProjectileSettings Projectile { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2WeaponFiring Firing { get; set; }
	float _hoverHeight;
	[Property] public string ModelPath { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public string GunModelPath { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public string ViewModelPath { get; set; }
	[Property] public string MountIdent { get; set; }
	[Sync( SyncFlags.FromHost )] public int ShotSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public int HitMarkerSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool HitMarkerKill { get; private set; }
	[Property, Sync( SyncFlags.FromHost )] public string FireSound { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2WeaponPresentation WeaponPresentation { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2WeaponAudio WeaponAudio { get; set; }
	[Property] public NavMeshAgent Agent { get; set; }
	[Property] public bool AttackPlayers { get; set; } = true;
	[Property] public float MaxShield { get; set; }
	[Property] public bool IsTitan { get; set; }
	[Property] public bool CanFight { get; set; } = true;
	[Property] public float BodyHeight { get; set; } = 72;
	[Property] public float BodyRadius { get; set; } = 14;
	[Property] public Titanfall2TitanSeat Seat { get; set; }
	[Property] public float TitanShotDamage { get; set; } = 10;
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2DamageProfile DamageProfile { get; set; }
	[Property] public int ShotPellets { get; set; } = 1;
	[Property] public float ShotSpread { get; set; } = 4;
	[Sync( SyncFlags.FromHost )] public float Shield { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Rounds { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Reloading { get; private set; }
	TimeUntil _shieldDelay;
	[Property] public float DetectionRange { get; set; } = 2000;
	[Property] public float AttackRange { get; set; } = 1100;
	[Property] public float ShotInterval { get; set; } = 0.12f;
	[Property] public float ShotDamage { get; set; } = 10;
	[Property] public int MagazineSize { get; set; } = 24;
	[Property] public float ReloadSeconds { get; set; } = 2.2f;
	[Sync( SyncFlags.FromHost )] public bool Dead { get; private set; }

	/// <summary>When true, this actor aligns with players/Militia and can be commanded to follow with E.</summary>
	[Property, Title( "Friendly" ), ClientEditable]
	public bool Friendly
	{
		get => IsAllyFaction( Faction );
		set
		{
			// Like the official CombatNpc: a serialized false may mean a custom
			// wildlife/neutral faction, rather than a request to change it to IMC.
			if ( value == Friendly ) return;
			Faction = value ? Factions.Ally : "imc";
			AttackPlayers = CanFight && !value && !IsNeutralFaction( Faction );
			if ( !value ) Leader = null;
			SetupRelationships();
		}
	}

	GameObject _target;
	GameObject _gun;
	bool _triedGun;
	TimeUntil _scan;
	TimeUntil _nextShot;
	TimeUntil _remove;
	int _burst;
	bool _chasing;
	bool _followingLeader;
	[Sync( SyncFlags.FromHost )] bool Moving { get; set; }

	internal static bool IsAllyFaction( string faction ) =>
		string.Equals( faction, Factions.Ally, StringComparison.OrdinalIgnoreCase )
		|| string.Equals( faction, "militia", StringComparison.OrdinalIgnoreCase )
		|| string.Equals( faction, Factions.Player, StringComparison.OrdinalIgnoreCase )
		|| string.Equals( faction, Factions.Citizen, StringComparison.OrdinalIgnoreCase );

	internal static bool IsNeutralFaction( string faction ) =>
		string.Equals( faction, "neutral", StringComparison.OrdinalIgnoreCase );

	internal bool IsHostileToFaction( string otherFaction )
	{
		if ( IsNeutralFaction( Faction ) || IsNeutralFaction( otherFaction ) ) return false;
		if ( IsAllyFaction( Faction ) && IsAllyFaction( otherFaction ) ) return false;
		if ( string.Equals( Faction, otherFaction, StringComparison.OrdinalIgnoreCase ) ) return false;
		return true;
	}

	protected override void SetupRelationships()
	{
		base.SetupRelationships();
		if ( IsNeutralFaction( Faction ) )
		{
			SetRelationship( Factions.Player, Disposition.Neutral, 10 );
			SetRelationship( Factions.Ally, Disposition.Neutral, 10 );
			SetRelationship( Factions.Enemy, Disposition.Neutral, 10 );
			SetRelationship( "imc", Disposition.Neutral, 10 );
			SetRelationship( "militia", Disposition.Neutral, 10 );
			return;
		}
		if ( IsAllyFaction( Faction ) )
		{
			SetRelationship( Factions.Player, Disposition.Friendly, 10 );
			SetRelationship( Factions.Ally, Disposition.Friendly, 10 );
			SetRelationship( Factions.Citizen, Disposition.Friendly, 10 );
			SetRelationship( "militia", Disposition.Friendly, 10 );
			SetRelationship( Factions.Enemy, Disposition.Hostile, 10 );
			SetRelationship( "imc", Disposition.Hostile, 10 );
			SetRelationship( "wildlife", Disposition.Hostile, 10 );
			SetRelationship( Factions.Monster, Disposition.Hostile, 10 );
		}
		else if ( string.Equals( Faction, "wildlife", StringComparison.OrdinalIgnoreCase ) )
		{
			SetRelationship( "wildlife", Disposition.Friendly, 10 );
			SetRelationship( Factions.Player, Disposition.Hostile, 10 );
			SetRelationship( Factions.Ally, Disposition.Hostile, 10 );
			SetRelationship( Factions.Enemy, Disposition.Hostile, 10 );
			SetRelationship( "imc", Disposition.Hostile, 10 );
			SetRelationship( "militia", Disposition.Hostile, 10 );
		}
		else
		{
			SetRelationship( "imc", Disposition.Friendly, 10 );
			SetRelationship( Factions.Enemy, Disposition.Friendly, 10 );
			SetRelationship( Factions.Player, Disposition.Hostile, 10 );
			SetRelationship( Factions.Ally, Disposition.Hostile, 10 );
			SetRelationship( Factions.Citizen, Disposition.Hostile, 10 );
			SetRelationship( "militia", Disposition.Hostile, 10 );
			SetRelationship( "wildlife", Disposition.Hostile, 10 );
		}
	}

	internal bool UsesEngineSchedules => NavigationMode == Navigation.Ground && Role == FrontierRole.None;

	Component.IPressable.Tooltip? Component.IPressable.GetTooltip( Component.IPressable.Event e )
	{
		if ( IsTitan ) return Seat.IsValid() ? ((Component.IPressable)Seat).GetTooltip( e ) : null;
		if ( Dead || !Friendly ) return null;
		return Leader.IsValid()
			? new Component.IPressable.Tooltip( "Stop Following", "person_off", null )
			: new Component.IPressable.Tooltip( "Follow", "follow_the_signs", null );
	}

	// PlayerController may resolve a different IPressable on the same root between
	// hover and press. Both Titan components must dispatch to the same cockpit.
	bool Component.IPressable.CanPress( Component.IPressable.Event e ) => IsTitan
		? Seat.IsValid() && ((Component.IPressable)Seat).CanPress( e ) : !Dead && Friendly;

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		if ( IsTitan ) return Seat.IsValid() && ((Component.IPressable)Seat).Press( e );
		if ( Dead || !Friendly || !e.Source.IsValid() ) return false;
		if ( Networking.IsHost ) ApplyToggleFollow( e.Source.GameObject, caller: null );
		else RequestToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	void RequestToggleFollow( GameObject presserObject ) => ApplyToggleFollow( presserObject, Rpc.Caller );

	void ApplyToggleFollow( GameObject presserObject, Connection caller )
	{
		if ( Dead || IsTitan || !Friendly || !presserObject.IsValid() ) return;
		var player = presserObject.GetComponentInParent<PlayerController>() ?? presserObject.GetComponent<PlayerController>();
		if ( !player.IsValid() ) return;
		if ( caller is not null && player.Network.Owner != caller ) return;
		if ( player.WorldPosition.Distance( WorldPosition ) > 180f ) return;

		Leader = Leader == player.GameObject ? null : player.GameObject;
		SayVoice( Leader.IsValid() ? VoiceCue.Follow : VoiceCue.Unfollow, bypassCooldown: true );
	}

	protected override void OnStart()
	{
		EyeHeight = BodyHeight * 0.79f;
		FollowDistance = MathF.Max( 120f, BodyRadius * 8f );
		if ( string.IsNullOrWhiteSpace( Faction ) ) Faction = "imc";
		base.OnStart();

		if ( Networking.IsHost && !IsProxy )
		{
			Health = MathF.Max( 1, MaxHealth );
			Shield = MathF.Max( 0, MaxShield );
			InitializeTitanLife();
			Rounds = GetComponent<Titanfall2TitanCore>()?.MagazineCapacity ?? Math.Max( 1, MagazineSize );
			StartFrontier();
			_hoverHeight = WorldPosition.z + 85;
		}
		if ( !Renderer.IsValid() ) return;
		Renderer.CreateAttachments = true;
		Renderer.Model = Titanfall2GameplayVisuals.GetModel( ModelPath );
		if ( Renderer.Model is not { IsError: false } model ) return;
		InitializeAnimation( model );
		AttachGun();
	}

	protected override void OnUpdate()
	{
		if ( Senses.IsValid() ) Senses.Targets = Sandbox.Npcs.Layers.SensesLayer.SenseTargets.Npcs
			| (AttackPlayers && !Friendly ? Sandbox.Npcs.Layers.SensesLayer.SenseTargets.Players : 0);
		TickNpcWeaponPrewarm();
		UpdateAbilityLight();
		if ( Dead )
		{
			GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
			if ( _gun.IsValid() ) _gun.Enabled = false;
			PlaySequence( ModelLoader.DeathRole, looping: false );
			if ( Networking.IsHost && !IsProxy && _remove <= 0 ) GameObject.Destroy();
			return;
		}
		AttachGun();
		if ( Reloading ) GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		if ( !Networking.IsHost || IsProxy )
		{
			PlayLocomotion();
			return;
		}
		if ( TickTitanLife() ) { if ( !Dead ) PlayLocomotion(); return; }
		if ( IsInTermination ) { PlayLocomotion(); return; }
		TickMelee();
		if ( !IsDoomed && Shield < MaxShield && _shieldDelay <= 0 ) Shield = MathF.Min( MaxShield, Shield + MaxShield * 0.2f * Time.Delta );
		if ( Reloading && _nextShot <= 0 )
		{
			Rounds = GetComponent<Titanfall2TitanCore>()?.MagazineCapacity ?? Math.Max( 1, MagazineSize );
			Reloading = false;
		}
		if ( Seat.IsValid() && (Seat.IsOccupied || Seat.IsEmbarking) )
		{
			if ( ActiveSchedule is not null ) EndCurrentSchedule();
			if ( Movement.IsValid() ) { Movement.Stop(); if ( Movement.Enabled ) Movement.Enabled = false; }
			if ( Agent.IsValid() ) { Agent.Stop(); if ( Agent.Enabled ) Agent.Enabled = false; }
			_chasing = false;
			_target = null;
			Moving = Seat.IsDriving;
			PlayLocomotion();
			return;
		}
		if ( NavigationMode == Navigation.Ground && MoveSpeed > 0 )
		{
			if ( Agent.IsValid() && !Agent.Enabled ) Agent.Enabled = true;
			if ( Movement.IsValid() && !Movement.Enabled ) Movement.Enabled = true;
		}
		if ( TickMortar() ) { PlayLocomotion(); return; }
		if ( UsesEngineSchedules )
		{
			base.OnUpdate();
			Moving = Movement?.IsMoving == true;
			PlayLocomotion();
			return;
		}
		if ( TickNuke() ) { PlayLocomotion(); return; }
		if ( !CanFight )
		{
			if ( _scan <= 0 ) { _scan = 0.3f; UpdateFollowLeader(); }
			Moving = Agent.IsValid() && Agent.Velocity.LengthSquared > 25;
			PlayLocomotion();
			return;
		}
		if ( TickEliteDefense() ) { PlayLocomotion(); return; }
		TickArcAura();
		if ( _scan <= 0 )
		{
			_scan = 0.3f;
			var previous = _target;
			_target = Role == FrontierRole.NukeTitan ? FindHarvester() ?? FindTarget()
				: FindTarget() ?? (Role == FrontierRole.None ? null : FindHarvester());
			if ( _target.IsValid() && _target != previous ) SayVoice( VoiceCue.Spot );
			UpdateMovement();
		}
		Moving = NavigationMode == Navigation.Hover ? UpdateHover() : Agent.IsValid() && Agent.Velocity.LengthSquared > 25;
		PlayLocomotion();
		if ( !CanTarget( _target ) ) { TickArcCannon(); return; }
		var delta = _target.WorldPosition - WorldPosition;
		if ( delta.WithZ( 0 ).LengthSquared > 1 ) WorldRotation = Rotation.Lerp( WorldRotation, Rotation.LookAt( delta.WithZ( 0 ), Vector3.Up ), Math.Clamp( Time.Delta * (IsTitan ? 5.5f : 8f), 0, 1 ) );
		if ( TickArcCannon() ) return;
		if ( IsRushingHarvester )
		{
			// Nuke Titans march relentlessly toward the Harvester while laying down suppressive fire ahead.
			var forwardThreat = FindTarget();
			if ( forwardThreat.IsValid() && _nextShot <= 0 && HasLineOfSight( forwardThreat ) )
			{
				var toThreat = AimAt( forwardThreat ) - Eye;
				if ( toThreat.LengthSquared <= AttackRange * AttackRange && Vector3.Dot( WorldRotation.Forward, toThreat.WithZ( 0 ).Normal ) > 0.45f )
					Fire( new Ray( Eye, toThreat.Normal ), GameObject, piloted: false );
			}
			return;
		}
		if ( MeleeDamage > 0 && delta.LengthSquared <= MeleeRange * MeleeRange && HasLineOfSight( _target ) )
		{
			TryStartMelee( new Ray( Eye, (AimAt( _target ) - Eye).Normal ), GameObject );
			return;
		}
		if ( _nextShot <= 0 && delta.LengthSquared <= AttackRange * AttackRange && HasLineOfSight( _target ) ) Fire( new Ray( Eye, (AimAt( _target ) - Eye).Normal ), GameObject, piloted: false );
	}

	GameObject FindTarget()
	{
		GameObject bestTarget = null;
		var bestScore = float.MaxValue;
		var maxDistSq = DetectionRange * DetectionRange;
		if ( AttackPlayers && !Friendly )
			foreach ( var player in Scene.GetAllComponents<PlayerController>() ) Consider( player.GameObject );
		foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
		{
			if ( npc == this || !npc.IsValid() || npc.Health <= 0 ) continue;
			if ( npc is Titanfall2Npc tf2 && tf2.Dead ) continue;
			if ( !IsHostileToFaction( npc.Faction ) ) continue;
			Consider( npc.GameObject );
		}
		return bestTarget;

		void Consider( GameObject candidate )
		{
			if ( !CanTarget( candidate ) ) return;
			if ( (candidate.WorldPosition - WorldPosition).LengthSquared >= maxDistSq ) return;
			var score = EvaluateTargetScore( candidate );
			if ( score >= bestScore ) return;
			bestScore = score;
			bestTarget = candidate;
		}
	}

	internal bool CanTarget( GameObject target ) => target.IsValid() && target.Active && target != GameObject
		&& (target.GetComponent<PlayerController>() is null
			|| (target.GetComponentInParent<Titanfall2TitanSeat>()?.IsOccupied != true
				&& target.GetComponent<Titanfall2PilotTactical>() is not { IsPhaseActive: true }))
		&& target.GetComponent<Titanfall2Npc>() is not { Dead: true }
		&& target.GetComponent<Titanfall2TitanKit>() is not { IsPhasing: true }
		&& target.GetComponent<BaseNpc>() is not { Health: <= 0 }
		&& target.GetComponent<Titanfall2Harvester>() is not { Destroyed: true };

	Vector3 Eye => WorldPosition + Vector3.Up * (BodyHeight * 0.79f);
	static Vector3 AimAt( GameObject target ) => target.WorldPosition + Vector3.Up * ((target.GetComponent<Titanfall2Npc>()?.BodyHeight ?? 72) * 0.67f);

	internal bool HasLineOfSight( GameObject target )
	{
		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, AimAt( target ) ).UseHitboxes()
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		if ( trace.StartedSolid ) return false;
		if ( !trace.Hit ) return true;
		for ( var hit = trace.GameObject; hit.IsValid(); hit = hit.Parent )
			if ( hit == target ) return true;
		return false;
	}

	void UpdateMovement()
	{
		if ( NavigationMode != Navigation.Ground || !Agent.IsValid() || !Scene.NavMesh.IsEnabled ) return;
		if ( CanTarget( _target ) )
		{
			var distSq = (_target.WorldPosition - WorldPosition).LengthSquared;
			var arcNeedsClose = Role == FrontierRole.ArcTitan && distSq > EmpOuterRadius * EmpOuterRadius * 0.72f;
			var eliteStrafe = Role == FrontierRole.EliteMonarch && distSq < 650f * 650f && HasLineOfSight( _target );
			if ( IsRushingHarvester || arcNeedsClose || distSq > AttackRange * AttackRange || !HasLineOfSight( _target ) )
			{
				var dest = _target.WorldPosition;
				if ( Role == FrontierRole.ArcTitan && !IsRushingHarvester && distSq > 260f * 260f )
				{
					var toTarget = (dest - WorldPosition).WithZ( 0 ).Normal;
					var lateral = Vector3.Cross( toTarget, Vector3.Up ) * (MathF.Sin( (float)Time.NowDouble * 1.15f + GameObject.Id.GetHashCode() ) * 160f);
					dest = Scene.NavMesh.GetClosestPoint( dest + lateral ) ?? dest;
				}
				Agent.MoveTo( dest );
				_chasing = true;
				_followingLeader = false;
				return;
			}
			if ( eliteStrafe )
			{
				var toTarget = (_target.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
				var strafeDir = Vector3.Cross( toTarget, Vector3.Up ) * (MathF.Sin( (float)Time.NowDouble * 0.9f + GameObject.Id.GetHashCode() ) >= 0 ? 1f : -1f);
				var candidate = Scene.NavMesh.GetClosestPoint( WorldPosition + strafeDir * 220f );
				if ( candidate.HasValue )
				{
					Agent.MoveTo( candidate.Value );
					_chasing = true;
					_followingLeader = false;
					return;
				}
			}
		}
		if ( !CanTarget( _target ) && UpdateFollowLeader() )
		{
			_chasing = false;
		}
		else if ( _chasing || _followingLeader )
		{
			Agent.Stop();
			_chasing = false;
			_followingLeader = false;
		}
	}

	bool UpdateFollowLeader()
	{
		if ( !Friendly || !Leader.IsValid() || NavigationMode != Navigation.Ground || !Agent.IsValid() || !Scene.NavMesh.IsEnabled )
			return false;
		var followers = Scene.GetAllComponents<Titanfall2Npc>()
			.Where( ally => ally.Active && !ally.Dead && ally.Friendly && ally.Leader == Leader )
			.OrderBy( ally => ally.IsTitan ? 0 : 1 )
			.ThenBy( ally => ally.GameObject.Id )
			.ToList();
		var slot = Math.Max( 0, followers.IndexOf( this ) );
		var spacing = MathF.Max( FollowDistance * 0.65f, BodyRadius * 3.5f );
		var offset = Titanfall2TacticsRules.FormationOffset( slot, spacing, Leader.WorldRotation.Forward, Leader.WorldRotation.Right );
		var desired = Scene.NavMesh.GetClosestPoint( Leader.WorldPosition + offset ) ?? Leader.WorldPosition;
		var dist = desired.Distance( WorldPosition );
		if ( dist > MathF.Max( 48f, BodyRadius * 1.6f ) )
		{
			Agent.MoveTo( desired );
			_followingLeader = true;
			return true;
		}
		if ( _followingLeader )
		{
			Agent.Stop();
			_followingLeader = false;
		}
		return false;
	}

	bool UpdateHover()
	{
		var position = WorldPosition;
		var goal = position.WithZ( _hoverHeight );
		if ( CanTarget( _target ) )
		{
			var target = AimAt( _target );
			var offset = (target - position).WithZ( 0 );
			var distance = offset.Length;
			if ( distance > AttackRange * 0.8f || !HasLineOfSight( _target ) )
				goal += offset.Normal * MathF.Min( 320, distance );
		}
		var movement = goal - position;
		if ( movement.LengthSquared < 1 ) return false;
		var step = movement.Normal * MathF.Min( movement.Length, MoveSpeed * Time.Delta );
		var hull = new BBox( new Vector3( -BodyRadius + 2, -BodyRadius + 2, 2 ), new Vector3( BodyRadius - 2, BodyRadius - 2, BodyHeight - 1 ) );
		var trace = Scene.Trace.Box( hull, position, position + step ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( trace.StartedSolid ) return false;
		WorldPosition = trace.EndPosition;
		return (WorldPosition - position).LengthSquared > 0.05f;
	}

	internal bool Fire( Ray aim, GameObject attacker, bool piloted )
	{
		if ( !Networking.IsHost || IsProxy || !TitanCanAct || !CanFight ) return false;
		var core = GetComponent<Titanfall2TitanCore>();
		if ( core is { BlocksPrimary: true } ) return core.TryPrimaryAttack( aim, attacker );
		var kit = GetComponent<Titanfall2TitanKit>();
		if ( kit is { BlocksTitanPrimary: true } ) return false;
		if ( Reloading || _meleePending || _nextShot > 0 || Rounds <= 0 && core is not { UnlimitedAmmo: true } ) return false;
		if ( !MeleeOnly && Rounds < Firing.Minimum && core is not { UnlimitedAmmo: true } ) { StartReload(); return false; }
		var damageMultiplier = core?.PrimaryDamageMultiplier ?? 1;
		aim = core?.AssistAim( aim ) ?? aim;
		if ( MeleeOnly ) return TryStartMelee( aim, attacker );
		var ionShot = default( Titanfall2IonPrimaryShot );
		if ( kit.IsValid() && !kit.TryPrepareIonPrimary( piloted, out ionShot ) ) return false;
		if ( ionShot.Enabled ) damageMultiplier *= ionShot.DamageScale;
		var origin = _gun.IsValid() ? _gun.GetComponent<BaseWeaponModel>()?.GetMuzzleTransform().Position ?? Eye : Eye;
		// Aim at the point visible from the cockpit, but start the shot at the muzzle.
		// This prevents a close wall in front of the gun being bypassed by the camera.
		if ( piloted )
		{
			var target = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim, AttackRange )
				.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot ).EndPosition;
			if ( (target - origin).LengthSquared > 1f ) aim = new Ray( origin, (target - origin).Normal );
		}
		var spread = FiringSpread( piloted );
		var effectiveRange = AttackRange;
		var ammoCost = Firing.Cost;
		if ( kit is { Kit: Titanfall2TitanKit.Chassis.Legion } )
		{
			if ( kit.LegionLongRangeMode )
			{
				spread *= 0.28f;
				effectiveRange = MathF.Max( AttackRange * 1.45f, 3200f );
				ammoCost = Math.Max( 2, Firing.Cost * 2 );
			}
		}
		var patternDirection = Firing.HasPattern || ionShot.Enabled ? aim.Forward.WithAimCone( spread, spread ) : aim.Forward;
		// Retail NPC callbacks use a fixed zoom fraction of 0.5.
		var zoom = 0.5f;
		for ( var i = 0; i < (ionShot.Enabled ? ionShot.Count : Firing.Count( ShotPellets )); i++ )
		{
			var direction = ionShot.Enabled ? ionShot.Bearing( patternDirection, i )
				: Firing.HasPattern ? Firing.Bearing( patternDirection, i, zoom ) : aim.Forward.WithAimCone( spread, spread );
			var ray = new Ray( aim.Position, direction );
			if ( Projectile.Enabled )
			{
				var flight = ionShot.Enabled ? ionShot.Flight( Projectile ) : Firing.Flight( Projectile, zoom, i );
				Titanfall2Projectile.Launch( Scene, ray, flight, ShotDamage * damageMultiplier, TitanShotDamage * damageMultiplier, attacker, GameObject, GameObject, DamageProfile, !piloted );
				continue;
			}
			var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( ray, effectiveRange ).IgnoreGameObjectHierarchy( GameObject )
				.UseHitboxes().UseHitPosition().WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
			var intercepted = Titanfall2TitanDefense.TryInterceptHitscan( Scene, ref hit, attacker, GameObject,
				new Titanfall2DefenseShot( ShotDamage * damageMultiplier, TitanShotDamage * damageMultiplier, 0, DamageProfile, !piloted, effectiveRange ) );
			if ( !intercepted )
			{
				var hitNpc = hit.GameObject?.GetComponentInParent<Titanfall2Npc>();
				var heavyTarget = hitNpc is { IsTitan: true };
				var damage = DamageProfile.Evaluate( ray.Position.Distance( hit.HitPosition ), heavyTarget, !piloted, ShotDamage, TitanShotDamage )
					* (!DamageProfile.Enabled && !piloted && !heavyTarget ? 0.45f : 1) * damageMultiplier;
				var tags = new TagSet();
				tags.Add( "bullet" );
				var wasAlive = hitNpc is { Dead: false };
				Titanfall2HostDamage.ShootBullet( hit, damage, 0, attacker, GameObject, tags );
				if ( piloted && hit.Hit && (hitNpc is not null && IsHostileToFaction( hitNpc.Faction ) || hit.GameObject?.GetComponentInParent<Titanfall2Harvester>() is not null) )
					NotifyHitMarker( wasAlive && hitNpc is { Dead: true } );
			}
			PlayBulletTrace( origin, new BaseCombatWeapon.ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface ) );
		}
		NotifyShotFired();
		PlayShotSound( origin, npc: !piloted );
		core?.NotifyPrimaryFired();
		if ( core is not { UnlimitedAmmo: true } ) Rounds = Math.Max( 0, Rounds - ammoCost );
		var interval = core?.PrimaryInterval( ShotInterval ) ?? ShotInterval;
		if ( Rounds < Firing.Minimum && core is not { UnlimitedAmmo: true } ) StartReload();
		else if ( !piloted && Role != FrontierRole.EliteMonarch && ++_burst >= Math.Max( 1, BurstShots ) )
		{
			_burst = 0;
			_nextShot = MathF.Max( interval, BurstPause );
		}
		else _nextShot = MathF.Max( 0.05f, interval );
		return true;
	}

	internal void NotifyShotFired() => ShotSerial = ShotSerial == int.MaxValue ? 1 : ShotSerial + 1;
	internal void NotifyHitMarker( bool kill = false )
	{
		HitMarkerKill = kill;
		HitMarkerSerial = HitMarkerSerial == int.MaxValue ? 1 : HitMarkerSerial + 1;
	}

	internal void StartReload()
	{
		var core = GetComponent<Titanfall2TitanCore>();
		if ( !TitanCanAct || Reloading || core is { UnlimitedAmmo: true } || Rounds >= (core?.MagazineCapacity ?? MagazineSize) ) return;
		Reloading = true;
		GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		_burst = 0;
		_nextShot = MathF.Max( 0.1f, ReloadSeconds * (core?.ReloadMultiplier ?? 1) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBulletTrace( Vector3 origin, BaseCombatWeapon.ShotEffect shot )
	{
		if ( _gun.IsValid() && _gun.GetComponent<BaseWeaponModel>()?.MuzzleGameObject is { } muzzle )
			Titanfall2RetailWeaponEffects.Spawn( muzzle, MountIdent, WeaponPresentation.TracerWorld, false, 0.5f, shot.HitPosition );
		Titanfall2CombatEffects.Impact( Scene, shot, MountIdent, WeaponPresentation.ImpactTable );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayShotSound( Vector3 position, bool melee = false, bool npc = true )
	{
		CueActorAttack( melee );
		if ( !melee )
		{
			var firstPerson = !npc && Seat.IsValid() && Seat.LocalPilot is { ThirdPerson: false };
			GameObject.GetOrAddComponent<Titanfall2WeaponSoundEmitter>().Shot( MountIdent, WeaponAudio, firstPerson, position, ShotInterval,
				npc: npc, ammoPercent: MagazineSize > 0 ? Rounds * 100f / MagazineSize : 100 );
			if ( !firstPerson && _gun.IsValid() && _gun.GetComponent<BaseWeaponModel>()?.MuzzleGameObject is { } muzzle && muzzle.IsValid() )
			{
				Titanfall2RetailWeaponEffects.MuzzleFlash( _gun.GetComponent<BaseWeaponModel>(), MountIdent, WeaponPresentation, false );
				Titanfall2RetailWeaponEffects.Shells( _gun.GetComponent<BaseWeaponModel>(), MountIdent, WeaponPresentation, false );
			}
		}
	}

	protected override void OnDamaging( Component.IDamageable.DamageEvent e )
	{
		if ( !Networking.IsHost || IsProxy || Dead || (!_forceTitanDeath && (IsNuking || IsTitan && LifeState == Titanfall2TitanLifeState.Ejecting))
			|| !float.IsFinite( e.AcceptedDamage ) || e.AcceptedDamage <= 0 )
		{
			e.Cancelled = true;
			return;
		}
		var attackerNpc = e.DamageInfo.Weapon?.GetComponentInParent<BaseNpc>() ?? e.DamageInfo.Attacker?.GetComponentInParent<BaseNpc>();
		if ( !_forceTitanDeath && attackerNpc.IsValid()
			&& !IsHostileToFaction( attackerNpc.Faction ) )
		{
			e.Cancelled = true;
			return;
		}
		_shieldDelay = 4;
		if ( !_forceTitanDeath && GetComponent<Titanfall2TitanKit>() is { } kit )
		{
			e.AcceptedDamage *= kit.GuardDamage( e.DamageInfo ) / e.DamageInfo.Damage;
			if ( e.AcceptedDamage <= 0 ) { e.Cancelled = true; return; }
		}
		Titanfall2HostDamage.Observe( this, e.DamageInfo );
		e.DamageInfo.Weapon?.GetComponentInParent<Titanfall2TitanKit>()?.RegisterWeaponHit( this, e.DamageInfo );
		if ( Shield > 0 )
		{
			var absorbed = MathF.Min( Shield, e.AcceptedDamage );
			Shield -= absorbed;
			AddActualDamageTaken( absorbed );
			e.AcceptedDamage -= absorbed;
			if ( e.AcceptedDamage <= 0 )
			{
				e.Cancelled = true;
				return;
			}
		}
		_damageHealthSnapshots.Capture( e, Health );
		if ( !_forceTitanDeath && Role == FrontierRole.NukeTitan && Health - e.AcceptedDamage <= 0 )
		{
			e.AcceptedDamage = MathF.Max( 0, Health - 1 );
			ArmNuke();
			if ( e.AcceptedDamage <= 0 ) e.Cancelled = true;
			return;
		}
		base.OnDamaging( e );
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		SettleTitanLifeDamage( damage );
		base.OnDamaged( damage );
		ReactToCombatDamage( damage.Instigator );
		if ( !Dead && Health > 0 )
		{
			SayVoice( VoiceCue.Pain );
			CueActorFlinch( damage );
		}
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( Dead ) return;
		KillActor( spawnRagdoll: !IsTitan );
	}

	protected override GameObject CreateRagdoll()
	{
		if ( !Renderer.IsValid() || Renderer.Model is null ) return null;
		if ( Renderer.Model.Physics is { Parts.Count: > 0 } )
		{
			var nativeRagdoll = base.CreateRagdoll();
			if ( nativeRagdoll.IsValid() )
			{
				nativeRagdoll.Tags.Add( "ragdoll", "removable" );
				return nativeRagdoll;
			}
		}

		var corpse = new GameObject( Scene, false, $"{DisplayName ?? "TF2"} Corpse" );
		corpse.Tags.Add( "ragdoll", "removable" );
		corpse.Flags |= GameObjectFlags.NotSaved;
		corpse.WorldTransform = Renderer.WorldTransform;

		var renderer = corpse.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Renderer.Model;
		renderer.UseAnimGraph = false;

		var bounds = Renderer.Model.Bounds;
		var box = corpse.AddComponent<BoxCollider>();
		box.Center = bounds.Center;
		box.Scale = new Vector3( MathF.Max( 8f, bounds.Size.x * 0.6f ), MathF.Max( 8f, bounds.Size.y * 0.6f ), MathF.Max( 8f, bounds.Size.z * 0.35f ) );
		var body = corpse.AddComponent<Rigidbody>();
		body.Velocity = Vector3.Up * 65f - WorldRotation.Forward * 85f;
		corpse.AddComponent<Titanfall2CorpseCleanup>();
		corpse.Enabled = true;
		if ( _actorClips?.Contains( ModelLoader.DeathRole ) == true && renderer.SceneModel.IsValid() )
		{
			renderer.Sequence.Name = ModelLoader.DeathRole;
			renderer.Sequence.Looping = false;
			renderer.Sequence.Time = 0;
		}
		if ( GameObject.Network.Active ) corpse.NetworkSpawn( true, null );
		return corpse;
	}

	void KillActor( bool spawnRagdoll = false )
	{
		if ( !Networking.IsHost || IsProxy || Dead ) return;
		SetTitanLifeState( Titanfall2TitanLifeState.Dead );
		GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		Health = 0;
		Dead = true;
		OnCombatDeathEffects();
		EndCurrentSchedule();
		Movement?.Stop();
		Ability = FrontierAbility.None;
		SayVoice( VoiceCue.Death, bypassCooldown: true );
		if ( Seat.IsValid() ) Seat.ReleaseSeat();
		Agent?.Stop();
		if ( GetComponent<BoxCollider>() is { } collider ) collider.Enabled = false;
		if ( _gun.IsValid() ) _gun.Enabled = false;

		if ( spawnRagdoll && NavigationMode == Navigation.Ground && !IsTitan && Renderer.IsValid() && Renderer.Model is { IsError: false } )
		{
			CreateRagdoll();
			GameObject.Destroy();
			return;
		}
		_remove = 4;
	}

	void AttachGun()
	{
		if ( _triedGun && _attachedGunPath != GunModelPath )
		{
			if ( _gun.IsValid() ) _gun.Destroy();
			_gun = null;
			_gunBone = null;
			_triedGun = false;
		}
		if ( _triedGun ) { UpdateGunPose(); return; }
		if ( !Renderer.IsValid() || Renderer.Model is not { IsError: false } body || string.IsNullOrEmpty( GunModelPath ) ) return;
		_triedGun = true;
		_attachedGunPath = GunModelPath;
		var weapon = Titanfall2GameplayVisuals.GetModel( GunModelPath );
		if ( weapon is not { IsError: false } ) return;
		var mount = Renderer.GetAttachmentObject( "PROPGUN" ) ?? Renderer.GetAttachmentObject( "R_HAND" )
			?? Renderer.GetAttachmentObject( "def_r_propHand" );
		// Respawn's held weapons share a skeleton root with the carrier. The PROPGUN
		// attachment can cancel its parent's rotation: attaching by position alone leaves the XO-16 upright.
		foreach ( var root in weapon.Bones.AllBones )
		{
			if ( root.Parent is not null || body.Bones.GetBone( root.Name ) is not { } shared ) continue;
			if ( !root.Name.Contains( "gun", StringComparison.OrdinalIgnoreCase ) && !root.Name.Contains( "prophand", StringComparison.OrdinalIgnoreCase ) ) continue;
			_gunBone = shared;
			_gunBindInverse = root.LocalTransform.ToLocal( global::Transform.Zero );
			mount ??= GameObject;
			break;
		}
		if ( !mount.IsValid() && (IsTitan || BodyHeight > 100) )
			mount = new GameObject( GameObject, true, "TF2 weapon mount fallback" )
			{
				LocalPosition = new Vector3( BodyRadius * 0.7f, -BodyRadius * 0.85f, BodyHeight * 0.63f ),
				Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked
			};
		if ( !mount.IsValid() ) return;
		_gun = Titanfall2GameplayVisuals.CreateGun( mount, GunModelPath, firstPerson: false, presentation: WeaponPresentation );
		UpdateGunPose();
	}
	string _attachedGunPath;
}

[Hide]
public sealed class Titanfall2CorpseCleanup : Component
{
	TimeUntil _expires = 10f;
	protected override void OnStart() => _expires = 10f;
	protected override void OnUpdate()
	{
		if ( Networking.IsHost && !IsProxy && _expires <= 0 ) GameObject.Destroy();
	}
}
