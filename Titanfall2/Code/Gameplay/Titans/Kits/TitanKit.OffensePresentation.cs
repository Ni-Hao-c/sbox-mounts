/// <summary>Retail fire/activation aliases for the seven Titan offense slots.</summary>
internal static class Titanfall2OffensePresentation
{
	internal const string MonarchRocketFlight = "Weapon_Sidwinder_Projectile";

	internal static (string FirstPerson, string World) FireSounds( Titanfall2TitanKit.Chassis kit, bool legionLongRange ) => kit switch
	{
		Titanfall2TitanKit.Chassis.Monarch => ("ShoulderRocket_Salvo_Fire_1P", "ShoulderRocket_Salvo_Fire_3P"),
		Titanfall2TitanKit.Chassis.Ion => ("Weapon_ShoulderLaser_Fire_1P", "Weapon_ShoulderLaser_Fire_3P"),
		// Tracker Rockets define a burst start, not the single-shot alias inherited from Rocket Salvo.
		Titanfall2TitanKit.Chassis.Tone => ("weapon_trackingrockets_fire_1p", "weapon_trackingrockets_fire_3P"),
		Titanfall2TitanKit.Chassis.Northstar => ("ShoulderRocket_Cluster_Fire_1P", "ShoulderRocket_Cluster_Fire_3P"),
		// Firewall emits its cast cue in OnWeaponActivate rather than as a WeaponData fire sound.
		Titanfall2TitanKit.Chassis.Scorch => ("flamewall_start_1p", "flamewall_start_3p"),
		Titanfall2TitanKit.Chassis.Ronin => ("arcwave_fire_1p", "arcwave_fire_3p"),
		Titanfall2TitanKit.Chassis.Legion when legionLongRange => ("Weapon_Predator_Powershot_LongRange_1P", "Weapon_Predator_Powershot_LongRange_3P"),
		Titanfall2TitanKit.Chassis.Legion => ("Weapon_Predator_Powershot_ShortRange_1P", "Weapon_Predator_Powershot_ShortRange_3P"),
		_ => default
	};
}

public sealed partial class Titanfall2TitanKit
{
	// Call once after TryOffense accepts the attack, in place of PlayActivation(false).
	void PresentAcceptedOffenseFire( GameObject attacker )
	{
		if ( !Networking.IsHost || IsProxy || !Actor.IsValid() ) return;
		var occupant = Actor.Seat?.GetOccupant();
		var pilot = occupant.IsValid() && occupant.GameObject == attacker ? attacker : null;
		PlayAcceptedOffenseFireRpc( Kit, LegionLongRangeMode, pilot, Eye );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayAcceptedOffenseFireRpc( Chassis kit, bool legionLongRange, GameObject pilot, Vector3 point )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		var (firstPerson, world) = Titanfall2OffensePresentation.FireSounds( kit, legionLongRange );
		if ( pilot.IsValid() && Actor.Seat?.GetOccupant() is { IsProxy: false, Active: true } localPilot
			&& localPilot.GameObject == pilot && localPilot.Mode is Sandbox.Movement.SitMoveMode )
			Titanfall2GameplayVisuals.PlayViewSound( Scene, Actor.MountIdent, firstPerson, point );
		else
			Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent, world, point );
	}
}
