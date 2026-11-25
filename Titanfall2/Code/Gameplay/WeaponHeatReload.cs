using Titanfall2.Formats;

/// <summary>An authored reload animation event, expressed in normalized sequence time.</summary>
public record struct Titanfall2HeatReloadEvent
{
	public int Milestone { get; set; }
	public float Cycle { get; set; }
}

/// <summary>One normal empty-reload sequence. Milestone zero selects the complete animation.</summary>
public record struct Titanfall2HeatReloadStage
{
	public int Milestone { get; set; }
	public float FillCycle { get; set; }
	public float ReadyCycle { get; set; }
	public Titanfall2HeatReloadEvent[] Events { get; set; }
}

/// <summary>Retail L-STAR reload metadata; callers resolve weapon/mod durations separately.</summary>
internal static class Titanfall2HeatReloadData
{
	internal static Titanfall2HeatReloadStage[] ReadStages( Titanfall2ContentSnapshot snapshot, string viewModelPath )
	{
		if ( snapshot is null || string.IsNullOrWhiteSpace( viewModelPath ) ) return [];
		var source = snapshot.GetSource( viewModelPath );
		if ( source is null || !source.TryReadAllBytes( out var bytes, out _ ) || bytes.Length > 64 * 1024 * 1024 ) return [];
		try { return ReadStages( Titanfall2Mdl53Reader.Parse( bytes, includeGeometry: false ) ); }
		catch ( Exception error ) when ( error is InvalidOperationException or InvalidDataException or ArgumentException ) { return []; }
	}

	internal static Titanfall2HeatReloadStage[] ReadStages( Titanfall2Mdl53Reader.ParsedModel model )
	{
		if ( model?.Sequences is null ) return [];
		var stages = new Titanfall2HeatReloadStage[5];
		for ( var milestone = 0; milestone < stages.Length; milestone++ )
		{
			// Several crouch/one-handed variants share activities. Select the verified
			// normal labels, never whichever matching activity happens to appear first.
			var label = milestone == 0 ? "reload_empty_seq" : $"reload_empty_late{milestone}_seq";
			var matches = model.Sequences.Where( sequence => string.Equals( sequence.Label, label, StringComparison.OrdinalIgnoreCase ) ).ToArray();
			if ( matches.Length != 1 || matches[0].Events is null ) return [];
			var events = matches[0].Events;
			var fills = events.Where( item => IsEvent( item, "AE_WPN_FILLAMMO", 75 ) ).ToArray();
			var ready = events.Where( item => IsEvent( item, "AE_WPN_READYTOFIRE", 76 ) ).ToArray();
			if ( fills.Length != 1 || ready.Length != 1 || !ValidCycle( fills[0].Cycle ) || !ValidCycle( ready[0].Cycle )
				|| fills[0].Cycle <= 0 || ready[0].Cycle < fills[0].Cycle ) return [];
			var schedule = new List<Titanfall2HeatReloadEvent>();
			foreach ( var item in events )
			{
				var next = EventMilestone( item );
				if ( next == 0 ) continue;
				if ( next <= milestone || next >= stages.Length || !ValidCycle( item.Cycle ) || item.Cycle >= fills[0].Cycle
					|| schedule.Any( previous => previous.Milestone == next ) ) return [];
				schedule.Add( new() { Milestone = next, Cycle = item.Cycle } );
			}
			var ordered = schedule.OrderBy( item => item.Cycle ).ThenBy( item => item.Milestone ).ToArray();
			for ( var i = 1; i < ordered.Length; i++ ) if ( ordered[i].Milestone <= ordered[i - 1].Milestone ) return [];
			stages[milestone] = new() { Milestone = milestone, FillCycle = fills[0].Cycle, ReadyCycle = ready[0].Cycle, Events = ordered };
		}
		return stages;
	}

	/// <summary>
	/// Native 1805AA000: resumed duration retains the original full-reload deadline
	/// and the selected late duration. Holstered time reduces only the former.
	/// Zero means invalid data; do not invent a usable stage for missing metadata.
	/// </summary>
	internal static float EffectiveDuration( float fullSeconds, float lateSeconds, double fullStartedAt, double now, int milestone )
	{
		if ( !Titanfall2ReloadProfile.ValidDuration( fullSeconds ) || milestone is < 0 or > 4 ) return 0;
		if ( milestone == 0 ) return fullSeconds;
		if ( !Titanfall2ReloadProfile.ValidDuration( lateSeconds ) || !double.IsFinite( fullStartedAt ) || !double.IsFinite( now ) ) return 0;
		var remaining = Math.Max( 0, fullSeconds - Math.Max( 0, now - fullStartedAt ) );
		return (float)Math.Max( remaining, lateSeconds );
	}

	static bool ValidCycle( float cycle ) => float.IsFinite( cycle ) && cycle is >= 0 and <= 1;
	static bool IsEvent( Titanfall2Mdl53Reader.EventInfo item, string name, int number ) =>
		string.Equals( item.Name, name, StringComparison.OrdinalIgnoreCase ) || string.IsNullOrWhiteSpace( item.Name ) && item.Event == number;
	static int EventMilestone( Titanfall2Mdl53Reader.EventInfo item )
	{
		for ( var i = 1; i <= 5; i++ ) if ( IsEvent( item, $"AE_WPN_RELOAD_MILESTONE_{i}", 78 + i ) ) return i;
		return 0;
	}
}
