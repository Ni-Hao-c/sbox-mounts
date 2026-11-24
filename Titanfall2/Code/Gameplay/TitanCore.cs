using Chassis = Titanfall2TitanKit.Chassis;

/// <summary>Host-owned core charge and retail chassis cores, reusable on a Titan prefab.</summary>
[Title( "Titanfall 2 Titan Core" ), Group( "Titanfall 2" )]
public sealed class Titanfall2TitanCore : Component
{
	[Property] public Titanfall2Npc Actor { get; set; }
	[Property] public bool UseAi { get; set; } = true;
	/// <summary>Adaptation balance setting. Passive build time follows the retail core scripts.</summary>
	[Property] public float DamageForFullCore { get; set; } = 10000;
	[Sync( SyncFlags.FromHost )] public float Charge { get; private set; }
	[Sync( SyncFlags.FromHost )] public float ActiveRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float ChargeupRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public int UpgradeLevel { get; private set; }
	[Sync( SyncFlags.FromHost )] Vector3 BeamEnd { get; set; }
	public Chassis Kit => Actor.IsValid() ? Actor.GetComponent<Titanfall2TitanKit>( true )?.Kit ?? Chassis.None : Chassis.None;
	public bool Supported => Supports( Kit );
	public bool IsActive => ActiveRemaining > 0;
	public bool IsCharging => ChargeupRemaining > 0;
	public bool CanActivate => Active && Supported && Actor is { Active: true, IsTitan: true, Dead: false, CanFight: true }
		&& Charge >= 1 && !IsActive && !IsCharging;
	public string Name => Kit switch
	{
		Chassis.Monarch => "Upgrade Core", Chassis.Ion => "Laser Core", Chassis.Legion => "Smart Core", Chassis.Ronin => "Sword Core",
		Chassis.Tone => "Salvo Core", Chassis.Scorch => "Flame Core", _ => "Core Unavailable"
	};
	public float Duration => CoreDuration( Kit );
	public bool BlocksPrimary => (IsActive || IsCharging) && Kit is Chassis.Ion or Chassis.Ronin or Chassis.Tone or Chassis.Scorch;
	public bool UnlimitedAmmo => IsActive && Kit == Chassis.Legion;
	public bool SwordCoreActive => IsActive && Kit == Chassis.Ronin;
	public bool ArcRoundsActive => Kit == Chassis.Monarch && UpgradeLevel > 0 && HasXo16;
	public int MagazineCapacity => Math.Clamp( (Actor?.MagazineSize ?? 1) + (ArcRoundsActive ? 10 : 0), 1, 260 );
	public float ReloadMultiplier => ArcRoundsActive && UpgradeLevel >= 2 ? 0.75f : 1;
	public float PrimaryDamageMultiplier => ArcRoundsActive && UpgradeLevel >= 3 ? 1.25f : 1;
	bool HasXo16 => Actor?.GunModelPath?.Contains( "/titan_xo16", StringComparison.OrdinalIgnoreCase ) == true;
	bool HostReady => Networking.IsHost && !IsProxy && Active && Actor is { Active: true, IsTitan: true, Dead: false, CanFight: true } && Supported;
	bool HasPilot => Actor.Seat is { IsOccupied: true } or { IsEmbarking: true };
	Vector3 Eye => Actor.WorldPosition + Vector3.Up * (Actor.BodyHeight * 0.79f);
	Ray _aim;
	GameObject _attacker;
	Chassis _runningKit;
	bool _committed;
	TimeUntil _pulse;
	TimeUntil _aiCheck;
	TimeUntil _swingCooldown;
	TimeUntil _swingContact;
	bool _swingPending;
	Vector3 _swingDirection;
	GameObject _swingAttacker;
	TimeUntil _smartScan;
	GameObject _smartTarget;
	TimeSince _lastPrimaryShot;
	float _accelerator;
	GameObject _beamVisual;
	string _beamEffect;
	GameObject _swordVisual;
	BoneCollection.Bone _swordBone;
	Transform _swordBindInverse;
	bool _triedSword;
	Titanfall2TitanAbilityData _salvoData;
	readonly List<Titanfall2Projectile> _salvoMissiles = [];
	float _salvoElapsed;
	int _salvoBursts;

