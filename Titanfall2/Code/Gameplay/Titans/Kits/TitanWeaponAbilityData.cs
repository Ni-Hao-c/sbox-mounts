using System.Globalization;
using System.Text;

/// <summary>Retail offhand weapon data, without pilot inventory/reload requirements.</summary>
internal sealed record Titanfall2TitanAbilityData( Titanfall2ProjectileSettings Projectile, float Damage,
	float TitanDamage, float Range, int Shots, float Interval, string Sound, string Tracer )
{
	internal static string ScriptFor( Titanfall2TitanKit.Chassis kit, bool defense = false )
	{
		var profile = Titanfall2TitanChassisCatalog.Get( kit );
		return defense ? profile.DefenseScript : profile.OffenseScript;
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal static Titanfall2IonData ReadIonData( Func<string, string> read )
	{
		Dictionary<string, string> Values( string name )
		{
			var root = ReadFile( $"scripts/weapons/{name}.txt", read, new( StringComparer.OrdinalIgnoreCase ) );
			var values = new Dictionary<string, string>( root.Values, StringComparer.OrdinalIgnoreCase );
			if ( root.Blocks.TryGetValue( "MP_BASE", out var mp ) )
				foreach ( var (key, value) in mp.Values ) if ( value != "<KEEP_DEFAULT>" ) values[key] = value;
			return values;
		}
		static float Number( Dictionary<string, string> values, string key ) =>
			float.TryParse( values.GetValueOrDefault( key ), NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) ? value : -1;
		var laser = Values( "mp_titanweapon_laser_lite" );
		var split = Values( "mp_titanweapon_particle_accelerator" );
		var vortex = Values( "mp_titanweapon_vortex_shield_ion" );
		var result = new Titanfall2IonData( (int)Number( laser, "shared_energy_cost" ), (int)Number( split, "shared_energy_cost" ),
			(int)Number( vortex, "shared_energy_charge_cost" ), Number( laser, "charge_cooldown_time" ), Number( split, "zoom_time_in" ) );
		return result.LaserCost is > 0 and <= 1000 && result.SplitCost is > 0 and <= 1000 && result.VortexChargeCost is > 0 and < 100
			&& result.LaserRecovery > 0 && result.ZoomSeconds > 0 ? result : null;
	}

	internal static Titanfall2TitanAbilityData ReadTitanAbility( string path, Func<string, string> read,
		float scriptSpeed = 0, Titanfall2ProjectileBehavior behavior = Titanfall2ProjectileBehavior.Standard )
	{
		var root = ReadFile( path, read, new( StringComparer.OrdinalIgnoreCase ) );
		var values = new Dictionary<string, string>( root.Values, StringComparer.OrdinalIgnoreCase );
		if ( root.Blocks.TryGetValue( "MP_BASE", out var multiplayer ) )
			foreach ( var (key, value) in multiplayer.Values )
				if ( !value.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) ) values[key] = value;
		string Get( string key ) => values.GetValueOrDefault( key, "" );
		float Number( string key, float fallback = 0 ) => float.TryParse( Get( key ), NumberStyles.Float,
			CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) ? value : fallback;
		var damage = Number( "damage_near_value" );
		var heavy = Number( "damage_near_value_titanarmor", damage );
		var speed = Number( "projectile_launch_speed", scriptSpeed );
		if ( damage is <= 0 or > 10000 || heavy is <= 0 or > 10000 || speed is < 0 or > 20000 ) return null;
		var settings = new Titanfall2ProjectileSettings
		{
			Speed = speed,
			ModelPath = ModelPath( Get( "projectilemodel" ) ),
			TrailEffect = Get( "projectile_trail_effect_0" ),
			TrailAttachment = Get( "projectile_trail_attachment" ),
			ImpactTable = Get( "impact_effect_table" ),
			VortexAbsorbOnly = Get( "vortex_refire_behavior" ).Equals( "absorb", StringComparison.OrdinalIgnoreCase ),
			Lifetime = Math.Clamp( Number( "projectile_lifetime", 8 ), 0.1f, 12 ),
			Radius = Math.Clamp( Number( "projectile_radius", 3 ), 0.5f, 10 ),
			GravityScale = Math.Clamp( Number( "projectile_gravity_scale" ), 0, 4 ),
			SplashRadius = Math.Clamp( Number( "explosionradius" ), 0, 1024 ),
			SplashDamage = Math.Clamp( Number( "explosion_damage" ), 0, 10000 ),
			SplashTitanDamage = Math.Clamp( Number( "explosion_damage_heavy_armor", Number( "explosion_damage" ) ), 0, 10000 ),
			Behavior = behavior,
			Tint = new Color( 1f, 0.45f, 0.1f )
		};
		return new( settings, damage, heavy,
			Math.Clamp( Number( "damage_far_distance", 6000 ), 256, 16384 ),
			(int)Math.Clamp( Number( "burst_fire_count", 1 ), 1, 12 ),
			1f / Math.Clamp( Number( "fire_rate", 10 ), 0.1f, 60 ),
			Get( "fire_sound_1_player_3p" ) is { Length: > 0 } sound ? sound : Get( "burst_or_looping_fire_sound_start_3p" ),
			Get( "tracer_effect" ) );
	}
}

internal sealed record Titanfall2IonData( int LaserCost, int SplitCost, int VortexChargeCost, float LaserRecovery, float ZoomSeconds );
