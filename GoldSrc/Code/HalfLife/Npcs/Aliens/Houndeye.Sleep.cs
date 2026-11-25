namespace GoldSrc.HalfLife;

public sealed partial class Houndeye
{
	[Sync( SyncFlags.FromHost )] bool Sleeping { get; set; }
	[Sync( SyncFlags.FromHost )] int EyeFrame { get; set; }
	TimeUntil _idleCheck, _closeEyes, _nextStudioThink;
	int _shownEye;

	internal override string[] ExtraActions => ["gotosleep", "sleep", "sleeptostand", "wakefast", "jumpback"];
	internal override StudioSequence RestSequence => Sleeping ? Find( "sleep" ) : base.RestSequence;

	protected override void Think()
	{
		if ( Sleeping )
		{
			if ( FindVisibleEnemy().IsValid() ) Wake( true );
			else if ( Senses?.Disturbance is { } sound )
			{
				Animation.SetLookTarget( sound.Position );
				Wake( sound.Radius - WorldPosition.Distance( sound.Position ) >= 128 );
			}
			else AdvanceAction();
			return;
		}

		if ( _idleCheck <= 0 )
		{
			_idleCheck = Game.Random.Float( 1, 5 );
			if ( Game.Random.Int( 0, 29 ) == 0 && Find( "gotosleep" ) is { } lieDown
				&& !Movement.IsMoving && !FindVisibleEnemy().IsValid() && Senses.Disturbance is null
				&& IsPackFollower )
			{
				Sleeping = true;
				_closeEyes = lieDown.Duration + Game.Random.Float( 0, 25 );
				Movement.Stop();
				PlayAction( lieDown );
				return;
			}
		}
		if ( !ContinueRetreat() ) base.Think();
	}

	void Wake( bool startled )
	{
		Sleeping = false;
		EyeFrame = 0;
		_idleCheck = 5;
		if ( Find( startled ? "wakefast" : "sleeptostand" ) is { } stand ) PlayAction( stand );
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		var wasSleeping = Sleeping;
		if ( wasSleeping && Health > 0 ) Wake( true );
		base.OnDamaged( damage );
		if ( !wasSleeping && Health > 0 && damage.AcceptedDamage > 0 && InCombat && !_retreatFrom.IsValid() )
			StartRetreat( LastHostile );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Renderer.IsValid() || !Renderer.Animation.IsValid || Health <= 0 ) return;
		if ( Networking.IsHost && _nextStudioThink <= 0 )
		{
			_nextStudioThink = 0.1f;
			if ( InCombat && !Sleeping && Action is null && !PhysicsCarrier.IsValid() &&
				MovementSequence( Animation.MoveVelocity.Length )?.Description.Activity == Run && Game.Random.Float() < 0.2f )
				Call( HuntSample() );
			if ( Sleeping && _closeEyes <= 0 ) EyeFrame = System.Math.Min( EyeFrame + 1, 3 );
			else if ( EyeFrame > 0 ) EyeFrame--;
			else if ( Game.Random.Int( 0, 127 ) == 0 ) EyeFrame = 3;
		}
		if ( EyeFrame == _shownEye ) return;
		Renderer.MaterialGroup = $"Skin {EyeFrame}";
		_shownEye = EyeFrame;
	}
}
