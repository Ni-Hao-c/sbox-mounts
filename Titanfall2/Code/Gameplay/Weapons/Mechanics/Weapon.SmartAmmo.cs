using Sandbox.Npcs;

/// <summary>
/// Composed Smart Pistol target-acquisition, multi-lock burst queueing, audio, and damage multiplier controller.
/// </summary>
internal sealed class Titanfall2WeaponSmartAmmoController : ITitanfall2WeaponMechanic
{
	readonly List<SmartTarget> _locks = [];
	internal readonly Queue<BaseNpc> BurstQueue = new();
	PlayerController _owner;
	double _tick = double.NaN;
	double _nextTarget;
	TimeUntil _shotTime;
	float _burstZoom;
	int _requestSerial;
	int _lastRequestSerial;
	int _observedBurstSerial;
	bool _remoteAiming;
	bool _sentAiming;
	bool _acquiringSoundActive;
	GameObject _audioRoot;
	Titanfall2SoundEventEmitter _audio;

	sealed class SmartTarget
	{
		internal BaseNpc Npc;
		internal float Fraction;
		internal int Required = 1;
		internal int ConfirmedLocks;
		internal double LastVisible;
	}

	internal float LockProgress => _locks.Count == 0 ? 0 : Math.Clamp( _locks.Max( entry => entry.Fraction ), 0, 1 );
	internal int ReadyShots( Titanfall2Weapon w ) => Math.Min( w.SmartAmmo.MaxBurst, _locks.Sum( entry => (int)MathF.Floor( entry.Fraction ) ) );
	internal bool CanStartAttack( Titanfall2Weapon w ) => w.SmartBurstRemaining == 0 && _shotTime <= 0;

	public void OnHolster( Titanfall2Weapon w ) => Reset( w );
	public void OnReload( Titanfall2Weapon w ) => Reset( w );
	public void OnDisabled( Titanfall2Weapon w ) => Reset( w );

	internal (GameObject Target, float Fraction)[] ObserveLocks() => _locks.Select( entry => (entry.Npc.GameObject, entry.Fraction) ).ToArray();

	internal IReadOnlyList<(Vector3 AimPoint, float Fraction, int Required)> GetDrawTargets( Titanfall2Weapon w ) => _locks
		.Where( entry => IsTargetAlive( w, entry.Npc ) && entry.Fraction > 0 )
		.Select( entry => (AimPoint( entry.Npc ), entry.Fraction, Math.Max( 1, entry.Required )) )
		.ToArray();

	static Vector3 AimPoint( BaseNpc npc )
	{
		// The original script chooses HEADSHOT for AI. Imported models preserve
		// their authored attachments; generic Sandbox NPCs can use their eye point.
		var renderer = npc.GetComponentInChildren<SkinnedModelRenderer>();
		if ( renderer.IsValid() && renderer.SceneModel.IsValid()
			&& renderer.SceneModel.GetAttachment( "HEADSHOT", true ) is { } attachment ) return attachment.Position;
		return npc.EyePosition;
	}

	static bool IsTargetAlive( Titanfall2Weapon w, BaseNpc npc ) => npc.IsValid() && npc.Active && npc.Health > 0 && w.Owner.IsValid()
		// npc_titan.txt declares large; the retail NPC vtable predicate requires
		// the weapon's small lock type to match. Titans remain manually shootable.
		&& (npc is not Titanfall2Npc retail || retail.AcceptsSmallSmartAmmo)
		&& npc.GetDisposition( w.Owner.GameObject ) == Disposition.Hostile;

