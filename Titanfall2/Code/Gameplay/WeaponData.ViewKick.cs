using System.Globalization;

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2ViewKickProfile ViewKick { get; init; }

	static Titanfall2ViewKickProfile ReadViewKickProfile( Func<string, string> get, Func<string, float, float> number, Func<string, string> read )
	{
		var springName = get( "viewkick_spring" );
		if ( string.IsNullOrWhiteSpace( springName ) ) return default;
		// 0x1803CE5B0: named springs are a separate resource, not WeaponData defaults.
		var text = read( "scripts/weapons/springs.txt" ) ?? read( "scripts/springs.txt" );
		if ( !TryReadViewKickSpring( text, springName, out var hip, out var ads ) )
		{
			Titanfall2Log.Warning( $"Titanfall2: viewkick spring '{springName}' missing or invalid; recoil profile disabled." );
			return new() { SpringName = springName };
		}
		Titanfall2ViewKickAxis Axis( string name, bool permanent = false, bool roll = false )
		{
			var prefix = "viewkick_" + (permanent ? "perm_" : "") + name + "_";
			return new()
			{
				Base = number( prefix + "base", 0 ),
				RandomMin = number( prefix + (roll ? "randomMin" : "random_innerexclude"), 0 ) * (roll ? 1 : 0.5f),
				RandomMax = number( prefix + (roll ? "randomMax" : "random"), 0 ) * (roll ? 1 : 0.5f),
				SoftScale = number( prefix + "softScale", 0 ), HardScale = number( prefix + "hardScale", 0 )
			};
		}
		return new()
		{
			Enabled = true, SpringName = springName, HipSpring = hip, AdsSpring = ads,
			Pitch = Axis( "pitch" ), Yaw = Axis( "yaw" ), Roll = Axis( "roll", roll: true ),
			PermanentPitch = Axis( "pitch", permanent: true ), PermanentYaw = Axis( "yaw", permanent: true ),
			HipWeaponFraction = number( "viewkick_hipfire_weaponFraction", 0 ), AdsWeaponFraction = number( "viewkick_ads_weaponFraction", 0 ),
			HipWeaponScale = number( "viewkick_hipfire_weaponFraction_vmScale", 0 ), AdsWeaponScale = number( "viewkick_ads_weaponFraction_vmScale", 0 ),
			FirstShotHip = number( "viewkick_scale_firstshot_hipfire", 0 ), FirstShotAds = number( "viewkick_scale_firstshot_ads", 0 ),
			MinHip = number( "viewkick_scale_min_hipfire", 1 ), MaxHip = number( "viewkick_scale_max_hipfire", 1 ),
			MinAds = number( "viewkick_scale_min_ads", 1 ), MaxAds = number( "viewkick_scale_max_ads", 1 ),
			ValuePerShot = number( "viewkick_scale_valuePerShot", 0 ), ValueLerpStart = number( "viewkick_scale_valueLerpStart", 0 ),
			ValueLerpEnd = number( "viewkick_scale_valueLerpEnd", 0 ), ValueDecayDelay = number( "viewkick_scale_valueDecayDelay", 0 ),
			ValueDecayRate = number( "viewkick_scale_valueDecayRate", 0 ), DuckScale = number( "viewkick_duck_scale", 1 ),
			HoverScale = number( "viewkick_hover_scale", 1 ), MoveScale = number( "viewkick_move_scale", 1 ), AirScaleAds = number( "viewkick_air_scale_ads", 1 )
		};
	}

	static bool TryReadViewKickSpring( string text, string name, out Titanfall2ViewKickSpring hip, out Titanfall2ViewKickSpring ads )
	{
		hip = ads = default;
		if ( string.IsNullOrWhiteSpace( text ) || text.Length > 512 * 1024 ) return false;
		try
		{
			var reader = new Reader( text.Trim( '\uFEFF', '\0' ) );
			if ( !reader.Next().Text.Equals( "WeaponSprings", StringComparison.OrdinalIgnoreCase ) || reader.Next().Kind != Kind.Open ) return false;
			var root = reader.ReadBlock( 0 );
			if ( reader.Peek.Kind != Kind.End || !root.Blocks.TryGetValue( name, out var block ) ) return false;
			float Value( string key ) => block.Values.TryGetValue( key, out var value )
				&& float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed )
				&& float.IsFinite( parsed ) && parsed >= 0 ? parsed : throw new InvalidDataException( $"Invalid viewkick spring field: {key}" );
			Titanfall2ViewKickSpring Spring( string prefix ) => new()
			{
				Constant = new( Value( prefix + "pitch_springConstant" ), Value( prefix + "yaw_springConstant" ), Value( prefix + "roll_springConstant" ) ),
				Damping = new( Value( prefix + "pitch_damping" ), Value( prefix + "yaw_damping" ), Value( prefix + "roll_damping" ) )
			};
			hip = Spring( "hipfire_" );
			ads = Spring( "ADS_" );
			return true;
		}
		catch ( InvalidDataException ) { return false; }
	}
}
