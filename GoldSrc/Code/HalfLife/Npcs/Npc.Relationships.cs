using Sandbox.Npcs;
using static GoldSrc.HalfLife.MonsterClass;

namespace GoldSrc.HalfLife;

public partial class Npc
{
	// GoldSrc stores the cosine of the horizontal view cone's half-angle.
	internal virtual float ViewDot => 0.5f;
	internal virtual float SightRange => 2048;

	internal GameObject FindVisibleEnemy() => BestInView( Senses?.VisibleHostiles );

	internal GameObject FindVisibleThreat() => BestInView( Senses?.VisibleThreats );

	// GoldSrc checks the horizontal cone even when someone is within touching distance.
	internal bool InViewCone( Vector3 offset ) => WorldRotation.Forward.WithZ( 0 ).Dot( offset.WithZ( 0 ).Normal ) > ViewDot;

	GameObject BestInView( IReadOnlyList<GameObject> candidates )
	{
		if ( candidates is null ) return null;
		GameObject best = null;
		var bestPriority = int.MinValue;
		var bestDistance = float.MaxValue;
		for ( int i = 0; i < candidates.Count; i++ )
		{
			var candidate = candidates[i];
			if ( !IsLiveTarget( candidate ) ) continue;
			var offset = candidate.WorldPosition - WorldPosition;
			if ( !InViewCone( offset ) ) continue;
			var priority = GetRelationship( candidate ).Priority;
			var distance = offset.LengthSquared;
			if ( priority < bestPriority || (priority == bestPriority && distance >= bestDistance) ) continue;
			best = candidate;
			bestPriority = priority;
			bestDistance = distance;
		}
		return best;
	}

	internal virtual MonsterClass Classification => Definition?.Classification ?? None;

	public override string Faction => Classification switch
	{
		HumanPassive => Factions.Citizen,
		PlayerAlly => Factions.Ally,
		HumanMilitary or Machine => Factions.Enemy,
		None or Insect => "wildlife",
		_ => Factions.Monster
	};

	// BaseNpc applies explicit entity overrides before asking us for the class relationship.
	protected override void SetupRelationships() { }

	internal static MonsterClass ClassOf( GameObject other )
	{
		if ( other.Tags.Has( "hornet" ) && other.GetComponent<Hornet>() is { } hornet ) return hornet.Classification;
		if ( other.GetComponentInParent<BaseNpc>() is { } npc )
		{
			if ( npc is Npc mounted ) return mounted.Classification;
			return npc.Faction switch
			{
				Factions.Citizen => HumanPassive, Factions.Ally => PlayerAlly, Factions.Enemy => HumanMilitary,
				Factions.Monster => AlienMonster, Factions.Player => MonsterClass.Player, _ => None
			};
		}
		return other.Tags.Has( Factions.Player ) || other.Root.Tags.Has( Factions.Player ) || other.GetComponentInParent<PlayerController>().IsValid() ? MonsterClass.Player : None;
	}

	protected override Relationship GetFallbackRelationship( GameObject other ) => ClassRelationship( Classification, ClassOf( other ) );

	internal static Relationship ClassRelationship( MonsterClass self, MonsterClass target )
	{
		return (self, target) switch
		{
			(HumanPassive, MonsterClass.Player or HumanPassive or PlayerAlly) or (PlayerAlly, MonsterClass.Player or HumanPassive)
				or (AlienWeapon, AlienMilitary) => new( Disposition.Friendly ),
			(HumanPassive, AlienMilitary or AlienPredator) or (AlienPrey, AlienPredator)
				or (Insect, None or Machine or MonsterClass.Player or HumanPassive or HumanMilitary or AlienPassive or AlienMonster or AlienPrey or AlienPredator or PlayerAlly)
				=> new( Disposition.Fearful ),
			(HumanMilitary, MonsterClass.Player or AlienMilitary or PlayerAlly) or (AlienMilitary, MonsterClass.Player or HumanMilitary)
				or (HumanPassive, HumanMilitary or AlienMonster) or (AlienPredator, AlienPrey) => new( Disposition.Hostile, 1 ),
			(Machine, MonsterClass.Player or HumanPassive or AlienMilitary or AlienPassive or AlienMonster or AlienPrey or AlienPredator or PlayerAlly or PlayerWeapon or AlienWeapon)
				or (HumanPassive, AlienPrey)
				or (HumanMilitary, HumanPassive or AlienPassive or AlienMonster or AlienPrey or AlienPredator)
				or (AlienMilitary, Machine or HumanPassive or PlayerAlly)
				or (AlienMonster, Machine or MonsterClass.Player or HumanPassive or HumanMilitary or PlayerAlly)
				or (AlienPrey or AlienPredator, MonsterClass.Player or HumanPassive or HumanMilitary or PlayerAlly)
				or (AlienPredator, AlienPredator)
				or (PlayerAlly, Machine or HumanMilitary or AlienMilitary or AlienPassive or AlienMonster or AlienPrey or AlienPredator)
				or (PlayerWeapon, MonsterClass.Player or HumanPassive or HumanMilitary or AlienMilitary or AlienPassive or AlienMonster or AlienPrey or AlienPredator or PlayerAlly or AlienWeapon)
				or (AlienWeapon, MonsterClass.Player or HumanPassive or HumanMilitary or AlienMonster or AlienPrey or PlayerAlly or PlayerWeapon)
				=> new( Disposition.Hostile ),
			_ => new( Disposition.Neutral )
		};
	}
}
