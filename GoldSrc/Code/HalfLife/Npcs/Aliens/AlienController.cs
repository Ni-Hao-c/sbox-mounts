namespace GoldSrc.HalfLife;

[Title( "Alien Controller" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeAlienController" )]
[NpcDefinition( "monster_alien_controller", "controller", MonsterClass.AlienMilitary, 60, 32, 64, 135, Attack = NpcAttack.Projectile, Damage = 4, Range = 2048, Movement = NpcMovement.Flying, IdleSequence = "up", Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed partial class AlienController : VoicedNpc
{
	internal override float ViewDot => -1;

	internal override NpcSounds Sounds { get; } = new( "controller/con_idle", "controller/con_alert", "controller/con_pain", "controller/con_die", "controller/con_attack" );

	void Call( string sample ) => EmitSound( sample, WorldPosition, pitch: Game.Random.Int( 95, 105 ) * 0.01f, speaker: this );

	internal override void OnEnemySpotted( GameObject target ) => Call( Sounds.Alert );
	internal override void IdleSound() => Call( Sounds.Idle );
	internal override void DeathSound() => Call( Sounds.Death );

	internal override void PainSound()
	{
		if ( Game.Random.Int( 0, 5 ) < 2 ) Call( Sounds.Pain );
	}

	static readonly string[] FlightClips = ["attack1", "attack2", "shoot", "forward", "backward", "right", "left", "up", "down"];
	internal override string[] ExtraActions => FlightClips;
	GameObject _enemy;
	TimeUntil _nextBall;
	int _ballsLeft;
	Vector3 _estimatedVelocity;
	int _attackAction = -1;

	internal override void OnModelLoaded( ModelLoader loader ) => EyeHeight = loader.EyePosition.z;
	internal override bool InAttackRange( GameObject target, Vector3 aim ) =>
		WorldPosition.DistanceSquared( target.WorldPosition ) <= AttackRange * AttackRange;

	internal override StudioSequence MovementSequence( float speed )
	{
		if ( speed < 32 ) return Find( "up" );
		var local = WorldRotation.Inverse * Animation.MoveVelocity;
		var magnitudes = local.Abs();
		if ( magnitudes.x > magnitudes.y && magnitudes.x > magnitudes.z )
			return Find( local.x > 0 ? "forward" : "backward" );
		if ( magnitudes.y > magnitudes.z )
			return Find( local.y > 0 ? "left" : "right" );
		return Find( local.z > 0 ? "up" : "down" );
	}

	internal override void BeginAttack( GameObject target )
	{
		var offset = target.WorldPosition - WorldPosition;
		if ( offset.Length <= 64 || Vector3.Dot( WorldRotation.Forward, offset.WithZ( 0 ).Normal ) <= 0.5f ) return;
		if ( WeightedActivity( offset.Length > 256 ? Ranged : Ranged + 1 ) is not { } attack ) return;
		if ( _enemy != target ) _estimatedVelocity = Vector3.Zero;
		_enemy = target;
		PlayAction( attack );
		_attackAction = ActionSerial;
	}

	internal override void HandleActionEvent( Mdl.Event e )
	{
		int.TryParse( e.Options, out var frames );
		if ( e.Index is 1 or 4 or 5 ) ChargeTime = frames / 15f;
		switch ( e.Index )
		{
			case 1:
				Charge = new Vector2( 192, 255 );
				HeadLight( true );
				break;
			case 2:
				Charge = Vector2.Zero;
				HeadLight( false );
				NpcProjectile.Fire<HeadBall>( GameObject, null, Definition.Damage, EventTarget(), Attachment( 1 ), Vector3.Up, null );
				break;
			case 3:
				Call( Sounds.Attack );
				_enemy = EventTarget();
				_ballsLeft = (frames + 2) / 3;
				_nextBall = 0;
				break;
			case 4: Charge = new Vector2( 255, 255 ); break;
			case 5: Charge = new Vector2( 192, 192 ); break;
		}
	}

	GameObject EventTarget() => _attackAction == ActionSerial ? _enemy : FindVisibleEnemy();

	Vector3 Attachment( int index ) => Renderer.GetAttachment( $"attachment{index}" )?.Position ?? WorldPosition + Vector3.Up * 40;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Health <= 0 || !Renderer.IsValid() || !Renderer.Animation.IsValid ) return;
		UpdateLights();
		if ( !Networking.IsHost ) return;
		while ( _ballsLeft > 0 && _nextBall <= 0 )
		{
			var overdue = -(float)_nextBall;
			if ( IsLiveTarget( _enemy ) ) FireHandBall( overdue );
			_nextBall = 0.2f - overdue;
			if ( --_ballsLeft == 0 )
			{
				Charge = new Vector2( 64, 64 );
				ChargeTime = 0;
			}
		}
	}

	void FireHandBall( float overdue )
	{
		const float speed = 800;
		var source = Attachment( 3 ) - GetVelocity( GameObject ) * overdue;
		var offset = TargetBodyPosition( _enemy ) - source;
		_estimatedVelocity = Senses.VisibleHostiles.Contains( _enemy )
			? Vector3.Lerp( _estimatedVelocity, GetVelocity( _enemy ), 0.5f ) : _estimatedVelocity * 0.8f;
		var closingSpeed = System.MathF.Sqrt( System.MathF.Max( 1, speed * speed - _estimatedVelocity.LengthSquared ) );
		var lead = offset + _estimatedVelocity * (offset.Length / closingSpeed).Clamp( 0.1f, 10f );
		const float spread = 0.0349f;
		var scatter = new Vector3( Game.Random.Float( -spread, spread ), Game.Random.Float( -spread, spread ), Game.Random.Float( -spread, spread ) );
		var velocity = (lead.Normal + scatter) * speed;
		NpcProjectile.Fire<EnergyBall>( GameObject, null, Definition.Damage, _enemy, source + velocity * overdue, velocity.Normal, velocity.Length );
	}
}
