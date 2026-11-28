internal static class Titanfall2UtilityPresentation
{
	internal const string SonarPulse = "P_ar_sonar_CP";
	internal const string HoverSmall = "P_xo_jet_fly_small";
	internal const string HoverLarge = "P_xo_jet_fly_large";
	internal const string TripwireModel = "models/weapons/titan_trip_wire/titan_trip_wire.mdl";
	internal const string TripwireBase = "P_wpn_lasertrip_base";
	internal const string TripwireBlast = "P_impact_exp_XLG_metal";
	internal const string TripwireDestroyed = "Wpn_LaserTripMine_MineDestroyed";

	// Retail weapon keyvalues or script EmitWeaponSound_1p3p/EmitSoundOnEntity calls.
	internal static (string First, string World) Sounds( string cue ) => cue switch
	{
		"tripwire" => ("Wpn_LaserTripMine_Deploy_1P", "Wpn_LaserTripMine_Deploy_3P"),
		"mode_short" => ("weapon_predator_rangeswitch_toshort_1p", "weapon_predator_rangeswitch_toshort_3p"),
		"mode_long" => ("weapon_predator_rangeswitch_tolong_1p", "weapon_predator_rangeswitch_tolong_3p"),
		"sonar" => ("Titan_Tone_SonarLock_Fired_ShoulderPod_1P", "Titan_Tone_SonarLock_Fired_ShoulderPod_3P"),
		"sonar_hit" => ("Titan_Tone_SonarLock_Impact_Pulse_1P", "Titan_Tone_SonarLock_Impact_Pulse_3P"),
		"canister" => ("incendiary_trap_deploy_1p", "incendiary_trap_deploy_3p"),
		"siphon" => ("Weapon_EnergySyphon_Lvl1_Fire_1P", "Weapon_EnergySyphon_Lvl1_Fire_3P"),
		"hover_start" => ("titan_flight_liftoff_1p", "titan_flight_liftoff_3p"),
		"hover_loop" => ("titan_flight_hover_1p", "titan_flight_hover_3p"),
		"phase_start" => ("Pilot_PhaseShift_Activate_1P", "Pilot_PhaseShift_Activate_3P"),
		"phase_end" => ("Pilot_PhaseShift_End_1P", "Pilot_PhaseShift_End_3P"),
		_ => default
	};

	internal static IEnumerable<Titanfall2WeaponPrewarmItem> Dependencies( string ident, Titanfall2TitanKit.Chassis chassis )
	{
		var keys = chassis switch
		{
			Titanfall2TitanKit.Chassis.Monarch => new[] { "siphon" },
			Titanfall2TitanKit.Chassis.Legion => new[] { "mode_short", "mode_long" },
			Titanfall2TitanKit.Chassis.Tone => ["sonar", "sonar_hit"],
			Titanfall2TitanKit.Chassis.Ion => ["tripwire"],
			Titanfall2TitanKit.Chassis.Scorch => ["canister"],
			Titanfall2TitanKit.Chassis.Northstar => ["hover_start", "hover_loop"],
			Titanfall2TitanKit.Chassis.Ronin => ["phase_start", "phase_end"],
			_ => Array.Empty<string>()
		};
		foreach ( var key in keys )
		{
			var (first, world) = Sounds( key );
			yield return new( Titanfall2WeaponPrewarmKind.Event, first, key == "hover_loop" );
			yield return new( Titanfall2WeaponPrewarmKind.Event, world, key == "hover_loop" );
		}
		if ( chassis != Titanfall2TitanKit.Chassis.None )
		{
			var (first, world) = Titanfall2OffensePresentation.FireSounds( chassis, legionLongRange: false );
			yield return new( Titanfall2WeaponPrewarmKind.Event, first );
			yield return new( Titanfall2WeaponPrewarmKind.Event, world );
		}
		switch ( chassis )
		{
			case Titanfall2TitanKit.Chassis.Monarch:
				yield return new( Titanfall2WeaponPrewarmKind.Event, Titanfall2OffensePresentation.MonarchRocketFlight, Loop: true );
				yield return new( Titanfall2WeaponPrewarmKind.Model, $"mount://{ident}/{Titanfall2RearmPresentation.ViewModel}.vmdl" );
				foreach ( var cue in Titanfall2RearmPresentation.FirstPersonCues.Concat( Titanfall2RearmPresentation.WorldCues ) )
					yield return new( Titanfall2WeaponPrewarmKind.Event, cue.Name );
				break;
			case Titanfall2TitanKit.Chassis.Ion:
				yield return new( Titanfall2WeaponPrewarmKind.Model, $"mount://{ident}/{TripwireModel}.visual.vmdl" );
				yield return new( Titanfall2WeaponPrewarmKind.Particle, TripwireBase );
				yield return new( Titanfall2WeaponPrewarmKind.Particle, TripwireBlast );
				yield return new( Titanfall2WeaponPrewarmKind.Event, TripwireDestroyed );
				break;
			case Titanfall2TitanKit.Chassis.Tone:
				yield return new( Titanfall2WeaponPrewarmKind.Particle, SonarPulse );
				break;
			case Titanfall2TitanKit.Chassis.Legion:
				var (longFirst, longWorld) = Titanfall2OffensePresentation.FireSounds( chassis, legionLongRange: true );
				yield return new( Titanfall2WeaponPrewarmKind.Event, longFirst );
				yield return new( Titanfall2WeaponPrewarmKind.Event, longWorld );
				break;
			case Titanfall2TitanKit.Chassis.Northstar:
				yield return new( Titanfall2WeaponPrewarmKind.Particle, HoverSmall );
				yield return new( Titanfall2WeaponPrewarmKind.Particle, HoverLarge );
				break;
		}
	}
}

