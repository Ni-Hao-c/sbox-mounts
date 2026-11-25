namespace GoldSrc.HalfLife;

[Hide]
public sealed class CorpseDamage : Component, Component.IDamageable
{
	[Property] public float Health { get; set; }
	[Property] public bool Human { get; set; }
	[Property] public Npc.BloodColor Blood { get; set; }
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	internal static void Attach( GameObject go, SkinnedModelRenderer renderer, float health, GibSet set, Npc.BloodColor blood )
	{
		var damage = go.AddComponent<CorpseDamage>();
		damage.Renderer = renderer;
		damage.Health = health;
		damage.Human = set == GibSet.Human;
		damage.Blood = blood;
		// Include the low corpse hull in area damage queries while the hitboxes follow its pose.
		var hull = go.AddComponent<BoxCollider>();
		hull.IsTrigger = true;
		hull.Center = Vector3.Up * 4;
		hull.Scale = new Vector3( 32, 32, 8 );
	}

	void IDamageable.OnDamage( in DamageInfo hit )
	{
		if ( !Networking.IsHost || Health <= 0 || !(hit.Damage > 0) ) return;
		var amount = Npc.HitGroups( hit ).Has( "hitgroup_1" ) ? hit.Damage * 3 : hit.Damage;
		BloodEffects.Wound( GameObject, Blood, hit, amount );
		var tags = hit.Tags;
		if ( !(tags.Has( "crush" ) || tags.Has( "fall" ) || tags.Has( "blast" ) || tags.Has( "explosion" ) || tags.Has( "sonic" ) || tags.Has( "club" )) ) return;
		if ( amount < Health )
		{
			Health -= amount * 0.1f;
			return;
		}

		Health = 0;
		var hull = new BBox( WorldPosition - new Vector3( 16, 16, 0 ), WorldPosition + new Vector3( 16, 16, 8 ) );
		var head = Renderer.GetAttachment( "eyes" )?.Position ?? hull.Center;
		Gib.Scatter( hull, head, Npc.DamageDirection( hit, hit.Attacker, hull.Center ), -50, Human, Blood, sourceModel: Renderer.Model?.Name );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount ) mount.NpcAudio.Emit( "common/bodysplat.wav", WorldPosition );
		GetComponent<DeadGrunt>()?.TossWeapon();
		GameObject.Destroy();
	}
}
