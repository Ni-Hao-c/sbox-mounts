public sealed partial class Titanfall2PilotViewModel
{
	internal const string TossPullout = "pilot_fp_toss_pullout";
	internal const string TossPrep = "pilot_fp_toss_prep";
	internal const string TossHold = "pilot_fp_toss_hold";
	internal const string Toss = "pilot_fp_toss";
	internal const string Detonate = "pilot_fp_detonate";

	internal static bool IsOrdnanceAction( string role ) => role is TossPullout or TossPrep or TossHold or Toss or Detonate;

	internal bool PlayOrdnanceAction( string role, float seconds = 0, float elapsed = 0 )
	{
		if ( !Renderer.IsValid() || !Renderer.SceneModel.IsValid() || !_clips.Contains( role ) ) return false;
		_aiming = _sprinting = false;
		// Broadcasts can arrive slightly after the host start. Seek their pose to the
		// correct cycle, but still fire the original frame-zero throw or trigger sound.
		Play( role, seconds, elapsed, eventCursor: -0.001f );
		return true;
	}

	internal void CancelOrdnanceAction()
	{
		if ( Renderer.IsValid() && Renderer.SceneModel.IsValid() && IsOrdnanceAction( _action ) ) Play( Idle );
	}

	// A held charge has its own ready pose. Watch the action here instead of adding
	// a second gameplay timer. A short click may go straight from pullout to toss.
	bool TickOrdnancePresentation()
	{
		if ( _action == TossHold ) return true;
		if ( _actionEnds > 0 || _action is not (TossPullout or TossPrep) ) return false;
		if ( _action == TossPullout && PlayOrdnanceAction( TossPrep ) ) return true;
		return PlayOrdnanceAction( TossHold );
	}
}
