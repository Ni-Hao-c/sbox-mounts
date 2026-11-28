using Titanfall2.Formats;
using Sequence = Titanfall2.Formats.Titanfall2Mdl53Reader.SequenceInfo;
using Animation = Titanfall2.Formats.Titanfall2Mdl53AnimationReader.DecodedAnimation;

partial class ModelLoader
{
	// Retail puts many of these behind activity modifiers. Use exact known labels
	// so a missing crouch clip cannot silently pick a standing or optic variant.
	internal static Dictionary<string, Sequence> ResolvePilotStanceSequences( Titanfall2Mdl53Reader.ParsedModel model )
	{
		var roles = new Dictionary<string, Sequence>( StringComparer.Ordinal );
		Find( Titanfall2PilotViewModel.FirstDraw, "drawfirst_seq" );
		Add( Titanfall2PilotViewModel.FirstDraw, false, "drawfirst_crouch_seq" );
		Add( Titanfall2PilotViewModel.Draw, false, "draw_crouch_seq", "draw_seq_crouch" );
		Add( Titanfall2PilotViewModel.Draw, true, "draw_onehanded_crouch_seq" );
		Add( Titanfall2PilotViewModel.Reload, false, "reload_crouch_seq", "reload_segStart_crouch_seq" );
		Add( Titanfall2PilotViewModel.Reload, true, "reload_onehanded_crouch_seq", "reload_segStart_onehanded_crouch_seq" );
		Add( Titanfall2PilotViewModel.EmptyReload, false, "reload_empty_crouch_seq", "reload_seq_crouch_empty", "reload_empty_segStart_crouch_seq" );
		Add( Titanfall2PilotViewModel.EmptyReload, true, "reload_empty_onehanded_crouch_seq", "reload_onehanded_empty_crouch_seq", "reload_empty_segStart_onehanded_crouch_seq" );
		Add( Titanfall2PilotViewModel.ReloadSegLoop, false, "reload_segLoop_crouch_seq" );
		Add( Titanfall2PilotViewModel.ReloadSegLoop, true, "reload_segLoop_onehanded_crouch_seq" );
		Add( Titanfall2PilotViewModel.ReloadSegEnd, false, "reload_segEnd_crouch_seq" );
		Add( Titanfall2PilotViewModel.ReloadSegEnd, true, "reload_segEnd_onehanded_crouch_seq" );
		Add( Titanfall2PilotViewModel.ReloadEmptySegEnd, false, "reload_empty_end_crouch_seq", "reload_empty_crouch_end_seq" );
		Add( Titanfall2PilotViewModel.ReloadEmptySegEnd, true, "reload_empty_end_onehanded_crouch_seq" );
		for ( var i = 1; i <= 5; ++i )
		{
			var role = Titanfall2PilotViewModel.EmptyReloadLate( i );
			Add( role, false, $"reload_empty_late{i}_crouch_seq" );
			Add( role, true, $"reload_empty_onehanded_late{i}_crouch_seq", $"reload_empty_late{i}_onehanded_crouch_seq", $"reload_onehanded_empty_late{i}_crouch_seq" );
			// R-97's actual late-2 label ends in qeq; its activity and source clip are correct.
			if ( i == 2 && !roles.ContainsKey( Titanfall2PilotViewModel.StanceRole( role, true ) ) )
				Add( role, true, "reload_onehanded_empty_late2_crouch_qeq" );
		}
		Find( Titanfall2PilotViewModel.CrouchAdsIn, "ads_in_crouch_seq" );
		Find( Titanfall2PilotViewModel.CrouchAdsOut, "ads_out_crouch_seq" );
		Find( Titanfall2PilotViewModel.SlideRaise, "sprintraise_onehanded_seq_slide" );
		Find( Titanfall2PilotViewModel.SlideFire, "attack_onehanded_slide_manual_seq", "attack_onehanded_slide_manual_seq_sequential", "attack_seq_slide", "attack_onehanded_slide_seq" );
		// The retail slide ADS transition starts in the unzoomed slide grip. If
		// missing, use the final frame of the authored slide raise. No generic
		// one-handed idle or crouch transition is a valid replacement.
		if ( roles.TryGetValue( Titanfall2PilotViewModel.CrouchAdsIn, out var adsIn ) ) roles[Titanfall2PilotViewModel.SlideIdle] = adsIn;
		else if ( roles.TryGetValue( Titanfall2PilotViewModel.SlideRaise, out var raise ) ) roles[Titanfall2PilotViewModel.SlideIdle] = raise;
		return roles;

		void Add( string role, bool slide, params string[] labels ) => Find( Titanfall2PilotViewModel.StanceRole( role, slide ), labels );
		void Find( string role, params string[] labels )
		{
			if ( FindSequence( model, labels, null ) is { } sequence ) roles[role] = sequence;
		}
	}

	// Both samples must be additive for the graph to switch the entire shot
	// to the slide grip. EPG even uses its slide recoil in both cells.
	internal static Animation PilotSlideFireAds( byte[] data, Titanfall2Mdl53Reader.ParsedModel model, Sequence sequence, Animation hip )
	{
		if ( !hip.Delta ) return null;
		var adsIndex = ResolvePilotAdsAnimationIndex( Titanfall2SequenceRuntimeData.Read( data, model, sequence ) );
		if ( adsIndex == sequence.AnimationIndices[0] ) return hip;
		var ads = Titanfall2Mdl53AnimationReader.Decode( data, model, adsIndex, sequence.Label, sequence.Sequence.Flags );
		return ads.Delta ? ads : null;
	}

	internal static Animation PilotSlideIdle( Animation source, Sequence sequence )
	{
		var pose = sequence.ActivityName == "ACT_VM_ADS_IN" ? source.Frames[0] : source.Frames[^1];
		return new()
		{
			Name = Titanfall2PilotViewModel.SlideIdle,
			FramesPerSecond = source.FramesPerSecond,
			Delta = source.Delta,
			Looping = true,
			Frames = [pose, pose]
		};
	}
}
