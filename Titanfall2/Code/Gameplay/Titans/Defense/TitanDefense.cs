/// <summary>Host-owned shot data. None of these damage values are accepted from a client claim.</summary>
internal readonly record struct Titanfall2DefenseShot( float Damage, float TitanDamage, float Force,
	Titanfall2DamageProfile Profile = default, bool NpcFired = false, float Range = 16384, bool Refire = true )
{
	internal float At( float distance, bool heavy ) => Profile.Evaluate( distance, heavy, NpcFired, Damage, TitanDamage );
}

internal readonly record struct Titanfall2CapturedProjectile( Titanfall2ProjectileSettings Settings,
	float Damage, float TitanDamage, Titanfall2DamageProfile Profile, bool NpcFired, float Remaining );

/// <summary>
/// Analytic shield volumes queried before a host's world hit. These are ballistic volumes,
/// not actor damage multipliers or movement colliders. Explosions use a separate entry point.
/// </summary>
internal static class Titanfall2TitanDefense
{
	internal const float VortexRadius = 150;
	internal const float GunShieldRadius = 105;
	internal const float ThermalShieldRadius = 125;
	internal const float GunShieldHealth = 2500;
	internal const float GunShieldDuration = 6;
	internal const float VortexAiDuration = 2.5f;
	internal const int VortexCapacity = 4500;

	internal static bool TryInterceptHitscan( Scene scene, ref SceneTraceResult hit, GameObject attacker,
		GameObject weapon, Titanfall2DefenseShot shot )
	{
		if ( !Networking.IsHost || !scene.IsValid() || hit.StartedSolid || !ValidSource( scene, attacker, weapon ) ) return false;
		var hasShield = TryFind( scene, hit.StartPosition, hit.EndPosition, 0, attacker,
			out var shield, out var shieldPoint, out var shieldNormal, out var shieldFrac );
		var hasWall = TryFindWall( scene, hit.StartPosition, hit.EndPosition, 0, attacker,
			out var wall, out var wallPoint, out var wallNormal, out var wallFrac );
		if ( !hasShield && !hasWall ) return false;

		if ( hasWall && (!hasShield || wallFrac <= shieldFrac) )
		{
			var wallDist = hit.StartPosition.Distance( wallPoint );
			wall.ApplyDamage( shot.At( wallDist, true ), wallPoint );
			hit = new SceneTraceResult
			{
				Scene = scene, Hit = true, StartPosition = hit.StartPosition, EndPosition = wallPoint, HitPosition = wallPoint,
				Normal = wallNormal, Direction = hit.Direction, GameObject = wall.GameObject, Component = wall,
				Fraction = hit.Distance > 0 ? Math.Clamp( wallDist / hit.Distance, 0, 1 ) * hit.Fraction : 0
			};
			return true;
		}

		var distance = hit.StartPosition.Distance( shieldPoint );
		shield.AbsorbHitscan( shot, distance, attacker, weapon, shieldPoint );
		hit = new SceneTraceResult
		{
			Scene = scene, Hit = true, StartPosition = hit.StartPosition, EndPosition = shieldPoint, HitPosition = shieldPoint,
			Normal = shieldNormal, Direction = hit.Direction, GameObject = shield.GameObject, Component = shield,
			Fraction = hit.Distance > 0 ? Math.Clamp( distance / hit.Distance, 0, 1 ) * hit.Fraction : 0
		};
		return true;
	}

	internal static bool TryInterceptProjectile( Scene scene, Vector3 start, Vector3 end, float radius,
		GameObject attacker, GameObject weapon, Titanfall2CapturedProjectile captured, Vector3 launchOrigin, out Vector3 contactPoint )
	{
		contactPoint = default;
		if ( !Networking.IsHost || !scene.IsValid() || !ValidSource( scene, attacker, weapon ) ) return false;
		var hasShield = TryFind( scene, start, end, radius, attacker, out var shield, out var shieldPoint, out _, out var shieldFrac );
		var hasWall = TryFindWall( scene, start, end, radius, attacker, out var wall, out var wallPoint, out _, out var wallFrac );
		if ( !hasShield && !hasWall ) return false;

		if ( hasWall && (!hasShield || wallFrac <= shieldFrac) )
		{
			var dist = launchOrigin.Distance( wallPoint );
			var direct = captured.Profile.Evaluate( dist, true, captured.NpcFired, captured.Damage, captured.TitanDamage );
			var splash = captured.Settings.SplashTitanDamage > 0 ? captured.Settings.SplashTitanDamage : captured.Settings.SplashDamage;
			wall.ApplyDamage( direct + MathF.Max( 0f, splash ), wallPoint );
			contactPoint = wallPoint;
			return true;
		}

		shield.AbsorbProjectile( captured, launchOrigin.Distance( shieldPoint ), attacker, weapon, shieldPoint );
		contactPoint = shieldPoint;
		return true;
	}

	internal static bool TryFind( Scene scene, Vector3 start, Vector3 end, float radius, GameObject attacker,
		out Titanfall2TitanKit selected, out Vector3 point, out Vector3 normal ) =>
		TryFind( scene, start, end, radius, attacker, out selected, out point, out normal, out _ );

