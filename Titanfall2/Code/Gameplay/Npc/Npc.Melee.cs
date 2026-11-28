/// <summary>Composed NPC windup-and-resweep melee attack controller.</summary>
internal sealed class Titanfall2NpcMeleeController
{
	internal bool Pending;
	TimeUntil _hit;
	TimeUntil _cooldown;
	Vector3 _direction;
	GameObject _attacker;

	static bool IsControlBlocked( Titanfall2Npc npc ) => npc.Seat is { IsOccupied: true } or { IsEmbarking: true }
		|| npc.IsInTermination || npc.IsTraversing
		|| npc.GetComponent<Titanfall2TitanCore>() is { BlocksPrimary: true };

	internal void Cancel() { Pending = false; _attacker = null; }

	internal bool TryStart( Titanfall2Npc npc, Ray aim, GameObject attacker )
	{
		if ( !Networking.IsHost || npc.IsProxy || npc.Dead || !npc.CanFight || npc.MeleeDamage <= 0
			|| Pending || _cooldown > 0 || IsControlBlocked( npc ) ) return false;
		_direction = aim.Forward.Normal;
		_attacker = attacker;
		Pending = true;
		_hit = MathF.Max( 0.05f, npc.MeleeWindup );
		_cooldown = MathF.Max( npc.MeleeWindup + 0.15f, npc.MeleeInterval );
		npc.SetNextShotDelay( MathF.Max( npc.MeleeWindup + 0.15f, npc.MeleeInterval ) );
		npc.Movement?.Stop();
		npc.PlayShotSoundInternal( npc.EyePositionInternal, melee: true );
		return true;
	}

	internal void Tick( Titanfall2Npc npc )
	{
		if ( !npc.CanFight || IsControlBlocked( npc ) ) { Pending = false; return; }
		if ( !Pending || _hit > 0 ) return;
		Pending = false;
		// A fresh sweep at the contact frame allows dodging and respects intervening cover.
		// The swing direction is committed at windup; it never follows an unseen target.
		var hit = Titanfall2CollisionQueries.Run( npc.Scene.Trace.Ray( new Ray( npc.EyePositionInternal, _direction ), npc.MeleeRange ).Radius( 12 )
			.UseHitboxes().UseHitPosition().IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.ShotHull );
		if ( !hit.Hit || hit.StartedSolid ) return;
		if ( hit.GameObject?.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is { } other && !npc.IsHostileToFaction( other.Faction ) ) return;
		if ( hit.GameObject?.GetComponentInParent<PlayerController>() is not null && (!npc.AttackPlayers || npc.Friendly) ) return;
		var tags = new TagSet();
		tags.Add( "melee" );
		Titanfall2HostDamage.ShootBullet( hit, npc.MeleeDamage, 100, _attacker, npc.GameObject, tags );
	}
}
