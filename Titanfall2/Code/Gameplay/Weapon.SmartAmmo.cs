using Sandbox.Npcs;

public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2SmartAmmoProfile SmartAmmo { get; set; }
	[Sync( SyncFlags.FromHost )] public int SmartBurstRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public int SmartBurstSerial { get; private set; }
	public float SmartLockProgress => _smartLocks.Count == 0 ? 0 : Math.Clamp( _smartLocks.Max( entry => entry.Fraction ), 0, 1 );
	public int SmartReadyShots => Math.Min( SmartAmmo.MaxBurst, _smartLocks.Sum( entry => (int)MathF.Floor( entry.Fraction ) ) );
	internal bool UsesRetailSmartAmmo => SmartLockEnabled && SmartAmmo.Enabled;
	readonly List<SmartTarget> _smartLocks = [];
	readonly Queue<BaseNpc> _smartBurst = new();
	PlayerController _smartOwner;
	double _smartTick = double.NaN, _nextSmartTarget;
	TimeUntil _smartShotTime;
	float _smartBurstZoom;
	int _smartRequestSerial, _lastSmartRequestSerial, _observedSmartBurstSerial;
	bool _smartRemoteAiming, _smartSentAiming;
	sealed class SmartTarget
	{
		internal BaseNpc Npc;
		internal float Fraction;
		internal double LastVisible;
	}
	internal (GameObject Target, float Fraction)[] ObserveSmartLocks() => _smartLocks.Select( entry => (entry.Npc.GameObject, entry.Fraction) ).ToArray();

	static Vector3 SmartAimPoint( BaseNpc npc )
	{
		// The original script chooses HEADSHOT for AI. Imported models preserve
		// their authored attachments; generic Sandbox NPCs can use their eye point.
		var renderer = npc.GetComponentInChildren<SkinnedModelRenderer>();
		if ( renderer.IsValid() && renderer.SceneModel.IsValid()
			&& renderer.SceneModel.GetAttachment( "HEADSHOT", true ) is { } attachment ) return attachment.Position;
		return npc.EyePosition;
	}

	bool SmartAlive( BaseNpc npc ) => npc.IsValid() && npc.Active && npc.Health > 0 && Owner.IsValid()
		// npc_titan.txt declares large; the retail NPC vtable predicate requires
		// the weapon's small lock type to match. Titans remain manually shootable.
		&& (npc is not Titanfall2Npc retail || retail.AcceptsSmallSmartAmmo)
		&& npc.GetDisposition( Owner.GameObject ) == Disposition.Hostile;

	bool CanSeeSmartTarget( BaseNpc npc, Ray aim )
	{
		if ( !SmartAlive( npc ) ) return false;
		var point = SmartAimPoint( npc );
		if ( Owner.WorldPosition.Distance( npc.WorldPosition ) > SmartAmmo.SearchDistance
			|| SmartAmmo.SearchMetric( aim, npc.GameObject.GetBounds(), point ) >= 1 ) return false;
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, point ).UseHitboxes()
			.IgnoreGameObjectHierarchy( Owner.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		return !hit.StartedSolid && (!hit.Hit || hit.GameObject == npc.GameObject || npc.GameObject.IsDescendant( hit.GameObject ));
	}

	void UpdateSmartLock()
	{
		if ( _smartTick == Time.NowDouble ) return;
		_smartTick = Time.NowDouble;
		if ( !UsesRetailSmartAmmo || !Active || !IsHeld || !IsActive || IsReloading || !Owner.IsValid() )
		{
			ResetSmartAmmo(); return;
		}
		BindSmartOwner();
		if ( !Networking.IsHost && IsProxy ) return;
		if ( !Networking.IsHost && _smartSentAiming != IsAiming )
		{
			_smartSentAiming = IsAiming;
			RequestSmartAimMode( _smartSentAiming );
		}
		if ( _observedSmartBurstSerial != SmartBurstSerial )
		{
			_observedSmartBurstSerial = SmartBurstSerial;
			_smartLocks.Clear();
		}
		if ( Networking.IsHost && SmartBurstRemaining > 0 )
		{
			if ( _smartShotTime <= 0 ) FireNextSmartRound();
			return;
		}
		var aim = new Ray( Owner.EyePosition, Owner.EyeTransform.Forward );
		var aiming = Networking.IsHost && IsProxy ? _smartRemoteAiming : IsAiming;
		var maySearch = SmartAmmo.SearchNpcs && (aiming ? SmartAmmo.AllowAds : SmartAmmo.AllowHip);
		foreach ( var entry in _smartLocks.ToArray() )
		{
			if ( !SmartAlive( entry.Npc ) ) { _smartLocks.Remove( entry ); continue; }
			var distance = Owner.WorldPosition.Distance( entry.Npc.WorldPosition );
			var heavy = entry.Npc is Titanfall2Npc { IsTitan: true };
			var required = SmartAmmo.RequiredLocks( entry.Npc.Health, heavy ? TitanDamage : Ballistics.Damage, true, heavy );
			if ( maySearch && CanSeeSmartTarget( entry.Npc, aim ) )
			{
				entry.LastVisible = Time.NowDouble;
				var seconds = SmartAmmo.LockSeconds( distance, true );
				entry.Fraction = seconds <= 0 ? required : Math.Clamp( entry.Fraction + Time.Delta / seconds, 0, required );
			}
			else if ( Time.NowDouble - entry.LastVisible > SmartAmmo.UnlockDebounce
				&& !(SmartAmmo.StickToLocked && entry.Fraction >= 1) )
			{
				var seconds = SmartAmmo.LockSeconds( distance, true, decay: true );
				entry.Fraction = seconds <= 0 ? 0 : MathF.Max( 0, entry.Fraction - Time.Delta * required / seconds );
				if ( entry.Fraction == 0 ) _smartLocks.Remove( entry );
			}
		}
		if ( !maySearch || _smartLocks.Count >= SmartAmmo.MaxTargets || Time.NowDouble < _nextSmartTarget ) return;
		// Stable target objects survive acquisition and burst snapshots. A dead
		// first target must never shift subsequent rounds to a different actor.
		foreach ( var candidate in Scene.GetAllComponents<BaseNpc>()
			.Where( npc => !_smartLocks.Any( entry => entry.Npc == npc ) && CanSeeSmartTarget( npc, aim ) )
			.OrderBy( npc => SmartAmmo.SearchMetric( aim, npc.GameObject.GetBounds(), SmartAimPoint( npc ) ) ) )
		{
			var required = SmartAmmo.RequiredLocks( candidate.Health, Ballistics.Damage, true, false );
			var seconds = SmartAmmo.LockSeconds( Owner.WorldPosition.Distance( candidate.WorldPosition ), true );
			_smartLocks.Add( new() { Npc = candidate, Fraction = seconds <= 0 ? required
				: MathF.Min( required, 0.0001f + Time.Delta / seconds ), LastVisible = Time.NowDouble } );
			_nextSmartTarget = Time.NowDouble + SmartAmmo.NewTargetDelay;
			if ( SmartAmmo.NewTargetDelay > 0 || _smartLocks.Count >= SmartAmmo.MaxTargets ) break;
		}
	}

	void BeginSmartAttack()
	{
		if ( Networking.IsHost ) StartSmartBurst( ViewKickAimRay.Forward, _adsBlend );
		else RequestSmartBurst( ++_smartRequestSerial, ViewKickAimRay.Forward, _adsBlend );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestSmartBurst( int serial, Vector3 direction, float zoom )
	{
		if ( !Networking.IsHost || !Owner.IsValid() ) return;
		BindSmartOwner();
		if ( serial <= _lastSmartRequestSerial ) return;
		_lastSmartRequestSerial = serial;
		StartSmartBurst( direction, zoom );
	}

	void StartSmartBurst( Vector3 direction, float zoom )
	{
		if ( !Networking.IsHost || !Active || !UsesRetailSmartAmmo || !Owner.IsValid() || !IsActive || !IsHeld || IsReloading
			|| SmartBurstRemaining > 0 || _smartShotTime > 0 || !HasRoundAmmo() || !direction.IsFinite
			|| direction.LengthSquared is < 0.25f or > 4 || !float.IsFinite( zoom ) || zoom is < 0 or > 1
			|| Vector3.Dot( Owner.EyeTransform.Forward, direction.Normal ) < 0.35f ) return;
		BindSmartOwner();
		_smartBurst.Clear();
		var available = UsesPrimaryClip ? Clip1 / Firing.Cost : SmartAmmo.MaxBurst;
		foreach ( var entry in _smartLocks )
		{
			if ( zoom > 0 ? !SmartAmmo.AllowAds : !SmartAmmo.AllowHip ) break;
			if ( !SmartAlive( entry.Npc ) ) continue;
			for ( var i = 0; i < (int)MathF.Floor( entry.Fraction ) && _smartBurst.Count < Math.Min( available, SmartAmmo.MaxBurst ); ++i )
				_smartBurst.Enqueue( entry.Npc );
		}
		_smartBurstZoom = zoom;
		if ( _smartBurst.Count == 0 )
		{
			ResolveHitscan( new Ray( Owner.EyePosition, direction.Normal ), zoom );
			_smartShotTime = MathF.Max( PrimaryDelay, 0.015f );
			return;
		}
		SmartBurstRemaining = _smartBurst.Count;
		SmartBurstSerial = SmartBurstSerial == int.MaxValue ? 1 : SmartBurstSerial + 1;
		_observedSmartBurstSerial = SmartBurstSerial;
		FireNextSmartRound();
	}

	void FireNextSmartRound()
	{
		if ( !Networking.IsHost ) return;
		if ( !Active || !IsActive || !IsHeld || IsReloading || !Owner.IsValid() ) { ResetSmartAmmo(); return; }
		while ( _smartBurst.TryDequeue( out var target ) )
		{
			if ( !SmartAlive( target ) ) continue;
			var direction = SmartAimPoint( target ) - Owner.EyePosition;
			if ( direction.LengthSquared < 0.001f ) continue;
			// Re-trace every round against current geometry. A stored lock can aim
			// at its target but cannot bypass a newly closed door or a vortex.
			ResolveHitscan( new Ray( Owner.EyePosition, direction.Normal ), _smartBurstZoom, target );
			_smartShotTime = MathF.Max( PrimaryDelay, 0.015f );
			break;
		}
		SmartBurstRemaining = _smartBurst.Count;
		if ( SmartBurstRemaining == 0 || !HasRoundAmmo() )
		{
			_smartBurst.Clear(); SmartBurstRemaining = 0; _smartLocks.Clear();
			SmartBurstSerial = SmartBurstSerial == int.MaxValue ? 1 : SmartBurstSerial + 1;
			_observedSmartBurstSerial = SmartBurstSerial;
			_nextSmartTarget = Time.NowDouble + SmartAmmo.NewTargetDelay;
		}
	}

	void ResetSmartAmmo()
	{
		_smartLocks.Clear(); _smartBurst.Clear();
		if ( Networking.IsHost ) SmartBurstRemaining = 0;
	}

	void BindSmartOwner()
	{
		if ( _smartOwner == Owner ) return;
		ResetSmartAmmo(); _smartOwner = Owner; _lastSmartRequestSerial = 0;
		_smartRemoteAiming = _smartSentAiming = false;
		_nextSmartTarget = Time.NowDouble + (SmartAmmo.DelayFirstTarget ? SmartAmmo.NewTargetDelay : 0);
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestSmartAimMode( bool aiming )
	{
		if ( !Networking.IsHost || !UsesRetailSmartAmmo || !Owner.IsValid() || !IsActive || !IsHeld ) return;
		BindSmartOwner(); _smartRemoteAiming = aiming;
	}

	float SmartBulletDamage( float damage, GameObject hit, BaseNpc locked )
	{
		if ( !UsesRetailSmartAmmo || hit?.GetComponentInParent<BaseNpc>() is not { } actor ) return damage;
		if ( locked == actor ) damage *= SmartAmmo.NpcLockFactor;
		if ( actor is not Titanfall2Npc npc ) return damage;
		// sh_smart_ammo.gnut's MP damage callback also applies to unlocked hits.
		if ( npc.ModelPath?.Contains( "/robots/spectre/", StringComparison.OrdinalIgnoreCase ) == true ) return MathF.Floor( npc.MaxHealth / 2 ) + 1;
		if ( npc.ModelPath?.Contains( "/humans/grunts/", StringComparison.OrdinalIgnoreCase ) == true ) return MathF.Max( damage, npc.MaxHealth );
		return damage;
	}
}
