using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

[Title( "Tentacle" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeTentacle" )]
[NpcDefinition( "monster_tentacle", "tentacle2", MonsterClass.AlienMonster, 75, 32, 64, 0, Movement = NpcMovement.Stationary, IdleSequence = "Pit_Idle", Blood = BloodColor.Yellow )]
public sealed partial class Tentacle : Npc
{
	public enum SurfaceSound { Silo, Dirt, Water, Silent }
	[Property] public SurfaceSound ImpactSurface { get; set; }
	[Property, Range( 0, 360 )] public float SweepArc { get; set; } = 130;
	ModelLoader _model;
	int _node, _heardLevel;
	float _heardYaw;
	Rotation _home;
	StudioSequence _playing;
	bool _backwards;
	TimeUntil _memory, _nextListen, _nextSong;
	bool _recovering;
	bool _atEngine, _ignited;
	SoundHandle _ambience;
	string _ambientSample;
	Stimulus? _lastNoise;

	internal override IEnumerable<StudioSequence> ExtraSequences => _model.Sequences.Where( clip =>
		clip.Description.EntryNode > 0 );

	internal override void OnModelLoaded( ModelLoader loader )
	{
		_model = loader;
		_contacts = Renderer.Model.HitboxSet.All.OrderByDescending( box => box.Tags.Has( "hitgroup_3" ) ).ToArray();
		_previousCenters = new Vector3?[_contacts.Length];
		_node = loader.Sequences[0].Description.ExitNode;
		_home = WorldRotation;
		Animation.TurnsBody = false;
		if ( Networking.IsHost )
		{
			var nearby = Scene.GetAllComponents<Tentacle>().Where( t => t != this && t.WorldPosition.Distance( WorldPosition ) <= 1024 );
			_ambientSample = !nearby.Any( t => t._ambientSample == "ambience/flies.wav" ) ? "ambience/flies.wav"
				: !nearby.Any( t => t._ambientSample == "ambience/squirm2.wav" ) ? "ambience/squirm2.wav" : null;
			if ( _ambientSample is not null ) PlayAmbience( _ambientSample );
		}
	}

	protected override void Think()
	{
		if ( _model is null || !Renderer.Animation.IsValid ) return;
		if ( !_atEngine && _nextListen <= 0 )
		{
			Listen();
			_nextListen = 0.1f;
		}
		if ( _atEngine || _memory > 0 && !_recovering )
		{
			var halfArc = SweepArc * 0.5f;
			var goal = _home * Rotation.FromYaw( _atEngine ? 180 : _heardYaw.Clamp( -halfArc, halfArc ) );
			WorldRotation = goal.Clamp( WorldRotation, 18 * Time.Delta, out _ );
		}
		var busy = AdvanceAction();
		if ( GameObject.IsDestroyed ) return;
		CheckContact();
		if ( busy ) return;
		var pit = Find( "Pit_Idle" );
		if ( !_atEngine && _recovering && _node == pit.Description.EntryNode )
		{
			Health = Definition.Health;
			_recovering = false;
			_memory = 0;
		}
		if ( !_atEngine && _memory <= 0 && _node == pit.Description.EntryNode ) return;
		var prefix = _heardLevel == 0 ? "Floor" : $"Lev{_heardLevel}";
		var clip = _atEngine ? EngineAction() : _recovering ? pit : ChooseAction( prefix );
		_playing = _model.TransitionTo( _node, clip, out _backwards );
		PlaySequence( _playing, _backwards );
	}

	StudioSequence ChooseAction( string prefix )
	{
		if ( _memory > 0 )
		{
			var angle = System.MathF.Abs( _heardYaw );
			var halfArc = SweepArc * 0.5f;
			return Find( $"{prefix}_{(angle <= halfArc + 30 ? "Strike" : angle <= SweepArc ? "Tap" : "Rear_Idle")}" );
		}
		if ( _nextSong <= 0 )
		{
			EmitSound( "tentacle/te_sing", Tip );
			_nextSong = Game.Random.Float( 10, 20 );
		}
		return Find( $"{prefix}_{(Game.Random.Int( 0, 3 ) == 0 ? "Tap" : "Idle")}" );
	}

	/// <summary>Raise the tentacle to its engine-level idle and swat animations.</summary>
	public void ReachForEngine()
	{
		if ( Networking.IsHost ) _atEngine = true;
	}

	/// <summary>Burn the tentacle in the rocket exhaust and remove it after the death sequence.</summary>
	public void Ignite()
	{
		if ( !Networking.IsHost ) return;
		_atEngine = true;
		_ignited = true;
	}

	StudioSequence EngineAction()
	{
		if ( _ignited ) return Find( "Engine_death1" );
		if ( _node != Find( "Engine_Idle" ).Description.EntryNode ) return Find( "Engine_Idle" );
		return Find( Game.Random.Int( 0, 2 ) switch { 0 => "Engine_Sway", 1 => "Engine_Swat", _ => "Engine_Bob" } );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayAmbience( string sample )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.NpcAudio.Find( sample ) is not { } path
			|| SoundFile.Load( path ) is not { } sound ) return;
		_ambience = GoldSrcSoundEvents.Play( sound, WorldPosition );
	}

	protected override void OnDisabled()
	{
		_ambience?.Stop();
		base.OnDisabled();
	}

	void Listen()
	{
		if ( Senses.Disturbance is { } noise && noise != _lastNoise )
		{
			_lastNoise = noise;
			Hear( noise.Position );
			return;
		}
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !player.IsOnGround || player.IsDucking || player.Velocity.WithZ( 0 ).Length < Senses.QuietSpeed ) continue;
			if ( WorldPosition.Distance( player.WorldPosition ) > Senses.HearingRange ) continue;
			Hear( player.WorldPosition );
			return;
		}
	}

	void Hear( Vector3 position )
	{
		var local = _home.Inverse * (position - WorldPosition);
		_heardYaw = local.EulerAngles.Normal.yaw;
		_heardLevel = local.z < 216 ? 0 : local.z < 408 ? 1 : local.z < 600 ? 2 : 3;
		_memory = Game.Random.Float( 5, 10 );
	}

	internal override void FinishAction( string name )
	{
		if ( _ignited && name.Equals( "Engine_death1", System.StringComparison.OrdinalIgnoreCase ) )
		{
			GameObject.Destroy();
			return;
		}
		if ( _playing is not null )
			_node = _backwards ? _playing.Description.EntryNode : _playing.Description.ExitNode;
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		base.ModifyDamage( damage );
		if ( _atEngine ) damage.AcceptedDamage = 0;
		else if ( damage.AcceptedDamage >= Health - 1 )
		{
			damage.AcceptedDamage = System.MathF.Max( 0, Health - 1 );
			_recovering = true;
		}
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		ShowWound( damage );
		if ( damage.Instigator.IsValid() && !_recovering ) Hear( damage.Instigator.WorldPosition );
	}
}
