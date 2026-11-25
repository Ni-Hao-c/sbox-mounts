public sealed partial class Titanfall2Npc
{
	Titanfall2Mount _npcPrewarmMount;
	Titanfall2WeaponPrewarmRequest _npcPrewarm;
	(Titanfall2WeaponAudio Audio, Titanfall2WeaponPresentation Presentation, Titanfall2ProjectileSettings Projectile, string Fire, string Gun) _npcPrewarmSettings;

	void TickNpcWeaponPrewarm()
	{
		if ( Dead || Application.IsDedicatedServer || Application.IsHeadless ) return;
		var settings = (WeaponAudio, WeaponPresentation, Projectile, FireSound, GunModelPath);
		if ( _npcPrewarm is null || _npcPrewarm.Cancelled || _npcPrewarmMount?.IsMounted != true || !_npcPrewarmSettings.Equals( settings ) )
		{
			if ( Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount || !mount.IsMounted ) return;
			var items = new List<Titanfall2WeaponPrewarmItem>();
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