public sealed partial class Titanfall2TitanKit
{
	readonly List<GameObject> _hoverThrusters = [];
	GameObject _hoverSound;
	bool _hoverPresented;
	bool _hoverFirstPerson;

	void PlayUtilityCue( string cue, Vector3 point )
	{
		var (first, world) = Titanfall2UtilityPresentation.Sounds( cue );
		if ( Actor.Seat?.LocalPilot.IsValid() == true && first is not null )
			Titanfall2GameplayVisuals.PlayViewSound( Scene, Actor.MountIdent, first, point );
		else Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent, world, point );
	}

	void UpdateUtilityPresentation()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		var hovering = Kit == Chassis.Northstar && IsHovering && Actor is { Active: true, Dead: false, TitanCanAct: true, IsInTermination: false }
			&& Actor.Seat is not { IsTransitioning: true };
		if ( !hovering ) { if ( _hoverPresented ) ClearHoverPresentation(); return; }
		if ( !Actor.Renderer.IsValid() ) return;
		var firstPerson = Actor.Seat?.LocalPilot.IsValid() == true;
		if ( _hoverPresented && _hoverFirstPerson == firstPerson ) return;
		ClearHoverPresentation();
		_hoverPresented = true;
		_hoverFirstPerson = firstPerson;

		_hoverSound = new GameObject( Actor.GameObject, true, "TF2 VTOL hover audio" );
		_hoverSound.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_hoverSound.LocalTransform = global::Transform.Zero;
		var (first, world) = Titanfall2UtilityPresentation.Sounds( "hover_loop" );
		_hoverSound.AddComponent<Titanfall2SoundEventEmitter>().Play( Actor.MountIdent, firstPerson ? first : world, firstPerson, Eye, sustain: true );
		foreach ( var (name, effect) in new[]
		{
			("FX_L_BOT_THRUST", Titanfall2UtilityPresentation.HoverLarge), ("FX_R_BOT_THRUST", Titanfall2UtilityPresentation.HoverLarge),
			("FX_L_TOP_THRUST", Titanfall2UtilityPresentation.HoverSmall), ("FX_R_TOP_THRUST", Titanfall2UtilityPresentation.HoverSmall)
		} )
		{
			var attachment = Actor.Renderer.GetAttachmentObject( name );
			if ( !attachment.IsValid() ) continue;
			var go = new GameObject( attachment, false, effect );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			go.LocalTransform = global::Transform.Zero;
			var particle = go.AddComponent<Titanfall2PlasmaFlight>();
			particle.MountIdent = Actor.MountIdent;
			particle.EffectName = effect;
			go.Enabled = true;
			_hoverThrusters.Add( go );
		}
	}

	void ClearHoverPresentation()
	{
		_hoverPresented = false;
		if ( _hoverSound.IsValid() ) { _hoverSound.GetComponent<Titanfall2SoundEventEmitter>()?.StopAll(); _hoverSound.Destroy(); }
		_hoverSound = null;
		foreach ( var effect in _hoverThrusters ) if ( effect.IsValid() ) effect.Destroy();
		_hoverThrusters.Clear();
	}
}
