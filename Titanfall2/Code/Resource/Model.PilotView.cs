using Titanfall2.Formats;

/// <summary>Serialized animation events extracted from a retail pilot viewmodel MDL53.</summary>
[Sandbox.ModelEditor.GameData( "titanfall2_pilot_view" )]
public sealed class Titanfall2PilotViewData
{
	public enum EventKind { Sound, MuzzleFlash, ShellEject, HideClip, ShowClip, CreateParticle, StopParticle, AddParticleControlPoint }

	public sealed record ClipEvent( string Clip, float Cycle, EventKind Kind, string Options );

	public ClipEvent[] Events { get; set; } = [];
	public string Skeleton { get; set; }
	public string ClipFolder { get; set; }
	public string[] Clips { get; set; } = [];
	public bool ContinuousAds { get; set; }
}

partial class ModelLoader
{
	static readonly string[] IdleSequenceNames = ["idle_seq_autoplay", "idle_seq", "toss_hold_seq"];
	static readonly string[] DrawSequenceNames = ["draw_seq", "toss_prep_pullout_seq"];
	static readonly string[] FireSequenceNames = ["attack_seq", "toss_seq"];
	static readonly string[] ReloadSequenceNames = ["reload_seq", "reload_segStart_seq"];
	static readonly string[] EmptyReloadSequenceNames = ["reload_empty_seq", "reload_seq_empty", "reload_empty_segStart_seq"];
	static readonly string[] AdsInSequenceNames = ["ads_in_seq"];
	static readonly string[] AdsOutSequenceNames = ["ads_out_seq"];
	static readonly string[] SprintSequenceNames = ["sprint_seq", "toss_hold_sprinting_seq"];
	static readonly string[] SprintRaiseSequenceNames = ["sprintraise_seq"];
	static readonly string[] OneHandedIdleSequenceNames = ["idle_onehanded_seq"];

	internal readonly record struct PilotViewSequenceSet(
		Titanfall2Mdl53Reader.SequenceInfo Idle,
		Titanfall2Mdl53Reader.SequenceInfo Draw,
		Titanfall2Mdl53Reader.SequenceInfo Fire,
		Titanfall2Mdl53Reader.SequenceInfo Reload,
		Titanfall2Mdl53Reader.SequenceInfo EmptyReload,
		Titanfall2Mdl53Reader.SequenceInfo AdsIn,
		Titanfall2Mdl53Reader.SequenceInfo AdsOut,
		Titanfall2Mdl53Reader.SequenceInfo Sprint,
		Titanfall2Mdl53Reader.SequenceInfo SprintRaise,
		Titanfall2Mdl53Reader.SequenceInfo OneHandedIdle,
		Titanfall2Mdl53Reader.SequenceInfo Charge,
		Titanfall2Mdl53Reader.SequenceInfo Cooldown,
		Titanfall2Mdl53Reader.SequenceInfo[] EmptyReloadLate );

	internal static PilotViewSequenceSet ResolvePilotViewSequences( Titanfall2Mdl53Reader.ParsedModel model, string modelPath )
	{
		var idle = FindSequence( model, IdleSequenceNames, "ACT_VM_IDLE" )
			?? FindSequence( model, [], "ACT_VM_TOSS_HOLD" )
			?? throw new InvalidDataException( $"Pilot first-person idle sequence was not found in '{modelPath}'." );
		var draw = FindSequence( model, DrawSequenceNames, "ACT_VM_DRAW" )
			?? FindSequence( model, [], "ACT_VM_TOSS_PREP_PULLOUT" )
			?? throw new InvalidDataException( $"Pilot first-person draw sequence was not found in '{modelPath}'." );
		var fire = FindSequence( model, FireSequenceNames, "ACT_VM_PRIMARYATTACK" )
			?? FindSequence( model, [], "ACT_VM_TOSS" )
			?? FindSequence( model, [], "ACT_VM_TOSS_OVERHEAD" )
			?? throw new InvalidDataException( $"Pilot first-person fire sequence was not found in '{modelPath}'." );

		var reload = FindSequence( model, ReloadSequenceNames, "ACT_VM_RELOAD" );
		var emptyReload = FindSequence( model, EmptyReloadSequenceNames, "ACT_VM_RELOADEMPTY" );
		// L-STAR only has empty reload; ordnance/throwables have no reload and redraw from draw/pullout.
		reload ??= emptyReload ?? draw;
		emptyReload ??= reload;

		var adsIn = FindSequence( model, AdsInSequenceNames, "ACT_VM_ADS_IN" );
		var adsOut = FindSequence( model, AdsOutSequenceNames, "ACT_VM_ADS_OUT" );
		var sprint = FindSequence( model, SprintSequenceNames, "ACT_VM_SPRINT" )
			?? FindSequence( model, [], "ACT_VM_TOSS_HOLD_SPRINTING" );
		var sprintRaise = FindSequence( model, SprintRaiseSequenceNames, "ACT_VM_RAISE_FROM_SPRINT" );
		var oneHandedIdle = FindSequence( model, OneHandedIdleSequenceNames, "ACT_VM_ONEHANDED_IDLE" );

		var charge = FindSequence( model, ["charge_seq"], "ACT_VM_CHARGE" );
		var cooldown = FindSequence( model, ["cooldown_seq"], "ACT_VM_COOLDOWN" );
		var late = Enumerable.Range( 1, 5 ).Select( index => FindSequence( model, [$"reload_empty_late{index}_seq"], $"ACT_VM_RELOADEMPTY_LATE{index}" ) ).ToArray();
		return new PilotViewSequenceSet( idle, draw, fire, reload, emptyReload, adsIn, adsOut, sprint, sprintRaise, oneHandedIdle, charge, cooldown, late );
	}

