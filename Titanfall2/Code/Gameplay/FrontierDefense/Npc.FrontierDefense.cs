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

/// <summary>Composed Frontier Defense AI ability controller (Arc Titan, Nuke Titan, Elite Monarch).</summary>
internal sealed class Titanfall2NpcFrontierController
{
	internal const float EmpTickSeconds = 0.3f;
	internal const float ArcChargeSeconds = 3.7f;
	internal const float NukeHarvesterRange = 400f;
	internal const float EliteShieldThreshold = 0.25f;
	const float SmokeTickSeconds = 0.25f;

	TimeUntil _clock;
	int _nukePulses;
	TimeUntil _empTick;
	TimeUntil _smokeTick;
	TimeUntil _smokeDeployPause;
	TimeUntil _arcLosGrace;
	TimeUntil _eliteCooldown;
	GameObject _arcChargeTarget;
	Vector3 _smokeOrigin;
	bool _eliteDefenseQueued;
	bool _elitePrefersCore;
	PointLight _abilityLight;

	internal void Start( Titanfall2Npc npc )
	{
		if ( npc.Role == Titanfall2Npc.FrontierRole.EliteMonarch )
			_elitePrefersCore = 3 + Random.Shared.NextSingle() * 2 <= 1 + Random.Shared.NextSingle() * 4;
		if ( npc.Role == Titanfall2Npc.FrontierRole.ArcTitan ) _empTick = Random.Shared.NextSingle() * EmpTickSeconds;
	}

	internal static GameObject FindHarvester( Titanfall2Npc npc ) => npc.Scene.GetAllComponents<Titanfall2Harvester>()
		.Where( x => !x.Destroyed && !npc.SameFaction( x.Faction ) )
		.MinBy( x => (x.WorldPosition - npc.WorldPosition).LengthSquared )?.GameObject;

	internal static bool IsRushingHarvester( Titanfall2Npc npc ) => npc.Role == Titanfall2Npc.FrontierRole.NukeTitan
		&& npc.CurrentTarget.IsValid()
		&& npc.CurrentTarget.GetComponent<Titanfall2Harvester>() is { Destroyed: false };

