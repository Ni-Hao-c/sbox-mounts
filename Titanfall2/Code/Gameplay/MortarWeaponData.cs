using System.Text;

internal sealed partial record Titanfall2WeaponData
{
	internal static Titanfall2TitanAbilityData ReadMortarAbility( Func<string, string> read )
	{
		const string path = "scripts/weapons/mp_titanweapon_rocketeer_rocketstream.txt";
		var ability = ReadTitanAbility( path, read, scriptSpeed: 2500 );
		if ( ability is null ) return null;
		var root = ReadFile( path, read, new( StringComparer.OrdinalIgnoreCase ) );
		if ( !root.Blocks.TryGetValue( "Mods", out var mods ) || !mods.Blocks.TryGetValue( "coop_mortar_titan", out var mortar ) ) return null;
		return ability with { Projectile = ability.Projectile with
		{
			Speed = 2500, Lifetime = 6, GravityScale = 0,
			TrailEffect = mortar.Values.GetValueOrDefault( "projectile_trail_effect_0", ability.Projectile.TrailEffect ),
			ImpactTable = mortar.Values.GetValueOrDefault( "impact_effect_table", ability.Projectile.ImpactTable )
		} };
	}
}

public partial class Titanfall2Mount
{
	Titanfall2ContentSnapshot _mortarWeaponSnapshot;
	Titanfall2TitanAbilityData _mortarWeapon;
	internal Titanfall2TitanAbilityData GetMortarWeapon()
	{
		if ( _contentSnapshot is null ) return null;
		if ( ReferenceEquals( _mortarWeaponSnapshot, _contentSnapshot ) ) return _mortarWeapon;
		_mortarWeaponSnapshot = _contentSnapshot;
		try { _mortarWeapon = Titanfall2WeaponData.ReadMortarAbility( Read ); }
		catch ( InvalidDataException error ) { Titanfall2Log.Warning( $"Mortar weapon unavailable: {error.Message}" ); _mortarWeapon = null; }
		return _mortarWeapon;
		string Read( string path ) => _contentSnapshot.GetSource( path ) is { } source && source.TryReadAllBytes( out var bytes, out _ )
			? Encoding.UTF8.GetString( bytes ) : null;
	}
}
