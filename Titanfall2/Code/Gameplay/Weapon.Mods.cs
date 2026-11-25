public enum Titanfall2WeaponModSlot { Optic, Magazine, Reload, Ads }

/// <summary>A retail mod resolved during mount indexing; only its own slot's fields are applied.</summary>
public record struct Titanfall2WeaponModOption
{
	public string Id { get; set; }
	public string Title { get; set; }
	public Titanfall2WeaponModSlot Slot { get; set; }
	public int Magazine { get; set; }
	public Titanfall2ReloadProfile Reload { get; set; }
	public float ZoomIn { get; set; }
	public float ZoomOut { get; set; }
	public float AdsFov { get; set; }
	public Titanfall2WeaponPresentation Presentation { get; set; }
	public Titanfall2WeaponUiDisplay[] UiDisplays { get; set; }
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2WeaponModOption[] AttachmentOptions { get; init; } = [];

	internal static Titanfall2WeaponModOption ModOption( string id, Titanfall2WeaponData variant ) => new()
	{
		Id = id,
		Title = id switch { "extended_ammo" => "Extended Mag", "pas_fast_reload" => "Speedloader", "pas_fast_ads" => "Gun Ready",
			"hcog" => "HCOG", "redline_sight" => "AOG", "holosight" => "Holo", "iron_sights" => "Iron Sights", _ => id },
		Slot = id switch { "extended_ammo" => Titanfall2WeaponModSlot.Magazine, "pas_fast_reload" => Titanfall2WeaponModSlot.Reload,
			"pas_fast_ads" => Titanfall2WeaponModSlot.Ads, _ => Titanfall2WeaponModSlot.Optic },
		Magazine = variant.Magazine, Reload = variant.ReloadProfile,
		ZoomIn = variant.SpreadProfile.ZoomIn, ZoomOut = variant.SpreadProfile.ZoomOut,
		AdsFov = variant.AdsFov, Presentation = variant.Presentation, UiDisplays = variant.UiDisplays
	};
}

public sealed partial class Titanfall2Weapon
{
	[Property] public bool RandomizeAttachments { get; set; } = true;
	[Property] public string AttachmentBaseTitle { get; set; }
	[Property] public Titanfall2WeaponModOption[] AttachmentOptions { get; set; } = [];
	// null means unrolled; an empty selection is a completed roll for a weapon without supported mods.
	// This belongs to the instance, so ownership transfers, holstering and pickup never reroll it.
	[Property, Sync( SyncFlags.FromHost )] public string AttachmentSelection { get; private set; }
	string _appliedAttachmentSelection;
	public string[] SelectedAttachments => AttachmentSelection?.Split( ';', StringSplitOptions.RemoveEmptyEntries ) ?? [];

	internal static string RollAttachments( Titanfall2WeaponModOption[] options, Random random )
	{
		if ( options is null || options.Length == 0 ) return "";
		var selected = new List<string>();
		var optics = options.Where( x => x.Slot == Titanfall2WeaponModSlot.Optic ).ToArray();
		// Include the weapon's native sight in the optic draw.
		var optic = random.Next( optics.Length + 1 );
		if ( optic < optics.Length ) selected.Add( optics[optic].Id );
		var perks = options.Where( x => x.Slot != Titanfall2WeaponModSlot.Optic ).GroupBy( x => x.Slot )
			.Select( group => group.ElementAt( random.Next( group.Count() ) ) ).ToList();
		var count = Math.Min( 2, perks.Count );
		for ( var i = 0; i < count; ++i )
		{
			var pick = random.Next( perks.Count ); selected.Add( perks[pick].Id ); perks.RemoveAt( pick );
		}
		if ( selected.Count == 0 && optics.Length > 0 ) selected.Add( optics[random.Next( optics.Length )].Id );
		return string.Join( ';', selected );
	}

	void UpdateAttachments()
	{
		if ( Networking.IsHost && AttachmentSelection is null )
			AttachmentSelection = RandomizeAttachments ? RollAttachments( AttachmentOptions, Random.Shared ) : "";
		if ( AttachmentSelection is null || AttachmentSelection == _appliedAttachmentSelection ) return;
		var selected = SelectedAttachments;
		if ( selected.Length > 0 && (AttachmentOptions is null || AttachmentOptions.Length == 0) ) return;
		var titles = new List<string>();
		foreach ( var id in selected )
		{
			var option = AttachmentOptions.FirstOrDefault( x => x.Id == id );
			if ( string.IsNullOrEmpty( option.Id ) ) continue;
			titles.Add( option.Title );
			switch ( option.Slot )
			{
				case Titanfall2WeaponModSlot.Magazine:
					var wasFull = Clip1 == ClipMaxSize;
					ClipMaxSize = option.Magazine;
					if ( Networking.IsHost && wasFull ) Clip1 = ClipMaxSize;
					break;
				case Titanfall2WeaponModSlot.Reload:
					// This immutable choice is derived from the host-owned selection on every peer.
					if ( Networking.IsHost ) ReloadProfile = option.Reload;
					ReloadTime = option.Reload.NormalSeconds;
					break;
				case Titanfall2WeaponModSlot.Ads:
					SpreadProfile = SpreadProfile with { ZoomIn = option.ZoomIn, ZoomOut = option.ZoomOut };
					break;
				case Titanfall2WeaponModSlot.Optic:
					Presentation = option.Presentation;
					UiDisplays = option.UiDisplays;
					AdsFov = option.AdsFov;
					break;
			}
		}
		if ( !string.IsNullOrWhiteSpace( AttachmentBaseTitle ) )
			DisplayName = titles.Count == 0 ? AttachmentBaseTitle : $"{AttachmentBaseTitle} · {string.Join( " / ", titles )}";
		Presentation.ApplyBodyGroups( DroppedModel );
		if ( WorldModel.IsValid() ) Presentation.ApplyBodyGroups( WorldModel.GetComponent<BaseWeaponModel>()?.Renderer );
		if ( ViewModel.IsValid() ) Presentation.ApplyBodyGroups( ViewModel.GetComponent<BaseWeaponModel>()?.Renderer );
		_appliedAttachmentSelection = AttachmentSelection;
		BeginWeaponPrewarm();
	}
}
