/// <summary>A faction-owned target for Frontier Defense actors. Add a solid collider to the objective object.</summary>
[Title( "Titanfall 2 Harvester" ), Group( "Titanfall 2" )]
public sealed class Titanfall2Harvester : Component, Component.IDamageable
{
	[Property] public string Faction { get; set; } = "militia";
	[Property] public float MaxHealth { get; set; } = 10000;
	[Property] public float MaxShield { get; set; } = 5000;
	[Sync( SyncFlags.FromHost )] public float Shield { get; private set; }
	TimeUntil _regen;
	[Sync( SyncFlags.FromHost )] public float Health { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Destroyed { get; private set; }

	protected override void OnStart()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		Health = MathF.Max( 1, MaxHealth );
		Shield = MathF.Max( 0, MaxShield );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && !IsProxy && !Destroyed && _regen <= 0 && Shield < MaxShield )
			Shield = MathF.Min( MaxShield, Shield + MaxShield * 0.1f * Time.Delta );
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || IsProxy || Destroyed || !float.IsFinite( damage.Damage ) || damage.Damage <= 0 ) return;
		if ( damage.Attacker?.GetComponentInParent<Titanfall2Npc>() is not { } actor
			|| string.Equals( actor.Faction, Faction, StringComparison.OrdinalIgnoreCase ) ) return;
		_regen = 10;
		var amount = damage.Tags.Has( "nuclearcore" ) ? damage.Damage * 0.25f
			: Shield > 0 && damage.Tags.Has( "arccannon" ) ? 1000
			: Shield > 0 && damage.Tags.Has( "emp" ) ? 25 : damage.Damage;
		var absorbed = MathF.Min( Shield, amount );
		Shield -= absorbed;
		Health = MathF.Max( 0, Health - (amount - absorbed) );
		Destroyed = Health <= 0;
	}
}

public sealed partial class Titanfall2Npc
{
	public enum FrontierRole { None, ArcTitan, NukeTitan, EliteMonarch }
	public enum FrontierAbility { None, ArcCharge, NukeArmed, NukeDetonating, ShieldCore, ElectricSmoke }

	[Property] public FrontierRole Role { get; set; }
	[Property] public float EmpInnerRadius { get; set; } = 150;
	[Property] public float EmpOuterRadius { get; set; } = 330;
	[Property] public float NukeWindup { get; set; } = 3.3f;
	[Property] public float NukeBlastRadius { get; set; } = 750;
	[Sync( SyncFlags.FromHost )] public FrontierAbility Ability { get; private set; }

	internal const float EmpTickSeconds = 0.3f;
	internal const float ArcChargeSeconds = 3.7f;
	internal const float NukeHarvesterRange = 400f;
	internal const float EliteShieldThreshold = 0.25f;
	const float SmokeTickSeconds = 0.25f;

	TimeUntil _frontierClock;
	int _nukePulses;
	bool IsNuking => Ability is FrontierAbility.NukeArmed or FrontierAbility.NukeDetonating;
	TimeUntil _empTick;
	TimeUntil _smokeTick;
	TimeUntil _eliteCooldown;
	GameObject _arcChargeTarget;
	Vector3 _smokeOrigin;
	bool _eliteDefenseQueued;
	bool _elitePrefersCore;
	PointLight _abilityLight;

	void StartFrontier()
	{
		if ( Role == FrontierRole.EliteMonarch )
			_elitePrefersCore = 3 + Random.Shared.NextSingle() * 2 <= 1 + Random.Shared.NextSingle() * 4;
		if ( Role == FrontierRole.ArcTitan ) _empTick = Random.Shared.NextSingle() * EmpTickSeconds;
	}

	GameObject FindHarvester() => Scene.GetAllComponents<Titanfall2Harvester>()
		.Where( x => !x.Destroyed && !SameFaction( x.Faction ) )
		.MinBy( x => (x.WorldPosition - WorldPosition).LengthSquared )?.GameObject;

	bool SameFaction( string faction ) => !IsHostileToFaction( faction );

	bool IsRushingHarvester => Role == FrontierRole.NukeTitan && _target.IsValid()
		&& _target.GetComponent<Titanfall2Harvester>() is { Destroyed: false };

