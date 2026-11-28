using Sandbox.Npcs;

/// <summary>Lifecycle contract for composed Titanfall 2 weapon mechanics owned by Titanfall2Weapon.</summary>
internal interface ITitanfall2WeaponMechanic
{
	void OnStart( Titanfall2Weapon weapon ) { }
	void OnAdded( Titanfall2Weapon weapon ) { }
	void OnRemoved( Titanfall2Weapon weapon ) { }
	void Tick( Titanfall2Weapon weapon );
	void OnHolster( Titanfall2Weapon weapon );
	void OnReload( Titanfall2Weapon weapon ) { }
	void OnDisabled( Titanfall2Weapon weapon ) { }
}

/// <summary>
/// Thin Component bridge for composed weapon mechanics (Charge, Heat, SpinUp, SmartAmmo, Ordnance).
/// Retains all s&amp;box [Property], [Sync], and [Rpc] contracts on Titanfall2Weapon while delegating state and logic.
/// </summary>
public sealed partial class Titanfall2Weapon
{
	readonly Titanfall2WeaponChargeController _charge = new();
	readonly Titanfall2WeaponHeatController _heat = new();
	readonly Titanfall2WeaponSpinUpController _spinUp = new();
	readonly Titanfall2WeaponSmartAmmoController _smartAmmo = new();
	readonly Titanfall2WeaponOrdnanceController _ordnance = new();
	ITitanfall2WeaponMechanic[] _mechanics;

	ITitanfall2WeaponMechanic[] Mechanics => _mechanics ??= [_ordnance, _heat, _charge, _spinUp, _smartAmmo];

	void StartMechanics() { foreach ( var m in Mechanics ) m.OnStart( this ); }
	void AddMechanics() { foreach ( var m in Mechanics ) m.OnAdded( this ); }
	void RemoveMechanics() { foreach ( var m in Mechanics ) m.OnRemoved( this ); }
	void TickMechanics() { foreach ( var m in Mechanics ) m.Tick( this ); }
	void HolsterMechanics() { foreach ( var m in Mechanics ) m.OnHolster( this ); }
	void ReloadMechanics() { foreach ( var m in Mechanics ) m.OnReload( this ); }
	void DisableMechanics() { foreach ( var m in Mechanics ) m.OnDisabled( this ); }

