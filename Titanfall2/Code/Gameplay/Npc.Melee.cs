public sealed partial class Titanfall2Npc
{
	bool _meleePending;
	TimeUntil _meleeHit;
	TimeUntil _meleeCooldown;
	Vector3 _meleeDirection;
	GameObject _meleeAttacker;
	bool MeleeControlBlocked => Seat is { IsOccupied: true } or { IsEmbarking: true }
		|| GetComponent<Titanfall2TitanCore>() is { BlocksPrimary: true };

	internal bool TryStartMelee( Ray aim, GameObject attacker )
	{
		if ( !Networking.IsHost || IsProxy || Dead || !CanFight || MeleeDamage <= 0
			|| _meleePending || _meleeCooldown > 0 || MeleeControlBlocked ) return false;
		_meleeDirection = aim.Forward.Normal;
		_meleeAttacker = attacker;
		_meleePending = true;
		_meleeHit = MathF.Max( 0.05f, MeleeWindup );
		_meleeCooldown = MathF.Max( MeleeWindup + 0.15f, MeleeInterval );
		_nextShot = MathF.Max( MeleeWindup + 0.15f, MeleeInterval );
		Movement?.Stop();
		PlayShotSound( Eye, melee: true );
		return true;
	}

	void TickMelee()
	{
		if ( !CanFight || MeleeControlBlocked ) { _meleePending = false; return; }
		if ( !_meleePending || _meleeHit > 0 ) return;
		_meleePending = false;
		// A fresh sweep at the contact frame allows dodging and respects intervening cover.
		// The swing direction is committed at windup; it never follows an unseen target.
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( new Ray( Eye, _meleeDirection ), MeleeRange ).Radius( 12 )
			.UseHitboxes().UseHitPosition().IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.ShotHull );
		if ( !hit.Hit || hit.StartedSolid ) return;
		if ( hit.GameObject?.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is { } npc && !IsHostileToFaction( npc.Faction ) ) return;
		if ( hit.GameObject?.GetComponentInParent<PlayerController>() is not null && (!AttackPlayers || Friendly) ) return;
		var tags = new TagSet();
		tags.Add( "melee" );
		BaseCombatWeapon.ShootBullet( hit, MeleeDamage, 100, _meleeAttacker, GameObject, tags );
	}
}