	// The retail FD script drives the Nuke Titan to the Harvester without pausing to fight and
	// ejects when doomed. Keep the telegraph alive even when the triggering shot overkills it.
	bool TickNuke()
	{
		if ( Role != FrontierRole.NukeTitan ) return false;
		if ( !IsNuking && IsRushingHarvester
			&& (_target.WorldPosition - WorldPosition).LengthSquared <= NukeHarvesterRange * NukeHarvesterRange ) ArmNuke();
		if ( !IsNuking ) return false;
		if ( _frontierClock <= 0 )
		{
			if ( Ability == FrontierAbility.NukeArmed ) PlayFrontierSound( "titan_nuclear_death_explode" );
			Ability = FrontierAbility.NukeDetonating;
			var center = WorldPosition + Vector3.Up * 200;
			// NPC payload 4 in sh_titan: three pulses 0.5s apart, scaled from ten player pulses.
			Pulse( center, 350, NukeBlastRadius, 75 * (10f / 3), 2500 * (10f / 3), "nuclearcore", includeHarvesters: true );
			_frontierClock = 0.5f;
			if ( ++_nukePulses == 3 ) KillActor();
		}
		return true;
	}

	void ArmNuke()
	{
		if ( IsNuking || Dead ) return;
		Ability = FrontierAbility.NukeArmed;
		_frontierClock = Math.Clamp( NukeWindup, 0.1f, 10f );
		PlayFrontierSound( "titan_nuclear_death_charge" );
		StopForAbility();
	}

	void StopForAbility()
	{
		Agent?.Stop();
		_chasing = false;
		Moving = false;
	}

	void TickArcAura()
	{
		if ( Role != FrontierRole.ArcTitan || _empTick > 0 ) return;
		_empTick = EmpTickSeconds;
		Pulse( WorldPosition + Vector3.Up * (BodyHeight * 0.32f), EmpInnerRadius, EmpOuterRadius, 40, 150, "emp", includeHarvesters: true );
	}

	bool TickArcCannon()
	{
		if ( Role != FrontierRole.ArcTitan ) return false;
		var valid = CanTarget( _target ) && (_target.WorldPosition - WorldPosition).LengthSquared <= AttackRange * AttackRange
			&& HasLineOfSight( _target );
		if ( !valid || (Ability == FrontierAbility.ArcCharge && _target != _arcChargeTarget) )
		{
			Ability = FrontierAbility.None;
			_arcChargeTarget = null;
			return true;
		}
		if ( Ability != FrontierAbility.ArcCharge )
		{
			if ( _nextShot > 0 ) return true;
			_arcChargeTarget = _target;
			Ability = FrontierAbility.ArcCharge;
			_frontierClock = ArcChargeSeconds;
			return true;
		}
		if ( _frontierClock > 0 ) return true;

		var muzzle = _gun.IsValid() ? _gun.GetComponent<BaseWeaponModel>()?.GetMuzzleTransform().Position ?? Eye : Eye;
		var toTarget = AimAt( _target ) - muzzle;
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( muzzle, muzzle + toTarget.Normal * AttackRange ).UseHitboxes().UseHitPosition()
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		var victim = hit.GameObject?.GetComponentInParent<Titanfall2Npc>();
		if ( victim is null || !SameFaction( victim.Faction ) )
		{
			var tags = new TagSet();
			tags.Add( "emp" );
			tags.Add( "arccannon" );
			BaseCombatWeapon.ShootBullet( hit, victim is { IsTitan: true } ? 700 : 80, 0, GameObject, GameObject, tags );
		}
		PlayShotSound( muzzle );
		Ability = FrontierAbility.None;
		_arcChargeTarget = null;
		_nextShot = 1;
		return true;
	}

	bool TickEliteDefense()
	{
		if ( Role != FrontierRole.EliteMonarch ) return false;
		if ( Ability == FrontierAbility.ElectricSmoke )
		{
			if ( _frontierClock <= 0 ) FinishEliteDefense();
			else if ( _smokeTick <= 0 )
			{
				_smokeTick = SmokeTickSeconds;
				Pulse( _smokeOrigin, 320, 375, 100 * SmokeTickSeconds, 1500 * SmokeTickSeconds, "electricsmoke" );
			}
			return true;
		}
		if ( Ability == FrontierAbility.ShieldCore )
		{
			if ( _frontierClock <= 0 ) FinishEliteDefense();
			return true;
		}
		if ( !_eliteDefenseQueued && _eliteCooldown <= 0 && MaxShield > 0 && Shield <= MaxShield * EliteShieldThreshold )
		{
			_eliteDefenseQueued = true;
			_frontierClock = 1 + Random.Shared.NextSingle() * 2;
		}
		if ( !_eliteDefenseQueued || _frontierClock > 0 ) return false;
		_eliteDefenseQueued = false;
		var both = Random.Shared.NextSingle() < 0.2f;
		if ( _elitePrefersCore || both )
		{
			Shield = MaxShield;
			PlayFrontierSound( "Titan_Monarch_Smart_Core_Activated_3P" );
		}
		if ( !_elitePrefersCore || both )
		{
			var ahead = WorldPosition + WorldRotation.Forward * 240;
			var blocked = Scene.Trace.Ray( Eye, ahead + Vector3.Up * 20 ).IgnoreGameObjectHierarchy( GameObject ).Run().Hit;
			_smokeOrigin = (blocked ? WorldPosition : ahead) + Vector3.Up * 100;
			Ability = FrontierAbility.ElectricSmoke;
			_smokeTick = 1;
			_frontierClock = 5;
		}
		else
		{
			Ability = FrontierAbility.ShieldCore;
			_frontierClock = 2;
		}
		StopForAbility();
		return true;
	}

