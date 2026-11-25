using System.Runtime.CompilerServices;

public enum Titanfall2TitanLifeState { Normal, Doomed, Ejecting, Dead }

internal readonly record struct Titanfall2TitanHealthSettlement( float Health, float ActualLoss, bool BecameDoomed );

internal static class Titanfall2TitanLifeRules
{
	internal static bool CanTransition( Titanfall2TitanLifeState from, Titanfall2TitanLifeState to ) => from switch
	{
		Titanfall2TitanLifeState.Normal => to is Titanfall2TitanLifeState.Doomed or Titanfall2TitanLifeState.Ejecting or Titanfall2TitanLifeState.Dead,
		Titanfall2TitanLifeState.Doomed => to is Titanfall2TitanLifeState.Ejecting or Titanfall2TitanLifeState.Dead,
		Titanfall2TitanLifeState.Ejecting => to == Titanfall2TitanLifeState.Dead,
		_ => false
	};

	internal static Titanfall2TitanHealthSettlement Settle( Titanfall2TitanLifeState state, float healthBefore, float healthAfter, float acceptedDamage,
		float doomedReserve, bool allowDoom, bool forced )
	{
		// BaseNpc clamps its Health setter at zero. A settled overkill cannot be
		// inverted by adding AcceptedDamage back to Health: the excess was lost.
		var loss = !float.IsNaN( acceptedDamage ) && float.IsFinite( healthBefore )
			? Math.Clamp( acceptedDamage, 0, MathF.Max( 0, healthBefore ) ) : 0;
		var doom = state == Titanfall2TitanLifeState.Normal && healthAfter < 1 && !forced && allowDoom
			&& float.IsFinite( doomedReserve ) && doomedReserve >= 1;
		return new( doom ? doomedReserve : healthAfter, loss, doom );
	}
}

/// <summary>Per-event health snapshots for the engine's synchronous, reentrant hit pipeline.</summary>
internal sealed class Titanfall2DamageHealthSnapshots
{
	sealed class Snapshot( float health ) { internal float Health = health; }
	// A listener can cancel after OnDamaging, in which case there is no OnDamaged
	// callback. Weak keys allow those events to disappear without a retained queue.
	readonly ConditionalWeakTable<object, Snapshot> _pending = new();
	internal void Capture( object damageEvent, float health )
	{
		RebasePending( health );
		_pending.Remove( damageEvent );
		_pending.Add( damageEvent, new( health ) );
	}
	internal bool TryTake( object damageEvent, out float healthBefore )
	{
		healthBefore = 0;
		if ( !_pending.TryGetValue( damageEvent, out var snapshot ) ) return false;
		_pending.Remove( damageEvent );
		healthBefore = snapshot.Health;
		return true;
	}
	internal void RebasePending( float health )
	{
		// An inner hit runs to completion before the outer hit deducts health.
		// Advance the outer event's pool to that result, including a new doomed
		// reserve, while retaining distinct event identities and settled amounts.
		foreach ( var pair in _pending ) pair.Value.Health = health;
	}
}

/// <summary>The persistent chassis state survives changes between pilot and AI control.</summary>
public record struct Titanfall2TitanLifeSnapshot
{
	public Titanfall2TitanLifeState State { get; set; }
	public int Serial { get; set; }
	public float NormalHealth { get; set; }
	public float DoomedHealth { get; set; }
	public double ChangedAt { get; set; }
}

