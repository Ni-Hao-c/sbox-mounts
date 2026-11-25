using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

[Title( "Bullsquid" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeBullsquid" )]
[NpcDefinition( "monster_bullchicken", "bullsquid", MonsterClass.AlienPredator, 40, 32, 64, 130, Attack = NpcAttack.Projectile, Damage = 10, Range = 784, StrikeEvents = new[] { 1 }, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed partial class Bullsquid : Npc
{
	internal override float ViewDot => 0.2f;

	internal override NpcSounds Sounds { get; } = new( "bullchicken/bc_idle", Alert: "bullchicken/bc_idle", Pain: "bullchicken/bc_pain", Death: "bullchicken/bc_die", Attack: "bullchicken/bc_attack" );

	static readonly string[] MeleeActions = ["bite", "whip", "seecrab", "suprisedhop", "sniff", "eat", "inspectdown"];
	internal override string[] ExtraActions => MeleeActions;
	TimeUntil _nextSpit, _ignoreCrabs;
	float _spitDelay;
	bool _displayedThreat;
	[Sync( SyncFlags.FromHost )] bool EyeClosed { get; set; }
	TimeUntil _nextBlinkTick;
	bool _shownClosed;

	static bool IsAdultHeadcrab( GameObject other ) => other?.GetComponent<Headcrab>() is Headcrab and not BabyHeadcrab;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Renderer.IsValid() || !Renderer.Animation.IsValid || Health <= 0 ) return;
		if ( Networking.IsHost && _nextBlinkTick <= 0 )
		{
			_nextBlinkTick = 0.1f;
			EyeClosed = Game.Random.Int( 0, 39 ) == 0;
		}
		if ( _shownClosed == EyeClosed ) return;
		_shownClosed = EyeClosed;
		Renderer.MaterialGroup = EyeClosed ? "Skin 1" : "Skin 0";
	}

	internal override void OnModelLoaded( ModelLoader loader )
	{
		EyeHeight = loader.EyePosition.z;
	}

	internal override void OnEnemySpotted( GameObject target )
	{
		EmitSound( $"bullchicken/bc_idle{Game.Random.Int( 1, 2 )}.wav", WorldPosition, pitch: Game.Random.Int( 140, 160 ) / 100f );
		if ( _displayedThreat || target.GetComponent<Headcrab>() is null || Find( "seecrab" ) is not { } display ) return;
		_displayedThreat = true;
		Movement?.Stop();
		PlayAction( display );
	}

	internal override void IdleSound() => Speak( Sounds.Idle, attenuation: 1.5f );

	internal override void PainSound() =>
		EmitSound( Sounds.Pain, WorldPosition, pitch: Game.Random.Int( 85, 120 ) / 100f );

	internal override void PlayAttackSound( Vector3 origin ) =>
		EmitSound( $"bullchicken/bc_attack{Game.Random.Int( 2, 3 )}.wav", origin );

	protected override Relationship GetFallbackRelationship( GameObject other )
	{
		if ( _ignoreCrabs > 0 && other.GetComponent<Headcrab>() is Headcrab and not BabyHeadcrab ) return new( Disposition.Neutral );
		return base.GetFallbackRelationship( other );
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		if ( _feeding != Feeding.None ) StopFeeding();
		if ( Action == "seecrab" ) InterruptAction();
		var hop = Health > 0 && Action is null && !FindVisibleEnemy().IsValid();
		base.OnDamaged( damage );
		if ( !IsAdultHeadcrab( damage.Instigator ) )
		{
			_ignoreCrabs = 5;
			_nextMeal = System.MathF.Max( _nextMeal, 20 );
		}
		if ( hop && Find( "suprisedhop" ) is { } reaction )
		{
			if ( damage.Instigator.IsValid() ) Animation.SetLookTarget( damage.Instigator );
			PlayAction( reaction );
		}
	}

	internal override bool Pursue( Vector3 goal )
	{
		var speed = Find( "run" )?.GroundSpeed ?? Definition.Speed;
		if ( (goal - WorldPosition).WithZ( 0 ).LengthSquared < 256 * 256 ) speed *= 1.25f;
		return MoveToward( goal, 32, speed / Definition.Speed );
	}

	internal override bool InAttackRange( GameObject target, Vector3 aim )
	{
		var offset = target.WorldPosition - WorldPosition;
		var distance = offset.Length;
		var facing = WorldRotation.Forward.Dot( offset.WithZ( 0 ).Normal );
		var moving = Movement?.IsMoving == true;
		// Engage stops navigation before starting the attack. Use the pace at selection time.
		_spitDelay = 0;
		if ( _nextSpit <= 0 && distance > 64 && distance <= Definition.Range && facing >= 0.5f &&
			(!moving || distance < 512) && System.MathF.Abs( offset.z ) <= 256 )
			_spitDelay = moving ? 5 : 0.5f;
		return _spitDelay > 0 || distance <= 85;
	}

	internal override void BeginAttack( GameObject target )
	{
		if ( _spitDelay > 0 )
		{
			_nextSpit = _spitDelay;
			base.BeginAttack( target );
			return;
		}
		var offset = target.WorldPosition - WorldPosition;
		if ( offset.Length > 85 || WorldRotation.Forward.Dot( offset.WithZ( 0 ).Normal ) < 0.7f ) return;
		var weak = target.GetComponent<Component.IDamageable>() is { Health: <= 35 };
		if ( Find( weak ? "whip" : "bite" ) is not { } melee ) return;
		if ( !weak ) EmitSound( "bullchicken/bc_attackgrowl", WorldPosition );
		PlayAction( melee );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id == 3 )
		{
			EyeClosed = true;
			_nextBlinkTick = 0.1f;
		}
		if ( id == 5 ) Launch( (GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero) + Vector3.Up * (-Scene.PhysicsWorld.Gravity.z * 0.3125f) );
		if ( id == 2 )
			PushVictim( TraceMelee( 70, 25, "slash", out _ ), -WorldRotation.Forward * 100 + Vector3.Up * 100 );
		if ( id == 4 )
		{
			var victim = TraceMelee( 70, 35, "club", out _, "alwaysgib" );
			var push = WorldRotation.Right * 200 + Vector3.Up * 100;
			if ( victim.IsValid() && victim.GameObject.GetComponent<PlayerController>() is { } player )
				PunchPlayer( player, push, new Angles( 20, 0, -20 ) );
			else
				PushVictim( victim, push );
		}
		if ( id == 6 && TraceMelee( 70, 0, null, out _ ) is { } prey )
		{
			EmitSound( $"bullchicken/bc_bite{Game.Random.Int( 2, 3 )}.wav", WorldPosition, pitch: Game.Random.Float( 0.9f, 1.1f ) );
			GroundShake.Emit( Scene, prey.WorldPosition, 25, 1.5f, 0.7f, 2 );
			if ( prey.GameObject.GetComponent<PlayerController>() is { } player )
			{
				using ( Rpc.FilterInclude( player.Network.Owner ?? Connection.Local ) )
					ThrowPlayer( player, (WorldRotation.Forward + Vector3.Up) * 300 );
			}
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ThrowPlayer( PlayerController player, Vector3 impulse )
	{
		if ( !player.IsValid() || player.IsProxy || !player.Body.IsValid() ) return;
		player.Body.Velocity += impulse;
	}

	internal override void FireProjectile( GameObject target, Vector3 origin, Vector3 direction )
	{
		var mouth = WorldPosition + WorldRotation.Forward * 37 + WorldRotation.Right * 8 + Vector3.Up * 23;
		var aim = (TargetEyePosition( target ) - mouth).Normal;
		aim += new Vector3( Game.Random.Float( -0.05f, 0.05f ), Game.Random.Float( -0.05f, 0.05f ), Game.Random.Float( -0.05f, 0 ) );
		Spit.Spray( mouth, aim, 15, 210, 0.25f );
		NpcProjectile.Fire<Spit>( GameObject, null, Definition.Damage, target, mouth, aim, null );
	}
}
