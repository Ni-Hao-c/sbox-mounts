using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

public sealed partial class Houndeye
{
	GameObject _retreatFrom;
	TimeUntil _coverDeadline;

	void StartRetreat( GameObject enemy )
	{
		_retreatFrom = enemy;
		if ( Game.Random.Float() <= 0.4f && Find( "jumpback" ) is { } hop )
		{
			var origin = WorldPosition + Vector3.Up * 18;
			var half = new Vector3( 16, 16, 17 );
			var behind = Scene.Trace.Box( new BBox( -half, half ), origin, origin + WorldRotation.Backward * 128 )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( !behind.Hit && !behind.StartedSolid )
			{
				Movement.Stop();
				PlayAction( hop );
				return;
			}
		}
		RunToCover();
	}

	void RunToCover()
	{
		if ( !_retreatFrom.IsValid() || FindCover( TargetEyePosition( _retreatFrom ), _retreatFrom, false ) is not { } cover )
		{
			_retreatFrom = null;
			return;
		}
		InterruptAction();
		Animation.ClearLookTarget();
		Movement.MoveTo( cover, 8, Definition.Speed );
		_coverDeadline = WorldPosition.Distance( cover ) / Definition.Speed * 2 + 1;
	}

	bool ContinueRetreat()
	{
		if ( !_retreatFrom.IsValid() ) return false;
		if ( Action == "jumpback" )
		{
			if ( !AdvanceAction() ) RunToCover();
			return true;
		}
		if ( Movement.Status == MovementLayer.MovementStatus.Moving && _coverDeadline > 0 ) return true;
		_retreatFrom = null;
		return false;
	}
}
