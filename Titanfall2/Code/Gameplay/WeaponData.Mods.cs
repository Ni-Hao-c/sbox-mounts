using System.Globalization;

internal sealed partial record Titanfall2WeaponData
{
	internal string[] ModCandidates { get; init; } = [];

	static Vector3 ReadViewModelOffset( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return default;
		var parts = value.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length != 3 || !float.TryParse( parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x )
			|| !float.TryParse( parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y )
			|| !float.TryParse( parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z )
			|| !float.IsFinite( x ) || !float.IsFinite( y ) || !float.IsFinite( z ) )
			throw new InvalidDataException( $"Invalid retail viewmodel offset: {value}" );
		return new( x, y, z );
	}

	static Block ReadMods( Block root )
	{
		var mods = new Block();
		if ( root.Blocks.TryGetValue( "Mods", out var normal ) ) mods.Merge( normal );
		if ( root.Blocks.TryGetValue( "MP_BASE", out var multiplayer ) && multiplayer.Blocks.TryGetValue( "Mods", out var mpMods ) ) mods.Merge( mpMods );
		return mods;
	}

	static string[] ReadModCandidates( Block mods, Dictionary<string, string> values, Action<string> warn )
	{
		var result = new List<string>();
		foreach ( var id in Titanfall2WeaponModProfile.Supported )
		{
			if ( !mods.Blocks.TryGetValue( id, out var mod ) ) continue;
			try
			{
				var candidate = new Dictionary<string, string>( values, StringComparer.OrdinalIgnoreCase );
				ApplyMod( id, mod, candidate );
				if ( candidate.Any( pair => Titanfall2WeaponModProfile.OwnsEffectiveField( id, pair.Key )
					&& !EquivalentModValue( values.GetValueOrDefault( pair.Key ), pair.Value ) ) ) result.Add( id );
			}
			catch ( InvalidDataException exception ) { warn( $"TF2 optional weapon mod {id} was excluded: {exception.Message}" ); }
		}
		return result.ToArray();
	}

	static bool EquivalentModValue( string left, string right )
	{
		if ( left == right ) return true;
		return float.TryParse( left, NumberStyles.Float, CultureInfo.InvariantCulture, out var a )
			&& float.TryParse( right, NumberStyles.Float, CultureInfo.InvariantCulture, out var b ) && a == b;
	}

	static void ApplySelectedMods( Block mods, Dictionary<string, string> values, IReadOnlyList<string> selected )
	{
		if ( selected is null || selected.Count == 0 ) return;
		if ( selected.Count > 3 ) throw new InvalidDataException( "A weapon supports one optic and at most two perks." );
		var seen = new HashSet<string>( StringComparer.Ordinal );
		var optics = 0;
		var perks = 0;
		foreach ( var id in selected )
		{
			if ( id is null || !Titanfall2WeaponModProfile.IsSupported( id ) || !seen.Add( id ) || !mods.Blocks.ContainsKey( id ) )
				throw new InvalidDataException( $"Unsupported, missing or duplicate player weapon mod: {id}" );
			if ( Titanfall2WeaponModProfile.IsOptic( id ) ) optics++; else perks++;
		}
		if ( optics > 1 || perks > 2 ) throw new InvalidDataException( "A weapon supports one optic and at most two perks." );
		// Stable ordering makes callers' list order irrelevant, including future shared fields.
		foreach ( var id in Titanfall2WeaponModProfile.Supported.Where( seen.Contains ) ) ApplyMod( id, mods.Blocks[id], values );
	}

	static void ApplyMod( string id, Block mod, Dictionary<string, string> values )
	{
		foreach ( var (key, value) in mod.Values )
		{
			if ( value.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) ) continue;
			// Retail Mastiff/Softball/AT scripts multiply omitted resume milestones.
			// Those engine-default milestones are not implemented by this full-magazine
			// reload path: keep them absent instead of inventing a numeric baseline.
			if ( !values.ContainsKey( key ) && (key.StartsWith( "reload_time_late", StringComparison.Ordinal )
				|| key.StartsWith( "reloadempty_time_late", StringComparison.Ordinal )) ) continue;
			values[key] = ApplyModValue( values.GetValueOrDefault( key ), value, $"{id}/{key}" );
		}
		if ( mod.Blocks.Count > 0 ) throw new InvalidDataException( $"Unsupported nested weapon mod: {id}" );
	}

	/// <summary>Retail numeric delta syntax; invalid operators never become parser fallbacks.</summary>
	internal static string ApplyModValue( string original, string modifier, string context = "weapon mod" )
	{
		var text = modifier?.Trim() ?? throw new InvalidDataException( $"Missing value for {context}." );
		var multiply = text.StartsWith( '*');
		var add = text.StartsWith( "++", StringComparison.Ordinal );
		if ( !multiply && !add )
		{
			if ( text.StartsWith( "--", StringComparison.Ordinal ) || text.StartsWith( '+') || text.StartsWith( '/')
				|| text.StartsWith( '%') || text.StartsWith( '^') || text.StartsWith( '=') )
				throw new InvalidDataException( $"Unsupported operator in {context}: {text}" );
			return modifier;
		}
		if ( !float.TryParse( original, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseline ) || !float.IsFinite( baseline )
			|| !float.TryParse( text[(multiply ? 1 : 2)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var operand ) || !float.IsFinite( operand ) )
			throw new InvalidDataException( $"Invalid numeric weapon mod in {context}: {original} -> {text}" );
		var result = multiply ? baseline * operand : baseline + operand;
		if ( !float.IsFinite( result ) ) throw new InvalidDataException( $"Non-finite weapon mod result in {context}." );
		return result.ToString( "R", CultureInfo.InvariantCulture );
	}
}
