namespace GoldSrc.HalfLife;

[Title( "Human Assassin" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeHumanAssassin" )]
[NpcDefinition( "monster_human_assassin", "hassassin", MonsterClass.HumanMilitary, 50, 16, 64, 230, Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, StrikeEvents = new[] { 1 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class HumanAssassin : Npc
{
	internal override float ViewDot => -0.7f;
	internal override Vector3 AimOrigin => WorldPosition + WorldRotation.Forward * 24 + WorldRotation.Up * 48;
	internal override Vector3 AimAt( GameObject target ) => TargetBodyPosition( target );

	static readonly string[] Attacks = ["grenadethrow", "jump", "fly_up", "fly_down", "fly_attack", "landfromjump"];
	internal override string[] ExtraActions => Attacks;
	GameObject _enemy;
	Vector3 _jump, _toss;
	TimeUntil _nextJump, _nextGrenade, _nextFootstep;
	TimeSince _sinceShot;
	float _spread = 0.1f;
	int _frustration;
	[Sync( SyncFlags.FromHost )] bool Jumping { get; set; }

	internal override void BeginAttack( GameObject target )
	{
		_enemy = target;
		var distance = WorldPosition.Distance( target.WorldPosition );
		if ( distance <= 128 && _nextJump <= 0 && CanJump() )
		{
			PlayAction( Find( "jump" ) );
			return;
		}
		if ( _frustration > 2 && distance <= 512 && _nextGrenade <= 0 && CanThrow( target ) )
		{
			PlayAction( Find( "grenadethrow" ) );
			return;
		}
		if ( distance > 64 ) base.BeginAttack( target );
	}

	bool CanJump()
	{
		var offset = new Vector3( Game.Random.Float( -64, 64 ), Game.Random.Float( -64, 64 ), 160 );
		var center = WorldPosition + Vector3.Up * 36;
		if ( Scene.Trace.Ray( center, center + offset ).Size( 32 ).IgnoreGameObjectHierarchy( GameObject ).Run().Hit ) return false;
		_jump = offset * System.MathF.Sqrt( System.MathF.Max( 1, -Scene.PhysicsWorld.Gravity.z ) / 80 );
		return true;
	}

	Vector3 GrenadeOrigin => WorldPosition + WorldRotation.Forward * 34 + Vector3.Up * 32;
	bool CanThrow( GameObject target )
	{
		return TimedGrenade.TryAim( this, GrenadeOrigin, target, TargetHullCenter( target ), out _toss );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id == 2 )
		{
			TimedGrenade.Throw( this, GrenadeOrigin, _toss, 2 );
			_nextGrenade = 6;
			_frustration = 0;
		}
		else if ( id == 3 )
		{
			WorldPosition += Vector3.Up;
			Jumping = true;
			_nextJump = 3;
			Launch( _jump );
			if ( IsLiveTarget( _enemy ) ) AttackWith( _enemy, Find( "fly_attack" ) );
		}
	}

	internal override StudioSequence MovementSequence( float speed ) => Jumping
		? Find( GetComponent<Rigidbody>().Velocity.z > 0 ? "fly_up" : "fly_down" ) : base.MovementSequence( speed );

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost ) return;
		if ( !Jumping )
		{
			if ( _nextFootstep <= 0 && Health > 0 && !IsBarnacleVictim && Action is null && IsOnGround && Animation.MoveVelocity.Length > 12 )
			{
				EmitSound( $"player/pl_step{Game.Random.Int( 1, 4 )}.wav", WorldPosition, 0.5f );
				_nextFootstep = 0.2f;
			}
			return;
		}
		if ( !IsOnGround )
		{
			AdvanceAction();
			return;
		}
		Jumping = false;
		PlayAction( Find( "landfromjump" ) );
	}

	internal override void FireBullet( GameObject target, Vector3 origin, Vector3 end )
	{
		_spread = _sinceShot > 2 ? 0.1f : System.MathF.Max( 0.02f, _spread - 0.01f );
		_sinceShot = 0;
		PlayGunshot( $"weapons/pl_gun{Game.Random.Int( 1, 2 )}.wav", origin, Game.Random.Float( 0.6f, 0.8f ) );
		TraceBullets( origin, end, 1, Definition.Damage, _spread, 2048 );
		EjectShell( WorldPosition + Vector3.Up * 32 + WorldRotation.Forward * 12, WorldRotation );
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		_frustration++;
		base.OnDamaged( damage );
	}
}