	internal static bool Supports( Chassis kit ) => kit is Chassis.Monarch or Chassis.Ion or Chassis.Legion or Chassis.Ronin or Chassis.Tone or Chassis.Scorch;
	internal static float BuildSeconds( Chassis kit ) => kit == Chassis.Monarch ? 100 : kit == Chassis.Ronin ? 120 : 150;
	internal static float CoreDuration( Chassis kit ) => kit switch { Chassis.Monarch or Chassis.Scorch => 0.5f, Chassis.Ion => 3.5f, Chassis.Legion or Chassis.Ronin => 12, Chassis.Tone => 2, _ => 0 };
	internal static float CoreChargeup( Chassis kit ) => kit switch { Chassis.Ion => 1.1f, Chassis.Ronin or Chassis.Scorch => 0.75f, Chassis.Tone => 0.5f, _ => 0 };
	internal static float AddCharge( float current, float gain )
	{
		current = float.IsFinite( current ) ? Math.Clamp( current, 0, 1 ) : 0;
		return !float.IsFinite( gain ) || gain <= 0 ? current : Math.Clamp( current + gain, 0, 1 );
	}
	internal static float DamageCharge( float damage, float availableHealth, float threshold ) =>
		!float.IsFinite( damage ) || damage <= 0 || !float.IsFinite( availableHealth ) || availableHealth <= 0
			|| !float.IsFinite( threshold ) || threshold <= 0 ? 0 : MathF.Min( damage, availableHealth ) / MathF.Max( 100, threshold );

	/// <summary>Only a host can grant charge. Core-generated damage cannot charge the next core.</summary>
	internal static void CreditDamage( Titanfall2Npc victim, in DamageInfo damage, float acceptedDamage )
	{
		if ( !Networking.IsHost || victim is null || damage is null || damage.Tags.Has( "titan-core" ) ) return;
		var source = damage.Weapon?.GetComponentInParent<Titanfall2Npc>() ?? damage.Attacker?.GetComponentInParent<Titanfall2Npc>();
		if ( source is not { IsTitan: true, Dead: false } || source == victim || !source.IsHostileToFaction( victim.Faction ) ) return;
		var core = source.GetComponent<Titanfall2TitanCore>();
		if ( !core.IsValid() || !core.HostReady || core.IsActive || core.IsCharging ) return;
		core.Charge = AddCharge( core.Charge, DamageCharge( acceptedDamage, victim.Health + victim.Shield, core.DamageForFullCore ) );
	}

	/// <summary>Update the host's aim while the cockpit moves; malformed or remote origins are rejected.</summary>
	public void SetAim( Ray aim )
	{
		if ( HostReady && ValidAim( aim ) ) _aim = new Ray( Eye, aim.Forward.Normal );
	}

	bool ValidAim( Ray aim ) => aim.Position.IsFinite && aim.Forward.IsFinite
		&& aim.Forward.LengthSquared is > 0.25f and < 4 && aim.Position.Distance( Eye ) < 180;

	/// <summary>Consume a full core once, retaining its aim throughout the charge animation.</summary>
	public bool TryActivate( Ray aim, GameObject attacker )
	{
		if ( !HostReady || !CanActivate || !ValidAim( aim ) ) return false;
		if ( Kit == Chassis.Tone )
		{
			_salvoData = (Sandbox.Mounting.Directory.Get( Actor.MountIdent ?? "titanfall2" ) as Titanfall2Mount)?.GetSalvoCoreOrdnance();
			if ( _salvoData is null ) return false;
		}
		Charge = 0;
		SetAim( aim );
		_attacker = attacker.IsValid() ? attacker : Actor.GameObject;
		_runningKit = Kit;
		ChargeupRemaining = CoreChargeup( Kit );
		_committed = true;
		PlayActivation( charging: IsCharging );
		if ( !IsCharging ) BeginActive();
		return true;
	}

