public sealed partial class Titanfall2PilotViewModel
{
	internal const string Rechamber = "pilot_fp_rechamber";
	internal const string RechamberAds = "pilot_fp_rechamber_ads";
	int _presentedRechamberSerial;

	void TickRechamberPresentation()
	{
		if ( !_owner.IsValid() ) return;
		if ( !_owner.IsRechambering )
		{
			if ( _action is Rechamber or RechamberAds ) Play( Idle );
			return;
		}
		if ( _owner.IsReloading || _presentedRechamberSerial == _owner.RechamberSerial || !_clips.Contains( Rechamber ) ) return;
		_presentedRechamberSerial = _owner.RechamberSerial;
		var role = !UsesContinuousAds && PresentationZoom >= 0.5f && _clips.Contains( RechamberAds ) ? RechamberAds : Rechamber;
		Play( role, _owner.RechamberDuration, _owner.RechamberElapsed );
	}
}
