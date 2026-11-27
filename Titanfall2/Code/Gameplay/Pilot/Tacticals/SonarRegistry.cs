/// <summary>
/// Scene-scoped registry of hostile targets currently revealed by Pilot Pulse Blade or Tone Sonar Lock.
/// Consumed by PilotCombatHud and TitanCockpitHud to project through-wall sonar target markers.
/// </summary>
public sealed class Titanfall2SonarRegistry : GameObjectSystem<Titanfall2SonarRegistry>
{
	readonly Dictionary<GameObject, double> _revealedUntil = [];

	public Titanfall2SonarRegistry( Scene scene ) : base( scene ) { }

	public void Reveal( GameObject target, float durationSeconds )
	{
		if ( !target.IsValid() || durationSeconds <= 0f ) return;
		var until = Time.NowDouble + durationSeconds;
		if ( !_revealedUntil.TryGetValue( target, out var existing ) || until > existing )
			_revealedUntil[target] = until;
	}

	public bool IsRevealed( GameObject target )
	{
		if ( !target.IsValid() ) return false;
		if ( !_revealedUntil.TryGetValue( target, out var until ) ) return false;
		if ( Time.NowDouble <= until ) return true;
		_revealedUntil.Remove( target );
		return false;
	}

	public void FillActiveTargets( List<GameObject> buffer )
	{
		buffer.Clear();
		if ( _revealedUntil.Count == 0 ) return;
		var now = Time.NowDouble;
		List<GameObject> expired = null;
		foreach ( var (obj, until) in _revealedUntil )
		{
			if ( !obj.IsValid() || !obj.Active || now > until
				|| obj.GetComponent<Titanfall2Npc>() is { Dead: true } )
			{
				expired ??= [];
				expired.Add( obj );
				continue;
			}
			buffer.Add( obj );
		}
		if ( expired is null ) return;
		foreach ( var stale in expired ) _revealedUntil.Remove( stale );
	}
}

