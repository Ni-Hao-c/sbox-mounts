using Titanfall2.Formats;

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2RechamberProfile RechamberProfile { get; init; }
}

/// <summary>Source bolt/ready event times; the state machine applies the retail playback rate.</summary>
public record struct Titanfall2RechamberProfile
{
	public float AnimationSeconds { get; set; }
	public float BoltSeconds { get; set; }
	public float ReadySeconds { get; set; }

	internal static Titanfall2RechamberProfile Read( Titanfall2ContentSnapshot snapshot, string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return default;
		var source = snapshot?.GetSource( path );
		if ( source is null || !source.TryReadAllBytes( out var bytes, out _ ) ) return default;
		try
		{
			var model = Titanfall2Mdl53Reader.Parse( bytes, includeGeometry: false );
			var sequence = ModelLoader.ResolvePilotViewSequences( model, path ).Rechamber;
			if ( sequence is null ) return default;
			var animation = Titanfall2Mdl53AnimationReader.Decode( bytes, model, sequence.AnimationIndices[0], sequence.Label, sequence.Sequence.Flags );
			var duration = (animation.Frames.Length - 1) / animation.FramesPerSecond;
			if ( !float.IsFinite( duration ) || duration <= 0 ) return default;
			float Seconds( string name, int number )
			{
				var item = sequence.Events.FirstOrDefault( evt => evt.Name == name || string.IsNullOrWhiteSpace( evt.Name ) && evt.Event == number );
				return item is not null && float.IsFinite( item.Cycle ) && item.Cycle is >= 0 and <= 1 ? duration * item.Cycle : 0;
			}
			return new() { AnimationSeconds = duration, BoltSeconds = Seconds( "AE_WPN_RECHAMBER", 77 ), ReadySeconds = Seconds( "AE_WPN_READYTOFIRE", 76 ) };
		}
		catch ( Exception error ) when ( error is InvalidOperationException or InvalidDataException or ArgumentException )
		{
			Titanfall2Log.Warning( $"Titanfall 2 rechamber metadata unavailable for '{path}': {error.Message}" );
			return default;
		}
	}
}