	static bool CanSeeTarget( Titanfall2Weapon w, BaseNpc npc, Ray aim )
	{
		if ( !IsTargetAlive( w, npc ) ) return false;
		var point = AimPoint( npc );
		if ( w.Owner.WorldPosition.Distance( npc.WorldPosition ) > w.SmartAmmo.SearchDistance
			|| w.SmartAmmo.SearchMetric( aim, npc.GameObject.GetBounds(), point ) >= 1 ) return false;
		var hit = Titanfall2CollisionQueries.Run( w.Scene.Trace.Ray( aim.Position, point ).UseHitboxes()
			.IgnoreGameObjectHierarchy( w.Owner.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		return !hit.StartedSolid && (!hit.Hit || hit.GameObject == npc.GameObject || npc.GameObject.IsDescendant( hit.GameObject ));
	}

	public void Tick( Titanfall2Weapon w )
	{
		if ( _tick == Time.NowDouble ) return;
		_tick = Time.NowDouble;
		if ( !w.UsesRetailSmartAmmo || !w.Active || !w.IsHeld || !w.IsActive || w.IsReloading || !w.Owner.IsValid() )
		{
			Reset( w ); return;
		}
		BindOwner( w );
		if ( !Networking.IsHost && w.IsProxy ) return;
		if ( !Networking.IsHost && _sentAiming != w.IsAiming )
		{
			_sentAiming = w.IsAiming;
			w.SendSmartAimModeRpc( _sentAiming );
		}
		if ( _observedBurstSerial != w.SmartBurstSerial )
		{
			_observedBurstSerial = w.SmartBurstSerial;
			_locks.Clear();
		}
		if ( Networking.IsHost && w.SmartBurstRemaining > 0 )
		{
			if ( _shotTime <= 0 ) FireNextRound( w );
			return;
		}
		var aim = new Ray( w.Owner.EyePosition, w.Owner.EyeTransform.Forward );
		var aiming = Networking.IsHost && w.IsProxy ? _remoteAiming : w.IsAiming;
		var maySearch = w.SmartAmmo.SearchNpcs && (aiming ? w.SmartAmmo.AllowAds : w.SmartAmmo.AllowHip);
		var lockedIncreased = false;
		foreach ( var entry in _locks.ToArray() )
		{
			if ( !IsTargetAlive( w, entry.Npc ) ) { _locks.Remove( entry ); continue; }
			var distance = w.Owner.WorldPosition.Distance( entry.Npc.WorldPosition );
			var heavy = entry.Npc is Titanfall2Npc { IsTitan: true };
			var required = w.SmartAmmo.RequiredLocks( entry.Npc.Health, heavy ? w.TitanDamage : w.Ballistics.Damage, true, heavy );
			entry.Required = required;
			if ( maySearch && CanSeeTarget( w, entry.Npc, aim ) )
			{
				entry.LastVisible = Time.NowDouble;
				var seconds = w.SmartAmmo.LockSeconds( distance, true );
				entry.Fraction = seconds <= 0 ? required : Math.Clamp( entry.Fraction + Time.Delta / seconds, 0, required );
			}
			else if ( Time.NowDouble - entry.LastVisible > w.SmartAmmo.UnlockDebounce
				&& !(w.SmartAmmo.StickToLocked && entry.Fraction >= 1) )
			{
				var seconds = w.SmartAmmo.LockSeconds( distance, true, decay: true );
				entry.Fraction = seconds <= 0 ? 0 : MathF.Max( 0, entry.Fraction - Time.Delta * required / seconds );
				if ( entry.Fraction == 0 ) _locks.Remove( entry );
			}
			var nowConfirmed = (int)MathF.Floor( entry.Fraction );
			if ( nowConfirmed > entry.ConfirmedLocks ) lockedIncreased = true;
			entry.ConfirmedLocks = nowConfirmed;
		}
		if ( maySearch && _locks.Count < w.SmartAmmo.MaxTargets && Time.NowDouble >= _nextTarget )
		{
			// Stable target objects survive acquisition and burst snapshots. A dead
			// first target must never shift subsequent rounds to a different actor.
			foreach ( var candidate in w.Scene.GetAllComponents<BaseNpc>()
				.Where( npc => !_locks.Any( entry => entry.Npc == npc ) && CanSeeTarget( w, npc, aim ) )
				.OrderBy( npc => w.SmartAmmo.SearchMetric( aim, npc.GameObject.GetBounds(), AimPoint( npc ) ) ) )
			{
				var required = w.SmartAmmo.RequiredLocks( candidate.Health, w.Ballistics.Damage, true, false );
				var seconds = w.SmartAmmo.LockSeconds( w.Owner.WorldPosition.Distance( candidate.WorldPosition ), true );
				var fraction = seconds <= 0 ? required : MathF.Min( required, 0.0001f + Time.Delta / seconds );
				var confirmed = (int)MathF.Floor( fraction );
				if ( confirmed > 0 ) lockedIncreased = true;
				_locks.Add( new() { Npc = candidate, Fraction = fraction, Required = required, ConfirmedLocks = confirmed, LastVisible = Time.NowDouble } );
				_nextTarget = Time.NowDouble + w.SmartAmmo.NewTargetDelay;
				if ( w.SmartAmmo.NewTargetDelay > 0 || _locks.Count >= w.SmartAmmo.MaxTargets ) break;
			}
		}
		UpdateAudio( w, lockedIncreased );
	}

	void UpdateAudio( Titanfall2Weapon w, bool lockedIncreased )
	{
		if ( !w.HasLocalHud ) { StopAudio(); return; }
		var acquiring = _locks.Any( entry => entry.Fraction > 0 && entry.Fraction < entry.Required );
		if ( !acquiring && !lockedIncreased )
		{
			if ( _acquiringSoundActive ) StopAudio();
			return;
		}
		if ( !_audioRoot.IsValid() )
		{
			_audioRoot = new GameObject( w.GameObject, false, "TF2 smart lock audio" );
			_audioRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_audio = _audioRoot.AddComponent<Titanfall2SoundEventEmitter>();
			_audioRoot.Enabled = true;
		}
		var sourcePosition = w.Owner.IsValid() ? w.Owner.EyePosition : w.WorldPosition;
		_audio.SetSource( sourcePosition );
		if ( acquiring && !_acquiringSoundActive && !string.IsNullOrEmpty( w.SmartAmmo.AcquiringSound ) )
			_acquiringSoundActive = _audio.Play( w.MountIdent, w.SmartAmmo.AcquiringSound, firstPerson: true, sourcePosition, sustain: true );
		else if ( !acquiring && _acquiringSoundActive )
		{
			_audio.StopAll();
			_acquiringSoundActive = false;
		}
		if ( lockedIncreased && !string.IsNullOrEmpty( w.SmartAmmo.ConfirmedSound ) )
			_audio.Play( w.MountIdent, w.SmartAmmo.ConfirmedSound, firstPerson: true, sourcePosition );
	}

	void StopAudio()
	{
		_audio?.StopAll();
		if ( _audioRoot.IsValid() ) _audioRoot.Destroy();
		_audioRoot = null;
		_audio = null;
		_acquiringSoundActive = false;
	}

	internal void BeginAttack( Titanfall2Weapon w )
	{
		if ( Networking.IsHost ) StartBurst( w, w.CurrentViewKickAimRay.Forward, w.AdsBlend );
		else w.SendSmartBurstRpc( ++_requestSerial, w.CurrentViewKickAimRay.Forward, w.AdsBlend );
	}

	internal void ApplyBurstRequest( Titanfall2Weapon w, int serial, Vector3 direction, float zoom )
	{
		if ( !Networking.IsHost || !w.Owner.IsValid() ) return;
		BindOwner( w );
		if ( serial <= _lastRequestSerial ) return;
		_lastRequestSerial = serial;
		StartBurst( w, direction, zoom );
	}

	void StartBurst( Titanfall2Weapon w, Vector3 direction, float zoom )
	{
		if ( !Networking.IsHost || !w.Active || !w.UsesRetailSmartAmmo || !w.Owner.IsValid() || !w.IsActive || !w.IsHeld || w.IsReloading
			|| w.SmartBurstRemaining > 0 || _shotTime > 0 || !w.HasRoundAmmoInternal() || !direction.IsFinite
			|| direction.LengthSquared is < 0.25f or > 4 || !float.IsFinite( zoom ) || zoom is < 0 or > 1
			|| Vector3.Dot( w.Owner.EyeTransform.Forward, direction.Normal ) < 0.35f ) return;
		BindOwner( w );
		BurstQueue.Clear();
		var available = w.IsPrimaryClipUsed ? w.Clip1 / w.Firing.Cost : w.SmartAmmo.MaxBurst;
		foreach ( var entry in _locks )
		{
			if ( zoom > 0 ? !w.SmartAmmo.AllowAds : !w.SmartAmmo.AllowHip ) break;
			if ( !IsTargetAlive( w, entry.Npc ) ) continue;
			for ( var i = 0; i < (int)MathF.Floor( entry.Fraction ) && BurstQueue.Count < Math.Min( available, w.SmartAmmo.MaxBurst ); ++i )
				BurstQueue.Enqueue( entry.Npc );
		}
		_burstZoom = zoom;
		if ( BurstQueue.Count == 0 )
		{
			w.ResolveHitscanInternal( new Ray( w.Owner.EyePosition, direction.Normal ), zoom );
			_shotTime = MathF.Max( w.PrimaryDelay, 0.015f );
			return;
		}
		var nextSerial = w.SmartBurstSerial == int.MaxValue ? 1 : w.SmartBurstSerial + 1;
		w.SetSmartBurstStateHost( BurstQueue.Count, nextSerial );
		_observedBurstSerial = nextSerial;
		FireNextRound( w );
	}

	void FireNextRound( Titanfall2Weapon w )
	{
		if ( !Networking.IsHost ) return;
		if ( !w.Active || !w.IsActive || !w.IsHeld || w.IsReloading || !w.Owner.IsValid() ) { Reset( w ); return; }
		while ( BurstQueue.TryDequeue( out var target ) )
		{
			if ( !IsTargetAlive( w, target ) ) continue;
			var direction = AimPoint( target ) - w.Owner.EyePosition;
			if ( direction.LengthSquared < 0.001f ) continue;
			// Re-trace every round against current geometry. A stored lock can aim
			// at its target but cannot bypass a newly closed door or a vortex.
			w.ResolveHitscanInternal( new Ray( w.Owner.EyePosition, direction.Normal ), _burstZoom, target );
			_shotTime = MathF.Max( w.PrimaryDelay, 0.015f );
			break;
		}
		w.SetSmartBurstRemainingHost( BurstQueue.Count );
		if ( w.SmartBurstRemaining == 0 || !w.HasRoundAmmoInternal() )
		{
			BurstQueue.Clear(); _locks.Clear();
			var nextSerial = w.SmartBurstSerial == int.MaxValue ? 1 : w.SmartBurstSerial + 1;
			w.SetSmartBurstStateHost( 0, nextSerial );
			_observedBurstSerial = nextSerial;
			_nextTarget = Time.NowDouble + w.SmartAmmo.NewTargetDelay;
		}
	}

	internal void Reset( Titanfall2Weapon w )
	{
		_locks.Clear(); BurstQueue.Clear();
		StopAudio();
		if ( Networking.IsHost ) w.SetSmartBurstRemainingHost( 0 );
	}

	void BindOwner( Titanfall2Weapon w )
	{
		if ( _owner == w.Owner ) return;
		Reset( w ); _owner = w.Owner; _lastRequestSerial = 0;
		_remoteAiming = _sentAiming = false;
		_nextTarget = Time.NowDouble + (w.SmartAmmo.DelayFirstTarget ? w.SmartAmmo.NewTargetDelay : 0);
	}

	internal void ApplyAimMode( Titanfall2Weapon w, bool aiming )
	{
		if ( !Networking.IsHost || !w.UsesRetailSmartAmmo || !w.Owner.IsValid() || !w.IsActive || !w.IsHeld ) return;
		BindOwner( w ); _remoteAiming = aiming;
	}

	internal static float ComputeBulletDamage( Titanfall2Weapon w, float damage, GameObject hit, BaseNpc locked )
	{
		if ( !w.UsesRetailSmartAmmo || hit?.GetComponentInParent<BaseNpc>() is not { } actor ) return damage;
		if ( locked == actor ) damage *= w.SmartAmmo.NpcLockFactor;
		if ( actor is not Titanfall2Npc npc ) return damage;
		// sh_smart_ammo.gnut's MP damage callback also applies to unlocked hits.
		if ( npc.ModelPath?.Contains( "/robots/spectre/", StringComparison.OrdinalIgnoreCase ) == true ) return MathF.Floor( npc.MaxHealth / 2 ) + 1;
		if ( npc.ModelPath?.Contains( "/humans/grunts/", StringComparison.OrdinalIgnoreCase ) == true ) return MathF.Max( damage, npc.MaxHealth );
		return damage;
	}
}
