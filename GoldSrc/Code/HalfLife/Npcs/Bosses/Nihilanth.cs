namespace GoldSrc.HalfLife;

[Title( "Nihilanth" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeNihilanth" )]
[NpcDefinition( "monster_nihilanth", "nihilanth", MonsterClass.AlienMilitary, 800, 32, 64, 80, Attack = NpcAttack.Projectile, Damage = 30, Range = 4096, Movement = NpcMovement.Flying, IdleSequence = "float", Blood = BloodColor.Yellow )]
public sealed partial class Nihilanth : Npc
{
	internal override float ViewDot => -1;
	internal override float SightRange => 4096;

	const int SphereCount = 20;
	[Sync( SyncFlags.FromHost )] public int ActiveSpheres { get; private set; } = SphereCount;
	internal override NpcSounds Sounds { get; } = new( Death: "x/x_die1.wav" );
	static readonly string[] Clips = ["float_open", "attack1", "attack1_open", "attack2", "walk_r", "walk_l", "walk_u", "walk_d"];
	internal override string[] ExtraActions => Clips;
	// The original encounter checks recharger levels 1 through 9 on successive activities.
	[Sync( SyncFlags.FromHost )] int ExhaustedRechargers { get; set; }
	bool BrainExposed => ExhaustedRechargers >= 9;
	internal override void FinishAction( string name ) => CheckRecharger();
	internal override void FinishMovementCycle() => CheckRecharger();

	void CheckRecharger()
	{
		if ( !BrainExposed && (Health < Definition.Health / 2 || ActiveSpheres < SphereCount / 2) )
			ExhaustedRechargers++;
	}

	bool IsBrainHit( DamageInfo info ) => BrainExposed && HitStudioModel( info ) && info.Damage > 2 && HitGroups( info ).Has( "hitgroup_2" );

	internal override StudioSequence RestSequence => BrainExposed ? Find( "float_open" ) : base.RestSequence;
	internal override float PlaybackRate => 2 - (Health / Definition.Health).Clamp( 0, 1 );
	internal override float HoverClearance => System.MathF.Max( 64, -(Find( "float" )?.Description.Min.z ?? 0) );

	internal override float MovementPlaybackRate( StudioSequence sequence, float speed ) => PlaybackRate;

	internal override StudioSequence MovementSequence( float speed )
	{
		if ( BrainExposed ) return RestSequence;
		var velocity = WorldRotation.Inverse * Animation.MoveVelocity;
		if ( velocity.y < -30 ) return Find( "walk_r" );
		if ( velocity.y > 30 ) return Find( "walk_l" );
		if ( velocity.z > 30 ) return Find( "walk_u" );
		if ( velocity.z < -30 ) return Find( "walk_d" );
		return RestSequence;
	}
	GameObject _enemy;
	int _pairsRemaining;
	TimeSince _volleyAge;
	TimeUntil _nextHeal, _nextPain;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		EyeHeight = 300;
	}

	internal override void PlaceAtSpawn()
	{
		base.PlaceAtSpawn();
		if ( Movement is Sandbox.Npcs.Layers.FlyingLayer flight ) flight.HoverHeight = HoverClearance;
	}

	internal override void BeginAttack( GameObject target )
	{
		_enemy = target;
		var sequence = BrainExposed && Health < Definition.Health / 2 ? "attack1_open" : CanTeleport && Game.Random.Int( 0, 1 ) == 1 ? "attack2" : "attack1";
		if ( Find( sequence ) is { } attack ) PlayAction( attack );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( !IsLiveTarget( _enemy ) ) return;
		if ( id == 6 )
		{
			FireFromHand( 3, Vector3.Zero );
			return;
		}
		if ( id == 3 && CanTeleport )
		{
			FireTeleport( _enemy );
			return;
		}
		if ( id != 2 && id != 3 ) return;
		if ( Game.Random.Int( 0, 4 ) == 0 ) EmitSound( "x/x_attack", WorldPosition, attenuation: 0.2f );
		EmitSound( "x/x_ballattack1.wav", WorldPosition, attenuation: 0.2f );
		HandLight( 3 );
		HandLight( 4 );
		_pairsRemaining = 5;
		_volleyAge = 0;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Health <= 0 || !Renderer.IsValid() || !Renderer.Animation.IsValid ) return;
		if ( Networking.IsHost )
		{
			if ( Health < Definition.Health && ActiveSpheres > 0 && _nextHeal <= 0 )
			{
				ActiveSpheres--;
				Health = System.MathF.Min( Definition.Health, Health + Definition.Health / SphereCount );
				_nextHeal = 0.1f;
			}
			UpdateVolley();
		}
		UpdateSpheres();
	}

	void UpdateVolley()
	{
		if ( _pairsRemaining == 0 ) return;
		float age = _volleyAge;
		if ( age >= 1 || !IsLiveTarget( _enemy ) )
		{
			_pairsRemaining = 0;
			return;
		}

		while ( _pairsRemaining > 0 )
		{
			float late = age - (5 - _pairsRemaining) * 0.2f;
			if ( late < 0 ) break;
			var aim = (_enemy.WorldPosition - WorldPosition).Normal;
			var offset = (aim - GetVelocity( GameObject )) * late;
			FireFromHand( 3, offset );
			FireFromHand( 4, offset );
			_pairsRemaining--;
		}
	}

	void FireFromHand( int index, Vector3 offset )
	{
		if ( Renderer.GetAttachment( $"attachment{index}" ) is not { } hand ) return;
		var origin = hand.Position + offset;
		var direction = (_enemy.WorldPosition - origin).Normal;
		NpcProjectile.Fire<NihilanthZap>( GameObject, ProjectilePath, Definition.Damage, _enemy, origin, direction, null );
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		if ( damage.Instigator == GameObject )
		{
			damage.AcceptedDamage = 0;
			return;
		}
		if ( !IsBrainHit( damage.DamageInfo ) ) damage.AcceptedDamage = System.MathF.Min( damage.AcceptedDamage, System.MathF.Max( 0, Health - 1 ) );
	}

	internal override void ShowWound( Component.IDamageable.DamageEvent hit )
	{
		var info = hit.DamageInfo;
		if ( !HitStudioModel( info ) || hit.Instigator == GameObject || Health <= 0 ) return;
		if ( IsBrainHit( info ) ) return;
		BloodEffects.Stream( Definition.Blood, info.Position, (info.Position - WorldPosition).Normal,
			info.Damage + 100 * (1 - (Health + hit.AcceptedDamage) / Definition.Health) );
	}

	internal override NpcCorpse AddCorpseComponent( GameObject go ) => go.AddComponent<DyingNihilanth>();

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		base.OnDamaged( damage );
		if ( Health <= 0 || _nextPain > 0 ) return;
		if ( Health > Definition.Health / 2 )
			EmitSound( "x/x_laugh", WorldPosition, attenuation: 0.2f );
		else if ( BrainExposed )
			EmitSound( $"x/x_pain{Game.Random.Int( 1, 2 )}.wav", WorldPosition, attenuation: 0.2f );
		_nextPain = Game.Random.Float( 2, 5 );
	}

	internal override void DeathSound() => EmitSound( Sounds.Death, WorldPosition, attenuation: 0.1f );

	protected override GameObject CreateCorpse() => Find( "die1" ) is { } death ? NpcCorpse.Spawn( this, death, true ) : base.CreateCorpse();
}
