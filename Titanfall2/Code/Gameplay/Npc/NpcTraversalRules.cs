using Sandbox.Engine.Resources;

public enum Titanfall2TraversalFamily { Auto, None, Human, Spectre, Stalker, Reaper, Titan, Northstar }
public enum Titanfall2TraversalKind { None, Jump, Drop, Climb, Ladder }
public enum Titanfall2TraversalPhase { None, Align, Traverse, Recover, Falling }

/// <summary>Conservative movement envelopes, not a claim that every retail LongJump selector is enabled.</summary>
internal readonly record struct Titanfall2TraversalProfile( float Rise, float Drop, float Distance, float ArcHeight, float Speed, bool Climb )
{
	internal bool Enabled => Distance > 0;
}

internal static class Titanfall2NpcTraversalRules
{
	internal static Titanfall2TraversalFamily ResolveFamily( Titanfall2TraversalFamily requested, string model, bool titan )
	{
		if ( requested != Titanfall2TraversalFamily.Auto ) return requested;
		var path = model?.ToLowerInvariant() ?? "";
		if ( titan ) return path.Contains( "raptor", StringComparison.Ordinal ) ? Titanfall2TraversalFamily.Northstar : Titanfall2TraversalFamily.Titan;
		if ( path.Contains( "super_spectre", StringComparison.Ordinal ) || path.Contains( "superspectre", StringComparison.Ordinal ) ) return Titanfall2TraversalFamily.Reaper;
		if ( path.Contains( "stalker", StringComparison.Ordinal ) ) return Titanfall2TraversalFamily.Stalker;
		if ( path.Contains( "spectre", StringComparison.Ordinal ) ) return Titanfall2TraversalFamily.Spectre;
		if ( path.Contains( "/humans/", StringComparison.Ordinal ) || path.Contains( "/pilots/", StringComparison.Ordinal ) ) return Titanfall2TraversalFamily.Human;
		return Titanfall2TraversalFamily.None;
	}

	internal static Titanfall2TraversalProfile Profile( Titanfall2TraversalFamily family ) => family switch
	{
		Titanfall2TraversalFamily.Human => new( 128, 256, 300, 56, 200, true ),
		Titanfall2TraversalFamily.Spectre => new( 320, 512, 720, 100, 300, true ),
		Titanfall2TraversalFamily.Stalker => new( 128, 256, 300, 64, 150, true ),
		Titanfall2TraversalFamily.Reaper => new( 384, 768, 1100, 140, 400, false ),
		Titanfall2TraversalFamily.Northstar => new( 360, 512, 1600, 140, 450, false ),
		_ => default
	};

	internal static bool Allows( Titanfall2TraversalProfile profile, Titanfall2TraversalKind kind, Vector3 start, Vector3 end )
	{
		if ( !profile.Enabled || !start.IsFinite || !end.IsFinite || kind == Titanfall2TraversalKind.None ) return false;
		var delta = end - start;
		if ( delta.z > profile.Rise || -delta.z > profile.Drop || delta.WithZ( 0 ).Length > profile.Distance ) return false;
		return kind is not (Titanfall2TraversalKind.Climb or Titanfall2TraversalKind.Ladder) || profile.Climb;
	}

	internal static float Duration( Titanfall2TraversalProfile profile, Titanfall2TraversalKind kind, Vector3 start, Vector3 end )
	{
		var delta = end - start;
		if ( kind is Titanfall2TraversalKind.Climb or Titanfall2TraversalKind.Ladder )
			return Math.Clamp( MathF.Abs( delta.z ) / 100f + delta.WithZ( 0 ).Length / 160f, 0.4f, 6f );
		return Math.Clamp( MathF.Max( delta.WithZ( 0 ).Length / MathF.Max( 80, profile.Speed ),
			2 * MathF.Sqrt( (profile.ArcHeight + MathF.Abs( delta.z ) * 0.5f) / 400f ) ), 0.45f, 4.5f );
	}

	internal static Vector3 Sample( Titanfall2TraversalKind kind, Vector3 start, Vector3 end, float cycle, float arcHeight,
		Vector3[] rootCurve = null )
	{
		var t = Math.Clamp( cycle, 0, 1 );
		if ( t <= 0 ) return start;
		if ( t >= 1 ) return end;
		if ( kind is Titanfall2TraversalKind.Climb or Titanfall2TraversalKind.Ladder )
		{
			var corner = start.z < end.z ? start.WithZ( end.z + 4 ) : end.WithZ( start.z + 4 );
			return t < 0.75f ? Vector3.Lerp( start, corner, t / 0.75f ) : Vector3.Lerp( corner, end, (t - 0.75f) / 0.25f );
		}
		var progress = t;
		if ( rootCurve is { Length: > 1 } )
		{
			var extent = rootCurve[^1].WithZ( 0 );
			if ( extent.LengthSquared > 64 )
			{
				var f = t * (rootCurve.Length - 1);
				var i = Math.Min( (int)f, rootCurve.Length - 2 );
				var authored = Vector3.Lerp( rootCurve[i], rootCurve[i + 1], f - i );
				progress = Math.Clamp( Vector3.Dot( authored.WithZ( 0 ), extent ) / extent.LengthSquared, 0, 1 );
			}
		}
		var lift = kind == Titanfall2TraversalKind.Drop ? MathF.Min( arcHeight, 20 ) : MathF.Max( arcHeight, MathF.Abs( end.z - start.z ) * 0.75f );
		return Vector3.Lerp( start, end, progress ) + Vector3.Up * (4 * lift * t * (1 - t));
	}

	internal static string Role( Titanfall2TraversalKind kind ) => kind switch
	{
		Titanfall2TraversalKind.Climb or Titanfall2TraversalKind.Ladder => ModelLoader.ClimbRole,
		Titanfall2TraversalKind.Drop => ModelLoader.DropRole,
		_ => ModelLoader.JumpRole
	};
}
