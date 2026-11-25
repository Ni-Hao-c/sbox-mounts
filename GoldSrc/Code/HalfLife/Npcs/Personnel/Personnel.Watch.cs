using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

public abstract partial class Personnel
{
	GameObject _watchedPlayer;
	TimeUntil _watchUntil;
	bool _staring;
	bool? _previousTurnsBody;

	void TryWatchPlayer( GameObject player )
	{
		if ( !HasSpoken || Leader.IsValid() || Movement?.IsMoving == true || Game.Random.Int( 0, 6 ) != 0 ) return;
		StartWatching( player );
	}

	void StartWatching( GameObject player )
	{
		_previousTurnsBody ??= Animation.TurnsBody;
		Animation.TurnsBody = false;
		_watchedPlayer = player;
		_staring = LooksBack( player );
		_watchUntil = 6;
		Animation.SetLookTarget( player );
	}

	void StopWatching()
	{
		_watchedPlayer = null;
		if ( _previousTurnsBody is not { } turnsBody ) return;
		Animation.TurnsBody = turnsBody;
		_previousTurnsBody = null;
	}

	bool LooksBack( GameObject player )
	{
		var direction = (WorldPosition - player.WorldPosition).WithZ( 0 );
		var view = player.GetComponentInParent<PlayerController>()?.EyeAngles.ToRotation() ?? player.WorldRotation;
		return direction.LengthSquared <= 128 * 128 && direction.Normal.Dot( view.Forward ) >= -0.7f;
	}

	bool WatchPlayer()
	{
		var player = _watchedPlayer;
		if ( !player.IsValid() || Leader.IsValid() || Movement?.IsMoving == true || _pushingPlayer.IsValid() || _talkUntil > 0 ||
			Senses.FindNearestVisible( "player" ) != player || FindVisibleEnemy().IsValid() ||
			FindVisibleThreat().IsValid() || (_staring && !LooksBack( player )) )
		{
			StopWatching();
			return false;
		}

		Animation.SetLookTarget( player );
		if ( _watchUntil > 0 ) return true;
		StopWatching();
		if ( _staring && CanStartConversation ) Say( "STARE", player, Game.Random.Float( 5, 7.5f ) );
		return true;
	}
}