	int AddPilotViewAnimations( ModelBuilder builder, Titanfall2Mdl53Reader.ParsedModel model, byte[] data )
	{
		var sequences = ResolvePilotViewSequences( model, ImportPath );
		var cache = new Dictionary<int, Titanfall2Mdl53AnimationReader.DecodedAnimation>();
		var events = new List<Titanfall2PilotViewData.ClipEvent>();

		var reload = Decode( sequences.Reload );
		var empty = Decode( sequences.EmptyReload );
		var draw = Decode( sequences.Draw );
		var idle = Decode( sequences.Idle );
		var fire = Decode( sequences.Fire );

		// Retail normal idle and fire clips are additive. The first frame of a non-delta
		// reload clip (or last frame of draw) is the authored ready stance, including both
		// hands and the camera rig.
		var readyFrame = !reload.Delta && reload.Frames.Length > 0 ? reload.Frames[0]
			: !empty.Delta && empty.Frames.Length > 0 ? empty.Frames[0]
			: !draw.Delta && draw.Frames.Length > 0 ? draw.Frames[^1]
			: throw new InvalidDataException( $"Pilot first-person ready stance was not found in '{ImportPath}'." );
		var ready = readyFrame.Select( Pose ).ToArray();

		var count = 0;
		var resourceClips = new List<string>();
		Add( Titanfall2PilotViewModel.Idle, idle, ready, loop: true );
		Add( Titanfall2PilotViewModel.Fire, fire, ready );
		Add( Titanfall2PilotViewModel.Reload, reload, ready );
		Add( Titanfall2PilotViewModel.EmptyReload, empty, ready );
		Add( Titanfall2PilotViewModel.Draw, draw, ready );
		count += 5;

		CollectEvents( Titanfall2PilotViewModel.Draw, sequences.Draw );
		CollectEvents( Titanfall2PilotViewModel.Fire, sequences.Fire );
		CollectEvents( Titanfall2PilotViewModel.Reload, sequences.Reload );
		CollectEvents( Titanfall2PilotViewModel.EmptyReload, sequences.EmptyReload );
		if ( sequences.Charge is { } charge )
		{
			Add( Titanfall2PilotViewModel.Charge, Decode( charge ), ready, loop: (charge.Sequence.Flags & 1) != 0 );
			CollectEvents( Titanfall2PilotViewModel.Charge, charge ); count++;
		}
		if ( sequences.Cooldown is { } cooldown )
		{
			Add( Titanfall2PilotViewModel.Cooldown, Decode( cooldown ), ready ); CollectEvents( Titanfall2PilotViewModel.Cooldown, cooldown ); count++;
			if ( cooldown.AnimationIndices.Length == 2 && sequences.AdsIn is { } ads )
			{
				var pose = Decode( ads ); var adsReady = ApplyBase( pose.Frames[^1], pose.Delta, ready );
				var decoded = Titanfall2Mdl53AnimationReader.Decode( data, model, cooldown.AnimationIndices[1], cooldown.Label, cooldown.Sequence.Flags );
				Add( Titanfall2PilotViewModel.CooldownAds, decoded, adsReady ); count++;
			}
		}
		for ( var i = 0; i < sequences.EmptyReloadLate.Length; ++i )
			if ( sequences.EmptyReloadLate[i] is { } late )
			{
				var role = Titanfall2PilotViewModel.EmptyReloadLate( i + 1 );
				Add( role, Decode( late ), ready ); CollectEvents( role, late ); count++;
			}

		if ( sequences.AdsIn is not null )
		{
			var adsIn = Decode( sequences.AdsIn );
			Add( Titanfall2PilotViewModel.AdsIn, adsIn, ready );
			count++;

			// Hold the authored ADS pose at the end of ads_in_seq and layer delta fire recoil over it.
			var adsPose = adsIn.Frames.Length > 0
				? ApplyBase( adsIn.Frames[^1], adsIn.Delta, ready )
				: ready;

			var adsIdleClip = builder.AddAnimation( Titanfall2PilotViewModel.AdsIdle, 30f ).WithLooping( true );
			adsIdleClip.AddFrame( adsPose );
			count++;

			Add( Titanfall2PilotViewModel.AdsFire, fire, adsPose );
			CollectEvents( Titanfall2PilotViewModel.AdsFire, sequences.Fire );
			count++;
		}

		if ( sequences.AdsOut is not null )
		{
			Add( Titanfall2PilotViewModel.AdsOut, Decode( sequences.AdsOut ), ready );
			count++;
		}

		if ( sequences.Sprint is not null )
		{
			Add( Titanfall2PilotViewModel.Sprint, Decode( sequences.Sprint ), ready, loop: true );
			count++;
		}

		if ( sequences.SprintRaise is not null )
		{
			Add( Titanfall2PilotViewModel.SprintRaise, Decode( sequences.SprintRaise ), ready );
			count++;
		}

		if ( sequences.OneHandedIdle is not null )
		{
			Add( Titanfall2PilotViewModel.OneHandedIdle, Decode( sequences.OneHandedIdle ), ready, loop: true );
			count++;
		}

		builder.WithData( new Titanfall2PilotViewData
		{
			Events = events.ToArray(), Skeleton = $"{Path}/pilot/skeleton.skel", ClipFolder = $"{Path}/pilot",
			Clips = resourceClips.ToArray(), ContinuousAds = idle.Delta && fire.Delta && sequences.AdsIn is not null
		} );

		return count;

		Titanfall2Mdl53AnimationReader.DecodedAnimation Decode( Titanfall2Mdl53Reader.SequenceInfo sequence )
		{
			if ( cache.TryGetValue( sequence.Index, out var decoded ) ) return decoded;
			decoded = Titanfall2Mdl53AnimationReader.Decode( data, model, sequence.AnimationIndices[0], sequence.Label, sequence.Sequence.Flags );
			cache[sequence.Index] = decoded;
			return decoded;
		}

		void Add( string name, Titanfall2Mdl53AnimationReader.DecodedAnimation animation, Transform[] basePose, bool loop = false )
		{
			resourceClips.Add( name );
			var clip = builder.AddAnimation( name, animation.FramesPerSecond ).WithLooping( loop );
			foreach ( var frame in animation.Frames )
				clip.AddFrame( ApplyBase( frame, animation.Delta, basePose ) );
		}

		void CollectEvents( string clipName, Titanfall2Mdl53Reader.SequenceInfo sequence )
		{
			if ( sequence.Events is not { Length: > 0 } ) return;
			foreach ( var evt in sequence.Events )
			{
				if ( TryMapPilotViewEvent( clipName, evt, out var mapped ) )
					events.Add( mapped );
			}
		}

		static Transform[] ApplyBase( Titanfall2Mdl53AnimationReader.BonePose[] frame, bool delta, Transform[] basePose )
		{
			var pose = new Transform[frame.Length];
			for ( int i = 0; i < pose.Length; ++i )
			{
				pose[i] = Pose( frame[i] );
				if ( delta )
					pose[i] = new Transform( basePose[i].Position + pose[i].Position,
						basePose[i].Rotation * pose[i].Rotation, basePose[i].Scale * pose[i].Scale );
			}
			return pose;
		}

		static Transform Pose( Titanfall2Mdl53AnimationReader.BonePose pose )
			=> new( ToSandbox( pose.Position ), ToSandbox( pose.Rotation ), ToSandbox( pose.Scale ) );
	}

