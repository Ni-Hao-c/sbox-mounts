/// <summary>Rechamber follows the normal fire interval; holstering preserves the pending bolt cycle.</summary>
public record struct Titanfall2RechamberState
{
	public bool Pending { get; set; }
	public bool Active { get; set; }
	public bool NeedsBolt { get; set; }
	public int Serial { get; set; }
	public double FireEndsAt { get; set; }
	public double StartedAt { get; set; }
	public float Duration { get; set; }
	public float ScriptDuration { get; set; }
	public float ReadySeconds { get; set; }
	public float AnimationDuration { get; set; }
	public float BoltSeconds { get; set; }

	internal readonly Titanfall2RechamberState Shot( double now, float fireInterval, float seconds, bool semiAuto, Titanfall2RechamberProfile profile = default )
	{
		if ( !semiAuto || !float.IsFinite( seconds ) || seconds <= 0 ) return this;
		// 5AA000/5AA7A0: ACT_VM_RECHAMBER uses rechamber_time as the target
		// clip duration; playback rate is natural duration / target, capped at 12.
		var animation = profile.AnimationSeconds > 0 ? MathF.Max( seconds, profile.AnimationSeconds / 12 ) : seconds;
		var scale = profile.AnimationSeconds > 0 ? animation / profile.AnimationSeconds : 1;
		var ready = profile.ReadySeconds * scale;
		var bolt = profile.BoltSeconds > 0 ? profile.BoltSeconds * scale : animation;
		return this with
		{
			Pending = true, NeedsBolt = true, Active = false, FireEndsAt = now + Math.Max( 0, fireInterval ),
			Duration = ready > 0 ? MathF.Min( seconds, ready ) : seconds,
			ScriptDuration = seconds, ReadySeconds = ready,
			AnimationDuration = animation, BoltSeconds = ready > 0 ? MathF.Min( bolt, ready ) : bolt
		};
	}

	internal readonly Titanfall2RechamberState Tick( double now, bool canAnimate )
	{
		var state = this;
		if ( Active && now >= StartedAt + BoltSeconds ) state.NeedsBolt = false;
		if ( !state.NeedsBolt && now >= StartedAt + Duration ) state.Pending = false;
		if ( !canAnimate )
		{
			// A cancelled animation cannot deliver its future READY event. Keep
			// the script deadline until a restarted bolt cycle supplies one.
			if ( Active && state.Pending ) state.Duration = ScriptDuration;
			return state with { Active = false };
		}
		if ( Active ) return state with { Active = now < StartedAt + AnimationDuration };
		if ( !state.NeedsBolt ) return state;
		return now < FireEndsAt ? state : state with
		{ Active = true, StartedAt = now, Serial = Serial == int.MaxValue ? 1 : Serial + 1,
			Duration = ReadySeconds > 0 ? MathF.Min( ScriptDuration, ReadySeconds ) : ScriptDuration };
	}

	internal readonly Titanfall2RechamberState ReloadFinished( bool empty, bool completed = true ) => empty && completed
		? this with { Pending = false, NeedsBolt = false, Active = false } : this;
}

public sealed partial class Titanfall2Weapon
{
	Titanfall2RechamberState _retailRechamber;
	[Sync( SyncFlags.FromHost )] bool RechamberActive { get; set; }
	[Sync( SyncFlags.FromHost )] bool RechamberPending { get; set; }
	[Sync( SyncFlags.FromHost )] int RechamberSyncSerial { get; set; }
	[Sync( SyncFlags.FromHost )] float RechamberAnimDuration { get; set; }
	[Sync( SyncFlags.FromHost )] double RechamberStartedAt { get; set; }
	Titanfall2RechamberState RetailRechamber
	{
		get => _retailRechamber;
		set
		{
			_retailRechamber = value;
			RechamberActive = value.Active;
			RechamberPending = value.Pending;
			RechamberSyncSerial = value.Serial;
			RechamberAnimDuration = value.AnimationDuration;
			RechamberStartedAt = value.StartedAt;
		}
	}
	[Property] public Titanfall2RechamberProfile RechamberProfile { get; set; }
	public bool IsRechambering => RechamberActive;
	public bool NeedsRechamber => RechamberPending;
	public int RechamberSerial => RechamberSyncSerial;
	public float RechamberDuration => RechamberAnimDuration;
	public float RechamberElapsed => (float)Math.Max( 0, Time.NowDouble - RechamberStartedAt );

	void TickRechamber()
	{
		if ( Networking.IsHost ) RetailRechamber = RetailRechamber.Tick( Time.NowDouble, IsHeld && IsActive && !IsReloading );
	}

	bool CanAcceptRechamberShot()
	{
		TickRechamber();
		return !RetailRechamber.Pending;
	}

	void RecordRechamberShot( float interval )
	{
		if ( !Networking.IsHost || !IsHeld ) return;
		// client.dll 5B48C0 sets the flag only for fire_mode=1 (semi-auto).
		RetailRechamber = RetailRechamber.Shot( Time.NowDouble, interval, RechamberTime, !PrimaryAutomatic && !UsesOrdnance, RechamberProfile );
	}

	void InterruptRechamber()
	{
		if ( Networking.IsHost ) RetailRechamber = RetailRechamber.Tick( Time.NowDouble, false );
		StopSpreadRun();
	}

	void FinishReloadRechamber()
	{
		// Base calls OnReloadFinished for cancellation too. CancelReload invalidates
		// the snapshot before that callback; only a completed load chambers a round.
		if ( Networking.IsHost ) RetailRechamber = RetailRechamber.ReloadFinished( ReloadStartedEmpty, RetailReload.Active && Clip1 > 0 );
	}
}
