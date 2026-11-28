/// <summary>Cloneable Titan armament preset. Equip on a Titan from host game code or the inspector.</summary>
[Title( "Titanfall 2 Titan Armament" ), Group( "Titanfall 2" )]
public sealed class Titanfall2TitanArmament : Component
{
	[Property] public Titanfall2Npc Actor { get; set; }
	[Property] public string ModelPath { get; set; }
	[Property] public string ViewModelPath { get; set; }
	[Property] public string MountIdent { get; set; }
	[Property] public string FireSound { get; set; }
	[Property] public string AiWeaponScript { get; set; }
	[Property] public Titanfall2WeaponPresentation Presentation { get; set; }
	[Property] public Titanfall2WeaponAudio Audio { get; set; }
	[Property] public Titanfall2ProjectileSettings Projectile { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public float TitanDamage { get; set; }
	[Property] public Titanfall2DamageProfile DamageProfile { get; set; }
	[Property] public Titanfall2WeaponFiring Firing { get; set; }
	[Property] public Titanfall2SpreadProfile SpreadProfile { get; set; }
	[Property] public float Interval { get; set; }
	[Property] public int Magazine { get; set; }
	[Property] public float ReloadSeconds { get; set; }
	[Property] public int Pellets { get; set; } = 1;
	[Property] public float Spread { get; set; }
	[Property] public float AdsFov { get; set; } = 55f;
	[Property] public float AdsMoveSpeedScale { get; set; } = 1f;
	[Property] public float Range { get; set; } = 2800;

	protected override void OnStart()
	{
		if ( Actor.IsValid() ) EquipTo( Actor );
		else if ( !Application.IsDedicatedServer && !Application.IsHeadless )
		{
			var model = Titanfall2GameplayVisuals.GetModel( ModelPath );
			if ( model is { IsError: false } )
			{
				var display = new GameObject( GameObject, true, "Armament display" );
				display.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
				display.AddComponent<ModelRenderer>().Model = model;
			}
		}
	}

	/// <summary>Installs this preset, replacing the visible gun and starting a normal magazine reload.</summary>
	public bool EquipTo( Titanfall2Npc actor )
	{
		if ( !Networking.IsHost || IsProxy || actor is not { IsTitan: true, Dead: false }
			|| !float.IsFinite( Damage ) || Damage <= 0 || !float.IsFinite( TitanDamage ) || TitanDamage <= 0
			|| !float.IsFinite( Interval ) || Interval is <= 0 or > 30 || Magazine is < 1 or > 250
			|| !float.IsFinite( ReloadSeconds ) || ReloadSeconds is <= 0 or > 30
			|| !float.IsFinite( Spread ) || Spread is < 0 or > 90 || Pellets is < 1 or > 12
			|| !float.IsFinite( Range ) || Range is < 256 or > 16384 || string.IsNullOrWhiteSpace( ModelPath )
			|| !ValidProjectile( Projectile ) ) return false;
		Actor = actor;
		actor.GunModelPath = ModelPath;
		actor.ViewModelPath = ViewModelPath;
		actor.MountIdent = string.IsNullOrWhiteSpace( MountIdent ) ? actor.MountIdent ?? "titanfall2" : MountIdent;
		actor.FireSound = FireSound;
		actor.AiWeaponScript = AiWeaponScript;
		actor.WeaponPresentation = Presentation;
		actor.WeaponAudio = Audio;
		actor.Projectile = Projectile;
		actor.ShotDamage = Damage;
		actor.TitanShotDamage = TitanDamage;
		actor.DamageProfile = DamageProfile;
		actor.Firing = Firing;
		actor.SpreadProfile = SpreadProfile;
		actor.ShotInterval = Interval;
		actor.MagazineSize = Magazine;
		actor.ReloadSeconds = ReloadSeconds;
		actor.ShotPellets = Pellets;
		actor.ShotSpread = Spread;
		actor.AdsFov = float.IsFinite( AdsFov ) && AdsFov > 0f ? Math.Clamp( AdsFov, 15f, 75f ) : 55f;
		actor.AdsMoveSpeedScale = float.IsFinite( AdsMoveSpeedScale ) ? Math.Clamp( AdsMoveSpeedScale, 0.2f, 1f ) : 1f;
		actor.AttackRange = Range;
		actor.GetComponent<Titanfall2TitanCore>()?.ApplyPrimaryPresentation();
		actor.ReplaceTitanGun();
		return true;
	}

	internal static bool ValidProjectile( in Titanfall2ProjectileSettings projectile ) =>
		float.IsFinite( projectile.Speed ) && projectile.Speed is >= 0 and <= 20000
		&& float.IsFinite( projectile.GravityScale ) && projectile.GravityScale is >= 0 and <= 4
		&& float.IsFinite( projectile.SplashDamage ) && projectile.SplashDamage >= 0
		&& float.IsFinite( projectile.SplashTitanDamage ) && projectile.SplashTitanDamage >= 0
		&& float.IsFinite( projectile.SplashRadius ) && projectile.SplashRadius is >= 0 and <= 1024
		&& (!projectile.Enabled || (float.IsFinite( projectile.Lifetime ) && projectile.Lifetime is > 0 and <= 12
			&& float.IsFinite( projectile.Radius ) && projectile.Radius is >= 0 and <= 12));
}

public sealed partial class Titanfall2Npc
{
	internal void ReplaceTitanGun()
	{
		ResetBurstCount();
		if ( _gun.IsValid() ) _gun.Destroy();
		_gun = null;
		_triedGun = false;
		ClearGunBoneBinding();
		AttachGun();
		Rounds = 0;
		Reloading = false;
		StartReload();
	}
}
