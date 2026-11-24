/// <summary>One target's tracker marks. Multiplayer retail marks expire after fifteen seconds.</summary>
internal readonly record struct Titanfall2TrackerState( float FirstExpiry, float SecondExpiry, float ThirdExpiry )
{
	internal const float Lifetime = 15;
	internal int ActiveCount( float now ) => !float.IsFinite( now ) ? 0
		: (FirstExpiry > now ? 1 : 0) + (SecondExpiry > now ? 1 : 0) + (ThirdExpiry > now ? 1 : 0);
	internal float Remaining( float now ) => ActiveCount( now ) == 0 ? 0
		: (FirstExpiry > now ? FirstExpiry : SecondExpiry > now ? SecondExpiry : ThirdExpiry) - now;
	internal Titanfall2TrackerState Add( float now )
	{
		if ( !float.IsFinite( now ) || ActiveCount( now ) == 3 ) return this;
		var first = FirstExpiry > now ? FirstExpiry : SecondExpiry > now ? SecondExpiry : ThirdExpiry > now ? ThirdExpiry : 0;
		var second = FirstExpiry > now ? SecondExpiry > now ? SecondExpiry : ThirdExpiry > now ? ThirdExpiry : 0
			: SecondExpiry > now && ThirdExpiry > now ? ThirdExpiry : 0;
		return first <= 0 ? new( now + Lifetime, 0, 0 ) : second <= 0
			? new( first, now + Lifetime, 0 ) : new( first, second, now + Lifetime );
	}
}

public sealed partial class Titanfall2TitanKit
{
	[Sync( SyncFlags.FromHost )] public GameObject TrackedTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] public int TrackerMarks { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TrackerRemaining { get; private set; }
	public bool HasTrackerLock => TrackerMarks == 3 && TrackerRemaining > 0 && TrackedTarget.IsValid();
	public string LastOffenseFailure { get; private set; }
	readonly Dictionary<Titanfall2Npc, Titanfall2TrackerState> _trackers = new();
	Titanfall2Npc _volleyTarget;
	Titanfall2TitanAbilityData _volleyData;

	/// <summary>Call after the target accepted a 40mm hit, including shield damage and splash.</summary>
	internal void RegisterWeaponHit( Titanfall2Npc target, in DamageInfo damage )
	{
		if ( !Ready || Kit != Chassis.Tone || !target.IsValid() || target == Actor || target.Dead
			|| !Actor.IsHostileToFaction( target.Faction ) || damage.Weapon != Actor.GameObject
			|| !damage.Tags.Has( "titan_40mm" ) ) return;
		if ( !_trackers.ContainsKey( target ) && _trackers.Count >= 64 ) return;
		_trackers[target] = _trackers.GetValueOrDefault( target ).Add( Time.Now );
		ShowTrackedTarget( target );
	}

	void TickTrackers()
	{
		foreach ( var (target, marks) in _trackers.ToArray() )
			if ( !target.IsValid() || target.Dead || !Actor.IsHostileToFaction( target.Faction ) || marks.ActiveCount( Time.Now ) == 0 )
				_trackers.Remove( target );
		var shown = TrackedTarget?.GetComponent<Titanfall2Npc>();
		if ( !shown.IsValid() || !_trackers.ContainsKey( shown ) ) shown = _trackers.Keys.FirstOrDefault();
		ShowTrackedTarget( shown );
	}

	void ShowTrackedTarget( Titanfall2Npc target )
	{
		var marks = target.IsValid() ? _trackers.GetValueOrDefault( target ) : default;
		TrackedTarget = marks.ActiveCount( Time.Now ) > 0 ? target.GameObject : null;
		TrackerMarks = marks.ActiveCount( Time.Now );
		TrackerRemaining = marks.Remaining( Time.Now );
	}

	Titanfall2Npc FindTrackerLock( Ray aim )
	{
		Titanfall2Npc best = null;
		var bestDot = 0.4f;
		foreach ( var (target, marks) in _trackers )
		{
			if ( !target.IsValid() || target.Dead || marks.ActiveCount( Time.Now ) < 3
				|| !Actor.IsHostileToFaction( target.Faction ) || !Actor.HasLineOfSight( target.GameObject ) ) continue;
			var delta = target.WorldPosition + Vector3.Up * (target.BodyHeight * 0.55f) - aim.Position;
			if ( delta.LengthSquared > 6000 * 6000 ) continue;
			var dot = Vector3.Dot( aim.Forward.Normal, delta.Normal );
			if ( dot <= bestDot ) continue;
			bestDot = dot;
			best = target;
		}
		return best;
	}

	void ClearTrackers()
	{
		_trackers.Clear();
		TrackedTarget = null;
		TrackerMarks = 0;
		TrackerRemaining = 0;
		_volleyTarget = null;
		_volleyData = null;
	}

	Titanfall2TitanAbilityData AbilityData( bool defense = false ) =>
		(Sandbox.Mounting.Directory.Get( Actor.MountIdent ?? "titanfall2" ) as Titanfall2Mount)?.GetTitanAbility( Kit, defense );
}
