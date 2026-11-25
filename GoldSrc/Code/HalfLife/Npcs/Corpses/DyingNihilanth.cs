namespace GoldSrc.HalfLife;

[Hide]
public sealed class DyingNihilanth : NpcCorpse
{
	float _rise, _turn;
	TimeUntil _nextDischarge;
	ParticleEffect _balls;

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || WorldPosition.z >= 4096 ) return;
		_rise = System.MathF.Min( _rise + Time.Delta * 100, 300 );
		var next = WorldPosition + Vector3.Up * System.MathF.Min( 4096 - WorldPosition.z, _rise * Time.Delta );
		var ceiling = Scene.Trace.Ray( WorldPosition, next + Vector3.Up * 300 ).IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( !ceiling.Hit ) WorldPosition = next;
		WorldRotation *= Rotation.FromYaw( _turn * Time.Delta );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Renderer.IsValid() || !Renderer.Animation.IsValid || _nextDischarge > 0 ) return;
		_nextDischarge = 0.1f;
		if ( Networking.IsHost ) _turn = (_turn + Game.Random.Float( -12, 12 )).Clamp( -100, 100 );
		int index = Game.Random.Int( 1, 4 );
		var origin = Renderer.GetAttachment( $"attachment{index}" )?.Position ?? WorldPosition;
		var axis = index switch { 1 => WorldRotation.Up, 2 => WorldRotation.Forward, 3 => WorldRotation.Left, _ => WorldRotation.Right };
		var trace = Scene.Trace.Ray( origin, origin + (axis * 2 + Vector3.Random) * 4096 ).IgnoreGameObjectHierarchy( GameObject ).Run();
		EnergyBeam.Show( origin, trace.EndPosition, new Color32( 64, 128, 255 ), "laserbeam", "Nihilanth death beam", 10, 0.5f );

		if ( !_balls.IsValid() )
		{
			if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( "exit1" ) is not { } sprite ) return;
			var go = new GameObject( GameObject, false, "Nihilanth death spheres" );
			go.NetworkMode = NetworkMode.Never;
			go.LocalPosition = Vector3.Zero;
			_balls = go.AddComponent<ParticleEffect>();
			_balls.Lifetime = 5;
			_balls.InitialScale = 32;
			_balls.Collision = true;
			_balls.DieOnCollisionChance = 1;
			go.AddComponent<ParticleSpriteRenderer>().Sprite = sprite;
			go.Enabled = true;
		}
		var head = Renderer.GetAttachment( "attachment1" )?.Position ?? WorldPosition;
		var ball = _balls.Emit( head, 0 );
		if ( ball is not null ) ball.Velocity = new Vector3( Game.Random.Float( -0.7f, 0.7f ), Game.Random.Float( -0.7f, 0.7f ), 1 ) * 600;
	}
}