	// --- Charge ---
	[Property] public Titanfall2ChargeProfile ChargeProfile { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public int ChargeReserve { get; private set; } = -1;
	// Mount assemblies cannot register custom structs with host BytePack; sync primitive fields.
	[Sync( SyncFlags.FromHost )] Guid ChargeOwnerId { get; set; }
	[Sync( SyncFlags.FromHost )] int ChargeSnapshotSerial { get; set; }
	[Sync( SyncFlags.FromHost )] int ChargeInputSerial { get; set; }
	[Sync( SyncFlags.FromHost )] bool ChargeActive { get; set; }
	[Sync( SyncFlags.FromHost )] bool ChargeFiredOnPress { get; set; }
	[Sync( SyncFlags.FromHost )] double ChargeChangedAt { get; set; }
	[Sync( SyncFlags.FromHost )] float ChargeInitialFraction { get; set; }
	[Sync( SyncFlags.FromHost )] double ChargeReadyAt { get; set; }
	public Titanfall2ChargeSnapshot ChargeSnapshot => new()
	{
		OwnerId = ChargeOwnerId,
		Serial = ChargeSnapshotSerial,
		InputSerial = ChargeInputSerial,
		State = new()
		{
			Charging = ChargeActive,
			FiredOnPress = ChargeFiredOnPress,
			ChangedAt = ChargeChangedAt,
			InitialFraction = ChargeInitialFraction,
			ReadyAt = ChargeReadyAt
		}
	};
	internal bool UsesRetailCharge => ChargeProfile.Valid;
	internal bool IsDispatchingChargeShot => _charge.IsDispatchingShot;
	internal int AvailableChargeAmmo => Math.Max( 0, ChargeReserve );
	public int ChargeSerial => ChargeSnapshotSerial;
	public float ChargeFraction => _charge.ChargeFraction( this );
	public Titanfall2ChargePhase ChargePhase => _charge.ChargePhase( this );
	internal bool CanBeginCharge => _charge.CanBegin( this );
	protected override int GetReserveAmmo( BaseAmmoResource ammoType ) => UsesRetailCharge && ammoType == PrimaryAmmoType
		? AvailableChargeAmmo : base.GetReserveAmmo( ammoType );
	void TickCharge() => _charge.Tick( this );
	void SetChargeInput( bool held ) => _charge.SetInput( this, held );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestChargeInput( int serial, bool held, float zoom ) => _charge.ApplyInput( this, serial, held, zoom );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestChargeView( int inputSerial, float zoom ) => _charge.ApplyView( this, inputSerial, zoom );
	internal void SendChargeInputRpc( int serial, bool held, float zoom ) => RequestChargeInput( serial, held, zoom );
	internal void SendChargeViewRpc( int inputSerial, float zoom ) => RequestChargeView( inputSerial, zoom );
	internal void SetChargeReserveHost( int value ) => ChargeReserve = value;
	internal void SetChargeSnapshotHost( Titanfall2ChargeSnapshot snapshot )
	{
		ChargeOwnerId = snapshot.OwnerId;
		ChargeSnapshotSerial = snapshot.Serial;
		ChargeInputSerial = snapshot.InputSerial;
		ChargeActive = snapshot.State.Charging;
		ChargeFiredOnPress = snapshot.State.FiredOnPress;
		ChargeChangedAt = snapshot.State.ChangedAt;
		ChargeInitialFraction = snapshot.State.InitialFraction;
		ChargeReadyAt = snapshot.State.ReadyAt;
	}

	// --- Heat ---
	[Property] public Titanfall2HeatProfile HeatProfile { get; set; }
	[Sync( SyncFlags.FromHost )] public double HeatVentUntil { get; private set; }
	public bool HeatVenting => HeatProfile.Valid && Time.NowDouble < HeatVentUntil;
	internal void TickHeatHost() => _heat.TickHost( this );
	void StopHeatRun() => _heat.StopRun( this );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestStopHeatRun() => _heat.ApplyStopRun( this );
	internal void SendStopHeatRunRpc() => RequestStopHeatRun();
	internal void SetHeatVentUntilHost( double value ) => HeatVentUntil = value;
	public override void DryFire()
	{
		// The minimum-to-start gate waits for recovery; it is not an empty
		// magazine and must not introduce the base dry-fire/reload delay.
		if ( HeatProfile.Valid && Clip1 > 0 ) return;
		base.DryFire();
	}

	// --- SpinUp ---
	[Property] public Titanfall2SpinUpProfile SpinUp { get; set; }
	public float SpinUpFraction => _spinUp.Fraction( this );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestStopSpinUp() => _spinUp.ApplyStopHost( this );
	internal void SendStopSpinUpRpc() => RequestStopSpinUp();

	// --- SmartAmmo ---
	[Property] public Titanfall2SmartAmmoProfile SmartAmmo { get; set; }
	[Sync( SyncFlags.FromHost )] public int SmartBurstRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public int SmartBurstSerial { get; private set; }
	public float SmartLockProgress => _smartAmmo.LockProgress;
	public int SmartReadyShots => _smartAmmo.ReadyShots( this );
	internal bool UsesRetailSmartAmmo => SmartLockEnabled && SmartAmmo.Enabled;
	internal (GameObject Target, float Fraction)[] ObserveSmartLocks() => _smartAmmo.ObserveLocks();
	internal IReadOnlyList<(Vector3 AimPoint, float Fraction, int Required)> GetSmartLockDrawTargets() => _smartAmmo.GetDrawTargets( this );
	void UpdateSmartLock() => _smartAmmo.Tick( this );
	void ResetSmartAmmo() => _smartAmmo.Reset( this );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestSmartBurst( int serial, Vector3 direction, float zoom ) => _smartAmmo.ApplyBurstRequest( this, serial, direction, zoom );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestSmartAimMode( bool aiming ) => _smartAmmo.ApplyAimMode( this, aiming );
	internal void SendSmartBurstRpc( int serial, Vector3 direction, float zoom ) => RequestSmartBurst( serial, direction, zoom );
	internal void SendSmartAimModeRpc( bool aiming ) => RequestSmartAimMode( aiming );
	internal void SetSmartBurstStateHost( int remaining, int serial ) { SmartBurstRemaining = remaining; SmartBurstSerial = serial; }
	internal void SetSmartBurstRemainingHost( int remaining ) => SmartBurstRemaining = remaining;

	// --- Ordnance ---
	[Property] public Titanfall2OrdnanceProfile OrdnanceProfile { get; set; }
	[Sync( SyncFlags.FromHost )] public bool OrdnanceBusy { get; private set; }
	[Sync( SyncFlags.FromHost )] public int OrdnanceThrowSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public double OrdnanceReadyAt { get; private set; }
	internal bool UsesOrdnance => OrdnanceProfile.Enabled;
	internal bool CanBeginOrdnance => _ordnance.CanBegin( this );
	internal int OrdnanceCharges => Math.Max( 0, Clip1 ) / Firing.Cost;
	internal int OrdnanceMaximumCharges => Math.Max( 0, ClipMaxSize ) / Firing.Cost;
	internal void ReleaseOrdnance() => _ordnance.SetInput( this, false );
	internal void TickOrdnanceHost() => _ordnance.TickHost( this );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestOrdnanceInput( int serial, bool held ) => _ordnance.ApplyInput( this, serial, held );
	[Rpc.Host( NetFlags.OwnerOnly )] void RequestCancelOrdnance() { if ( Networking.IsHost ) _ordnance.CancelHost( this ); }
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayOrdnancePrep( float seconds )
	{
		if ( IsActive && WeaponModel is Titanfall2PilotViewModel view ) view.PlayOrdnanceAction( Titanfall2PilotViewModel.TossPullout, seconds );
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayOrdnanceToss( float seconds, double startedAt )
	{
		if ( !IsActive ) return;
		var shown = WeaponModel;
		if ( Projectile.Behavior == Titanfall2ProjectileBehavior.RemoteCharge && shown is Titanfall2PilotViewModel view )
		{
			view.PlayOrdnanceAction( Titanfall2PilotViewModel.Toss, seconds, (float)Math.Max( 0, Time.NowDouble - startedAt ) );
			return;
		}
		shown?.OnAttack();
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayOrdnanceCancel()
	{
		if ( ViewModel.IsValid() ) ViewModel.GetComponent<Titanfall2PilotViewModel>()?.CancelOrdnanceAction();
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayOrdnanceSound( bool thrown )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		var firstPerson = StateEffectsFirstPerson;
		var sound = thrown ? firstPerson ? OrdnanceProfile.ThrowSound1P : OrdnanceProfile.ThrowSound3P
			: firstPerson ? OrdnanceProfile.PrepSound1P : OrdnanceProfile.PrepSound3P;
		if ( firstPerson ) Titanfall2GameplayVisuals.PlayViewSound( Scene, MountIdent, sound, WorldPosition );
		else Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, sound, WorldPosition );
	}
	internal void SendOrdnanceInputRpc( int serial, bool held ) => RequestOrdnanceInput( serial, held );
	internal void SendCancelOrdnanceRpc() => RequestCancelOrdnance();
	internal void BroadcastOrdnancePrep( float seconds ) => PlayOrdnancePrep( seconds );
	internal void BroadcastOrdnanceToss( float seconds, double startedAt ) => PlayOrdnanceToss( seconds, startedAt );
	internal void BroadcastOrdnanceCancel() => PlayOrdnanceCancel();
	internal void BroadcastOrdnanceSound( bool thrown ) => PlayOrdnanceSound( thrown );
	internal void SetOrdnanceBusyHost( bool value ) => OrdnanceBusy = value;
	internal void SetOrdnanceReadyAtHost( double value ) => OrdnanceReadyAt = value;
	internal void AdvanceOrdnanceThrowSerialHost() => OrdnanceThrowSerial = OrdnanceThrowSerial == int.MaxValue ? 1 : OrdnanceThrowSerial + 1;

	// --- Internal base-class bridges for composed mechanics ---
	internal TimeUntil NextPrimaryFireTimer => NextPrimaryFire;
	internal bool IsProjectileRequestPending => _nextProjectileRequest > 0;
	internal void ResetShootTimer() => TimeSinceShoot = 0;
	internal bool IsPrimaryClipUsed => UsesPrimaryClip;
	internal Ray CurrentViewKickAimRay => ViewKickAimRay;
	internal bool HasLocalHud => HasLocalFirstPersonHud;
	internal bool HasRoundAmmoInternal() => HasRoundAmmo();
	internal void ResolveHitscanInternal( Ray aim, float zoom, BaseNpc smartTarget = null ) => ResolveHitscan( aim, zoom, smartTarget );
	internal void PlayHeatWarningInternal() => PlayHeatWarning();
	internal void PlayHeatVentInternal() => PlayHeatVent();
	internal void TickHeatReloadInternal() => TickHeatReload();
}
