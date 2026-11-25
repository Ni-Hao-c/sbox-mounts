public sealed partial class Titanfall2Weapon
{
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2ReloadProfile ReloadProfile { get; set; }
	[Sync( SyncFlags.FromHost )] Titanfall2ReloadSnapshot RetailReload { get; set; }
	int _presentedReloadSerial;

	/// <summary>Captured before the host starts reloading; magazine replication cannot change the selected animation.</summary>
	public bool ReloadStartedEmpty => RetailReload.StartedEmpty;
	public float ReloadDuration => RetailReload.Serial != 0 ? RetailReload.Duration : ReloadTime;
	public float ReloadElapsed => RetailReload.ElapsedAt( Time.NowDouble );
	public int ReloadSerial => RetailReload.Serial;

	public override bool CanReload() => Active && IsHeld && IsActive && base.CanReload();

	public override void Reload()
	{
		// Base's OwnerOnly ReloadHost RPC calls this virtual method on the host.
		// Do not select from a client's potentially stale magazine or start a local timer.
		if ( !Networking.IsHost ) { base.Reload(); return; }
		if ( !CanReload() ) return;
		ResetSmartAmmo();
		StopSpinUp();
		var startedEmpty = Clip1 <= 0;
		var duration = ReloadProfile.DurationFor( startedEmpty, ReloadTime );
		RetailReload = new()
		{
			Serial = RetailReload.Serial == int.MaxValue ? 1 : RetailReload.Serial + 1,
			Active = true, StartedEmpty = startedEmpty, Duration = duration, StartedAt = Time.NowDouble
		};
		// The official reload loop reads ReloadTime after IsReloading is set. It
		// retains authority over reserve consumption, magazine fill and cancellation.
		ReloadTime = duration;
		base.Reload();
	}

	protected override void OnReloadStarted()
	{
		if ( !ReloadProfile.Enabled ) { base.OnReloadStarted(); return; }
		TryPresentRetailReload();
	}

	void TryPresentRetailReload()
	{
		// IsReloading and this atomic snapshot may arrive in either order. A snapshot
		// becomes inactive at completion, so a new reload cannot replay the previous one.
		if ( !IsReloading || !RetailReload.Active || RetailReload.Serial == 0 || _presentedReloadSerial == RetailReload.Serial ) return;
		if ( !WeaponModel.IsValid() ) return;
		_presentedReloadSerial = RetailReload.Serial;
		base.OnReloadStarted();
	}

	/// <summary>Call after base.OnUpdate: presentation waits for both the host state and the weapon model.</summary>
	internal void TickRetailReloadPresentation()
	{
		if ( Networking.IsHost && IsReloading && (!IsHeld || !IsActive) ) CancelReload();
		if ( ReloadProfile.Enabled ) TryPresentRetailReload();
	}

	protected override void OnReloadFinished()
	{
		if ( Networking.IsHost && RetailReload.Active ) RetailReload = RetailReload with { Active = false };
		base.OnReloadFinished();
	}

	protected override void OnDisabled()
	{
		ResetSmartAmmo();
		StopSpinUp();
		// Holstering already calls the engine's CancelReload. Disabling the component
		// must also cancel its pending task so re-enabling cannot complete an old reload.
		CancelReload();
		DestroyWeaponHud();
		base.OnDisabled();
	}
}
