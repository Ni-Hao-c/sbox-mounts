namespace GoldSrc.HalfLife;

public partial class Npc
{
	// Studio activities 40�43 are the hit, pull, chomp and chew victim animations.
	StudioSequence[] _captureAnimations;
	[Sync( SyncFlags.FromHost )] int CaptureStage { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince CaptureTime { get; set; }

	protected bool IsBarnacleVictim => CaptureStage != 0;

	void SetUpCaptureAnimations() => _captureAnimations = [Activity( 40 ), Activity( 41 ), Activity( 42 ), Activity( 43 )];

	internal void SetCaptured( bool bitten )
	{
		CaptureStage = bitten ? 2 : 1;
		CaptureTime = 0;
	}

	protected override void OnCarryStopped( GameObject previous )
	{
		base.OnCarryStopped( previous );
		CaptureStage = 0;
	}

	StudioSequence CaptureSequence( out float time )
	{
		time = CaptureTime;
		if ( CaptureStage == 0 ) return null;
		var index = CaptureStage == 1 ? 0 : 2;
		var first = _captureAnimations[index];
		if ( first is not null && time < first.Duration ) return first;
		var loop = _captureAnimations[index + 1];
		if ( loop is not null ) time = (time - (first?.Duration ?? 0)) % loop.Duration;
		return loop;
	}
}
