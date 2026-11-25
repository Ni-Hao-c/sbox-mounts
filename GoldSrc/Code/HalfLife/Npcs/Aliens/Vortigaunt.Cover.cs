using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

public sealed partial class Vortigaunt
{
	GameObject _lastDanger, _recentAttacker;
	TimeUntil _nextCoverCheck;
	bool _takingCover;

	bool TakeCover()
	{
		if ( !Movement.IsValid() || PhysicsCarrier.IsValid() || Health <= 0 ) return false;
		if ( Senses?.Disturbance is { Kind: TimedGrenade.DangerStimulus } danger && danger.Source.IsValid() && danger.Source != _lastDanger )
		{
			_lastDanger = danger.Source;
			if ( RetreatFrom( danger.Position, danger.Source ) ) return true;
		}
		if ( _takingCover && Movement.Status == MovementLayer.MovementStatus.Moving ) return true;
		_takingCover = false;
		var attacker = _recentAttacker;
		_recentAttacker = null;
		var hit = IsLiveTarget( attacker );
		if ( Health >= 20 || (!hit && (_nextCoverCheck > 0 || Action is not null)) ) return false;
		_nextCoverCheck = 0.5f;

		var enemy = hit ? attacker : FindVisibleEnemy();
		if ( !IsLiveTarget( enemy ) ) return false;
		var offset = WorldPosition - enemy.WorldPosition;
		if ( CanClaw( enemy ) ) return false;
		var facing = enemy.GetComponentInParent<PlayerController>()?.EyeAngles.ToRotation() ?? enemy.WorldRotation;
		return (hit || facing.Forward.Dot( offset.Normal ) >= 0.5f) && RetreatFrom( TargetEyePosition( enemy ), enemy );
	}

	bool RetreatFrom( Vector3 danger, GameObject source )
	{
		if ( FindCover( danger, source, false ) is not { } cover ) return false;
		InterruptAction();
		Animation.ClearLookTarget();
		Movement.MoveTo( cover, 8, Definition.Speed );
		_takingCover = true;
		return true;
	}
}