	void BeginActive()
	{
		_committed = false;
		ActiveRemaining = Duration;
		_pulse = 0;
		if ( Kit == Chassis.Monarch )
		{
			var previous = UpgradeLevel;
			UpgradeLevel = Math.Min( 3, UpgradeLevel + 1 );
			Actor.RestoreTitanShield( Actor.MaxShield );
			if ( previous == 0 && ArcRoundsActive ) Actor.AddCoreMagazineRounds( 10, MagazineCapacity );
			ApplyPrimaryPresentation();
		}
		else if ( Kit == Chassis.Legion ) Actor.PrepareSmartCore( MagazineCapacity );
		else if ( Kit == Chassis.Tone ) { _salvoElapsed = 0; _salvoBursts = 0; }
		else if ( Kit == Chassis.Scorch ) Titanfall2FlameCoreWave.Launch( Actor, _aim.Forward, _attacker );
		else if ( Kit == Chassis.Ion ) { _pulse = 0.1f; PulseLaser(); }
		if ( CoreChargeup( Kit ) > 0 ) PlayActivation( charging: false );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && !IsProxy ) TickHost();
		UpdateVisuals();
	}

	void TickHost()
	{
		if ( !HostReady || (_committed || IsActive) && _runningKit != Kit ) { CancelActive(); return; }
		if ( _lastPrimaryShot > 0.2f ) _accelerator = MathF.Max( 0, _accelerator - Time.Delta / 2.15f );
		if ( !HasPilot && Actor.CombatTarget is { } target && target.IsValid() && Actor.HasLineOfSight( target ) )
			_aim = new Ray( Eye, (target.GetBounds().Center - Eye).Normal );
		UpdateSalvoGuidance();
		if ( IsCharging )
		{
			ChargeupRemaining = MathF.Max( 0, ChargeupRemaining - Time.Delta );
			if ( !IsCharging && _committed ) BeginActive();
			return;
		}
		if ( IsActive )
		{
			if ( Kit == Chassis.Ion && _pulse <= 0 ) { _pulse = 0.1f; PulseLaser(); }
			if ( Kit == Chassis.Tone )
			{
				_salvoElapsed += Time.Delta;
				while ( _salvoBursts < 20 && _salvoElapsed >= _salvoBursts / 12f )
				{
					LaunchSalvoBurst();
					_salvoBursts++;
				}
			}
			if ( SwordCoreActive )
			{
				if ( UseAi && !HasPilot && Actor.CombatTarget.IsValid()
					&& Actor.CombatTarget.WorldPosition.Distance( Actor.WorldPosition ) < 400 ) TryPrimaryAttack( _aim, Actor.GameObject );
				if ( _swingPending && _swingContact <= 0 ) FinishSwordSwing();
			}
			ActiveRemaining = MathF.Max( 0, ActiveRemaining - Time.Delta );
			if ( !IsActive ) CancelActive();
			return;
		}
		Charge = AddCharge( Charge, Time.Delta / BuildSeconds( Kit ) );
		if ( !UseAi || !CanActivate || HasPilot || _aiCheck > 0 ) return;
		_aiCheck = 0.5f;
		var enemy = Actor.CombatTarget;
		if ( !enemy.IsValid() || !Actor.HasLineOfSight( enemy ) || enemy.WorldPosition.Distance( Actor.WorldPosition ) > Actor.AttackRange ) return;
		if ( Kit == Chassis.Ronin && enemy.WorldPosition.Distance( Actor.WorldPosition ) > 500 ) return;
		TryActivate( new Ray( Eye, (enemy.GetBounds().Center - Eye).Normal ), Actor.GameObject );
	}

	void CancelActive()
	{
		ActiveRemaining = 0;
		ChargeupRemaining = 0;
		_committed = false;
		_swingPending = false;
		_smartTarget = null;
		_attacker = null;
	}

	Vector3 SalvoAimPoint()
	{
		var direction = _aim.Forward.IsFinite && _aim.Forward.LengthSquared > 0.25f ? _aim.Forward.Normal : Actor.WorldRotation.Forward;
		var sight = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, Eye + direction * 6000 )
			.UseHitPosition().IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
		return sight.Hit && !sight.StartedSolid ? sight.HitPosition : sight.EndPosition;
	}

	void UpdateSalvoGuidance()
	{
		if ( _salvoMissiles.Count == 0 ) return;
		_salvoMissiles.RemoveAll( missile => !missile.IsValid() );
		if ( _salvoMissiles.Count == 0 ) return;
		var point = SalvoAimPoint();
		foreach ( var missile in _salvoMissiles ) missile.DesignateHomingPoint( point );
	}

	void LaunchSalvoBurst()
	{
		if ( _salvoData is null ) return;
		var point = SalvoAimPoint();
		var direction = (point - Eye).Normal;
		var side = Vector3.Cross( direction, Vector3.Up ).Normal;
		var sign = (_salvoBursts & 1) == 0 ? 1 : -1;
		var settings = _salvoData.Projectile with { MountIdent = Actor.MountIdent, Lifetime = 10 };
		for ( var i = 0; i < 3; i++ )
		{
			var origin = Eye + side * (sign * 8);
			var forward = (direction + side * (sign * 0.25f) + Vector3.Up * ((i - 1) * 0.25f)).Normal;
			var projectile = Titanfall2Projectile.Launch( Scene, new Ray( origin, forward ), settings,
				_salvoData.Damage, _salvoData.TitanDamage, _attacker, Actor.GameObject, Actor.GameObject );
			if ( !projectile.IsValid() ) continue;
			projectile.IsCoreProjectile = true;
			projectile.DesignateHomingPoint( point );
			_salvoMissiles.Add( projectile );
		}
	}

	/// <summary>Primary attack is a delayed, cover-respecting sword sweep during Sword Core.</summary>
	public bool TryPrimaryAttack( Ray aim, GameObject attacker )
	{
		if ( !HostReady || !SwordCoreActive || !ValidAim( aim ) || _swingPending || _swingCooldown > 0 ) return false;
		SetAim( aim );
		_swingDirection = aim.Forward.Normal;
		_swingAttacker = attacker.IsValid() ? attacker : Actor.GameObject;
		_swingPending = true;
		_swingContact = 0.25f;
		_swingCooldown = 0.9f;
		PlaySwordSwing();
		return true;
	}

	void FinishSwordSwing()
	{
		_swingPending = false;
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, Eye + _swingDirection * 325 ).Radius( 20 )
			.UseHitboxes().UseHitPosition().IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.ShotHull );
		if ( !hit.Hit || hit.StartedSolid ) return;
		if ( IsHostileObject( hit.GameObject ) )
		{
			var tags = new TagSet(); tags.Add( "melee" ); tags.Add( "titan-core" );
			BaseCombatWeapon.ShootBullet( hit, 2200, 100, _swingAttacker, Actor.GameObject, tags );
		}
		PlayImpact( new BaseCombatWeapon.ShotEffect( hit.EndPosition, true, hit.Normal, hit.GameObject, hit.Surface ), "titan_sword" );
	}

	void PulseLaser()
	{
		var direction = _aim.Forward.IsFinite && _aim.Forward.LengthSquared > 0.25f ? _aim.Forward.Normal : Actor.WorldRotation.Forward;
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, Eye + direction * 6000 ).Radius( 18 )
			.UseHitboxes().UseHitPosition().IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		BeamEnd = hit.EndPosition;
		if ( !hit.Hit || hit.StartedSolid ) return;
		if ( IsHostileObject( hit.GameObject ) )
		{
			var tags = new TagSet(); tags.Add( "bullet" ); tags.Add( "titan-core" );
			BaseCombatWeapon.ShootBullet( hit, 325, 0, _attacker, Actor.GameObject, tags );
		}
		PlayImpact( new BaseCombatWeapon.ShotEffect( hit.EndPosition, true, hit.Normal, hit.GameObject, hit.Surface ), "laser_core" );
	}

	bool IsHostileObject( GameObject obj )
	{
		if ( !obj.IsValid() ) return false;
		if ( obj.GetComponentInParent<Titanfall2Npc>() is { } npc ) return npc != Actor && !npc.Dead && Actor.IsHostileToFaction( npc.Faction );
		return Actor.AttackPlayers && !Actor.Friendly && obj.GetComponentInParent<PlayerController>() is not null;
	}

	/// <summary>Assist only within the original aim cone, with a fresh cover check for every shot.</summary>
	public Ray AssistAim( Ray aim )
	{
		if ( !HostReady || !UnlimitedAmmo || !ValidAim( aim ) ) return aim;
		if ( _smartScan <= 0 )
		{
			_smartScan = 0.1f;
			_smartTarget = null;
			var best = 0.94f;
			foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>() ) Select( npc.GameObject );
			if ( Actor.AttackPlayers && !Actor.Friendly ) foreach ( var player in Scene.GetAllComponents<PlayerController>() ) Select( player.GameObject );
			void Select( GameObject obj )
			{
				if ( !CanAssist( obj, aim, out var dot ) || dot <= best ) return;
				_smartTarget = obj; best = dot;
			}
		}
		return CanAssist( _smartTarget, aim, out _ ) ? new Ray( aim.Position, (_smartTarget.GetBounds().Center - aim.Position).Normal ) : aim;
	}

	bool CanAssist( GameObject obj, Ray aim, out float dot )
	{
		dot = -1;
		if ( !IsHostileObject( obj ) ) return false;
		var delta = obj.GetBounds().Center - aim.Position;
		if ( delta.LengthSquared < 1 || delta.Length > Actor.AttackRange ) return false;
		dot = Vector3.Dot( aim.Forward.Normal, delta.Normal );
		return dot >= 0.94f && Actor.HasLineOfSight( obj );
	}

	public float PrimaryInterval( float nominal ) => ArcRoundsActive && UpgradeLevel >= 3 ? 1f / (8 + _accelerator * 10) : nominal;
	public void NotifyPrimaryFired()
	{
		if ( !HostReady ) return;
		if ( !ArcRoundsActive || UpgradeLevel < 3 ) { _accelerator = 0; _lastPrimaryShot = 0; return; }
		var since = (float)_lastPrimaryShot;
		var continuous = since >= 0 && since <= 0.2f ? since : 0;
		_accelerator = Math.Clamp( _accelerator + continuous / 3, 0, 1 );
		_lastPrimaryShot = 0;
	}

	/// <summary>Apply permanent XO-16 Arc Rounds after an armament change, without modifying base ballistics.</summary>
	public void ApplyPrimaryPresentation()
	{
		if ( !HostReady || !ArcRoundsActive ) return;
		Actor.WeaponPresentation = Actor.WeaponPresentation with
		{
			MuzzleView = "wpn_muzzleflash_xo_elec_FP", MuzzleWorld = "wpn_muzzleflash_xo_elec",
			TracerView = "P_wpn_tracer_xo16_elec", TracerWorld = "P_wpn_tracer_xo16_elec", ImpactTable = "titan_bullet_elec"
		};
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayActivation( bool charging )
	{
		if ( !Actor.IsValid() ) return;
		var sound = Kit switch
		{
			Chassis.Ion => charging ? "Titan_Core_Laser_ChargeUp_3P" : "Titan_Core_Laser_FireStart_3P",
			Chassis.Legion => "Titan_Legion_Smart_Core_Activated_3P",
			Chassis.Ronin => "Titan_Ronin_Sword_Core_Activated_3P", Chassis.Monarch => "Titan_Monarch_Smart_Core_Activated_3P",
			Chassis.Tone => charging ? "weapon_coreability_salvo_chargeup_3p" : "weapon_coreability_salvo_fire_3P",
			Chassis.Scorch => charging ? "flamewave_start_3p" : "flamewave_blast_middle", _ => null
		};
		Titanfall2GameplayVisuals.PlayFireSound( Actor.MountIdent, sound, Eye );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaySwordSwing()
	{
		if ( !Actor.IsValid() ) return;
		Actor.CueCoreMelee();
		Titanfall2GameplayVisuals.PlayFireSound( Actor.MountIdent, "ronin_sword_melee_3p", Eye );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayImpact( BaseCombatWeapon.ShotEffect hit, string table ) =>
		Titanfall2CombatEffects.Impact( Scene, hit, Actor?.MountIdent, table );

	void UpdateVisuals()
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless ) return;
		if ( Actor is not { Dead: false } ) { ClearVisuals(); return; }
		var beam = Kit == Chassis.Ion && (IsActive || IsCharging);
		if ( !beam ) { if ( _beamVisual.IsValid() ) _beamVisual.Destroy(); _beamEffect = null; }
		else
		{
			var effect = IsCharging ? "P_handlaser_charge" : "P_wpn_lasercannon";
			if ( _beamVisual.IsValid() && _beamEffect != effect ) _beamVisual.Destroy();
			if ( !_beamVisual.IsValid() )
			{
				_beamEffect = effect;
				_beamVisual = new GameObject( Actor.GameObject, false, "TF2 Laser Core" );
				_beamVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
				_beamVisual.AddComponent<Titanfall2ParticleControlPoints>();
				var retail = _beamVisual.AddComponent<Titanfall2PlasmaFlight>(); retail.MountIdent = Actor.MountIdent; retail.EffectName = effect;
				_beamVisual.Enabled = true;
			}
			_beamVisual.WorldPosition = Eye;
			var end = BeamEnd.IsFinite && BeamEnd.Distance( Eye ) > 1 ? BeamEnd : Eye + Actor.WorldRotation.Forward * 6000;
			_beamVisual.WorldRotation = Rotation.LookAt( end - Eye );
			var points = _beamVisual.GetComponent<Titanfall2ParticleControlPoints>(); points.SetControlPoint( 0, Eye ); points.SetControlPoint( 1, end );
		}
		if ( !SwordCoreActive )
		{
			if ( _swordVisual.IsValid() ) _swordVisual.Destroy();
			_triedSword = false;
			Actor.ShowCorePrimaryWeapon( !beam );
			return;
		}
		if ( !_swordVisual.IsValid() && !_triedSword )
		{
			if ( !Actor.Renderer.IsValid() || Actor.Renderer.Model is not { IsError: false } ) return;
			_triedSword = true;
			var model = Titanfall2GameplayVisuals.GetModel( $"mount://{Actor.MountIdent ?? "titanfall2"}/models/weapons/titan_sword/w_titan_sword.mdl.visual.vmdl" );
			if ( model is not { IsError: false } ) return;
			_swordVisual = new GameObject( Actor.GameObject, true, "TF2 Sword Core" );
			_swordVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_swordVisual.AddComponent<ModelRenderer>().Model = model;
			var mount = Actor.Renderer?.GetAttachmentObject( "PROPGUN" );
			if ( mount.IsValid() ) _swordVisual.WorldTransform = mount.WorldTransform;
			foreach ( var root in model.Bones.AllBones )
			{
				if ( root.Parent is not null || Actor.Renderer?.Model?.Bones.GetBone( root.Name ) is not { } shared ) continue;
				if ( !root.Name.Contains( "gun", StringComparison.OrdinalIgnoreCase ) && !root.Name.Contains( "prophand", StringComparison.OrdinalIgnoreCase ) ) continue;
				_swordBone = shared; _swordBindInverse = root.LocalTransform.ToLocal( global::Transform.Zero ); break;
			}
			var glow = _swordVisual.AddComponent<Titanfall2PlasmaFlight>(); glow.MountIdent = Actor.MountIdent; glow.EffectName = "P_xo_sword_core_hld";
		}
		Actor.ShowCorePrimaryWeapon( false );
		UpdateSwordPose();
	}

	void UpdateSwordPose()
	{
		if ( _swordVisual.IsValid() && _swordBone is not null && Actor.Renderer.IsValid() && Actor.Renderer.TryGetBoneTransform( _swordBone, out var pose ) )
			_swordVisual.WorldTransform = pose.ToWorld( _swordBindInverse );
		else if ( _swordVisual.IsValid() && Actor.Renderer?.GetAttachmentObject( "PROPGUN" ) is { } mount && mount.IsValid() )
			_swordVisual.WorldTransform = mount.WorldTransform;
	}
	protected override void OnPreRender() => UpdateSwordPose();
	void ClearVisuals()
	{
		if ( _beamVisual.IsValid() ) _beamVisual.Destroy();
		if ( _swordVisual.IsValid() ) _swordVisual.Destroy();
		if ( Actor is { Dead: false } ) Actor.ShowCorePrimaryWeapon( true );
		_swordBone = null; _beamEffect = null;
	}
	protected override void OnDisabled() { if ( Networking.IsHost && !IsProxy ) CancelActive(); _salvoMissiles.Clear(); ClearVisuals(); }
	protected override void OnDestroy() => ClearVisuals();
}