	void FinishEliteDefense()
	{
		Ability = FrontierAbility.None;
		_eliteCooldown = 3 + Random.Shared.NextSingle() * 4;
		_scan = 0;
	}

	internal static float PulseFalloff( float distance, float inner, float outer )
		=> distance >= outer || outer <= 0 ? 0 : distance <= inner ? 1 : (outer - distance) / MathF.Max( 0.001f, outer - inner );

	void Pulse( Vector3 center, float inner, float outer, float pilotDamage, float titanDamage, string tag, bool includeHarvesters = false )
	{
		outer = Math.Clamp( outer, 0, 1024 );
		inner = Math.Clamp( inner, 0, outer );
		if ( outer <= 0 ) return;
		var hits = new Dictionary<Component, (Vector3 Point, float Distance, bool Heavy)>();
		foreach ( var obj in Scene.FindInPhysics( new Sphere( center, outer ) ) )
		{
			var npc = obj.GetComponentInParent<Titanfall2Npc>();
			var objective = obj.GetComponentInParent<Titanfall2Harvester>();
			if ( npc == this || npc is { Dead: true } || (npc.IsValid() && SameFaction( npc.Faction ))
				|| (objective.IsValid() && (!includeHarvesters || objective.Destroyed || SameFaction( objective.Faction ))) ) continue;
			if ( npc is null && obj.GetComponentInParent<PlayerController>()?.GetComponentInParent<Titanfall2TitanSeat>()?.IsOccupied == true ) continue;
			var receiver = (Component)npc ?? (Component)objective ?? obj.GetComponentInParent<IDamageable>() as Component;
			if ( !receiver.IsValid() || receiver.GameObject == GameObject ) continue;
			foreach ( var collider in obj.GetComponents<Collider>() )
			{
				if ( collider.IsTrigger ) continue;
				var point = collider.FindClosestPoint( center );
				var distance = (point - center).Length;
				if ( distance >= outer || (hits.TryGetValue( receiver, out var prev ) && distance >= prev.Distance) ) continue;
				var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( center, point ).IgnoreGameObjectHierarchy( GameObject )
					.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
				if ( trace.StartedSolid || (trace.Hit && trace.GameObject != receiver.GameObject && trace.GameObject?.IsDescendant( receiver.GameObject ) != true) ) continue;
				hits[receiver] = (point, distance, npc?.IsTitan == true || objective.IsValid());
			}
		}
		foreach ( var (receiver, hit) in hits )
		{
			if ( !receiver.IsValid() || receiver is not IDamageable damageable ) continue;
			var damage = new DamageInfo( (hit.Heavy ? titanDamage : pilotDamage) * PulseFalloff( hit.Distance, inner, outer ), GameObject, GameObject )
				{ Origin = center, Position = hit.Point };
			damage.Tags.Add( tag );
			if ( tag == "nuclearcore" ) damage.Tags.Add( "explosion" );
			damageable.OnDamage( damage );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayFrontierSound( string sound ) => Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, sound, WorldPosition );

	// A local warning that follows replicated ability state. This is not a port of the original RPAK effects.
	void UpdateAbilityLight()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || Role == FrontierRole.None ) return;
		var lit = !Dead && (Ability != FrontierAbility.None || Role == FrontierRole.ArcTitan);
		if ( !lit )
		{
			if ( _abilityLight.IsValid() ) _abilityLight.GameObject.Enabled = false;
			return;
		}
		if ( !_abilityLight.IsValid() )
		{
			var light = new GameObject( GameObject, false, "Frontier ability tell" );
			light.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			light.LocalPosition = Vector3.Up * (BodyHeight * 0.55f);
			_abilityLight = light.AddComponent<PointLight>();
		}
		_abilityLight.LightColor = IsNuking ? new Color( 1, 0.24f, 0.08f ) : new Color( 0.16f, 0.55f, 1 );
		_abilityLight.Radius = IsNuking ? 180 + 90 * MathF.Sin( Time.Now * 17 ) : Ability == FrontierAbility.None ? 135 : 240;
		_abilityLight.GameObject.Enabled = true;
	}
}
