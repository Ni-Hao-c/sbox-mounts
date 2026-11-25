using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

[Hide]
public sealed class HelicopterRocket : RocketProjectile
{
	internal override float LaunchSpeed => 100;
	internal override float Lifetime => 12;
	Vector3 _axis;
	float _nextAcceleration = 0.2f;
	internal override float IgnitionDelay => 0.1f;
	internal override float SmokeLifetime => 1.5f;

	internal override void PrepareLaunch()
	{
		_axis = WorldRotation.Forward;
		if ( Shooter.GetComponent<FlyingLayer>() is { } flight ) Velocity += flight.Velocity;
	}

	internal override void Guide()
	{
		if ( Age < _nextAcceleration ) return;
		_nextAcceleration += 0.1f;
		if ( Velocity.Length < 1800 ) Velocity += _axis * 200;
		if ( Velocity.LengthSquared > 0 ) WorldRotation = Rotation.LookAt( Velocity );
	}

	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		Blast.Detonate( GameObject, Shooter, position, normal, 150, true );
	}

}
