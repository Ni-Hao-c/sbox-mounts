using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

[Title( "Barnacle" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeBarnacle" )]
[NpcDefinition( "monster_barnacle", "barnacle", MonsterClass.AlienMonster, 25, 16, 32, 0, Attack = NpcAttack.Melee, Damage = 35, Range = 2048, Movement = NpcMovement.Stationary, AttackSequence = "attack1", StrikeEvents = new[] { 1 }, Blood = BloodColor.Red )]
public sealed class Barnacle : Npc
{
	internal override NpcSounds Sounds { get; } = new( Alert: "barnacle/bcl_alert", Death: "barnacle/bcl_die", Attack: "barnacle/bcl_bite" );

	static readonly string[] Controls = [StudioAnimationGraphs.ExtensionParameter];
	internal override string[] ControllerParameters => Controls;
	StudioController _tongue;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		EyeHeight = 0;
		_tongue = loader.Controllers.FirstOrDefault();
		if ( GetComponentInChildren<BoxCollider>() is { } hull ) hull.Center = Vector3.Down * (Definition.Height * 0.5f);
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent hit )
	{
		if ( hit.DamageInfo.Tags.Has( "club" ) ) hit.AcceptedDamage = Health;
		else base.ModifyDamage( hit );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		Release();
		base.OnDeath( death );
	}

	internal override bool AnchoredCorpse => Scene.Trace.Ray( WorldPosition, WorldPosition + Vector3.Up * 4 )
		.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "trigger" ).Run().Hit;
	internal override float ModelOriginHeight => Definition.Height;
	public enum FeedingState { Waiting, Lifting, Chewing }

	[Sync( SyncFlags.FromHost )] FeedingState State { get; set; }
	[Sync( SyncFlags.FromHost )] float TongueLength { get; set; }
	GameObject _prey;
	Rigidbody _heldBody;
	Vector3 _mouthOffset;
	TimeUntil _bite, _nextChew;
	TimeUntil _scan;
	bool _placed;
	int _storedGibs;

	[Sync( SyncFlags.FromHost )] string IdleSequence { get; set; }

	internal override void FinishMovementCycle()
	{
		if ( State == FeedingState.Waiting ) IdleSequence = WeightedActivity( Idle )?.Name;
	}

	static readonly string[] FeedingActions = ["attack1", "chewcycle"];
	internal override string[] ExtraActions => FeedingActions;
	internal override IEnumerable<StudioSequence> ExtraSequences => base.ExtraSequences.Concat( Activities( Idle ) );
	internal override StudioSequence RestSequence => State switch
	{
		FeedingState.Lifting => Find( "attack1" ),
		FeedingState.Chewing => Find( "chewcycle" ),
		_ => Find( IdleSequence ) ?? base.RestSequence
	};

	internal override void PlaceAtSpawn()
	{
		var ceiling = Scene.Trace.Ray( WorldPosition + Vector3.Up * 4, WorldPosition + Vector3.Up * 2048 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "trigger" ).Run();
		if ( ceiling.Hit ) WorldPosition = ceiling.EndPosition - Vector3.Up;
		IdleSequence = WeightedActivity( Idle )?.Name;
		_scan = 0.5f;
		_placed = true;
	}

	protected override void Think()
	{
		if ( !_placed ) return;
		if ( State != FeedingState.Waiting )
		{
			if ( IsLiveTarget( _prey ) && (_prey.GetComponent<BaseNpc>() is not { } captive || captive.PhysicsCarrier == GameObject) ) LiftPrey();
			else Release();
			return;
		}
		if ( _scan > 0 ) return;
		_scan = 0.1f;
		if ( _storedGibs > 0 && Game.Random.Int( 0, 99 ) == 1 )
		{
			_storedGibs--;
			CoughGib( WorldPosition );
			EmitSound( "barnacle/bcl_chew", WorldPosition );
		}
		var end = WorldPosition + Vector3.Down * 2048;
		var floor = Scene.Trace.Ray( WorldPosition, end ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run();
		var length = WorldPosition.Distance( floor.EndPosition );
		var extended = TongueLength >= length;
		TongueLength = System.MathF.Min( TongueLength + 8, length );
		if ( !extended ) return;

		var box = new BBox( WorldPosition - new Vector3( 8, 8, length ), WorldPosition + new Vector3( 8, 8, 0 ) );
		foreach ( var body in Scene.FindInPhysics( box ) )
		{
			var target = body.GetComponentInParent<BaseNpc>()?.GameObject ?? body.GetComponentInParent<PlayerController>()?.GameObject;
			if ( !IsLiveTarget( target ) || GetDisposition( target ) != Disposition.Hostile ) continue;
			var npc = target.GetComponent<BaseNpc>();
			if ( npc.IsValid() && npc.PhysicsCarrier.IsValid() ) continue;
			var playerBody = target.GetComponent<PlayerController>()?.Body;
			if ( playerBody is { MotionEnabled: false } ) continue;
			_prey = target;
			target.WorldPosition = WorldPosition.WithZ( target.WorldPosition.z );
			npc?.StartCarry( GameObject );
			if ( target.GetComponent<Rigidbody>() is { MotionEnabled: true } movingBody ) movingBody.Velocity = Vector3.Zero;
			if ( playerBody.IsValid() )
			{
				playerBody.MotionEnabled = false;
				_heldBody = playerBody;
			}
			if ( npc is Npc mounted ) mounted.SetCaptured( false );
			TongueLength = WorldPosition.z - TargetEyePosition( target ).z;
			State = FeedingState.Lifting;
			EmitSound( "barnacle/bcl_alert2.wav", WorldPosition );
			break;
		}
	}

	void LiftPrey()
	{
		if ( _prey.GetComponent<Rigidbody>() is { MotionEnabled: true } body )
			body.Velocity = Vector3.Zero;
		if ( State == FeedingState.Lifting )
		{
			var eyeHeight = TargetEyePosition( _prey ).z - _prey.WorldPosition.z;
			var mouthHeight = WorldPosition.z - 36 - eyeHeight;
			var height = _prey.WorldPosition.z;
			var next = height + System.Math.Clamp( mouthHeight - height, 0, 80 * Time.Delta );
			var neck = WorldPosition - Rotation.FromYaw( _prey.WorldRotation.Yaw() ).Forward * 6;
			_prey.WorldPosition = neck.WithZ( next );
			TongueLength = WorldPosition.z - next - eyeHeight;
			if ( next < mouthHeight ) return;

			_mouthOffset = _prey.WorldPosition - WorldPosition;
			State = FeedingState.Chewing;
			EmitSound( "barnacle/bcl_bite3.wav", WorldPosition );
			_bite = 10;
			Chomp();
			return;
		}

		_prey.WorldPosition = WorldPosition + _mouthOffset;
		if ( _bite <= 0 )
		{
			Bite( playerChomp: false );
		}
		else if ( _nextChew <= 0 )
		{
			_nextChew = 0.1f;
			if ( Game.Random.Int( 0, 49 ) == 0 )
			{
				EmitSound( "barnacle/bcl_chew", WorldPosition );
				Chomp();
			}
		}
	}

	void Chomp()
	{
		if ( _prey.GetComponent<PlayerController>().IsValid() ) Bite( playerChomp: true );
		else _prey.GetComponent<Npc>()?.SetCaptured( true );
	}

	void Bite( bool playerChomp )
	{
		var victim = _prey.GetComponent<IDamageable>();
		if ( victim is null ) { Release(); return; }
		var damage = new DamageInfo { Damage = victim.Health + (playerChomp ? victim.Armour : 0), Attacker = GameObject, Weapon = GameObject, Origin = WorldPosition, Position = TargetEyePosition( _prey ) };
		damage.Tags.Add( "slash" );
		damage.Tags.Add( "alwaysgib" );
		victim.OnDamage( damage );
		if ( !_prey.IsValid() || victim.Health <= 0 )
		{
			if ( !playerChomp ) _storedGibs = 3;
			Release();
		}
		else if ( !playerChomp ) _bite = 0.1f;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _tongue is null || !Renderer.IsValid() || !Renderer.Animation.IsValid ) return;
		var adjustment = State == FeedingState.Waiting ? 100 : 20;
		Renderer.Animation.Parameters.TrySet( StudioAnimationGraphs.ExtensionParameter, _tongue.Fraction( adjustment - TongueLength ) );
	}

	internal static void CoughGib( Vector3 position )
	{
		var hull = new BBox( new Vector3( -16, -16, -32 ), new Vector3( 16, 16, 0 ) );
		Gib.Scatter( hull + position, position, Vector3.Zero, 0, true, BloodColor.Red, 1, false );
	}

	internal override NpcCorpse AddCorpseComponent( GameObject go ) => go.AddComponent<DeadBarnacle>();

	void Release()
	{
		if ( _heldBody.IsValid() ) _heldBody.MotionEnabled = true;
		_heldBody = null;
		if ( _prey.IsValid() ) _prey.GetComponent<BaseNpc>()?.StopCarry( GameObject );
		_prey = null;
		State = FeedingState.Waiting;
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) Release();
		base.OnDisabled();
	}
}