public sealed partial class Titanfall2Npc
{
	// Retail scripts/players/mp/titan_{atlas,stryder,ogre}.set: global.healthDoomed [$mp].
	// SP's 4000 and the hardcore ClassMod's 1250 are separate configurations.
	internal const float RetailMultiplayerDoomedHealth = 2500;
	internal const float RetailEjectDestructDelay = 0.15f; // sh_titan.gnut, after pilot launch.
	[Property] public bool AllowDoomedState { get; set; } = true;
	[Property] public float MaxDoomedHealth { get; set; } = RetailMultiplayerDoomedHealth;
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2TitanLifeSnapshot TitanLife { get; private set; }
	public Titanfall2TitanLifeState LifeState => IsTitan ? TitanLife.State : Dead ? Titanfall2TitanLifeState.Dead : Titanfall2TitanLifeState.Normal;
	public bool IsDoomed => IsTitan && LifeState == Titanfall2TitanLifeState.Doomed;
	public float DoomedHealth => IsDoomed ? TitanLife.DoomedHealth : 0;
	public bool TitanCanAct => !Dead && !IsNuking && (!IsTitan || LifeState is Titanfall2TitanLifeState.Normal or Titanfall2TitanLifeState.Doomed);
	/// <summary>Host-only monotonic loss counter. A health-pool transition is not damage or healing.</summary>
	internal double DamageTakenTotal { get; private set; }
	internal GameObject LastVerifiedTitanAttacker { get; private set; }
	internal GameObject LastVerifiedTitanWeapon { get; private set; }
	bool _forceTitanDeath;
	bool _ejectKillRequested;
	readonly Titanfall2DamageHealthSnapshots _damageHealthSnapshots = new();

	void InitializeTitanLife()
	{
		if ( !IsTitan ) return;
		if ( TitanLife.Serial == 0 )
			TitanLife = new() { State = Titanfall2TitanLifeState.Normal, NormalHealth = Health, Serial = 1, ChangedAt = Time.NowDouble };
		else
		{
			Health = LifeState == Titanfall2TitanLifeState.Ejecting ? TitanLife.NormalHealth + TitanLife.DoomedHealth
				: LifeState == Titanfall2TitanLifeState.Normal ? TitanLife.NormalHealth : TitanLife.DoomedHealth;
			if ( LifeState != Titanfall2TitanLifeState.Normal ) Shield = 0;
			if ( LifeState == Titanfall2TitanLifeState.Dead ) { Dead = true; Health = 0; }
		}
	}

	void AddActualDamageTaken( float amount )
	{
		if ( Networking.IsHost && !IsProxy && float.IsFinite( amount ) && amount > 0 ) DamageTakenTotal += amount;
	}

	/// <summary>Called after the engine has settled listeners and deducted health, before its death check.</summary>
	void SettleTitanLifeDamage( Component.IDamageable.DamageEvent e )
	{
		var captured = _damageHealthSnapshots.TryTake( e, out var healthBefore );
		if ( !Networking.IsHost || IsProxy || Dead || e.Cancelled ) return;
		// Every accepted hit reaches our OnDamaging hook, including direct damage
		// without a verified HostDamage receipt. Never invent credit if it did not.
		var settled = Titanfall2TitanLifeRules.Settle( LifeState, captured ? healthBefore : 0, Health, e.AcceptedDamage, MaxDoomedHealth,
			IsTitan && AllowDoomedState && Role != FrontierRole.NukeTitan, _forceTitanDeath );
		AddActualDamageTaken( settled.ActualLoss );
		if ( !IsTitan ) { _damageHealthSnapshots.RebasePending( Health ); return; }
		if ( Titanfall2HostDamage.TryGetVerifiedDelivery( this, e.DamageInfo, out var attacker, out var weapon ) )
		{
			LastVerifiedTitanAttacker = attacker;
			LastVerifiedTitanWeapon = weapon;
		}
		if ( settled.BecameDoomed )
		{
			// Ordinary retail killshots cannot spill through the new reserve pool.
			// Deliberate forced kills use ForceKillTitan instead of untrusted DamageInfo tags.
			Health = settled.Health;
			Shield = 0;
			SetTitanLifeState( Titanfall2TitanLifeState.Doomed );
			_damageHealthSnapshots.RebasePending( Health );
			return;
		}
		TitanLife = LifeState == Titanfall2TitanLifeState.Normal
			? TitanLife with { NormalHealth = MathF.Max( 0, Health ) }
			: TitanLife with { DoomedHealth = MathF.Max( 0, Health ) };
		_damageHealthSnapshots.RebasePending( Health );
	}

