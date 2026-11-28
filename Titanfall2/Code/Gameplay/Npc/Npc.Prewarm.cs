public sealed partial class Titanfall2Npc
{
	Titanfall2Mount _interactionPrewarmMount;
	Titanfall2WeaponPrewarmRequest _interactionPrewarm;
	(string Model, Titanfall2TitanKit.Chassis Chassis) _interactionPrewarmSettings;
	public Titanfall2WeaponPrewarmStatus InteractionPrewarmStatus => _interactionPrewarm?.Status ?? default;
	internal IReadOnlyList<string> InteractionPrewarmErrors => _interactionPrewarm?.Errors ?? [];

	void TickNpcInteractionPrewarm( bool pump = true )
	{
		if ( !IsTitan || !Seat.IsValid() || Dead || Application.IsHeadless || Application.IsDedicatedServer ) return;
		var path = Titanfall2InteractionAnimationRules.ExistingModelPath( Renderer?.Model?.ResourcePath ?? ModelPath );
		if ( string.IsNullOrEmpty( path ) ) return;
		var settings = (Model: path, Chassis: GetComponent<Titanfall2TitanKit>()?.Kit ?? Titanfall2TitanKit.Chassis.None);
		if ( _interactionPrewarm is null || _interactionPrewarm.Cancelled || _interactionPrewarmMount?.IsMounted != true || !_interactionPrewarmSettings.Equals( settings ) )
		{
			if ( Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount || !mount.IsMounted ) return;
			_interactionPrewarmMount = mount; _interactionPrewarmSettings = settings;
			_interactionPrewarm = mount.QueueWeaponPrewarm( Titanfall2TitanInteractionAssets.Dependencies( mount.Ident, settings.Model, settings.Chassis ) );
		}
		if ( pump ) _interactionPrewarmMount.PumpWeaponPrewarm();
	}

	Titanfall2Mount _npcPrewarmMount;
	Titanfall2WeaponPrewarmRequest _npcPrewarm;
	(Titanfall2WeaponAudio Audio, Titanfall2WeaponPresentation Presentation, Titanfall2ProjectileSettings Projectile, string Fire, string Gun, Titanfall2TitanKit.Chassis Chassis) _npcPrewarmSettings;

	void TickNpcWeaponPrewarm()
	{
		TickNpcInteractionPrewarm();
		if ( Dead || Application.IsDedicatedServer || Application.IsHeadless ) return;
		var chassis = IsTitan ? GetComponent<Titanfall2TitanKit>()?.Kit ?? Titanfall2TitanKit.Chassis.None : Titanfall2TitanKit.Chassis.None;
		var settings = (WeaponAudio, WeaponPresentation, Projectile, FireSound, GunModelPath, chassis);
		if ( _npcPrewarm is null || _npcPrewarm.Cancelled || _npcPrewarmMount?.IsMounted != true || !_npcPrewarmSettings.Equals( settings ) )
		{
			if ( Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount || !mount.IsMounted ) return;
			var items = Titanfall2UtilityPresentation.Dependencies( mount.Ident, chassis ).ToList();
			void Add( Titanfall2WeaponPrewarmKind kind, string name, bool loop = false )
			{
				if ( !string.IsNullOrWhiteSpace( name ) ) items.Add( new( kind, name, loop ) );
			}
			var sounds = WeaponAudio.Npc;
			foreach ( var name in new[] { sounds.Fire1, sounds.Fire2, sounds.Start, sounds.End, FireSound, WeaponPresentation.ShellSound } )
				Add( Titanfall2WeaponPrewarmKind.Event, name );
			Add( Titanfall2WeaponPrewarmKind.Event, sounds.Loop, WeaponAudio.Looping );
			foreach ( var name in new[] { WeaponPresentation.MuzzleWorld, WeaponPresentation.ShellWorld, WeaponPresentation.Shell2World,
				WeaponPresentation.TracerWorld, Projectile.TrailEffect, Projectile.ScriptTrailEffect, Projectile.HazardEffect } )
				Add( Titanfall2WeaponPrewarmKind.Particle, name );
			foreach ( var layer in WeaponPresentation.MuzzleLayers ?? [] )
				if ( !WeaponPresentation.Silenced || !layer.SkipWhenSilenced ) Add( Titanfall2WeaponPrewarmKind.Particle, layer.World );
			Add( Titanfall2WeaponPrewarmKind.Model, GunModelPath );
			Add( Titanfall2WeaponPrewarmKind.Model, Projectile.ModelPath );
			Add( Titanfall2WeaponPrewarmKind.Impact, WeaponPresentation.ImpactTable );
			Add( Titanfall2WeaponPrewarmKind.Impact, Projectile.ImpactTable );
			_npcPrewarmMount = mount; _npcPrewarmSettings = settings;
			_npcPrewarm = mount.QueueWeaponPrewarm( items );
		}
		_npcPrewarmMount.PumpWeaponPrewarm();
	}
}
