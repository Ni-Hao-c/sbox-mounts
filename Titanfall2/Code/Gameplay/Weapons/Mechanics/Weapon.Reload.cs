public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2ReloadProfile ReloadProfile { get; set; }
	[Sync( SyncFlags.FromHost )] int RetailReloadSerial { get; set; }
	[Sync( SyncFlags.FromHost )] bool RetailReloadActive { get; set; }
	[Sync( SyncFlags.FromHost )] bool RetailReloadStartedEmpty { get; set; }
	[Sync( SyncFlags.FromHost )] bool RetailReloadSegmented { get; set; }
	[Sync( SyncFlags.FromHost )] float RetailReloadDuration { get; set; }
	[Sync( SyncFlags.FromHost )] float RetailReloadLoopDuration { get; set; }
	[Sync( SyncFlags.FromHost )] float RetailReloadEndDuration { get; set; }
	[Sync( SyncFlags.FromHost )] double RetailReloadStartedAt { get; set; }
	[Sync( SyncFlags.FromHost )] int RetailReloadMilestone { get; set; }
	Titanfall2ReloadSnapshot RetailReload
	{
		get => new()
		{
			Serial = RetailReloadSerial,
			Active = RetailReloadActive,
			StartedEmpty = RetailReloadStartedEmpty,
			Segmented = RetailReloadSegmented,
			Duration = RetailReloadDuration,
			LoopDuration = RetailReloadLoopDuration,
			EndDuration = RetailReloadEndDuration,
			StartedAt = RetailReloadStartedAt,
			Milestone = RetailReloadMilestone
		};
		set
		{
			RetailReloadSerial = value.Serial;
			RetailReloadActive = value.Active;
			RetailReloadStartedEmpty = value.StartedEmpty;
			RetailReloadSegmented = value.Segmented;
			RetailReloadDuration = value.Duration;
			RetailReloadLoopDuration = value.LoopDuration;
			RetailReloadEndDuration = value.EndDuration;
			RetailReloadStartedAt = value.StartedAt;
			RetailReloadMilestone = value.Milestone;
		}
	}
	int _presentedReloadSerial;
	[Sync( SyncFlags.FromHost )] public int SavedHeatReloadMilestone { get; private set; }
	double _heatFullReloadStartedAt;
	int _heatReloadFilledSerial;

	/// <summary>Captured before the host starts reloading; magazine replication cannot change the selected animation.</summary>
	public bool ReloadStartedEmpty => RetailReload.StartedEmpty;
	public bool ReloadIsSegmented => RetailReload.Serial != 0 ? RetailReload.Segmented : ReloadProfile.Segmented;
	public float ReloadDuration => RetailReload.Serial != 0 ? RetailReload.Duration : ReloadTime;
	public float ReloadLoopDuration => RetailReload.Serial != 0 && RetailReload.LoopDuration > 0
		? RetailReload.LoopDuration : ReloadProfile.LoopDurationFor( ReloadTime );
	public float ReloadEndDuration => RetailReload.Serial != 0 && RetailReload.EndDuration > 0
		? RetailReload.EndDuration : ReloadProfile.EndDurationFor( ReloadStartedEmpty, 0.55f );
	public float ReloadElapsed => RetailReload.ElapsedAt( Time.NowDouble );
	public int ReloadSerial => RetailReload.Serial;
	public int ReloadMilestone => RetailReload.Milestone;

	public override bool CanReload() => !UsesOrdnance && !UsesRetailCharge && (!HeatProfile.Valid || Clip1 == 0)
		&& Active && IsHeld && IsActive && base.CanReload();

	public override void Reload()
	{
		// Base's OwnerOnly ReloadHost RPC calls this virtual method on the host.
		// Do not select from a client's potentially stale magazine or start a local timer.
		if ( !Networking.IsHost ) { base.Reload(); return; }
		if ( !CanReload() ) return;
		InterruptRechamber();
		ReloadMechanics();
		var startedEmpty = Clip1 <= 0;
		var duration = ReloadProfile.DurationFor( startedEmpty, ReloadTime );
		var segmented = ReloadProfile.Enabled && ReloadProfile.Segmented && !HeatProfile.Valid;
		var loopDuration = segmented ? ReloadProfile.LoopDurationFor( duration ) : 0f;
		var endDuration = segmented ? ReloadProfile.EndDurationFor( startedEmpty, 0.55f ) : 0f;
		var milestone = HeatProfile.Valid ? SavedHeatReloadMilestone : 0;
		var stage = HeatProfile.Valid ? HeatReloadStage( milestone ) : default;
		if ( HeatProfile.Valid && startedEmpty )
		{
			if ( milestone == 0 ) _heatFullReloadStartedAt = Time.NowDouble;
			var late = milestone > 0 && ReloadProfile.EmptyLateSeconds is { } durations && milestone <= durations.Length ? durations[milestone - 1] : duration;
			duration = Titanfall2HeatReloadData.EffectiveDuration( duration, late, _heatFullReloadStartedAt, Time.NowDouble, milestone );
			if ( duration <= 0 ) return;
			CanCancelReload = false;
		}
		RetailReload = new()
		{
			Serial = RetailReload.Serial == int.MaxValue ? 1 : RetailReload.Serial + 1,
			Active = true, StartedEmpty = startedEmpty, Segmented = segmented,
			Duration = duration, LoopDuration = loopDuration, EndDuration = endDuration,
			StartedAt = Time.NowDouble, Milestone = milestone
		};
		// The official reload loop reads IncrementalReloading, ReloadStartTime and ReloadTime
		// after IsReloading is set. For segmented weapons (Softball, Mastiff), each iteration
		// loads one round and invokes OnReloadInserted -> OnIncrementalReload.
		IncrementalReloading = segmented;
		if ( segmented )
		{
			ReloadStartTime = duration;
			ReloadTime = loopDuration;
			CanCancelReload = true;
		}
		else
		{
			ReloadStartTime = 0f;
			// L-STAR grants ammo at its model FILL event, then remains reloading
			// until READY. The base timer ends at READY; the animation keeps its full duration.
			ReloadTime = duration * (stage.ReadyCycle > 0 ? stage.ReadyCycle : 1);
		}
		base.Reload();
		if ( HeatProfile.Valid && IsReloading )
		{
			if ( milestone == 0 ) PlayHeatBurnout();
			TickHeatReload();
		}
	}

	Titanfall2HeatReloadStage HeatReloadStage( int milestone ) => ReloadProfile.EmptyStages?.FirstOrDefault( x => x.Milestone == milestone ) ?? default;

	void TickHeatReload()
	{
		if ( !Networking.IsHost || !HeatProfile.Valid || !IsReloading || !RetailReload.Active
			|| _heatReloadFilledSerial == RetailReload.Serial ) return;
		var stage = HeatReloadStage( RetailReload.Milestone );
		var elapsed = ReloadElapsed;
		foreach ( var item in stage.Events ?? [] )
			if ( elapsed >= item.Cycle * RetailReload.Duration ) SavedHeatReloadMilestone = item.Milestone;
		if ( stage.FillCycle <= 0 || elapsed < stage.FillCycle * RetailReload.Duration ) return;
		// Retail L-STAR has ammo_no_remove_from_stockpile=1. Heat recovery and
		// the lens replacement never draw ammunition from an inventory reserve.
		Clip1 = ClipMaxSize; SavedHeatReloadMilestone = 0; _heatReloadFilledSerial = RetailReload.Serial;
	}

	public override void CancelReload()
	{
		if ( Networking.IsHost ) TickHeatReload();
		// IsReloading=false invokes OnReloadFinished synchronously even on cancel.
		// Revoke completion eligibility before the base changes that property.
		if ( Networking.IsHost && RetailReload.Active ) RetailReload = RetailReload with { Active = false };
		base.CancelReload();
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
		TickHeatReload();
		if ( Networking.IsHost && IsReloading && (!IsHeld || !IsActive) ) CancelReload();
		if ( ReloadProfile.Enabled ) TryPresentRetailReload();
	}

	protected override void OnReloadFinished()
	{
		FinishReloadRechamber();
		// A frame may cross both FILL and READY before our model-event tick.
		// Base has already filled the clip in that case; do not retain a stale
		// milestone that would block regeneration or resume the next overheat.
		if ( Networking.IsHost && HeatProfile.Valid && Clip1 >= ClipMaxSize )
		{
			SavedHeatReloadMilestone = 0; _heatReloadFilledSerial = RetailReload.Serial;
		}
		if ( Networking.IsHost && RetailReload.Active ) RetailReload = RetailReload with { Active = false };
		base.OnReloadFinished();
	}

	protected override void OnDisabled()
	{
		InterruptRechamber();
		DisableMechanics();
		StopChargePresentation();
		StopHeatPresentation();
		// Holstering already calls the engine's CancelReload. Disabling the component
		// must also cancel its pending task so re-enabling cannot complete an old reload.
		CancelReload();
		DestroyWeaponHud();
		base.OnDisabled();
	}
}
