/// <summary>A retail UiData binding, resolved after multiplayer and attachment overrides.</summary>
public record struct Titanfall2WeaponUiDisplay
{
	public int Slot { get; set; }
	public string UiPath { get; set; }
	public string MeshName { get; set; }
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2WeaponUiDisplay[] UiDisplays { get; init; } = [];

	static Titanfall2WeaponUiDisplay[] ReadUiDisplays( Block root, Func<string, string> get )
	{
		var displays = new List<Titanfall2WeaponUiDisplay>();
		for ( var slot = 1; slot <= 16; slot++ )
		{
			if ( get( $"ui{slot}_enable" ) != "1" ) continue;
			var name = $"UiData{slot}";
			var definition = new Block();
			if ( root.Blocks.TryGetValue( name, out var baseUi ) ) definition.Merge( baseUi );
			if ( root.Blocks.TryGetValue( "MP_BASE", out var mp ) && mp.Blocks.TryGetValue( name, out var mpUi ) ) definition.Merge( mpUi );
			var ui = definition.Values.GetValueOrDefault( "ui", "" );
			var mesh = definition.Values.GetValueOrDefault( "mesh", "" );
			if ( !ui.StartsWith( "ui/", StringComparison.OrdinalIgnoreCase ) || !ui.Contains( "ammo_counter", StringComparison.OrdinalIgnoreCase )
				|| !definition.Blocks.TryGetValue( "Args", out var args )
				|| !string.Equals( args.Values.GetValueOrDefault( "ammo" ), "weapon_ammo", StringComparison.OrdinalIgnoreCase )
				|| !string.Equals( args.Values.GetValueOrDefault( "clipSize" ), "weapon_clipSize", StringComparison.OrdinalIgnoreCase )
				|| !Titanfall2ContentSnapshot.TryNormalize( mesh, out var normalized )
				|| !normalized.StartsWith( "models/weapons/attachments/", StringComparison.Ordinal ) ) continue;
			displays.Add( new() { Slot = slot, UiPath = ui, MeshName = normalized } );
		}
		return displays.ToArray();
	}
}