	// The retail FD script drives the Nuke Titan to the Harvester without pausing to fight and
	// ejects when doomed. Keep the telegraph alive even when the triggering shot overkills it.
	internal bool TickNuke( Titanfall2Npc npc )
	{
		if ( npc.Role != Titanfall2Npc.FrontierRole.NukeTitan ) return false;
		if ( !npc.IsNuking && IsRushingHarvester( npc )
			&& (npc.CurrentTarget.WorldPosition - npc.WorldPosition).LengthSquared <= NukeHarvesterRange * NukeHarvesterRange ) ArmNuke( npc );
		if ( !npc.IsNuking ) return false;
		if ( _clock <= 0 )
		{
			if ( npc.Ability == Titanfall2Npc.FrontierAbility.NukeArmed ) npc.BroadcastFrontierSound( "titan_nuclear_death_explode" );
			npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.NukeDetonating );
			var center = npc.WorldPosition + Vector3.Up * 200;
			// NPC payload 4 in sh_titan: three pulses 0.5s apart, scaled from ten player pulses.
			Pulse( npc, center, 350, npc.NukeBlastRadius, 75 * (10f / 3), 2500 * (10f / 3), "nuclearcore", includeHarvesters: true );
			_clock = 0.5f;
			if ( ++_nukePulses == 3 ) npc.ForceKillTitanInternal( npc.LastVerifiedAttacker, npc.LastVerifiedWeapon );
		}
		return true;
	}

	internal void ArmNuke( Titanfall2Npc npc )
	{
		if ( npc.IsNuking || npc.Dead ) return;
		npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.NukeArmed );
		_clock = Math.Clamp( npc.NukeWindup, 0.1f, 10f );
		npc.BroadcastFrontierSound( "titan_nuclear_death_charge" );
		npc.StopForAbility();
	}

	internal void TickArcAura( Titanfall2Npc npc )
	{
		if ( npc.Role != Titanfall2Npc.FrontierRole.ArcTitan || _empTick > 0 ) return;
		_empTick = EmpTickSeconds;
		Pulse( npc, npc.WorldPosition + Vector3.Up * (npc.BodyHeight * 0.32f), npc.EmpInnerRadius, npc.EmpOuterRadius, 40, 150, "emp", includeHarvesters: true );
	}

	internal bool TickArcCannon( Titanfall2Npc npc )
	{
		if ( npc.Role != Titanfall2Npc.FrontierRole.ArcTitan ) return false;
		var target = npc.CurrentTarget;
		var inRange = npc.CanTarget( target ) && (target.WorldPosition - npc.WorldPosition).LengthSquared <= npc.AttackRange * npc.AttackRange;
		var hasLos = inRange && npc.HasLineOfSight( target );
		if ( hasLos ) _arcLosGrace = 0.35f;
		var valid = inRange && (hasLos || (npc.Ability == Titanfall2Npc.FrontierAbility.ArcCharge && _arcLosGrace > 0));
		if ( !valid || (npc.Ability == Titanfall2Npc.FrontierAbility.ArcCharge && target != _arcChargeTarget) )
		{
			npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.None );
			_arcChargeTarget = null;
			return true;
		}
		if ( npc.Ability != Titanfall2Npc.FrontierAbility.ArcCharge )
		{
			if ( !npc.NextShotReady ) return true;
			_arcChargeTarget = target;
			npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.ArcCharge );
			_clock = ArcChargeSeconds;
			_arcLosGrace = 0.35f;
			return true;
		}
		if ( _clock > 0 ) return true;
		if ( !hasLos ) return true;

		var muzzle = npc.GunObject.IsValid() ? npc.GunObject.GetComponent<BaseWeaponModel>()?.GetMuzzleTransform().Position ?? npc.EyePositionInternal : npc.EyePositionInternal;
		var toTarget = Titanfall2Npc.AimAtInternal( target ) - muzzle;
		var hit = Titanfall2CollisionQueries.Run( npc.Scene.Trace.Ray( muzzle, muzzle + toTarget.Normal * npc.AttackRange ).UseHitboxes().UseHitPosition()
			.IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		var victim = hit.GameObject?.GetComponentInParent<Titanfall2Npc>();
		if ( victim is null || !npc.SameFaction( victim.Faction ) )
		{
			var tags = new TagSet();
			tags.Add( "emp" );
			tags.Add( "arccannon" );
			if ( !Titanfall2TitanDefense.TryInterceptHitscan( npc.Scene, ref hit, npc.GameObject, npc.GameObject,
				new Titanfall2DefenseShot( 80, 700, 0, default, true, npc.AttackRange, Refire: false ) ) )
				Titanfall2HostDamage.ShootBullet( hit, victim is { IsTitan: true } ? 700 : 80, 0, npc.GameObject, npc.GameObject, tags );
		}
		npc.PlayShotSoundInternal( muzzle );
		npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.None );
		_arcChargeTarget = null;
		npc.SetNextShotDelay( 1 );
		return true;
	}

	internal bool TickEliteDefense( Titanfall2Npc npc )
	{
		if ( npc.Role != Titanfall2Npc.FrontierRole.EliteMonarch ) return false;
		if ( npc.Ability == Titanfall2Npc.FrontierAbility.ElectricSmoke )
		{
			if ( _clock <= 0 ) FinishEliteDefense( npc );
			else if ( _smokeTick <= 0 )
			{
				_smokeTick = SmokeTickSeconds;
				Pulse( npc, _smokeOrigin, 320, 375, 100 * SmokeTickSeconds, 1500 * SmokeTickSeconds, "electricsmoke" );
			}
			// Only pause movement briefly while popping smoke; keep fighting and strafing afterward.
			return _smokeDeployPause > 0;
		}
		if ( npc.Ability == Titanfall2Npc.FrontierAbility.ShieldCore )
		{
			if ( _clock <= 0 ) FinishEliteDefense( npc );
			return true;
		}
		if ( !_eliteDefenseQueued && _eliteCooldown <= 0 && npc.MaxShield > 0 && npc.Shield <= npc.MaxShield * EliteShieldThreshold )
		{
			_eliteDefenseQueued = true;
			_clock = 1 + Random.Shared.NextSingle() * 2;
		}
		if ( !_eliteDefenseQueued || _clock > 0 ) return false;
		_eliteDefenseQueued = false;
		var both = Random.Shared.NextSingle() < 0.25f;
		if ( _elitePrefersCore || both )
		{
			npc.SetShieldHost( npc.MaxShield );
			npc.BroadcastFrontierSound( "Titan_Monarch_Smart_Core_Activated_3P" );
		}
		if ( !_elitePrefersCore || both )
		{
			var ahead = npc.WorldPosition + npc.WorldRotation.Forward * 240;
			var blocked = npc.Scene.Trace.Ray( npc.EyePositionInternal, ahead + Vector3.Up * 20 ).IgnoreGameObjectHierarchy( npc.GameObject ).Run().Hit;
			_smokeOrigin = (blocked ? npc.WorldPosition : ahead) + Vector3.Up * 100;
			npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.ElectricSmoke );
			_smokeTick = 1;
			_smokeDeployPause = 0.6f;
			_clock = 5;
		}
		else
		{
			npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.ShieldCore );
			_clock = 2;
		}
		npc.StopForAbility();
		return true;
	}

	void FinishEliteDefense( Titanfall2Npc npc )
	{
		npc.SetAbilityHost( Titanfall2Npc.FrontierAbility.None );
		_eliteCooldown = 3 + Random.Shared.NextSingle() * 4;
		npc.ResetScanTimer();
	}

	internal static float PulseFalloff( float distance, float inner, float outer )
		=> distance >= outer || outer <= 0 ? 0 : distance <= inner ? 1 : (outer - distance) / MathF.Max( 0.001f, outer - inner );

	static void Pulse( Titanfall2Npc npc, Vector3 center, float inner, float outer, float pilotDamage, float titanDamage, string tag, bool includeHarvesters = false )
	{
		outer = Math.Clamp( outer, 0, 1024 );
		inner = Math.Clamp( inner, 0, outer );
		if ( outer <= 0 ) return;
		var hits = new Dictionary<Component, (Vector3 Point, float Distance, bool Heavy)>();
		foreach ( var obj in npc.Scene.FindInPhysics( new Sphere( center, outer ) ) )
		{
			var otherNpc = obj.GetComponentInParent<Titanfall2Npc>();
			var objective = obj.GetComponentInParent<Titanfall2Harvester>();
			if ( otherNpc == npc || otherNpc is { Dead: true } || (otherNpc.IsValid() && npc.SameFaction( otherNpc.Faction ))
				|| (objective.IsValid() && (!includeHarvesters || objective.Destroyed || npc.SameFaction( objective.Faction ))) ) continue;
			if ( otherNpc is null && obj.GetComponentInParent<PlayerController>()?.GetComponentInParent<Titanfall2TitanSeat>()?.IsOccupied == true ) continue;
			var receiver = (Component)otherNpc ?? (Component)objective ?? obj.GetComponentInParent<Component.IDamageable>() as Component;
			if ( !receiver.IsValid() || receiver.GameObject == npc.GameObject ) continue;
			foreach ( var collider in obj.GetComponents<Collider>() )
			{
				if ( collider.IsTrigger ) continue;
				var point = collider.FindClosestPoint( center );
				var distance = (point - center).Length;
				if ( distance >= outer || (hits.TryGetValue( receiver, out var prev ) && distance >= prev.Distance) ) continue;
				var trace = Titanfall2CollisionQueries.Run( npc.Scene.Trace.Ray( center, point ).IgnoreGameObjectHierarchy( npc.GameObject )
					.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
				if ( trace.StartedSolid || (trace.Hit && trace.GameObject != receiver.GameObject && trace.GameObject?.IsDescendant( receiver.GameObject ) != true) ) continue;
				hits[receiver] = (point, distance, otherNpc?.IsTitan == true || objective.IsValid());
			}
		}
		foreach ( var (receiver, hit) in hits )
		{
			if ( !receiver.IsValid() || receiver is not Component.IDamageable damageable ) continue;
			var damage = new DamageInfo( (hit.Heavy ? titanDamage : pilotDamage) * PulseFalloff( hit.Distance, inner, outer ), npc.GameObject, npc.GameObject )
				{ Origin = center, Position = hit.Point };
			damage.Tags.Add( tag );
			if ( tag == "nuclearcore" ) damage.Tags.Add( "explosion" );
			Titanfall2HostDamage.Deal( damageable, damage );
		}
	}

	// A local warning that follows replicated ability state. This is not a port of the original RPAK effects.
	internal void UpdateAbilityLight( Titanfall2Npc npc )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || npc.Role == Titanfall2Npc.FrontierRole.None ) return;
		var lit = !npc.Dead && (npc.Ability != Titanfall2Npc.FrontierAbility.None || npc.Role == Titanfall2Npc.FrontierRole.ArcTitan);
		if ( !lit )
		{
			if ( _abilityLight.IsValid() ) _abilityLight.GameObject.Enabled = false;
			return;
		}
		if ( !_abilityLight.IsValid() )
		{
			var light = new GameObject( npc.GameObject, false, "Frontier ability tell" );
			light.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			light.LocalPosition = Vector3.Up * (npc.BodyHeight * 0.55f);
			_abilityLight = light.AddComponent<PointLight>();
		}
		_abilityLight.LightColor = npc.IsNuking ? new Color( 1, 0.24f, 0.08f ) : new Color( 0.16f, 0.55f, 1 );
		_abilityLight.Radius = npc.IsNuking ? 180 + 90 * MathF.Sin( Time.Now * 17 ) : npc.Ability == Titanfall2Npc.FrontierAbility.None ? 135 : 240;
		_abilityLight.GameObject.Enabled = true;
	}
}