	internal static bool TryFind( Scene scene, Vector3 start, Vector3 end, float radius, GameObject attacker,
		out Titanfall2TitanKit selected, out Vector3 point, out Vector3 normal, out float bestFraction )
	{
		selected = null; point = normal = default; bestFraction = 1f;
		if ( !Networking.IsHost || !start.IsFinite || !end.IsFinite ) return false;
		var best = 1f;
		foreach ( var kit in scene.GetAllComponents<Titanfall2TitanKit>() )
		{
			if ( !kit.DefenseVolumeActive || !kit.ShouldBlock( attacker ) ) continue;
			var pose = kit.DefenseTransform;
			// Ion's authored bullet_fov is 120 degrees; Scorch Thermal Shield blocks frontal cone; Legion blocks frontal hemisphere.
			var dot = kit.Kit == Titanfall2TitanKit.Chassis.Ion ? 0.5f
				: kit.Kit == Titanfall2TitanKit.Chassis.Scorch ? 0.25f : 0f;
			if ( !Intersect( start, end, pose.Position, pose.Rotation.Forward, kit.DefenseRadius + MathF.Max( 0, radius ), dot,
				out var fraction, out var contact ) || fraction > best ) continue;
			best = fraction;
			selected = kit;
			point = contact;
			normal = (contact - pose.Position).Normal;
			if ( normal.LengthSquared < 0.5f ) normal = pose.Rotation.Forward;
		}
		bestFraction = best;
		return selected.IsValid();
	}

	internal static bool TryFindWall( Scene scene, Vector3 start, Vector3 end, float radius, GameObject attacker,
		out Titanfall2ParticleWall selected, out Vector3 point, out Vector3 normal, out float bestFraction )
	{
		selected = null; point = normal = default; bestFraction = 1f;
		if ( !Networking.IsHost || !start.IsFinite || !end.IsFinite ) return false;
		var best = 1f;
		foreach ( var wall in scene.GetAllComponents<Titanfall2ParticleWall>() )
		{
			if ( !wall.IsValid() || !wall.IsActive || !wall.ShouldBlock( attacker ) ) continue;
			if ( !wall.TryIntersect( start, end, radius, out var frac, out var hitPoint, out var hitNormal ) || frac > best ) continue;
			best = frac;
			selected = wall;
			point = hitPoint;
			normal = hitNormal;
		}
		bestFraction = best;
		return selected.IsValid();
	}

	internal static bool Intersect( Vector3 start, Vector3 end, Vector3 center, Vector3 forward,
		float radius, float minFacingDot, out float fraction, out Vector3 point )
	{
		fraction = 0; point = default;
		var delta = end - start;
		var lengthSquared = delta.LengthSquared;
		if ( lengthSquared <= 0.0001f || radius <= 0 || Vector3.Dot( forward, -delta.Normal ) < minFacingDot ) return false;
		var offset = start - center;
		var c = offset.LengthSquared - radius * radius;
		if ( c > 0 )
		{
			var b = Vector3.Dot( offset, delta );
			var discriminant = b * b - lengthSquared * c;
			if ( discriminant < 0 ) return false;
			fraction = (-b - MathF.Sqrt( discriminant )) / lengthSquared;
			if ( fraction < 0 || fraction > 1 ) return false;
		}
		point = start + delta * fraction;
		return true;
	}

	internal static bool ValidSource( Scene scene, GameObject attacker, GameObject weapon ) =>
		attacker.IsValid() && weapon.IsValid() && attacker.Scene == scene && weapon.Scene == scene;

	/// <summary>Blast pressure may damage Legion's independent health, but is never ammunition.</summary>
	internal static void DamageGunShieldsInBlast( Scene scene, Vector3 origin, float radius, float damage,
		GameObject attacker, GameObject weapon )
	{
		if ( !Networking.IsHost || !ValidSource( scene, attacker, weapon ) || radius <= 0 || damage <= 0 ) return;
		foreach ( var kit in scene.GetAllComponents<Titanfall2TitanKit>().ToArray() )
		{
			if ( !kit.DefenseVolumeActive || kit.Kit != Titanfall2TitanKit.Chassis.Legion || !kit.ShouldBlock( attacker ) ) continue;
			var center = kit.DefenseTransform.Position;
			var delta = center - origin;
			var distance = MathF.Max( 0, delta.Length - kit.DefenseRadius );
			if ( distance >= radius ) continue;
			var point = delta.Length > kit.DefenseRadius ? origin + delta.Normal * distance : origin;
			var obstruction = Titanfall2CollisionQueries.Run( scene.Trace.Ray( origin, point )
				.IgnoreGameObjectHierarchy( kit.Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
			if ( obstruction.StartedSolid || obstruction.Hit ) continue;
			kit.DamageGunShield( damage * (1 - distance / radius), point );
		}
		foreach ( var wall in scene.GetAllComponents<Titanfall2ParticleWall>().ToArray() )
		{
			if ( !wall.IsValid() || !wall.IsActive || !wall.ShouldBlock( attacker ) ) continue;
			var center = wall.WorldPosition + Vector3.Up * (Titanfall2ParticleWall.Height * 0.5f);
			var distance = MathF.Max( 0f, center.Distance( origin ) - Titanfall2ParticleWall.HalfWidth * 0.5f );
			if ( distance >= radius ) continue;
			wall.ApplyDamage( damage * (1f - distance / radius), center );
		}
	}
}

public sealed partial class Titanfall2Npc
{
	internal bool TryGunShieldAttachment( out Transform pose )
	{
		pose = default;
		if ( !_gun.IsValid() || _gun.GetComponent<BaseWeaponModel>()?.Renderer is not { } renderer || !renderer.IsValid() ) return false;
		if ( renderer.GetAttachment( "gun_shield" ) is { } animated )
		{
			pose = animated;
			return true;
		}
		if ( renderer.Model?.Attachments.GetTransform( "gun_shield" ) is not { } local ) return false;
		pose = renderer.WorldTransform.ToWorld( local );
		return true;
	}
}
