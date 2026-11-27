public sealed partial class Titanfall2TitanKit
{
	[Sync( SyncFlags.FromHost )] public float GunShieldHealth { get; private set; }
	[Sync( SyncFlags.FromHost )] public int CapturedAmmo { get; private set; }
	[Sync( SyncFlags.FromHost )] Vector3 DefenseCenter { get; set; }
	[Sync( SyncFlags.FromHost )] Rotation DefenseFacing { get; set; } = Rotation.Identity;
	readonly List<CapturedRound> _capturedRounds = [];
	bool _defenseLive;
	Titanfall2Npc _defenseActor;
	GameObject _defensePilot;
	readonly record struct CapturedRound( Titanfall2DefenseShot Shot, Titanfall2CapturedProjectile? Projectile,
		GameObject OriginalAttacker, GameObject OriginalWeapon );

	internal bool DefenseVolumeActive => Active && !IsProxy && _defenseLive && GuardRemaining > 0
		&& Actor.IsValid() && Actor == _defenseActor && Actor.TitanCanAct
		&& (Kit == Chassis.Ion || Kit == Chassis.Scorch || Kit == Chassis.Legion && GunShieldHealth > 0);
	internal float DefenseRadius => Kit == Chassis.Ion ? Titanfall2TitanDefense.VortexRadius
		: Kit == Chassis.Scorch ? Titanfall2TitanDefense.ThermalShieldRadius
		: Titanfall2TitanDefense.GunShieldRadius;
	internal Transform DefenseTransform
	{
		get
		{
			var aim = DefenseAim();
			var rotation = aim.Forward.LengthSquared > 0.5f ? Rotation.LookAt( aim.Forward ) : Actor.WorldRotation;
			if ( Kit == Chassis.Legion && Actor.TryGunShieldAttachment( out var gun ) )
				return new Transform( gun.Position, gun.Rotation * Rotation.FromRoll( 180 ) );
			// Ion's authored collection offset is relative to the owner's eyes. Scorch projects directly forward.
			var offset = Kit == Chassis.Ion ? new Vector3( 110, -28, -22 )
				: Kit == Chassis.Scorch ? new Vector3( 96, 0, -14 )
				: new Vector3( Actor.BodyRadius + 16, -Actor.BodyRadius * 0.35f, -Actor.BodyHeight * 0.15f );
			return new Transform( Eye + rotation * offset, rotation );
		}
	}

	Ray DefenseAim()
	{
		if ( Actor.Seat?.GetOccupant() is { } pilot && pilot.IsValid() ) return pilot.EyeTransform.ForwardRay;
		if ( Actor.CombatTarget is { } target && target.IsValid() )
			return new Ray( Eye, (target.GetBounds().Center - Eye).Normal );
		return new Ray( Eye, Actor.WorldRotation.Forward );
	}

	internal bool ShouldBlock( GameObject attacker )
	{
		if ( !attacker.IsValid() || !Actor.IsValid() || attacker == Actor.GameObject
			|| attacker == Actor.Seat?.GetOccupant()?.GameObject || Actor.GameObject.IsDescendant( attacker ) ) return false;
		if ( attacker.GetComponentInParent<Titanfall2Npc>() is { } npc ) return npc != Actor && Actor.IsHostileToFaction( npc.Faction );
		if ( attacker.GetComponentInParent<PlayerController>() is not null ) return Actor.IsHostileToFaction( Sandbox.Npcs.Factions.Player );
		return true;
	}

	void BeginDefenseVolume()
	{
		ClearDefenseVolume();
		_defenseLive = true;
		_defenseActor = Actor;
		_defensePilot = Actor.Seat?.GetOccupant()?.GameObject;
		GunShieldHealth = Kit == Chassis.Legion ? Titanfall2TitanDefense.GunShieldHealth : 0;
		GuardRemaining = Kit == Chassis.Legion ? Titanfall2TitanDefense.GunShieldDuration
			: Kit == Chassis.Scorch ? (_holdDefenseHeld && _holdDefensePilot.IsValid() ? 1f : 3.0f)
			: _ionVortexHeld && _ionPilot.IsValid() ? 1 : Titanfall2TitanDefense.VortexAiDuration;
		var pose = DefenseTransform;
		DefenseCenter = pose.Position;
		DefenseFacing = pose.Rotation;
	}

	void TickDefenseVolume()
	{
		if ( !_defenseLive ) return;
		if ( !Actor.IsValid() || Actor != _defenseActor || !Actor.TitanCanAct || Kit is not (Chassis.Ion or Chassis.Legion or Chassis.Scorch) )
		{
			ClearDefenseVolume();
			GuardRemaining = 0;
			return;
		}
		if ( _defensePilot != Actor.Seat?.GetOccupant()?.GameObject )
		{
			ReleaseDefenseVolume( Kit == Chassis.Ion, DefenseAim() );
			return;
		}
		if ( GuardRemaining <= 0 ) ReleaseDefenseVolume( Kit == Chassis.Ion, DefenseAim() );
		else
		{
			var pose = DefenseTransform;
			DefenseCenter = pose.Position;
			DefenseFacing = pose.Rotation;
		}
	}

	void ClearDefenseVolume()
	{
		_defenseLive = false;
		_defenseActor = null;
		_defensePilot = null;
		_capturedRounds.Clear();
		CapturedAmmo = 0;
		GunShieldHealth = 0;
	}

	void ReleaseDefenseVolume( bool refire, Ray aim )
	{
		if ( !Networking.IsHost || !_defenseLive ) return;
		var rounds = refire && Actor.IsValid() && Actor.TitanCanAct ? _capturedRounds.ToArray() : [];
		var transform = DefenseTransform;
		// Stop interception before refiring: released shots can never enter our own list.
		ClearDefenseVolume();
		GuardRemaining = 0;
		if ( rounds.Length == 0 ) return;
		var target = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim, 16384 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).UseHitboxes().WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot ).EndPosition;
		var clearance = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, transform.Position )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		var origin = clearance.StartedSolid ? Eye : clearance.Hit
			? clearance.EndPosition + (Eye - clearance.EndPosition).Normal : transform.Position;
		var direction = (target - origin).LengthSquared > 1 ? (target - origin).Normal : aim.Forward.Normal;
		var attacker = Actor.Seat?.GetOccupant()?.GameObject ?? Actor.GameObject;
		foreach ( var round in rounds )
		{
			if ( round.Projectile is { } projectile )
			{
				var settings = projectile.Settings with
				{
					Lifetime = Math.Clamp( projectile.Remaining, 0.1f, 12 ),
					FuseSeconds = projectile.Settings.FuseSeconds > 0 ? MathF.Max( 0.1f, projectile.Remaining ) : 0
				};
				Titanfall2Projectile.Launch( Scene, new Ray( origin, direction ), settings, projectile.Damage,
					projectile.TitanDamage, attacker, GameObject, Actor.GameObject, projectile.Profile, projectile.NpcFired );
				continue;
			}
			var shot = round.Shot;
			var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( origin, origin + direction * Math.Clamp( shot.Range, 128, 16384 ) )
				.IgnoreGameObjectHierarchy( Actor.GameObject ).UseHitboxes().UseHitPosition().WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
			if ( !Titanfall2TitanDefense.TryInterceptHitscan( Scene, ref hit, attacker, GameObject, shot ) )
			{
				var tags = new TagSet(); tags.Add( "bullet" ); tags.Add( "vortex_refire" );
				Titanfall2HostDamage.ShootBullet( hit, shot.At( origin.Distance( hit.HitPosition ), hit.GameObject?.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true } ),
					shot.Force, attacker, GameObject, tags );
			}
			PlayVortexRefire( origin, new BaseCombatWeapon.ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface ) );
		}
		PlayDefenseSound( "vortex_shield_throw_3P", origin );
	}

	internal void AbsorbHitscan( Titanfall2DefenseShot shot, float distance, GameObject attacker, GameObject weapon, Vector3 point )
	{
		if ( !Networking.IsHost || !DefenseVolumeActive ) return;
		if ( Kit == Chassis.Legion ) { DamageGunShield( shot.At( distance, true ), point ); return; }
		if ( Kit == Chassis.Scorch ) { PlayDefenseHit( point, false ); return; }
		if ( shot.Refire && _capturedRounds.Count < Titanfall2TitanDefense.VortexCapacity )
		{
			// Preserve the host damage at capture for both armor classes; do not trust
			// original client hit claims or apply the incoming distance falloff twice.
			var captured = shot with { Damage = shot.At( distance, false ), TitanDamage = shot.At( distance, true ), Profile = default, NpcFired = false };
			_capturedRounds.Add( new( captured, null, attacker, weapon ) );
			CapturedAmmo = _capturedRounds.Count;
		}
		PlayDefenseHit( point, false );
	}

	internal void AbsorbProjectile( Titanfall2CapturedProjectile projectile, float distance, GameObject attacker, GameObject weapon, Vector3 point )
	{
		if ( !Networking.IsHost || !DefenseVolumeActive ) return;
		if ( Kit == Chassis.Legion )
		{
			var direct = projectile.Profile.Evaluate( distance, true, projectile.NpcFired, projectile.Damage, projectile.TitanDamage );
			DamageGunShield( direct + MathF.Max( 0, projectile.Settings.SplashTitanDamage > 0 ? projectile.Settings.SplashTitanDamage : projectile.Settings.SplashDamage ), point );
			return;
		}
		if ( Kit == Chassis.Scorch ) { PlayDefenseHit( point, false ); return; }
		// A remote satchel has no valid detonation controller after ownership changes;
		// it is absorbed without a refire payload in this first-stage implementation.
		if ( !projectile.Settings.VortexAbsorbOnly && projectile.Settings.Behavior != Titanfall2ProjectileBehavior.RemoteCharge
			&& _capturedRounds.Count < Titanfall2TitanDefense.VortexCapacity )
		{
			_capturedRounds.Add( new( default, projectile, attacker, weapon ) );
			CapturedAmmo = _capturedRounds.Count;
		}
		PlayDefenseHit( point, false );
	}

	internal void DamageGunShield( float damage, Vector3 point )
	{
		if ( !Networking.IsHost || !DefenseVolumeActive || Kit != Chassis.Legion || !float.IsFinite( damage ) || damage <= 0 ) return;
		GunShieldHealth = MathF.Max( 0, GunShieldHealth - damage );
		var broken = GunShieldHealth <= 0;
		if ( broken ) { GuardRemaining = 0; ClearDefenseVolume(); }
		PlayDefenseHit( point, broken );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayDefenseSound( string name, Vector3 point ) => Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent, name, point );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayDefenseHit( Vector3 point, bool broken )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		var effect = new GameObject( Scene, false, broken ? "Gun shield broken" : "Shield interception" );
		effect.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		effect.WorldPosition = point;
		var retail = effect.AddComponent<Titanfall2PlasmaFlight>();
		retail.MountIdent = Actor.MountIdent;
		retail.EffectName = broken ? "P_xo_armor_break_CP" : "wpn_vortex_shield_impact_titan";
		var cleanup = effect.AddComponent<TemporaryEffect>(); cleanup.DestroyAfterSeconds = 1.2f; cleanup.WaitForChildEffects = false;
		effect.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent,
			broken ? "titan_energyshield_down" : "Vortex_Shield_AbsorbBulletSmall", point );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVortexRefire( Vector3 start, BaseCombatWeapon.ShotEffect shot )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		var beam = new GameObject( Scene, false, "Vortex refire" );
		beam.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		beam.WorldPosition = start;
		if ( (shot.HitPosition - start).LengthSquared > 1 ) beam.WorldRotation = Rotation.LookAt( shot.HitPosition - start );
		var points = beam.AddComponent<Titanfall2ParticleControlPoints>(); points.SetControlPoint( 0, start ); points.SetControlPoint( 1, shot.HitPosition );
		var retail = beam.AddComponent<Titanfall2PlasmaFlight>(); retail.MountIdent = Actor.MountIdent; retail.EffectName = "weapon_tracers_vortex";
		var cleanup = beam.AddComponent<TemporaryEffect>(); cleanup.DestroyAfterSeconds = 1.2f; cleanup.WaitForChildEffects = false;
		beam.Enabled = true;
		Titanfall2CombatEffects.Impact( Scene, shot, Actor.MountIdent, "default" );
	}
}
