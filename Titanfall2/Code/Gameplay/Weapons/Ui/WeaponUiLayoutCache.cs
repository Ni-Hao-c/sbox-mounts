public partial class Titanfall2Mount
{
	Dictionary<string, Titanfall2WeaponUiLayout> _weaponUiLayouts;
	string _weaponUiRevision;

	internal void ClearWeaponUiLayouts()
	{
		_weaponUiLayouts = null;
		_weaponUiRevision = null;
	}

	internal Titanfall2WeaponUiLayout GetWeaponUiLayout( string name )
	{
		if ( _contentSnapshot is null || string.IsNullOrWhiteSpace( name ) ) return null;
		if ( _weaponUiRevision != _contentSnapshot.Revision ) { ClearWeaponUiLayouts(); _weaponUiRevision = _contentSnapshot.Revision; }
		if ( _weaponUiLayouts?.GetValueOrDefault( name ) is { } cached ) return cached;
		return Titanfall2WeaponUiCatalog.TryGet( name, out var known ) ? known : null;
	}

	static Titanfall2WeaponUiLayout ReadWeaponUiLayout( Titanfall2ContentSnapshot snapshot, string name, System.Threading.CancellationToken cancellation )
	{
		if ( Titanfall2WeaponUiCatalog.TryGet( name, out var catalogLayout ) ) return catalogLayout;
		ulong? matchedGuid = null;
		foreach ( var entry in snapshot.Entries.Where( x => x.Kind == "ui" && x.Archive is not null ) )
		{
			cancellation.ThrowIfCancellationRequested();
			if ( matchedGuid.HasValue && entry.Asset.Guid != matchedGuid.Value ) continue;
			if ( !RpakWeaponUiLayoutReader.TryReadName( entry.Archive, entry.Asset, out var candidate )
				|| !string.Equals( candidate, name, StringComparison.OrdinalIgnoreCase ) ) continue;
			matchedGuid = entry.Asset.Guid;
			if ( RpakWeaponUiLayoutReader.TryRead( entry.Archive, entry.Asset, out var layout ) ) return layout;
		}
		return null;
	}

	void PublishWeaponUiLayout( Titanfall2WeaponUiLayout layout )
	{
		if ( _weaponUiRevision != _contentSnapshot.Revision ) { ClearWeaponUiLayouts(); _weaponUiRevision = _contentSnapshot.Revision; }
		_weaponUiLayouts ??= new( StringComparer.OrdinalIgnoreCase );
		_weaponUiLayouts[layout.Name] = layout;
	}
}
