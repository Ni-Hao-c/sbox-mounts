/// <summary>Builds spawnable Pilot weapon prefabs.</summary>
internal sealed class Titanfall2WeaponPrefabLoader( string title, Titanfall2WeaponData data, bool pilotView ) : ResourceLoader<Titanfall2Mount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		string Mounted( string name ) => name is null ? null : $"mount://{Host.Ident}/{name}.vmdl";
		var preview = Mounted( data.WorldModel + ".preview" );
		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", title )
			.WithMetadata( "PreviewModel", preview )
			.WithMetadata( "PreviewSequence", null );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, title );
			root.Tags.Add( "weapon" );
			var gun = root.AddComponent<Titanfall2Weapon>();
			gun.DisplayName = title;
			gun.WorldModelPath = Mounted( data.WorldModel + ".held" );
			gun.ViewModelPath = Mounted( data.ViewModel is null ? null : data.ViewModel + (pilotView ? ".pov" : ".visual") );
			gun.UseOriginalViewModel = pilotView;
			if ( pilotView ) gun.ViewArmsModelPath = Mounted( Titanfall2PilotViewModel.PilotArms + ".visual" );
			gun.MountIdent = Host.Ident;
			gun.Presentation = data.Presentation;
			gun.Audio = Host.GetWeaponAudio( data );
			gun.FireSound = Host.GetGameplayFireSound( data.FireSounds );
			gun.FireSound1P = Host.GetGameplayFireSound( data.FireSounds1P ) ?? gun.FireSound;
			gun.AdsFov = data.AdsFov;
			gun.HoldType = data.HoldType;
			gun.PreferredSlot = data.HoldType == "grenade" ? 2 : data.HoldType == "pistol" ? 1 : 0;
			gun.SmartLockEnabled = data.SmartAmmo.Enabled;
			gun.SmartAmmo = data.SmartAmmo;
			gun.PrimaryAutomatic = data.Automatic;
			gun.PrimaryDelay = 1 / data.ShotsPerSecond;
			gun.SpinUp = data.SpinUp;
			gun.ChargeProfile = data.ChargeProfile;
			gun.ChargePresentation = data.ChargePresentation;
			gun.HeatProfile = data.HeatProfile;
			gun.OrdnanceProfile = data.OrdnanceProfile;
			gun.CanCancelReload = !data.HeatProfile.Valid;
			gun.UsesClips = !data.ChargeProfile.Valid;
			gun.AutoReload = !data.ChargeProfile.Valid && !data.OrdnanceProfile.Enabled;
			gun.ClipMaxSize = data.Magazine;
			gun.Firing = data.Firing;
			gun.SpreadProfile = data.SpreadProfile;
			gun.ReloadTime = data.ReloadProfile.Segmented ? data.ReloadProfile.LoopDurationFor( data.ReloadSeconds ) : data.ReloadSeconds;
			gun.IncrementalReloading = data.ReloadProfile.Segmented;
			gun.ReloadStartTime = data.ReloadProfile.Segmented ? data.ReloadProfile.DurationFor( false, data.ReloadSeconds ) : 0f;
			gun.ViewKick = data.ViewKick;
			gun.UiDisplays = data.UiDisplays;
			gun.AttachmentBaseTitle = title;
			gun.AttachmentOptions = data.AttachmentOptions;
			gun.ReloadProfile = data.ReloadProfile;
			gun.Projectile = data.Projectile with { ModelPath = Mounted( data.Projectile.ModelPath ), MountIdent = Host.Ident };
			gun.TitanDamage = data.TitanDamage;
			gun.DamageProfile = data.DamageProfile;
			gun.BurstCount = data.BurstCount;
			gun.BurstRecovery = data.BurstDelay;
			gun.Ballistics = new() { Damage = data.Damage, Pellets = data.Pellets, Range = data.Range, SpreadBase = new Vector2( data.Spread, data.Spread ), SpreadGrowth = Vector2.Zero };
			var visual = new GameObject( root, true, "Dropped weapon model" );
			gun.DroppedModel = visual.AddComponent<SkinnedModelRenderer>();
			gun.DroppedModel.UseAnimGraph = false;
			root.AddComponent<BoxCollider>().Scale = data.HoldType == "pistol" ? new( 12, 5, 6 ) : new( 30, 6, 8 );
			root.AddComponent<Rigidbody>();
		}
		return _prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
	}
}

/// <summary>Builds equippable Titan armament prefabs.</summary>
internal sealed class Titanfall2TitanArmamentPrefabLoader( Titanfall2WeaponData data, string name ) : ResourceLoader<Titanfall2Mount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		var model = $"mount://{Host.Ident}/{data.WorldModel}.visual.vmdl";
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", name ).WithMetadata( "PreviewModel", model );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, name );
			root.Tags.Add( "titan-weapon" );
			var gun = root.AddComponent<Titanfall2TitanArmament>();
			gun.ModelPath = model;
			gun.ViewModelPath = data.ViewModel is null ? null : $"mount://{Host.Ident}/{data.ViewModel}.titanpov.vmdl";
			gun.MountIdent = Host.Ident;
			gun.FireSound = Host.GetGameplayFireSound( data.FireSounds );
			gun.Presentation = data.Presentation;
			gun.Audio = Host.GetWeaponAudio( data );
			gun.Projectile = data.Projectile with { ModelPath = data.Projectile.ModelPath is null ? null : $"mount://{Host.Ident}/{data.Projectile.ModelPath}.vmdl", MountIdent = Host.Ident };
			gun.Damage = data.Damage;
			gun.TitanDamage = data.TitanDamage;
			gun.DamageProfile = data.DamageProfile;
			gun.Firing = data.Firing;
			gun.SpreadProfile = data.SpreadProfile;
			gun.Interval = 1 / data.ShotsPerSecond;
			gun.Magazine = data.Magazine;
			gun.ReloadSeconds = data.ReloadSeconds;
			gun.Pellets = data.Pellets;
			gun.Spread = data.Spread;
			gun.Range = MathF.Min( 6000, data.Range );
		}
		return _prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
	}
}
