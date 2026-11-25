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
	[Sync( SyncFlags.FromHost )] public float OffenseRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float DefenseRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float GuardRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float DashRemaining { get; private set; }
	public string OffenseName => Kit switch
	{
		Chassis.Ion => "Laser Shot", Chassis.Legion => "Power Shot", Chassis.Tone => "Tracker Rockets",
		Chassis.Scorch => "Firewall", Chassis.Northstar => "Cluster Missile", Chassis.Ronin => "Arc Wave", _ => "Rocket Salvo"
	};
	public string DefenseName => Kit switch
	{
		Chassis.Ion => "Vortex Guard", Chassis.Legion => "Gun Shield", Chassis.Tone => "Particle Guard",
		Chassis.Scorch => "Thermal Shield", Chassis.Northstar => "Electric Smoke", Chassis.Ronin => "Sword Block", _ => "Energy Siphon"
	};
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
		if ( !Networking.IsHost || IsProxy ) return;
		if ( !Ready ) { _rockets = 0; GuardRemaining = 0; ClearDefenseVolume(); ClearTrackers(); ClearIonInput(); return; }
		TickTrackers();
		OffenseRemaining = MathF.Max( 0, OffenseRemaining - Time.Delta );
		DefenseRemaining = MathF.Max( 0, DefenseRemaining - Time.Delta );
		if ( Kit != Chassis.Ion || !_ionVortexHeld || !_ionPilot.IsValid() ) GuardRemaining = MathF.Max( 0, GuardRemaining - Time.Delta );
		TickIonEnergy();
		TickDefenseVolume();
		DashRemaining = MathF.Max( 0, DashRemaining - Time.Delta );
		if ( _rockets > 0 && _volleyStep <= 0 )
		{
			_volleyStep = _volleyData?.Interval ?? 0.12f;
			_rockets--;
			LaunchOrdnance( _volleyAim, _volleyAttacker );
		}
		if ( GuardRemaining > 0 && Kit == Chassis.Scorch && _heatStep <= 0 )
		{
			_heatStep = 0.25f;
			foreach ( var enemy in Scene.GetAllComponents<Titanfall2Npc>().Where( npc => npc != Actor && !npc.Dead && Actor.IsHostileToFaction( npc.Faction ) ).ToArray() )
			{
				var delta = enemy.WorldPosition - Actor.WorldPosition;
				if ( delta.Length > 260 || Vector3.Dot( Actor.WorldRotation.Forward, delta.Normal ) < 0.4f || !Actor.HasLineOfSight( enemy.GameObject ) ) continue;
				var damage = new DamageInfo( enemy.IsTitan ? 200 : 30, Actor.GameObject, GameObject ) { Origin = Eye, Position = enemy.WorldPosition };
				damage.Tags.Add( "burn" );
				Titanfall2HostDamage.Deal( (IDamageable)enemy, damage );
			}
		}
		if ( !UseAi || Actor.Seat is { IsOccupied: true } or { IsEmbarking: true } || _aiStep > 0 ) return;
		_aiStep = 0.35f;
		var target = Actor.CombatTarget;
		if ( !target.IsValid() || !Actor.HasLineOfSight( target ) ) return;
		var aim = new Ray( Eye, (target.GetBounds().Center - Eye).Normal );
		if ( Actor.Health < Actor.MaxHealth * 0.7f || Actor.Shield < Actor.MaxShield * 0.5f ) TryDefense( aim, Actor.GameObject );
		if ( target.WorldPosition.Distance( Actor.WorldPosition ) <= Actor.AttackRange ) TryOffense( aim, Actor.GameObject );
	}

	Vector3 Eye => Actor.WorldPosition + Vector3.Up * (Actor.BodyHeight * 0.79f);
	bool ValidAim( Ray aim ) => aim.Position.IsFinite && aim.Forward.IsFinite && aim.Forward.LengthSquared is > 0.25f and < 4f
		&& aim.Position.Distance( Eye ) < 180;

	/// <summary>Activate ordnance from the cockpit or a host controller; returns false during cooldown.</summary>
	public bool TryOffense( Ray aim, GameObject attacker )
	{
		LastOffenseFailure = null;
		if ( !Ready || !ValidAim( aim ) ) { LastOffenseFailure = "机体无法使用攻击技能"; return false; }
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
		if ( Kit is Chassis.Ion or Chassis.Legion ) Beam( aim, attacker, siphon: false, data );
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
		if ( DefenseRemaining > 0 ) return false;
		DefenseRemaining = Math.Clamp( DefenseCooldown, 1, 120 );
		PlayActivation( true );
		if ( Kit == Chassis.Monarch ) Beam( aim, attacker, siphon: true, AbilityData( defense: true ) );
		else if ( Kit == Chassis.Northstar )
		{
			var smoke = new Titanfall2ProjectileSettings { Behavior = Titanfall2ProjectileBehavior.ElectricSmoke, SplashRadius = 280, Tint = new Color( 0.3f, 0.6f, 1f ) };
			Titanfall2AreaHazard.Spawn( Scene, Actor.WorldPosition + Vector3.Up * 40, smoke, 10, 100, Actor.GameObject, GameObject, Actor.GameObject );
		}
		else if ( Kit is Chassis.Ion or Chassis.Legion ) BeginDefenseVolume();
		else GuardRemaining = Kit == Chassis.Ronin ? 3 : 5;
		return true;
	}

	public bool TryDash() 
	{
		if ( !Ready || DashRemaining > 0 || Actor.Seat is not { IsOccupied: true } ) return false;
		DashRemaining = DashRechargeSeconds;
		return true;
	}

	public float DashRechargeSeconds => Kit == Chassis.Ronin && Actor.IsValid()
		&& Actor.GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true } ? 1
		: Kit is Chassis.Legion or Chassis.Scorch ? 8 : 5;

	internal float GuardDamage( in DamageInfo damage )
	{
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
				Actor.RestoreTitanShield( enemy is { IsTitan: true } ? 750 : 250 );
		}
		PlayBeam( aim.Position, new BaseCombatWeapon.ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface ), siphon );
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
			if ( Kit is Chassis.Ion or Chassis.Legion ) _guardVisual.WorldTransform = new Transform( DefenseCenter, DefenseFacing );
			return;
		}
		_guardVisual = new GameObject( Actor.GameObject, false, DefenseName );
		_guardVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_guardVisual.LocalPosition = new Vector3( Actor.BodyRadius + 8, 0, Actor.BodyHeight * 0.6f );
		if ( Kit is Chassis.Ion or Chassis.Legion ) _guardVisual.WorldTransform = new Transform( DefenseCenter, DefenseFacing );
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
		if ( Networking.IsHost && !IsProxy ) { _rockets = 0; GuardRemaining = 0; ClearDefenseVolume(); ClearTrackers(); ClearIonInput(); }
	}
}

public sealed partial class Titanfall2Npc
{
	internal GameObject CombatTarget => _target;
	internal void RestoreTitanShield( float amount )
	{
		if ( Networking.IsHost && !IsProxy && !Dead && IsTitan ) Shield = MathF.Min( MaxShield, Shield + MathF.Max( 0, amount ) );
	}
}