public sealed partial class Titanfall2Npc
{
	internal void CueCoreMelee() => CueActorAttack( true );
	internal void ShowCorePrimaryWeapon( bool visible ) { if ( _gun.IsValid() ) _gun.Enabled = visible && !Dead; }
	internal void AddCoreMagazineRounds( int amount, int capacity ) { if ( Networking.IsHost && !IsProxy ) Rounds = Math.Clamp( Rounds + amount, 0, capacity ); }
	internal void PrepareSmartCore( int capacity )
	{
		if ( !Networking.IsHost || IsProxy ) return;
		Rounds = Math.Max( Rounds, capacity ); Reloading = false; _nextShot = 0;
	}
}

public partial class Titanfall2Mount
{
	Titanfall2ContentSnapshot _salvoCoreSnapshot;
	Titanfall2TitanAbilityData _salvoCoreOrdnance;
	internal Titanfall2TitanAbilityData GetSalvoCoreOrdnance()
	{
		if ( _contentSnapshot is null ) return null;
		if ( ReferenceEquals( _salvoCoreSnapshot, _contentSnapshot ) ) return _salvoCoreOrdnance;
		_salvoCoreSnapshot = _contentSnapshot;
		try
		{
			_salvoCoreOrdnance = Titanfall2WeaponData.ReadTitanAbility( "scripts/weapons/mp_titancore_salvo_core.txt", Read,
				1000, Titanfall2ProjectileBehavior.HomingTitan );
		}
		catch ( InvalidDataException ) { _salvoCoreOrdnance = null; }
		return _salvoCoreOrdnance;
		string Read( string path ) => _contentSnapshot.GetSource( path ) is { } source
			&& source.TryReadAllBytes( out var bytes, out _ ) && bytes.Length <= 512 * 1024 ? System.Text.Encoding.UTF8.GetString( bytes ) : null;
	}
}

