/// <summary>Retail mp_weapon_tether.nut and client.dll 0x180207E00 movement rules.</summary>
internal static class Titanfall2TetherRules
{
	internal const float LaunchSpeed = 1000, ArmSeconds = 1.5f, TrapHealth = 100;
	internal const float Radius = 250, Strength = 25, MaximumPullSpeed = 200;
	internal const float TetherHealth = 1000, HealthDrain = 200, DrainSeconds = 0.1f, RecoverySeconds = DrainSeconds + 200f / 10f;
	internal const float Gravity = 750; // sv_gravity; projectile_gravity_scale defaults to 1.
	internal const int MaximumDeployed = 4, MaximumAttached = 2;
	internal static float MineLifetime( bool npcOwner, bool frontierDefense ) => npcOwner ? 10 : frontierDefense ? 10000 : 60;
	internal static bool InTriggerRange( Vector3 anchor, Vector3 feet, float eyeHeight, bool piloted )
	{
		if ( piloted && anchor.z > feet.z ) feet.z = MathF.Min( feet.z + eyeHeight, anchor.z );
		var radius = piloted ? 350f : 450f;
		return (anchor - feet).LengthSquared <= radius * radius;
	}
	internal static Vector3 ConstrainVelocity( Vector3 anchor, Vector3 position, float height, Vector3 velocity, float delta )
	{
		if ( !anchor.IsFinite || !position.IsFinite || !velocity.IsFinite || !float.IsFinite( delta ) || delta <= 0 ) return velocity;
		var targetZ = position.z;
		if ( anchor.z > targetZ ) targetZ = MathF.Min( anchor.z, targetZ + MathF.Max( 0, height ) );
		var offset = anchor - position.WithZ( targetZ );
		if ( offset.LengthSquared <= Radius * Radius ) return velocity;
		// Retail does not pull the actor upward, even with an elevated anchor.
		offset.z = MathF.Min( offset.z, 0 );
		var distance = offset.Length;
		if ( distance < 0.0001f ) return velocity;
		var direction = offset / distance;
		var radial = Vector3.Dot( direction, velocity );
		var impulse = MathF.Max( 0, MathF.Min( (distance - Radius) * Strength * delta + radial, MaximumPullSpeed ) - radial );
		return velocity + direction * impulse;
	}
}