	internal static bool TryMapPilotViewEvent( string clipName, Titanfall2Mdl53Reader.EventInfo evt, out Titanfall2PilotViewData.ClipEvent mapped )
	{
		mapped = null;
		if ( string.IsNullOrWhiteSpace( evt.Name ) ) return false;
		var particleEvent = evt.Name switch
		{
			"AE_CL_CREATE_PARTICLE_EFFECT" => Titanfall2PilotViewData.EventKind.CreateParticle,
			"AE_CL_STOP_PARTICLE_EFFECT" => Titanfall2PilotViewData.EventKind.StopParticle,
			"AE_CL_ADD_PARTICLE_EFFECT_CP" => Titanfall2PilotViewData.EventKind.AddParticleControlPoint,
			_ => (Titanfall2PilotViewData.EventKind?)null
		};
		if ( particleEvent is { } kind && !string.IsNullOrWhiteSpace( evt.Options ) )
		{ mapped = new( clipName, Math.Clamp( evt.Cycle, 0, 1 ), kind, evt.Options ); return true; }

		if ( evt.Name.Equals( "AE_CL_PLAYSOUND", StringComparison.OrdinalIgnoreCase )
			&& !string.IsNullOrWhiteSpace( evt.Options ) )
		{
			var sound = evt.Options.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault();
			if ( string.IsNullOrWhiteSpace( sound ) ) return false;
			mapped = new( clipName, Math.Clamp( evt.Cycle, 0f, 1f ), Titanfall2PilotViewData.EventKind.Sound, sound );
			return true;
		}

		if ( evt.Name.Equals( "AE_CL_VSCRIPT_CALLBACK", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( evt.Options.Equals( "muzzle_flash", StringComparison.OrdinalIgnoreCase ) )
			{
				mapped = new( clipName, Math.Clamp( evt.Cycle, 0f, 1f ), Titanfall2PilotViewData.EventKind.MuzzleFlash, string.Empty );
				return true;
			}
			if ( evt.Options.Equals( "shell_eject", StringComparison.OrdinalIgnoreCase ) )
			{
				mapped = new( clipName, Math.Clamp( evt.Cycle, 0f, 1f ), Titanfall2PilotViewData.EventKind.ShellEject, string.Empty );
				return true;
			}
		}

		if ( evt.Name.Equals( "AE_WPN_CLIPBODYGROUP_HIDE", StringComparison.OrdinalIgnoreCase ) )
		{
			mapped = new( clipName, Math.Clamp( evt.Cycle, 0f, 1f ), Titanfall2PilotViewData.EventKind.HideClip, string.Empty );
			return true;
		}

		if ( evt.Name.Equals( "AE_WPN_CLIPBODYGROUP_SHOW", StringComparison.OrdinalIgnoreCase ) )
		{
			mapped = new( clipName, Math.Clamp( evt.Cycle, 0f, 1f ), Titanfall2PilotViewData.EventKind.ShowClip, string.Empty );
			return true;
		}

		return false;
	}

	static Titanfall2Mdl53Reader.SequenceInfo FindSequence( Titanfall2Mdl53Reader.ParsedModel model, string[] preferredLabels, string fallbackActivity )
	{
		foreach ( var label in preferredLabels )
		{
			var match = model.Sequences.FirstOrDefault( entry =>
				entry.AnimationIndices.Length > 0
				&& entry.Label.Equals( label, StringComparison.OrdinalIgnoreCase ) );
			if ( match is not null ) return match;
		}

		Titanfall2Mdl53Reader.SequenceInfo fallback = null;
		foreach ( var entry in model.Sequences )
		{
			if ( entry.AnimationIndices.Length == 0
				|| !entry.ActivityName.Equals( fallbackActivity, StringComparison.OrdinalIgnoreCase ) )
				continue;

			fallback ??= entry;
			if ( !IsVariantSequenceLabel( entry.Label ) ) return entry;
		}

		return fallback;
	}

	static bool IsVariantSequenceLabel( string label ) =>
		label.Contains( "crouch", StringComparison.OrdinalIgnoreCase )
		|| label.Contains( "onehanded", StringComparison.OrdinalIgnoreCase )
		|| label.Contains( "slide", StringComparison.OrdinalIgnoreCase )
		|| label.Contains( "late", StringComparison.OrdinalIgnoreCase )
		|| label.Contains( "alt", StringComparison.OrdinalIgnoreCase );
}
