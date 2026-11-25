using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

public sealed partial class Bullsquid
{
	enum Feeding { None, Sniffing, Approaching, Eating, Returning }

	Feeding _feeding;
	TimeUntil _nextMeal, _routeStart;
	Vector3 _scent, _returnTo;
	GameObject _enemyAtMeal;
	bool _wallow;
	int _bites;

	protected override void Think()
	{
		var enemy = FindVisibleEnemy();
		if ( _feeding != Feeding.None )
		{
			if ( enemy.IsValid() && enemy != _enemyAtMeal ) StopFeeding();
			else
			{
				ContinueFeeding();
				return;
			}
		}
		if ( Action is null && _nextMeal <= 0 && Movement.IsValid() && Find( "eat" ) is not null
			&& Senses?.Disturbance is { Kind: "carcass" or "meat" or "garbage" } scent
			&& (!enemy.IsValid() || (enemy == LastHostile && !IsAdultHeadcrab( enemy ) && scent.Kind != "garbage")) )
		{
			_enemyAtMeal = enemy;
			_wallow = scent.Kind == "garbage";
			_nextMeal = 10;
			_returnTo = WorldPosition;
			_scent = scent.Position;
			_bites = 0;
			var hidden = WorldRotation.Forward.Dot( (_scent - WorldPosition).Normal ) < 0.2f ||
				Scene.Trace.Ray( EyePosition, _scent + Vector3.Up ).IgnoreGameObjectHierarchy( GameObject )
					.WithoutTags( "npc", "player", "playercontroller", "ragdoll", "debris", "trigger" ).Run().Hit;
			if ( !_wallow && hidden && Find( "sniff" ) is { } sniff )
			{
				Movement.Stop();
				_feeding = Feeding.Sniffing;
				PlayAction( sniff );
			}
			else WalkTo( Feeding.Approaching, _scent );
			return;
		}
		base.Think();
	}

	void WalkTo( Feeding phase, Vector3 point )
	{
		_feeding = phase;
		// The agent resolves a new path on its next navigation update.
		_routeStart = 0.5f;
		MoveToward( point, 16, 0.6f );
	}

	void ContinueFeeding()
	{
		if ( _feeding is Feeding.Sniffing or Feeding.Eating ) { AdvanceAction(); return; }
		switch ( Movement.Status )
		{
			case MovementLayer.MovementStatus.Arrived:
				Movement.Stop();
				if ( _feeding == Feeding.Returning ) { StopFeeding(); return; }
				_feeding = Feeding.Eating;
				PlayAction( Find( _wallow ? "inspectdown" : "eat" ) );
				break;
			case MovementLayer.MovementStatus.Failed:
			case MovementLayer.MovementStatus.Idle:
				if ( _routeStart <= 0 ) StopFeeding();
				break;
		}
	}

	internal override void FinishAction( string name )
	{
		if ( _feeding == Feeding.Sniffing ) WalkTo( Feeding.Approaching, _scent );
		if ( _feeding != Feeding.Eating ) return;
		if ( !_wallow && ++_bites < 3 ) { PlayAction( Find( "eat" ) ); return; }
		_nextMeal = 50;
		WalkTo( Feeding.Returning, _returnTo );
	}

	void StopFeeding()
	{
		_feeding = Feeding.None;
		_enemyAtMeal = null;
		InterruptAction();
		Movement.Stop();
	}
}
