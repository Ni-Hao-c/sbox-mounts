namespace GoldSrc.HalfLife;

[Title( "Gonarch" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeGonarch" )]
[NpcDefinition( "monster_bigmomma", "big_mom", MonsterClass.AlienMonster, 225, 32, 64, 140, Attack = NpcAttack.Projectile, Damage = 120, Range = 800, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class Gonarch : Npc
{
	internal override float ViewDot => 0.3f;

	internal override NpcSounds Sounds { get; } = new( Alert: "gonarch/gon_alert", Death: "gonarch/gon_die", Attack: "gonarch/gon_attack" );
	StudioSequence[] _claws;
	internal override IEnumerable<StudioSequence> ExtraSequences
	{
		get
		{
			_claws = Activities( 30 ).ToArray();
			return [.. _claws, Find( "spawn" ), Find( "mortar" ), Find( "jump" )];
		}
	}
	const string PainSample = "gonarch/gon_pain";
	static float SoundPitch => Game.Random.Int( 95, 105 ) * 0.01f;
	TimeUntil _nextBirth, _nextMortar, _nextPain;
	SoundHandle _voice;
	bool _secondChild;
	Vector3 _throw;
	GameObject _enemy;
	readonly List<BabyHeadcrab> _children = [];

	public void JumpForward()
	{
		if ( Networking.IsHost && IsOnGround && Find( "jump" ) is { } jump ) PlayAction( jump );
	}

	internal override void BeginAttack( GameObject target )
	{
		_enemy = target;
		var offset = target.WorldPosition - WorldPosition;
		if ( _nextMortar <= 0 && offset.Length <= AttackRange && CanToss( target.GetBounds().Center ) )
			PlayAction( Find( "mortar" ) );
		else if ( _claws.Length > 0 && offset.Length <= 170 && Vector3.Dot( offset.WithZ( 0 ).Normal, WorldRotation.Forward ) >= 0.7f )
			PlayAction( _claws[Game.Random.Int( 0, _claws.Length - 1 )] );
		else if ( _nextBirth <= 0 && CanLay() )
			PlayAction( Find( "spawn" ) );
	}

	bool CanLay()
	{
		_children.RemoveAll( child => !child.IsValid() );
		if ( _children.Count >= 20 ) return false;
		var area = new BBox( WorldPosition - new Vector3( 32, 32, 0 ), WorldPosition + new Vector3( 32, 32, 1 ) );
		return !Scene.FindInPhysics( area ).Any( obj => obj.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is { } npc && npc != this );
	}

	Vector3 MortarOrigin => WorldPosition + Vector3.Up * 180;

	bool CanToss( Vector3 destination )
	{
		var start = MortarOrigin;
		var middle = (start + destination) * 0.5f;
		var top = Scene.Trace.Ray( middle, middle + Vector3.Up * Game.Random.Float( 150, 500 ) ).IgnoreGameObjectHierarchy( GameObject ).Run().EndPosition;
		var height = top.z - start.z - 15;
		var gravity = -Scene.PhysicsWorld.Gravity.z;
		if ( height <= 0 || gravity <= 0 || Scene.Trace.Ray( start, top ).IgnoreGameObjectHierarchy( GameObject ).Run().Hit ) return false;
		var rise = System.MathF.Sqrt( 2 * gravity * height );
		_throw = ((destination - start) * (0.5f * gravity / rise)).WithZ( rise );
		return true;
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id == 11 )
		{
			_nextMortar = Game.Random.Float( 2, 15 );
			EmitSound( "gonarch/gon_sack", WorldPosition, pitch: SoundPitch );
			NpcProjectile.Fire<AcidMortar>( GameObject, ProjectilePath, Definition.Damage, _enemy, MortarOrigin, _throw.Normal, _throw.Length );
		}
		else if ( id == 12 ) LayCrab();
		else if ( id == 13 )
		{
			WorldPosition += Vector3.Up;
			Launch( WorldRotation.Forward * 200 + WorldRotation.Up * 500 );
		}
		else if ( id is 8 or 9 or 10 ) Swipe( id );
	}

	internal override void PlaySequenceSound( NpcAudio audio, int id )
	{
		var name = id switch
		{
			1 or 2 or 3 or 4 => "gonarch/gon_step", 5 when Game.Random.Int( 0, 100 ) < 30 => "gonarch/gon_sack", 6 => Sounds.Death,
			14 => Sounds.Alert, 15 => PainSample, 16 => Sounds.Attack, 17 => "gonarch/gon_birth", _ => null
		};
		if ( name is null ) return;
		if ( id is 6 or 14 or 15 ) PlayVoice( audio.Find( name ), SoundPitch );
		else NpcAudio.Play( audio.Find( name ), WorldPosition, pitch: SoundPitch );
	}

	void PlayVoice( string path, float pitch )
	{
		_voice?.Stop();
		_voice = NpcAudio.Play( path, WorldPosition, pitch: pitch );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayPainVoice( string path, float pitch ) => PlayVoice( path, pitch );

	void LayCrab()
	{
		var prefab = PrefabFile.Load( "mount://hl1/entities/npcs/monster_babycrab.prefab" );
		if ( prefab is null ) return;
		var child = GameObject.Clone( prefab, WorldTransform, startEnabled: false );
		var crab = child.GetComponent<BabyHeadcrab>( true );
		crab.Mother = this;
		_children.Add( crab );
		child.Enabled = true;
		child.NetworkSpawn( true, null );
		_nextBirth = _secondChild ? Game.Random.Float( 5, 10 ) : Game.Random.Float( 0.5f, 2.5f );
		_secondChild = !_secondChild;
		var hit = Scene.Trace.Ray( WorldPosition + Vector3.Up, WorldPosition + Vector3.Down * 100 )
			.WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run();
		if ( hit.Hit ) BirthMark( hit.EndPosition, hit.Normal );
		EmitSound( "gonarch/gon_birth", WorldPosition, pitch: SoundPitch );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void BirthMark( Vector3 position, Vector3 normal ) => ImpactDecal.Place( position, normal, "mommablob", 0 );

	internal void OnChildDied( BabyHeadcrab child )
	{
		_children.Remove( child );
		if ( Health > 0 ) EmitSound( "gonarch/gon_childdie", WorldPosition, pitch: SoundPitch );
	}

	void Swipe( int id )
	{
		var center = WorldPosition + WorldRotation.Forward * 128;
		var area = new BBox( center - new Vector3( 64, 64, 0 ), center + new Vector3( 64, 64, 64 ) );
		foreach ( var obj in Scene.FindInPhysics( area ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component victim || victim == this || victim is BabyHeadcrab child && child.Mother == this ) continue;
			var player = victim.GameObject.GetComponent<PlayerController>();
			if ( victim is not Sandbox.Npcs.BaseNpc && !player.IsValid() ) continue;
			var hit = new DamageInfo { Damage = 60, Attacker = GameObject, Weapon = GameObject, Origin = WorldPosition, Position = victim.WorldPosition };
			hit.Tags.Add( "slash" );
			hit.Tags.Add( "crush" );
			((IDamageable)victim).OnDamage( hit );
			if ( victim.IsValid() )
			{
				var push = id == 10 ? WorldRotation.Forward * 220 + Vector3.Up * 200 : WorldRotation.Forward * 150 + Vector3.Up * 250 + WorldRotation.Right * (id == 8 ? -200 : 200);
				if ( player.IsValid() ) PunchPlayer( player, push, new Angles( 15, 0, 0 ) );
				else PushVictim( victim, push );
			}
			EmitSound( "zombie/claw_strike", WorldPosition, pitch: SoundPitch );
			break;
		}
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		base.ModifyDamage( damage );
		var info = damage.DamageInfo;
		if ( HitStudioModel( info ) )
		{
			if ( !HitGroups( info ).Has( "hitgroup_1" ) )
			{
				if ( ShouldShowArmorImpact() ) ArmorRicochet.Show( info.Position, Game.Random.Float( 1, 2 ) );
				damage.AcceptedDamage = 0.1f;
			}
			else if ( _nextPain <= 0 )
			{
				_nextPain = Game.Random.Int( 1, 3 );
				if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
					PlayPainVoice( mount.NpcAudio.Find( PainSample ), SoundPitch );
			}
		}
		if ( info.Tags.Has( "acid" ) ) damage.Cancelled = true;
	}
}
