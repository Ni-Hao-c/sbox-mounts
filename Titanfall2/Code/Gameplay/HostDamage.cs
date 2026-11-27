/// <summary>
/// A synchronous host hit receipt. It is never serialized or accepted from an RPC.
/// Only these delivery sites may attribute Titan damage for core charge.
/// </summary>
internal static class Titanfall2HostDamage
{
	[ThreadStatic] static Receipt _current;
	sealed class Receipt
	{
		public Component.IDamageable Victim;
		public GameObject Attacker, Weapon;
		public Titanfall2Npc Source;
		public Titanfall2Npc Target;
		public Scene Scene;
		public DamageInfo Damage;
		public float AvailableHealth;
		public double DamageTakenBefore;
		public float NestedLoss;
		public Receipt Parent;
		public bool Consumed;
	}

	static Receipt Create( Component.IDamageable victim, GameObject attacker, GameObject weapon )
	{
		var source = weapon?.GetComponentInParent<Titanfall2Npc>();
		// An attacker field alone never establishes provenance. The weapon must
		// belong to a host-controlled Titan, and the attacker to it or its pilot.
		if ( !Networking.IsHost ) return null;
		if ( !source.IsValid() || !attacker.IsValid() || source is not { IsTitan: true, Dead: false, IsProxy: false }
			|| (attacker != source.GameObject && attacker != source.Seat?.GetOccupant()?.GameObject) ) source = null;
		var target = victim as Titanfall2Npc;
		return new() { Victim = victim, Target = target, Source = source, Attacker = attacker, Weapon = weapon, Scene = target?.Scene,
			AvailableHealth = target.IsValid() ? target.Health + target.Shield : 0,
			DamageTakenBefore = target.IsValid() ? target.DamageTakenTotal : 0, Parent = _current };
	}

	internal static void Observe( Titanfall2Npc victim, DamageInfo damage )
	{
		if ( _current is { Damage: null } receipt && ReferenceEquals( receipt.Victim, victim )
			&& damage.Attacker == receipt.Attacker && damage.Weapon == receipt.Weapon ) receipt.Damage = damage;
	}

	static void Complete()
	{
		var receipt = _current;
		if ( receipt?.Target is null ) return;
		// Listeners may cancel or reduce a hit after OnDamaging. Credit only the
		// health/shield actually lost after synchronous host delivery has completed.
		var lost = (float)Math.Max( 0, receipt.Target.DamageTakenTotal - receipt.DamageTakenBefore - receipt.NestedLoss );
		for ( var parent = receipt.Parent; parent is not null; parent = parent.Parent )
			if ( ReferenceEquals( parent.Target, receipt.Target ) ) parent.NestedLoss += lost;
		if ( receipt.Damage is not null ) Titanfall2TitanCore.CreditDamage( receipt.Target, receipt.Damage, lost, receipt.AvailableHealth );
	}

	internal static Titanfall2Npc Consume( Titanfall2Npc victim, DamageInfo damage )
	{
		var receipt = _current;
		if ( receipt is null || receipt.Consumed || !ReferenceEquals( receipt.Victim, victim ) || !receipt.Source.IsValid() || receipt.Source.Scene != receipt.Scene
			|| damage.Attacker != receipt.Attacker || damage.Weapon != receipt.Weapon ) return null;
		receipt.Consumed = true;
		return receipt.Source;
	}

	internal static bool TryGetVerifiedDelivery( Titanfall2Npc victim, DamageInfo damage, out GameObject attacker, out GameObject weapon )
	{
		attacker = weapon = null;
		var receipt = _current;
		if ( !Networking.IsHost || receipt is null || !ReferenceEquals( receipt.Target, victim )
			|| !ReferenceEquals( receipt.Damage, damage ) || damage.Attacker != receipt.Attacker || damage.Weapon != receipt.Weapon ) return false;
		attacker = receipt.Attacker;
		weapon = receipt.Weapon;
		return true;
	}

	static bool IsPhasedPilot( Component.IDamageable victim ) =>
		victim is Component c && c.IsValid()
		&& c.GameObject.GetComponentInParent<Titanfall2PilotTactical>() is { IsValid: true, IsPhaseActive: true };

	internal static void Deal( Component.IDamageable victim, DamageInfo damage )
	{
		if ( !Networking.IsHost || victim is null || damage is null || IsPhasedPilot( victim ) ) return;
		var previous = _current;
		_current = Create( victim, damage.Attacker, damage.Weapon );
		try { victim.OnDamage( damage ); Complete(); }
		finally { _current = previous; }
	}

	internal static SceneTraceResult ShootBullet( SceneTraceResult hit, float damage, float force,
		GameObject attacker, GameObject weapon, TagSet tags )
	{
		if ( !Networking.IsHost ) return hit;
		var victim = hit.GameObject?.GetComponentInParent<Component.IDamageable>();
		if ( IsPhasedPilot( victim ) ) return hit;
		var previous = _current;
		_current = Create( victim, attacker, weapon );
		try
		{
			var result = BaseCombatWeapon.ShootBullet( hit, damage, force, attacker, weapon, tags );
			Complete();
			return result;
		}
		finally { _current = previous; }
	}
}
