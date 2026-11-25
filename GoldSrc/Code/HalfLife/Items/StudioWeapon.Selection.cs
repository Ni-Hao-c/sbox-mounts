namespace GoldSrc.HalfLife;

public abstract partial class StudioWeapon
{
	internal virtual bool SwitchWhenSpent => UsesPrimaryClip;
	TimeUntil _retrySelection;

	protected override void Think()
	{
		base.Think();
		if ( Networking.MaxPlayers <= 1 || !SwitchWhenSpent || NextPrimaryFire > 0 || !ShouldAvoid ||
			Input.Down( "attack1" ) || Input.Down( "attack2" ) || _retrySelection > 0 ) return;

		// SDK inventory lists run from newest to oldest within each bucket.
		StudioWeapon best = null;
		foreach ( var candidate in Inventory.GetComponentsInChildren<StudioWeapon>( true ) )
		{
			if ( candidate == this || candidate.Inventory != Inventory || candidate.ShouldAvoid || !candidate.CanSwitchTo() ) continue;
			var item = candidate.Definition;
			if ( item.SkipAutoSelect || item.Weight < 0 ) continue;
			if ( best is null || Prefer( item, best.Definition ) ) best = candidate;
		}

		if ( best.IsValid() && OnHolstering( best ) ) Inventory.Switch( best );
		_retrySelection = 0.1f;
	}

	bool Prefer( WeaponPickupAttribute candidate, WeaponPickupAttribute best )
	{
		if ( candidate.Weight == best.Weight ) return candidate.Bucket <= best.Bucket;
		if ( best.Weight == Definition.Weight ) return false;
		return candidate.Weight == Definition.Weight || candidate.Weight > best.Weight;
	}
}