/// <summary>Three parallel ground waves share one receiver set, so overlapping lanes cannot multiply core damage.</summary>
[Hide]
public sealed class Titanfall2FlameCoreWave : Component
{
	[Property] public Titanfall2Npc Actor { get; set; }
	GameObject _attacker;
	HashSet<Component> _receivers = [];
	float _distance;
	GameObject _visual;

	internal static void Launch( Titanfall2Npc actor, Vector3 direction, GameObject attacker )
	{
		if ( !Networking.IsHost || actor is not { Dead: false } || !direction.IsFinite ) return;
		var forward = direction.WithZ( 0 );
		forward = forward.LengthSquared > 0.01f ? forward.Normal : actor.WorldRotation.Forward;
		var side = Vector3.Cross( forward, Vector3.Up ).Normal;
		var receivers = new HashSet<Component>();
		for ( var lane = -1; lane <= 1; lane++ )
		{
			var position = actor.WorldPosition + side * (lane * 128) + Vector3.Up * 12;
			var path = Titanfall2CollisionQueries.Run( actor.Scene.Trace.Ray( actor.WorldPosition + Vector3.Up * 32, position + Vector3.Up * 20 )
				.IgnoreGameObjectHierarchy( actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ), Titanfall2TraceMask.Grenade );
			if ( path.Hit || path.StartedSolid ) continue;
			var go = new GameObject( actor.Scene, false, "TF2 Flame Core Wave" );
			go.Flags |= GameObjectFlags.NotSaved; go.Tags.Add( "projectile" );
			go.WorldPosition = position; go.WorldRotation = Rotation.LookAt( forward );
			var wave = go.AddComponent<Titanfall2FlameCoreWave>(); wave.Actor = actor; wave._attacker = attacker; wave._receivers = receivers;
			if ( Networking.IsActive ) go.NetworkSpawn( true, null ); else go.Enabled = true;
		}
	}

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !Actor.IsValid() ) return;
		_visual = new GameObject( GameObject, false, "Flame Core particles" );
		_visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var points = _visual.AddComponent<Titanfall2ParticleControlPoints>();
		points.SetControlPoint( 0, WorldPosition + WorldRotation.Left * 64 );
		points.SetControlPoint( 1, WorldPosition + WorldRotation.Right * 64 );
		var effect = _visual.AddComponent<Titanfall2PlasmaFlight>();
		effect.MountIdent = Actor.MountIdent; effect.EffectName = "P_wpn_meteor_wave";
		_visual.Enabled = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( Actor is not { Dead: false } || _distance >= 1800 ) { GameObject.Destroy(); return; }
		var travel = MathF.Min( 1200 * Time.Delta, 1800 - _distance );
		var end = WorldPosition + WorldRotation.Forward * travel;
		var wall = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( WorldPosition + Vector3.Up * 20, end + Vector3.Up * 20 ).Radius( 8 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ), Titanfall2TraceMask.Grenade );
		var ground = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( end + Vector3.Up * 48, end - Vector3.Up * 110 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ), Titanfall2TraceMask.Grenade );
		if ( wall.Hit || wall.StartedSolid || !ground.Hit || ground.StartedSolid || ground.Normal.z < 0.5f ) { GameObject.Destroy(); return; }
		WorldPosition = ground.EndPosition + Vector3.Up * 12;
		_distance += travel;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition + Vector3.Up * 100, 128 ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component receiver || _receivers.Contains( receiver ) || receiver.GameObject == Actor.GameObject ) continue;
			var npc = receiver.GameObject.GetComponentInParent<Titanfall2Npc>();
			if ( npc.IsValid() && (npc.Dead || !Actor.IsHostileToFaction( npc.Faction )) ) continue;
			if ( receiver.GameObject.GetComponentInParent<PlayerController>() is not null && (!Actor.AttackPlayers || Actor.Friendly) ) continue;
			var point = receiver.GameObject.GetBounds().Center;
			var cover = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( WorldPosition + Vector3.Up * 60, point )
				.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
			if ( cover.StartedSolid || cover.Hit && cover.GameObject?.GetComponentInParent<IDamageable>() != receiver ) continue;
			_receivers.Add( receiver );
			var damage = new DamageInfo( npc is { IsTitan: true } ? 4500 : 300, _attacker, Actor.GameObject ) { Origin = WorldPosition, Position = point };
			damage.Tags.Add( "burn" ); damage.Tags.Add( "explosion" ); damage.Tags.Add( "titan-core" );
			((IDamageable)receiver).OnDamage( damage );
		}
	}

	protected override void OnUpdate()
	{
		if ( !_visual.IsValid() || _visual.GetComponent<Titanfall2ParticleControlPoints>() is not { } points ) return;
		points.SetControlPoint( 0, WorldPosition + WorldRotation.Left * 64 );
		points.SetControlPoint( 1, WorldPosition + WorldRotation.Right * 64 );
	}
}