	void SetTitanLifeState( Titanfall2TitanLifeState state )
	{
		if ( !Networking.IsHost || IsProxy || !IsTitan || !Titanfall2TitanLifeRules.CanTransition( TitanLife.State, state ) ) return;
		TitanLife = TitanLife with { State = state, Serial = TitanLife.Serial == int.MaxValue ? 1 : TitanLife.Serial + 1,
			NormalHealth = state == Titanfall2TitanLifeState.Normal || state == Titanfall2TitanLifeState.Ejecting && LifeState == Titanfall2TitanLifeState.Normal ? Health : 0,
			DoomedHealth = state == Titanfall2TitanLifeState.Doomed || state == Titanfall2TitanLifeState.Ejecting && LifeState == Titanfall2TitanLifeState.Doomed ? Health : 0,
			ChangedAt = Time.NowDouble };
		if ( state is Titanfall2TitanLifeState.Ejecting or Titanfall2TitanLifeState.Dead )
		{
			GetComponent<Titanfall2TitanCore>()?.CancelForTitanLife();
			GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
			Reloading = false;
			_target = null;
			Leader = null;
			Moving = false;
			Movement?.Stop();
			Agent?.Stop();
			EndCurrentSchedule();
		}
	}

	/// <summary>Commit only after the seat has found a safe launch point; it then detaches its pilot.</summary>
	internal bool BeginTitanEjection()
	{
		if ( !Networking.IsHost || IsProxy || !Active || !IsTitan || !TitanCanAct ) return false;
		SetTitanLifeState( Titanfall2TitanLifeState.Ejecting );
		if ( Role == FrontierRole.NukeTitan ) ArmNuke();
		return true;
	}

	/// <summary>Force a native once-only death without manufacturing a client-provided attacker.</summary>
	internal void ForceKillTitan( GameObject attacker = null, GameObject weapon = null )
	{
		if ( !Networking.IsHost || IsProxy || !IsTitan || Dead || _forceTitanDeath ) return;
		_forceTitanDeath = true;
		try
		{
			var damage = new DamageInfo( MathF.Max( 1, Health ) + MathF.Max( 0, Shield ), attacker, weapon )
				{ Origin = WorldPosition, Position = WorldPosition };
			damage.Tags.Add( "titan-self-destruct" );
			Titanfall2HostDamage.Deal( this, damage );
		}
		finally { _forceTitanDeath = false; }
	}

	// Returns true while the chassis may no longer run ordinary AI, guns, dash or cores.
	bool TickTitanLife()
	{
		if ( !IsTitan ) return false;
		if ( IsNuking && LifeState is Titanfall2TitanLifeState.Normal or Titanfall2TitanLifeState.Doomed )
		{
			SetTitanLifeState( Titanfall2TitanLifeState.Ejecting );
			if ( Seat.IsValid() ) Seat.ReleaseSeat();
		}
		if ( LifeState != Titanfall2TitanLifeState.Ejecting ) return LifeState == Titanfall2TitanLifeState.Dead;
		if ( IsNuking ) { TickNuke(); return true; }
		if ( !_ejectKillRequested && Time.NowDouble - TitanLife.ChangedAt >= RetailEjectDestructDelay )
		{
			_ejectKillRequested = true;
			ForceKillTitan( LastVerifiedTitanAttacker.IsValid() ? LastVerifiedTitanAttacker : null,
				LastVerifiedTitanWeapon.IsValid() ? LastVerifiedTitanWeapon : null );
		}
		return true;
	}
}

/// <summary>Deliver the launch after SitMoveMode has restored the pilot's rigidbody and collider.</summary>
[Hide]
public sealed class Titanfall2EjectionImpulse : Component
{
	PlayerController _pilot;
	Vector3 _velocity;
	internal void Launch( PlayerController pilot, Vector3 velocity ) { _pilot = pilot; _velocity = velocity; }
	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( !_pilot.IsValid() || !_pilot.Active ) { Destroy(); return; }
		if ( _pilot.Mode is Sandbox.Movement.SitMoveMode ) return;
		if ( _pilot.Body.IsValid() && _pilot.Body.Enabled )
		{
			_pilot.Body.Velocity = _velocity;
			Destroy();
		}
	}
}
