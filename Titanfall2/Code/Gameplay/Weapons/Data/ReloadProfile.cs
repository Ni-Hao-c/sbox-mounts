/// <summary>
/// Retail whole-magazine and segmented (shell-by-shell) reload durations after WeaponData
/// inheritance and MP_BASE. Late-stage durations and authored model events are independent inputs.
/// </summary>
public record struct Titanfall2ReloadProfile
{
	public bool Enabled { get; set; }
	public float NormalSeconds { get; set; }
	public float EmptySeconds { get; set; }
	public bool Segmented { get; set; }
	public int SegmentAmmo { get; set; }
	public float LoopSeconds { get; set; }
	public float EndSeconds { get; set; }
	public float EmptyEndSeconds { get; set; }
	public float[] EmptyLateSeconds { get; set; }
	public Titanfall2HeatReloadStage[] EmptyStages { get; set; }

	internal readonly float DurationFor( bool startedEmpty, float fallback )
	{
		var selected = startedEmpty ? EmptySeconds : NormalSeconds;
		if ( Enabled && ValidDuration( selected ) ) return selected;
		if ( Enabled && ValidDuration( NormalSeconds ) ) return NormalSeconds;
		return ValidDuration( fallback ) ? fallback : 2;
	}

	internal readonly float LoopDurationFor( float fallback ) =>
		Enabled && ValidDuration( LoopSeconds ) ? LoopSeconds
			: ValidDuration( fallback ) ? fallback : 0.75f;

	internal readonly float EndDurationFor( bool startedEmpty, float fallback )
	{
		var selected = startedEmpty ? EmptyEndSeconds : EndSeconds;
		if ( Enabled && ValidDuration( selected ) ) return selected;
		if ( Enabled && ValidDuration( EndSeconds ) ) return EndSeconds;
		return ValidDuration( fallback ) ? fallback : 0.55f;
	}

	internal static bool ValidDuration( float seconds ) => float.IsFinite( seconds ) && seconds is > 0 and <= 30;
}

/// <summary>One atomic, host-authored decision; elapsed time is measured on the scene clock.</summary>
public record struct Titanfall2ReloadSnapshot
{
	public int Serial { get; set; }
	public bool Active { get; set; }
	public bool StartedEmpty { get; set; }
	public bool Segmented { get; set; }
	public float Duration { get; set; }
	public float LoopDuration { get; set; }
	public float EndDuration { get; set; }
	public double StartedAt { get; set; }
	public int Milestone { get; set; }

	internal readonly float ElapsedAt( double now ) => !double.IsFinite( now ) || !double.IsFinite( StartedAt ) || !Titanfall2ReloadProfile.ValidDuration( Duration )
		? 0 : (float)Math.Clamp( now - StartedAt, 0, Duration );
}
