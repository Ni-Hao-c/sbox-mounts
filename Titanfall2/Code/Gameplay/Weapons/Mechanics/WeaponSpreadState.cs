/// <summary>Retail continuous spread ramp (client.dll 5BD5B0/5BD8C0) and discrete kick decay.</summary>
internal struct Titanfall2SpreadState
{
	internal Vector2 Kick { get; private set; }
	internal bool Firing { get; private set; }
	Vector2 _initialFraction;
	double _started, _updated, _discreteShot;
	bool _initialized;

	internal static bool ShouldStop( bool interrupted, bool held, int burstRemaining, bool automatic, double sinceShot, float fireInterval ) =>
		interrupted || burstRemaining == 0 && (!held || !automatic && sinceShot >= fireInterval);

	internal Vector2 Sample( Titanfall2SpreadProfile profile, Titanfall2SpreadStance stance, float zoom, double now )
	{
		if ( !_initialized ) { _initialized = true; _updated = now; }
		var decay = (float)Math.Max( 0, now - Math.Max( _updated, _discreteShot + profile.DecayDelay ) );
		Kick = Vector2.Max( Vector2.Zero, Kick - Vector2.One * (decay * profile.DecayRate) );
		_updated = now;
		if ( Firing && profile.TimeToMax > 0 )
		{
			var progress = (float)Math.Max( 0, now - _started ) / profile.TimeToMax;
			var kick = Kick;
			if ( zoom < 0.5f ) kick.x = Math.Clamp( progress + _initialFraction.x, 0, 1 ) * stance.MaxKickHip;
			kick.y = Math.Clamp( progress + _initialFraction.y, 0, 1 ) * stance.MaxKickAds;
			Kick = kick;
		}
		return Kick;
	}

	internal void AcceptShot( Titanfall2SpreadProfile profile, Titanfall2SpreadStance stance, float zoom, double now )
	{
		Sample( profile, stance, zoom, now );
		if ( profile.TimeToMax > 0 )
		{
			if ( Firing ) return;
			_started = now;
			_initialFraction = new( stance.MaxKickHip > 0 ? Kick.x / stance.MaxKickHip : 0,
				stance.MaxKickAds > 0 ? Kick.y / stance.MaxKickAds : 0 );
		}
		else { Kick = profile.AddShot( Kick, stance, zoom ); _discreteShot = now; }
		Firing = true;
	}

	internal void Stop( Titanfall2SpreadProfile profile, Titanfall2SpreadStance stance, float zoom, double now )
	{
		Sample( profile, stance, zoom, now );
		Firing = false;
	}
}
