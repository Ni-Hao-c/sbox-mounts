using System.Globalization;
using System.Text;

/// <summary>Retail offhand weapon data, without pilot inventory/reload requirements.</summary>
internal sealed record Titanfall2TitanAbilityData( Titanfall2ProjectileSettings Projectile, float Damage,
	float TitanDamage, float Range, int Shots, float Interval, string Sound, string Tracer )
{
	internal static string ScriptFor( Titanfall2TitanKit.Chassis kit, bool defense = false ) => defense
		? kit == Titanfall2TitanKit.Chassis.Monarch ? "mp_titanweapon_stun_laser" : null
		: kit switch
		{
			Titanfall2TitanKit.Chassis.Monarch => "mp_titanweapon_salvo_rockets",
			Titanfall2TitanKit.Chassis.Ion => "mp_titanweapon_laser_lite",
			Titanfall2TitanKit.Chassis.Tone => "mp_titanweapon_tracker_rockets",
			Titanfall2TitanKit.Chassis.Northstar => "mp_titanweapon_dumbfire_rockets",
			_ => null
		};
}

internal sealed partial record Titanfall2WeaponData
{
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

public partial class Titanfall2Mount
{
	Titanfall2ContentSnapshot _titanAbilitySnapshot;
	readonly Dictionary<string, Titanfall2TitanAbilityData> _titanAbilities = new( StringComparer.OrdinalIgnoreCase );

	internal Titanfall2TitanAbilityData GetTitanAbility( Titanfall2TitanKit.Chassis kit, bool defense = false )
	{
		var script = Titanfall2TitanAbilityData.ScriptFor( kit, defense );
		if ( script is null || _contentSnapshot is null ) return null;
		if ( !ReferenceEquals( _titanAbilitySnapshot, _contentSnapshot ) )
		{
			_titanAbilitySnapshot = _contentSnapshot;
			_titanAbilities.Clear();
		}
		if ( _titanAbilities.TryGetValue( script, out var result ) ) return result;
		try
		{
			var speed = kit == Titanfall2TitanKit.Chassis.Northstar ? 3500f : kit == Titanfall2TitanKit.Chassis.Tone ? 1800f : 2200f;
			var behavior = kit == Titanfall2TitanKit.Chassis.Tone ? Titanfall2ProjectileBehavior.HomingTitan
				: kit == Titanfall2TitanKit.Chassis.Northstar ? Titanfall2ProjectileBehavior.ClusterBurst : Titanfall2ProjectileBehavior.Standard;
			result = Titanfall2WeaponData.ReadTitanAbility( $"scripts/weapons/{script}.txt", Read, speed, behavior );
		}
		catch ( InvalidDataException ) { result = null; }
		_titanAbilities[script] = result;
		return result;

		string Read( string path ) => _contentSnapshot.GetSource( path ) is { } source
			&& source.TryReadAllBytes( out var bytes, out _ ) && bytes.Length <= 512 * 1024 ? Encoding.UTF8.GetString( bytes ) : null;
	}
}
