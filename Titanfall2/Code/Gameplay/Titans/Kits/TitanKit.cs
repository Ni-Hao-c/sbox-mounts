/// <summary>Reusable, serialized Titan kit. All activation and damage decisions are host-owned.</summary>
[Title( "Titanfall 2 Titan Kit" ), Group( "Titanfall 2" )]
public sealed partial class Titanfall2TitanKit : Component
{
	public enum Chassis { None, Monarch, Ion, Legion, Tone, Scorch, Northstar, Ronin }
	[Property] public Titanfall2Npc Actor { get; set; }
	[Property] public Chassis Kit { get; set; }
	[Property] public bool UseAi { get; set; } = true;
	[Property] public float OffenseCooldown { get; set; } = 12;
	[Property] public float DefenseCooldown { get; set; } = 16;
	[Property] public float SmokeCooldown { get; set; } = 20;
	[Property] public float TitanMeleeCooldown { get; set; } = 1.1f;
	[Sync( SyncFlags.FromHost )] public float OffenseRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float DefenseRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float SmokeRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float MeleeRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float GuardRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float DashRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public int DashCharges { get; private set; } = 1;
	[Sync( SyncFlags.FromHost )] public float DashRechargeProgress { get; private set; } = 1f;
	public int MaxDashCharges => MaxDashChargesForChassis( Kit );
	internal static int MaxDashChargesForChassis( Chassis kit ) => kit is Chassis.Northstar or Chassis.Ronin ? 2 : 1;
	Chassis _initializedDashKit = (Chassis)(-1);
	float _dashChargeTimer;
	public string OffenseName => Kit switch
	{
		Chassis.Ion => "Laser Shot", Chassis.Legion => "Power Shot", Chassis.Tone => "Tracker Rockets",
		Chassis.Scorch => "Firewall", Chassis.Northstar => "Cluster Missile", Chassis.Ronin => "Arc Wave", _ => "Rocket Salvo"
	};
	public string DefenseName => Kit switch
	{
		Chassis.Ion => "Vortex Guard", Chassis.Legion => "Gun Shield", Chassis.Tone => "Particle Guard",
		Chassis.Scorch => "Thermal Shield", Chassis.Northstar => "Tether Trap", Chassis.Ronin => "Sword Block", _ => "Energy Siphon"
	};
	public string SmokeName => "Electric Smoke";
	public string MeleeName => Kit == Chassis.Ronin ? "Broad Sword" : "Titan Melee";
	public float CurrentMeleeCooldown => Kit == Chassis.Ronin
		? (Actor.IsValid() && Actor.GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true } ? 0.65f : 0.85f)
		: Math.Clamp( TitanMeleeCooldown, 0.4f, 5f );
	int _rockets;
	TimeUntil _volleyStep;
	TimeUntil _aiStep;
	TimeUntil _heatStep;
	Ray _volleyAim;
	GameObject _volleyAttacker;
	GameObject _guardVisual;
	bool Ready => Networking.IsHost && !IsProxy && Actor is { IsTitan: true, CanFight: true, TitanCanAct: true }
		&& Kit != Chassis.None;

	protected override void OnUpdate()
	{
		UpdateGuardVisual();
		UpdatePhaseVisual();
		if ( !Networking.IsHost || IsProxy ) return;
		EnsureDashInitialized();
		if ( !Ready ) { _rockets = 0; GuardRemaining = 0; ClearUtilityState(); ClearDefenseVolume(); ClearTrackers(); ClearIonInput(); ClearHoldDefenseInput(); return; }
		TickTrackers();
		OffenseRemaining = MathF.Max( 0, OffenseRemaining - Time.Delta );
		DefenseRemaining = MathF.Max( 0, DefenseRemaining - Time.Delta );
		SmokeRemaining = MathF.Max( 0, SmokeRemaining - Time.Delta );
		MeleeRemaining = MathF.Max( 0, MeleeRemaining - Time.Delta );
		TickUtility();
		var heldGuard = (Kit == Chassis.Ion && _ionVortexHeld && _ionPilot.IsValid())
			|| (Kit is Chassis.Scorch or Chassis.Ronin && _holdDefenseHeld && _holdDefensePilot.IsValid());
		if ( !heldGuard ) GuardRemaining = MathF.Max( 0, GuardRemaining - Time.Delta );
		TickIonEnergy();
		TickDefenseVolume();
		TickDashCharges();
		if ( _rockets > 0 && _volleyStep <= 0 )
		{
			_volleyStep = _volleyData?.Interval ?? 0.12f;
			_rockets--;
			LaunchOrdnance( _volleyAim, _volleyAttacker );
		}
		if ( GuardRemaining > 0 && Kit == Chassis.Scorch && _heatStep <= 0 )
		{
			_heatStep = 0.2f;
			Titanfall2GasCanister.IgniteInRadius( Scene, Eye + Actor.WorldRotation.Forward * 140f, 220f, Actor.GameObject );
			foreach ( var enemy in Scene.GetAllComponents<Titanfall2Npc>().Where( npc => npc != Actor && !npc.Dead && Actor.IsHostileToFaction( npc.Faction ) ).ToArray() )
			{
				var delta = enemy.WorldPosition - Actor.WorldPosition;
				if ( delta.Length > 260 || Vector3.Dot( Actor.WorldRotation.Forward, delta.Normal ) < 0.4f || !Actor.HasLineOfSight( enemy.GameObject ) ) continue;
				var damage = new DamageInfo( enemy.IsTitan ? 220 : 40, Actor.GameObject, GameObject ) { Origin = Eye, Position = enemy.WorldPosition };
				damage.Tags.Add( "burn" );
				Titanfall2HostDamage.Deal( (IDamageable)enemy, damage );
			}
		}
		if ( !UseAi || Actor.Seat is { IsOccupied: true } or { IsEmbarking: true } || _aiStep > 0 ) return;
		_aiStep = 0.35f;
		TickAiAbilities();
	}

	void EnsureDashInitialized()
	{
		if ( _initializedDashKit == Kit ) return;
		_initializedDashKit = Kit;
		DashCharges = MaxDashCharges;
		_dashChargeTimer = 0f;
		DashRemaining = 0f;
		DashRechargeProgress = 1f;
	}

	void TickDashCharges()
	{
		var max = MaxDashCharges;
		if ( DashCharges >= max )
		{
			DashCharges = max;
			_dashChargeTimer = 0f;
			DashRemaining = 0f;
			DashRechargeProgress = 1f;
			return;
		}

		var recharge = MathF.Max( 0.5f, DashRechargeSeconds );
		if ( _dashChargeTimer <= 0f ) _dashChargeTimer = recharge;
		_dashChargeTimer = MathF.Max( 0f, _dashChargeTimer - Time.Delta );
		if ( _dashChargeTimer <= 0f )
		{
			DashCharges = Math.Min( max, DashCharges + 1 );
			_dashChargeTimer = DashCharges < max ? recharge : 0f;
		}
		DashRemaining = DashCharges > 0 ? 0f : _dashChargeTimer;
		DashRechargeProgress = DashCharges >= max ? 1f : Math.Clamp( 1f - _dashChargeTimer / recharge, 0f, 1f );
	}

	internal void RefreshAllDashes()
	{
		if ( !Networking.IsHost ) return;
		DashCharges = MaxDashCharges;
		_dashChargeTimer = 0f;
		DashRemaining = 0f;
		DashRechargeProgress = 1f;
	}

	void TickAiAbilities()
	{
		var healthFrac = Actor.MaxHealth > 0 ? Math.Clamp( Actor.Health / Actor.MaxHealth, 0f, 1f ) : 1f;
		var shieldFrac = Actor.MaxShield > 0 ? Math.Clamp( Actor.Shield / Actor.MaxShield, 0f, 1f ) : 0f;
		var hazardNear = Actor.TryFindNearbyHazard( out var hazardOrigin, out _ );

		var target = Actor.CombatTarget;
		var hasTarget = target.IsValid() && Actor.HasLineOfSight( target );
		var targetDist = hasTarget ? target.WorldPosition.Distance( Actor.WorldPosition ) : float.MaxValue;
		var preferredRange = Titanfall2TacticsRules.PreferredRange( true, Kit, Actor.CombatStyle, Actor.AttackRange );

		if ( DashRemaining <= 0 )
		{
			var dashIntent = Titanfall2TacticsRules.ChooseAiDash( Kit, healthFrac, shieldFrac, Actor.IsUnderFire, hazardNear, targetDist, preferredRange );
			if ( dashIntent != Titanfall2AiDashIntent.None )
			{
				Vector3 dashDir = dashIntent switch
				{
					Titanfall2AiDashIntent.EvadeHazard => (Actor.WorldPosition - hazardOrigin).WithZ( 0 ),
					Titanfall2AiDashIntent.KiteBack when hasTarget => (Actor.WorldPosition - target.WorldPosition).WithZ( 0 ),
					Titanfall2AiDashIntent.CloseIn when hasTarget => (target.WorldPosition - Actor.WorldPosition).WithZ( 0 ),
					Titanfall2AiDashIntent.LateralStrafe when hasTarget =>
						Vector3.Cross( (target.WorldPosition - Actor.WorldPosition).WithZ( 0 ).Normal, Vector3.Up )
						* (Random.Shared.NextSingle() < 0.5f ? 1f : -1f),
					_ => Vector3.Zero
				};
				if ( dashDir.LengthSquared > 1f ) TryAiDash( dashDir.Normal );
			}
		}

		if ( !hasTarget ) return;
		var aimPoint = target.GetBounds().Center;
		var aim = new Ray( Eye, (aimPoint - Eye).Normal );
		var targetGrounded = MathF.Abs( target.WorldPosition.z - Actor.WorldPosition.z ) < 160f;

		if ( Titanfall2TacticsRules.ShouldUseTitanDefense( Kit, healthFrac, shieldFrac, Actor.IsUnderFire, targetDist, SharedEnergy, GuardRemaining > 0 ) )
			TryDefense( aim, Actor.GameObject );

		if ( Titanfall2TacticsRules.ShouldUseTitanOffense( Kit, targetDist, Actor.AttackRange, targetGrounded, HasTrackerLock, SharedEnergy, Actor.IsUnderFire ) )
			TryOffense( aim, Actor.GameObject );
	}

	internal bool TryAiDash( Vector3 direction )
	{
		if ( !Ready || DashRemaining > 0 || Actor.Seat is { IsOccupied: true } || direction.WithZ( 0 ).LengthSquared < 0.01f ) return false;
		var flatDir = direction.WithZ( 0 ).Normal;
		var dashDist = Kit is Chassis.Ronin or Chassis.Northstar ? 240f : Kit is Chassis.Legion or Chassis.Scorch ? 160f : 200f;
		var radius = MathF.Max( 8f, Actor.BodyRadius - 2f );
		var hull = new BBox( new Vector3( -radius, -radius, 4f ), new Vector3( radius, radius, MathF.Max( 24f, Actor.BodyHeight - 2f ) ) );
		var origin = Actor.WorldPosition + Vector3.Up * 16f;
		const Titanfall2TraceMask mask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.NpcSolid | (uint)Titanfall2Contents.TitanClip);
		var sweep = Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, origin, origin + flatDir * dashDist )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "tether-trap" ), mask );
		if ( sweep.StartedSolid || (sweep.EndPosition - origin).WithZ( 0 ).Length < 48f ) return false;
		var ground = Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, sweep.EndPosition, sweep.EndPosition - Vector3.Up * 56f )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "tether-trap" ), mask );
		if ( !ground.Hit || ground.StartedSolid || ground.Normal.z < Titanfall2TitanDriveRules.WalkableNormalZ ) return false;

		var destination = Scene.NavMesh.IsEnabled ? Scene.NavMesh.GetClosestPoint( ground.EndPosition ) ?? ground.EndPosition : ground.EndPosition;
		if ( destination.Distance( ground.EndPosition ) > 80f ) return false;

		DashRemaining = DashRechargeSeconds;
		var wasAgentEnabled = Actor.Agent.IsValid() && Actor.Agent.Enabled;
		if ( wasAgentEnabled ) Actor.Agent.Enabled = false;
		Actor.WorldPosition = destination;
		if ( wasAgentEnabled ) Actor.Agent.Enabled = true;
		return true;
	}

	Vector3 Eye => Actor.WorldPosition + Vector3.Up * (Actor.BodyHeight * 0.79f);
	bool ValidAim( Ray aim ) => aim.Position.IsFinite && aim.Forward.IsFinite && aim.Forward.LengthSquared is > 0.25f and < 4f
		&& aim.Position.Distance( Eye ) < 180;

	/// <summary>Activate ordnance from the cockpit or a host controller; returns false during cooldown.</summary>
	public bool TryOffense( Ray aim, GameObject attacker )
	{
		LastOffenseFailure = null;
		if ( !Ready || IsPhasing || !ValidAim( aim ) ) { LastOffenseFailure = "机体无法使用攻击技能"; return false; }
		if ( OffenseRemaining > 0 || _rockets > 0 ) { LastOffenseFailure = "攻击技能冷却中"; return false; }
		var data = AbilityData();
		if ( Titanfall2TitanAbilityData.ScriptFor( Kit ) is not null && data is null )
		{
			LastOffenseFailure = "缺少原版技能武器配置";
			return false;
		}
		if ( Kit == Chassis.Tone )
		{
			_volleyTarget = FindTrackerLock( aim );
			if ( !_volleyTarget.IsValid() ) { LastOffenseFailure = "40mm 命中同一目标三次后锁定"; return false; }
			_trackers.Remove( _volleyTarget );
			ShowTrackedTarget( null );
		}
		if ( Kit == Chassis.Ion )
		{
			if ( DefenseVolumeActive || Actor.GetComponent<Titanfall2TitanCore>() is { BlocksPrimary: true } )
			{ LastOffenseFailure = "涡旋护盾或核心正在使用"; return false; }
			if ( IonData() is not { } ion || !_ionEnergy.Spend( ion.LaserCost ) )
			{ LastOffenseFailure = "共享能量不足"; return false; }
			SharedEnergy = _ionEnergy.Count;
			OffenseRemaining = ion.LaserRecovery;
		}
		else OffenseRemaining = Math.Clamp( OffenseCooldown, 0.5f, 120 );
		PlayActivation( false );
		if ( Kit == Chassis.Legion ) FireLegionPowerShot( aim, attacker );
		else if ( Kit == Chassis.Ion ) Beam( aim, attacker, siphon: false, data );
		else
		{
			_volleyData = data;
			_volleyAim = aim;
			_volleyAttacker = attacker;
			_rockets = data?.Shots ?? 1;
			_volleyStep = 0;
		}
		return true;
	}

	/// <summary>Activate a chassis defense. Siphon restores shield only after hitting a hostile.</summary>
	public bool TryDefense( Ray aim, GameObject attacker )
	{
		if ( !Ready || !ValidAim( aim ) ) return false;
		// AI uses a bounded tactical hold. The cockpit supplies hold/release separately.
		if ( Kit == Chassis.Ion ) return !DefenseVolumeActive && TryStartIonVortex( aim );
		if ( Kit == Chassis.Scorch )
		{
			if ( DefenseVolumeActive || ThermalShieldMeter < 0.2f ) return false;
			BeginDefenseVolume();
			PlayUtilitySoundRpc( "heat_shield", Eye );
			return true;
		}
		if ( DefenseRemaining > 0 ) return false;
		if ( Kit == Chassis.Northstar )
		{
			var owner = Actor.Seat?.GetOccupant()?.GameObject ?? Actor.GameObject;
			if ( !Titanfall2TetherTrap.Launch( Actor, aim, owner ).IsValid() ) return false;
			DefenseRemaining = Titanfall2TetherRules.RecoverySeconds;
			return true;
		}
		if ( Kit == Chassis.Tone )
		{
			if ( !Titanfall2ParticleWall.Deploy( Actor, aim ).IsValid() ) return false;
			DefenseRemaining = 14f;
			return true;
		}
		DefenseRemaining = Math.Clamp( DefenseCooldown, 1, 120 );
		PlayActivation( true );
		if ( Kit == Chassis.Monarch ) Beam( aim, attacker, siphon: true, AbilityData( defense: true ) );
		else if ( Kit is Chassis.Ion or Chassis.Legion ) BeginDefenseVolume();
		else GuardRemaining = Kit == Chassis.Ronin ? 3 : 5;
		return true;
	}

	public bool TryDash()
	{
		EnsureDashInitialized();
		if ( !Ready || DashCharges <= 0 || Actor.Seat is not { IsOccupied: true } ) return false;
		DashCharges = Math.Max( 0, DashCharges - 1 );
		if ( _dashChargeTimer <= 0f ) _dashChargeTimer = MathF.Max( 0.5f, DashRechargeSeconds );
		DashRemaining = DashCharges > 0 ? 0f : _dashChargeTimer;
		DashRechargeProgress = Math.Clamp( 1f - _dashChargeTimer / MathF.Max( 0.5f, DashRechargeSeconds ), 0f, 1f );
		return true;
	}

	/// <summary>Slot 3 countermeasure: deploys an anti-pilot/area Electric Smoke cloud centered on the Titan.</summary>
	public bool TryElectricSmoke( GameObject attacker )
	{
		if ( !Ready || IsPhasing || SmokeRemaining > 0 ) return false;
		SmokeRemaining = Math.Clamp( SmokeCooldown, 2f, 120f );
		var origin = Actor.WorldPosition + Vector3.Up * (Actor.BodyHeight * 0.42f);
		var smokeSettings = new Titanfall2ProjectileSettings
		{
			Speed = 1f,
			SplashRadius = 320f,
			HazardSeconds = 6.5f,
			HazardEffect = "P_wpn_smk_electric_pilot",
			DamageOwner = false,
			MountIdent = Actor.MountIdent ?? "titanfall2",
			Tint = new Color( 0.45f, 0.75f, 1f ),
			Behavior = Titanfall2ProjectileBehavior.ElectricSmoke
		};
		Titanfall2AreaHazard.Spawn( Scene, origin, smokeSettings, 110f, 450f,
			attacker.IsValid() ? attacker : Actor.GameObject, GameObject, Actor.GameObject );
		PlayTitanSlotSoundRpc( "smoke", origin );
		return true;
	}

	/// <summary>Slot 4 close-quarters strike: initiates Titan-vs-Titan Termination on a Doomed enemy Titan, else heavy punch/sword cleave.</summary>
	public bool TryTitanMelee( Ray aim, GameObject attacker )
	{
		if ( !Ready || IsPhasing || Actor.IsInTermination || MeleeRemaining > 0 || !ValidAim( aim ) ) return false;
		var core = Actor.GetComponent<Titanfall2TitanCore>();
		if ( core is { BlocksPrimary: true, SwordCoreActive: false } ) return false;

		// Retail Titanfall 2 rule: meleeing a Doomed enemy Titan within range initiates a paired Titan-vs-Titan Termination!
		var doomedTarget = FindDoomedTerminationTarget( aim );
		if ( doomedTarget.IsValid() && Titanfall2TitanTermination.TryStart( Actor, doomedTarget, attacker, aim ) )
		{
			MeleeRemaining = CurrentMeleeCooldown;
			return true;
		}

		MeleeRemaining = CurrentMeleeCooldown;
		var isRonin = Kit == Chassis.Ronin;
		var swordCore = core is { SwordCoreActive: true };
		var reach = isRonin ? 280f : 220f;
		var radius = isRonin ? 56f : 42f;
		var titanDamage = swordCore ? 2200f : isRonin ? 800f : 500f;
		var pilotDamage = 250f;
		var forward = aim.Forward.Normal;

		PlayTitanSlotSoundRpc( isRonin ? "sword_swing" : "punch_swing", Eye );

		var hits = Scene.Trace.Ray( Eye, Eye + forward * reach )
			.Radius( radius )
			.IgnoreGameObjectHierarchy( Actor.GameObject )
			.WithoutTags( "trigger", "projectile" )
			.RunAll();

		var hitVictims = new HashSet<GameObject>();
		var landed = false;
		var killedAny = false;
		var impactPoint = Eye + forward * (reach * 0.7f);

		foreach ( var hit in hits )
		{
			if ( !hit.Hit || hit.StartedSolid || !hit.GameObject.IsValid() ) continue;
			var npc = hit.GameObject.GetComponentInParent<Titanfall2Npc>();
			var player = hit.GameObject.GetComponentInParent<PlayerController>();
			var victimRoot = npc?.GameObject ?? player?.GameObject ?? hit.GameObject;
			if ( victimRoot == Actor.GameObject || victimRoot == attacker || !hitVictims.Add( victimRoot ) ) continue;
			if ( npc.IsValid() && (!Actor.IsHostileToFaction( npc.Faction ) || npc.Dead) ) continue;

			var damageable = victimRoot.GetComponentInParent<Component.IDamageable>();
			if ( damageable is not null )
			{
				var wasDead = npc is { Dead: true };
				var dmg = npc is { IsTitan: true } ? titanDamage : pilotDamage;
				var info = new DamageInfo( dmg, attacker.IsValid() ? attacker : Actor.GameObject, GameObject )
				{
					Origin = Eye,
					Position = hit.HitPosition
				};
				info.Tags.Add( "melee" );
				Titanfall2HostDamage.Deal( damageable, info );
				landed = true;
				impactPoint = hit.HitPosition;
				if ( !wasDead && npc is { Dead: true } ) killedAny = true;
			}

			if ( victimRoot.GetComponentInParent<Rigidbody>() is { IsValid: true, Enabled: true } body )
			{
				body.Velocity += forward * 560f + Vector3.Up * 180f;
				if ( !landed ) { landed = true; impactPoint = hit.HitPosition; }
			}
		}

		if ( landed )
		{
			Actor.NotifyHitMarker( killedAny );
			PlayTitanSlotSoundRpc( "melee_hit", impactPoint );
		}
		return true;
	}

	Titanfall2Npc FindDoomedTerminationTarget( Ray aim )
	{
		if ( !Scene.IsValid() || !Actor.IsValid() ) return null;
		var fwd = aim.Forward.WithZ( 0 );
		if ( fwd.LengthSquared < 0.01f ) fwd = Actor.WorldRotation.Forward.WithZ( 0 );
		fwd = fwd.Normal;

		Titanfall2Npc best = null;
		var bestScore = float.NegativeInfinity;
		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>() )
		{
			if ( !npc.IsValid() || npc == Actor || !npc.IsTitan || !npc.IsDoomed || npc.Dead || npc.IsInTermination ) continue;
			if ( !Actor.IsHostileToFaction( npc.Faction ) ) continue;

			var delta = (npc.WorldPosition - Actor.WorldPosition).WithZ( 0 );
			var dist = delta.Length;
			if ( dist > Titanfall2TitanTerminationRules.TerminationRange ) continue;
			var dot = dist > 0.01f ? Vector3.Dot( fwd, delta / dist ) : 1f;
			if ( dot < Titanfall2TitanTerminationRules.MinForwardDot ) continue;
			if ( !Actor.HasLineOfSight( npc.GameObject ) ) continue;

			var score = dot * 2f - dist / Titanfall2TitanTerminationRules.TerminationRange;
			if ( score > bestScore )
			{
				bestScore = score;
				best = npc;
			}
		}
		return best;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayTitanSlotSoundRpc( string kind, Vector3 point )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		var sound = kind switch
		{
			"sword_swing" => "ronin_sword_melee_3p",
			"punch_swing" => Kit is Chassis.Legion or Chassis.Scorch ? "ogre_melee_swing_fast"
				: Kit is Chassis.Northstar or Chassis.Ronin ? "stryder_melee_swing_fast"
				: "atlas_melee_swing_fast",
			"melee_hit" => "titan_melee_hit",
			_ => null
		};
		if ( !string.IsNullOrWhiteSpace( sound ) )
			Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent, sound, point );
	}

	public float DashRechargeSeconds => Kit == Chassis.Ronin && Actor.IsValid()
		&& Actor.GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true } ? 1
		: Kit is Chassis.Legion or Chassis.Scorch ? 8 : 5;

	internal float GuardDamage( in DamageInfo damage )
	{
		if ( PhaseRemaining > 0 || Actor is { IsInTermination: true } ) return 0f;
		// These shields now intercept at a world-space volume before actor damage.
		// Damage arriving here (blast, melee, side/back hits) is not retroactively blocked.
		if ( Kit is Chassis.Ion or Chassis.Legion ) return damage.Damage;
		if ( GuardRemaining <= 0 || Actor is not { Dead: false } ) return damage.Damage;
		if ( damage.Tags.Has( "melee" ) ) return damage.Damage;
		if ( !damage.Tags.Has( "bullet" ) && !damage.Tags.Has( "projectile" )
			&& damage.Weapon?.GetComponent<BaseCombatWeapon>() is null ) return damage.Damage;
		var toOrigin = damage.Origin - Eye;
		var forward = Vector3.Dot( Actor.WorldRotation.Forward, toOrigin.Normal );
		if ( Kit == Chassis.Ronin && Actor.GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true } ) return damage.Damage * 0.15f;
		return damage.Damage * GuardMultiplier( Kit, forward );
	}

	internal static float GuardMultiplier( Chassis kit, float forwardDot ) => kit == Chassis.Ronin ? 0.3f
		: forwardDot < 0.25f ? 1 : kit == Chassis.Scorch ? 0 : kit == Chassis.Tone ? 0.2f : 1;

	void Beam( Ray aim, GameObject attacker, bool siphon, Titanfall2TitanAbilityData data )
	{
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, aim.Position + aim.Forward.Normal * (data?.Range ?? 6000) ).UseHitboxes().UseHitPosition()
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		var intercepted = Titanfall2TitanDefense.TryInterceptHitscan( Scene, ref hit, attacker, GameObject,
			new Titanfall2DefenseShot( data?.Damage ?? (siphon ? 20 : 100), data?.TitanDamage ?? (siphon ? 100 : 2000), 0,
				Range: data?.Range ?? 6000, Refire: Kit == Chassis.Legion ) );
		var enemy = hit.GameObject?.GetComponentInParent<Titanfall2Npc>();
		var hostile = enemy.IsValid() ? Actor.IsHostileToFaction( enemy.Faction )
			: hit.GameObject?.GetComponentInParent<PlayerController>() is not null && Actor.AttackPlayers && !Actor.Friendly;
		if ( !intercepted && hit.Hit && hostile )
		{
			var damage = enemy is { IsTitan: true } ? data?.TitanDamage ?? (siphon ? 100 : 2000)
				: data?.Damage ?? (siphon ? 20 : 100);
			var tags = new TagSet(); tags.Add( "bullet" );
			var before = enemy.IsValid() ? enemy.Health + enemy.Shield : 0;
			Titanfall2HostDamage.ShootBullet( hit, damage, 0, attacker, GameObject, tags );
			if ( siphon && (!enemy.IsValid() || enemy.Health + enemy.Shield < before) )
			{
				Actor.RestoreTitanShield( enemy is { IsTitan: true } ? 750 : 250 );
				enemy?.GetComponent<Titanfall2TitanKit>()?.ApplySiphonSlow( 2.0f );
			}
		}
		PlayBeam( aim.Position, new BaseCombatWeapon.ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface ), siphon );
	}

	void FireLegionPowerShot( Ray aim, GameObject attacker )
	{
		var source = attacker.IsValid() ? attacker : Actor.GameObject;
		if ( !LegionLongRangeMode )
		{
			// Close-Range Power Shot: devastating 8-pellet shotgun blast cone with heavy knockback.
			const int pellets = 8;
			const float range = 950f;
			for ( var i = 0; i < pellets; i++ )
			{
				var dir = aim.Forward.Normal.WithAimCone( 6.5f, 4.5f );
				var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, aim.Position + dir * range )
					.UseHitboxes().UseHitPosition().IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ),
					Titanfall2TraceMask.Shot );
				var intercepted = Titanfall2TitanDefense.TryInterceptHitscan( Scene, ref hit, source, GameObject,
					new Titanfall2DefenseShot( 45f, 250f, 0f, Range: range, Refire: true ) );
				if ( !intercepted && hit.Hit )
				{
					var enemy = hit.GameObject?.GetComponentInParent<Titanfall2Npc>();
					var hostile = enemy.IsValid() ? Actor.IsHostileToFaction( enemy.Faction )
						: hit.GameObject?.GetComponentInParent<PlayerController>() is not null && Actor.AttackPlayers && !Actor.Friendly;
					if ( hostile )
					{
						var dmg = enemy is { IsTitan: true } ? 250f : 60f;
						var tags = new TagSet(); tags.Add( "bullet" );
						Titanfall2HostDamage.ShootBullet( hit, dmg, 240f, source, GameObject, tags );
						if ( hit.GameObject?.GetComponentInParent<Rigidbody>() is { IsValid: true, Enabled: true } body )
							body.Velocity += dir * 420f + Vector3.Up * 110f;
					}
				}
				PlayBeam( aim.Position, new BaseCombatWeapon.ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface ), siphon: false );
			}
			return;
		}

		// Long-Range Power Shot: piercing rail/cannon shot penetrating enemies in a line + terminal explosion.
		const float longRange = 6000f;
		var forward = aim.Forward.Normal;
		var worldStop = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, aim.Position + forward * longRange )
			.UseHitPosition().IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ),
			Titanfall2TraceMask.Shot );
		var stopDist = worldStop.Hit ? aim.Position.Distance( worldStop.HitPosition ) : longRange;
		var endPos = aim.Position + forward * stopDist;

		// Check shield/wall interception first.
		var shieldTrace = worldStop;
		shieldTrace.StartPosition = aim.Position;
		shieldTrace.EndPosition = endPos;
		shieldTrace.HitPosition = endPos;
		shieldTrace.Direction = forward;
		var blocked = Titanfall2TitanDefense.TryInterceptHitscan( Scene, ref shieldTrace, source, GameObject,
			new Titanfall2DefenseShot( 250f, 2000f, 0f, Range: longRange, Refire: true ) );
		if ( blocked )
		{
			PlayBeam( aim.Position, new BaseCombatWeapon.ShotEffect( shieldTrace.EndPosition, true, shieldTrace.Normal, shieldTrace.GameObject, shieldTrace.Surface ), siphon: false );
			return;
		}

		// Pierce all hostile actors along the line up to worldStop.
		var pierced = new HashSet<GameObject>();
		foreach ( var hit in Scene.Trace.Ray( aim.Position, endPos ).Radius( 14f ).UseHitboxes().UseHitPosition()
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ).RunAll() )
		{
			if ( !hit.Hit || !hit.GameObject.IsValid() ) continue;
			var enemy = hit.GameObject.GetComponentInParent<Titanfall2Npc>();
			var player = hit.GameObject.GetComponentInParent<PlayerController>();
			var root = enemy?.GameObject ?? player?.GameObject;
			if ( !root.IsValid() || !pierced.Add( root ) ) continue;
			var hostile = enemy.IsValid() ? Actor.IsHostileToFaction( enemy.Faction )
				: player is not null && Actor.AttackPlayers && !Actor.Friendly;
			if ( !hostile ) continue;
			var dmg = enemy is { IsTitan: true } ? 2000f : 250f;
			var tags = new TagSet(); tags.Add( "bullet" );
			Titanfall2HostDamage.ShootBullet( hit, dmg, 120f, source, GameObject, tags );
		}

		// Terminal blast splash around impact point.
		foreach ( var enemy in Scene.GetAllComponents<Titanfall2Npc>().ToArray() )
		{
			if ( !enemy.IsValid() || enemy == Actor || enemy.Dead || !Actor.IsHostileToFaction( enemy.Faction ) ) continue;
			if ( pierced.Contains( enemy.GameObject ) ) continue;
			if ( enemy.WorldPosition.Distance( endPos ) > 180f ) continue;
			var info = new DamageInfo( enemy.IsTitan ? 500f : 120f, source, GameObject ) { Origin = endPos, Position = enemy.WorldPosition };
			info.Tags.Add( "blast" );
			Titanfall2HostDamage.Deal( (IDamageable)enemy, info );
		}

		PlayBeam( aim.Position, new BaseCombatWeapon.ShotEffect( endPos, worldStop.Hit, worldStop.Normal, worldStop.GameObject, worldStop.Surface ), siphon: false );
	}

	void LaunchOrdnance( Ray aim, GameObject attacker )
	{
		if ( Kit is Chassis.Ronin or Chassis.Scorch )
		{
			Titanfall2GroundWave.Launch( Actor, aim.Forward, attacker, flame: Kit == Chassis.Scorch );
			return;
		}
		if ( _volleyData is null ) return;
		if ( Kit == Chassis.Tone && (!_volleyTarget.IsValid() || _volleyTarget.Dead
			|| !Actor.IsHostileToFaction( _volleyTarget.Faction )) ) { _rockets = 0; return; }
		var target = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, aim.Position + aim.Forward.Normal * 5000 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade ).EndPosition;
		var origin = Eye;
		if ( Kit == Chassis.Tone ) target = _volleyTarget.WorldPosition + Vector3.Up * (_volleyTarget.BodyHeight * 0.55f);
		var settings = _volleyData.Projectile with { MountIdent = Actor.MountIdent };
		var projectile = Titanfall2Projectile.Launch( Scene, new Ray( origin, (target - origin).Normal ), settings,
			_volleyData.Damage, _volleyData.TitanDamage, attacker, GameObject, Actor.GameObject );
		if ( Kit == Chassis.Tone && projectile.IsValid() ) projectile.DesignateHomingTarget( _volleyTarget.GameObject );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayActivation( bool defense )
	{
		if ( !Actor.IsValid() ) return;
		var sound = AbilityData( defense )?.Sound ?? (defense ? Kit == Chassis.Monarch ? "Weapon_EnergySyphon_Lvl1_Fire_3P" : null
			: Kit switch
			{
				Chassis.Ion => "Weapon_ShoulderLaser_Fire_3P", Chassis.Monarch => "ShoulderRocket_Paint_Fire_3P",
				Chassis.Tone => "weapon_trackingrockets_fire_3P", Chassis.Northstar => "ShoulderRocket_Cluster_Fire_3P",
				Chassis.Ronin => "arcwave_fire_3p", _ => Actor.FireSound
			});
		Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent, sound, Eye );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBeam( Vector3 start, BaseCombatWeapon.ShotEffect shot, bool siphon )
	{
		var color = siphon ? new Color( 0.25f, 0.65f, 1f ) : new Color( 1f, 0.2f, 0.08f );
		var data = AbilityData( siphon );
		var tracer = data?.Tracer ?? (Kit == Chassis.Legion ? Actor.WeaponPresentation.TracerWorld : null);
		if ( !string.IsNullOrEmpty( tracer ) && (shot.HitPosition - start).LengthSquared > 1 )
		{
			var beam = new GameObject( Scene, false, tracer );
			beam.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			beam.WorldPosition = start;
			beam.WorldRotation = Rotation.LookAt( shot.HitPosition - start );
			var control = beam.AddComponent<Titanfall2ParticleControlPoints>();
			control.SetControlPoint( 0, start ); control.SetControlPoint( 1, shot.HitPosition );
			var retail = beam.AddComponent<Titanfall2PlasmaFlight>();
			retail.MountIdent = Actor.MountIdent; retail.EffectName = tracer;
			var cleanup = beam.AddComponent<TemporaryEffect>(); cleanup.DestroyAfterSeconds = 1.5f; cleanup.WaitForChildEffects = false;
			beam.Enabled = true;
		}
		var impact = data?.Projectile.ImpactTable ?? (Kit == Chassis.Legion ? Actor.WeaponPresentation.ImpactTable : null);
		Titanfall2CombatEffects.Impact( Scene, shot, Actor.MountIdent, impact ?? "default", data?.Projectile.SplashRadius ?? 0, color );
	}

	void UpdateGuardVisual()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( GuardRemaining <= 0 || Actor is not { Dead: false } )
		{
			if ( _guardVisual.IsValid() ) _guardVisual.Destroy();
			return;
		}
		if ( _guardVisual.IsValid() )
		{
			if ( Kit is Chassis.Ion or Chassis.Legion or Chassis.Scorch ) _guardVisual.WorldTransform = new Transform( DefenseCenter, DefenseFacing );
			return;
		}
		_guardVisual = new GameObject( Actor.GameObject, false, DefenseName );
		_guardVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_guardVisual.LocalPosition = new Vector3( Actor.BodyRadius + 8, 0, Actor.BodyHeight * 0.6f );
		if ( Kit is Chassis.Ion or Chassis.Legion or Chassis.Scorch ) _guardVisual.WorldTransform = new Transform( DefenseCenter, DefenseFacing );
		var color = Kit == Chassis.Scorch ? new Color( 1, 0.35f, 0.08f ) : new Color( 0.2f, 0.6f, 1 );
		if ( Kit is Chassis.Ion or Chassis.Scorch or Chassis.Legion )
		{
			var retail = _guardVisual.AddComponent<Titanfall2PlasmaFlight>();
			retail.MountIdent = Actor.MountIdent;
			retail.EffectName = Kit == Chassis.Ion ? "wpn_vortex_chargingCP_titan"
				: Kit == Chassis.Legion ? "P_titan_gun_shield_3P" : "P_wpn_HeatShield";
			_guardVisual.Enabled = true;
			return;
		}
		var points = new List<GameObject>();
		for ( var i = 0; i <= 24; i++ )
		{
			var angle = i * MathF.PI * 2 / 24;
			var point = new GameObject( _guardVisual, true, "Guard edge" );
			point.LocalPosition = new Vector3( 0, MathF.Cos( angle ) * Actor.BodyRadius * 1.4f, MathF.Sin( angle ) * Actor.BodyHeight * 0.35f );
			points.Add( point );
		}
		var line = _guardVisual.AddComponent<LineRenderer>();
		line.Points = points;
		line.Width = 3;
		line.Color = color * 3;
		line.Additive = true;
		line.Opaque = false;
		line.CastShadows = false;
		_guardVisual.Enabled = true;
	}

	protected override void OnDisabled()
	{
		if ( _guardVisual.IsValid() ) _guardVisual.Destroy();
		ReleaseRoninPhaseCamera();
		if ( Actor.IsValid() && Actor.Renderer.IsValid() ) Actor.Renderer.RenderOptions.Game = true;
		if ( Actor.IsValid() ) Titanfall2PhaseDimension.MarkRealspace( Actor.GameObject, inRealspace: true );
		if ( Networking.IsHost && !IsProxy ) { _rockets = 0; GuardRemaining = 0; ClearUtilityState(); ClearDefenseVolume(); ClearTrackers(); ClearIonInput(); ClearHoldDefenseInput(); }
	}

	protected override void OnDestroy() => ReleaseRoninPhaseCamera();
}

public sealed partial class Titanfall2Npc
{
	internal GameObject CombatTarget => _target;
	internal void RestoreTitanShield( float amount )
	{
		if ( Networking.IsHost && !IsProxy && !Dead && IsTitan ) Shield = MathF.Min( MaxShield, Shield + MathF.Max( 0, amount ) );
	}
	internal void InstantRearmReload()
	{
		if ( !Networking.IsHost || IsProxy || Dead || !IsTitan ) return;
		var core = GetComponent<Titanfall2TitanCore>();
		Rounds = core?.MagazineCapacity ?? Math.Max( 1, MagazineSize );
		Reloading = false;
		_nextShot = 0;
	}
}
