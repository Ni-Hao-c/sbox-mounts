namespace GoldSrc.HalfLife;

[Title( "Vortigaunt" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeVortigaunt" )]
[NpcDefinition( "monster_alien_slave", "islave", MonsterClass.AlienMilitary, 30, 16, 72, 140, Attack = NpcAttack.Shock, Damage = 10, Range = 784, Aliases = new[] { "monster_vortigaunt" }, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed partial class Vortigaunt : VoicedNpc
{
	internal override float ViewDot => -0.7f;

	[Property] public bool WaitUntilProvoked { get; set; }
	[Sync( SyncFlags.FromHost )] bool Provoked { get; set; }

	protected override Sandbox.Npcs.Relationship GetFallbackRelationship( GameObject other ) =>
		WaitUntilProvoked && !Provoked && ClassOf( other ) == MonsterClass.Player
			? new( Sandbox.Npcs.Disposition.Neutral ) : base.GetFallbackRelationship( other );

	protected override void OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		if ( hit.AcceptedDamage > 0 )
		{
			Provoked = true;
			_recentAttacker = hit.Instigator;
		}
		base.OnDamaged( hit );
	}

	internal override NpcSounds Sounds { get; } = new( "!SLV_IDLE", "!SLV_ALERT", "aslave/slv_pain", "aslave/slv_die", "hassault/hw_shoot1.wav" );
	internal override float VoicePitch => (85 + (uint)GameObject.Id.GetHashCode() % 26) / 100f;
	[Property] public string AlertGroup { get; set; }

	internal override void OnEnemySpotted( GameObject target )
	{
		EmitSound( Sounds.Alert, WorldPosition, 0.85f, VoicePitch, this );
		if ( string.IsNullOrEmpty( AlertGroup ) ) return;
		foreach ( var ally in Scene.GetAllComponents<Vortigaunt>() )
		{
			if ( ally.Health <= 0 || ally.AlertGroup != AlertGroup || WorldPosition.DistanceSquared( ally.WorldPosition ) >= 512 * 512 ) continue;
			ally.Provoked = true;
			if ( ally.GetDisposition( target ) != Sandbox.Npcs.Disposition.Hostile )
				ally.SetRelationship( target, Sandbox.Npcs.Disposition.Hostile );
		}
	}

	internal override void IdleSound()
	{
		if ( Game.Random.Int( 0, 2 ) == 0 ) EmitSound( Sounds.Idle, WorldPosition, 0.85f, VoicePitch, this );
	}

	internal override void PainSound()
	{
		if ( Game.Random.Int( 0, 2 ) == 0 ) EmitSound( Sounds.Pain, WorldPosition, pitch: VoicePitch );
	}

	static readonly string[] Attacks = ["attack1", "zapattack1"];
	internal override string[] ExtraActions => Attacks;
	GameObject _enemy;
	int _attackAction = -1;
	TimeUntil _nextZap;
	int _beamCount;
	int _chargeAction = -1;

	internal override void OnModelLoaded( ModelLoader loader ) => EyeHeight = loader.EyePosition.z;

	internal override bool InAttackRange( GameObject target, Vector3 aim ) =>
		WorldPosition.DistanceSquared( target.WorldPosition ) <= AttackRange * AttackRange;

	protected override void Think()
	{
		if ( !TakeCover() ) base.Think();
		if ( _chargeAction >= 0 && (_chargeAction != ActionSerial || Action is null) ) ClearBeams();
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		ClearBeams();
		base.OnDeath( death );
	}

	bool CanClaw( GameObject target )
	{
		var offset = target.WorldPosition - WorldPosition;
		return offset.LengthSquared <= 64 * 64 && WorldRotation.Forward.Dot( offset.WithZ( 0 ).Normal ) >= 0.7f &&
			(target.GetComponent<Sandbox.Npcs.BaseNpc>()?.IsOnGround ?? target.GetComponent<PlayerController>()?.IsOnGround ?? false);
	}

	internal override void BeginAttack( GameObject target )
	{
		var offset = target.WorldPosition - WorldPosition;
		var claw = offset.LengthSquared <= 64 * 64;
		if ( WorldRotation.Forward.Dot( offset.WithZ( 0 ).Normal ) < (claw ? 0.7f : 0.5f) ) return;
		if ( claw && !CanClaw( target ) ) return;
		if ( !claw && _nextZap > 0 ) return;
		var action = Find( claw ? "attack1" : "zapattack1" );
		if ( action is null ) return;
		_enemy = target;
		PlayAction( action );
		_attackAction = ActionSerial;
	}

	internal override void HandleActionEvent( int id )
	{
		switch ( id )
		{
			case 1:
			case 2:
				var victim = TraceClaw( 70, id == 1 ? 10 : 25, "slash", VoicePitch );
				if ( victim.IsValid() && victim.GameObject.GetComponent<PlayerController>() is { } player )
					PunchPlayer( player, Vector3.Zero, new Angles( 5, 0, -18 ) );
				break;
			case 3:
				_chargeAction = ActionSerial;
				if ( _beamCount == 0 ) ShowChargeLight();
				ChargeArm( -1 );
				ChargeArm( 1 );
				PlayChargeSound( 1 + _beamCount * 0.1f );
				break;
			case 4:
				ClearBeams();
				_chargeAction = ActionSerial;
				Discharge();
				EmitSound( Sounds.Attack, WorldPosition, pitch: Game.Random.Int( 130, 160 ) / 100f );
				_nextZap = Game.Random.Float( 0.5f, 4 );
				break;
			case 5:
				ClearBeams();
				break;
		}
	}

	void ChargeArm( int side )
	{
		if ( _beamCount >= 8 ) return;
		var start = WorldPosition + Vector3.Up * 36 + WorldRotation.Right * side * 16 + WorldRotation.Forward * 32;
		SceneTraceResult? closest = null;
		var fraction = 1f;
		for ( int i = 0; i < 3; ++i )
		{
			var aim = WorldRotation.Right * side * Game.Random.Float( 0, 1 ) + Vector3.Up * Game.Random.Float( -1, 1 );
			var hit = Scene.Trace.Ray( start, start + aim * 512 ).IgnoreGameObjectHierarchy( GameObject ).Run();
			if ( !hit.Hit || hit.Fraction >= fraction ) continue;
			fraction = hit.Fraction;
			closest = hit;
		}
		if ( closest is not { } end ) return;
		_beamCount++;
		ShowBeam( side, end.EndPosition, false );
		if ( end.GameObject?.GetComponentInParent<IDamageable>() is null )
			ImpactDecal.Gunshot( end.EndPosition, end.Normal, Game.Random.Int( 1, 5 ) );
	}

	void Discharge()
	{
		var left = Zap( -1 );
		var right = Zap( 1 );
		if ( left.GameObject == right.GameObject && left.Hitbox == right.Hitbox )
			Hurt( right, Definition.Damage * 2 );
		else
		{
			Hurt( left, Definition.Damage );
			Hurt( right, Definition.Damage );
		}
	}

	SceneTraceResult Zap( int side )
	{
		var start = WorldPosition + Vector3.Up * 36;
		var target = _attackAction == ActionSerial ? _enemy : FindVisibleEnemy();
		var aim = target.IsValid() ? (TargetBodyPosition( target ) - start).Normal : WorldRotation.Forward;
		aim += WorldRotation.Right * side * Game.Random.Float( 0, 0.01f ) + Vector3.Up * Game.Random.Float( -0.01f, 0.01f );
		var hit = Scene.Trace.Ray( start, start + aim * 1024 ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		ShowBeam( side, hit.EndPosition, true );
		EmitSound( "weapons/electro4.wav", hit.EndPosition, 0.5f, Game.Random.Int( 140, 160 ) / 100f );
		return hit;
	}

	void Hurt( SceneTraceResult hit, float amount )
	{
		if ( !hit.GameObject.IsValid() || hit.GameObject.GetComponentInParent<IDamageable>() is not { } victim ) return;
		var damage = new DamageInfo { Attacker = GameObject, Weapon = GameObject, Damage = amount, Origin = hit.StartPosition, Position = hit.EndPosition, Hitbox = hit.Hitbox };
		damage.Tags.Add( "shock" );
		victim.OnDamage( damage );
	}

	void ClearBeams()
	{
		_beamCount = 0;
		_chargeAction = -1;
		RemoveEffects();
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		base.ModifyDamage( damage );
		var info = damage.DamageInfo;
		if ( info.Tags.Has( "shock" ) || info.Tags.Has( "slash" ) && info.Attacker.IsValid() && GetDisposition( info.Attacker ) != Sandbox.Npcs.Disposition.Hostile )
			damage.Cancelled = true;
	}
}
