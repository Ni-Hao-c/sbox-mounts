/// <summary>
/// Slot 3 chassis-specific Utility/Tactical abilities for all 7 retail Titans:
/// Monarch Rearm, Ion Laser Tripwire, Legion Mode Switch, Tone Sonar Lock,
/// Scorch Incendiary Trap, Northstar VTOL Hover, and Ronin Phase Dash.
/// </summary>
public sealed partial class Titanfall2TitanKit
{
	const int IonTripwireEnergyCost = 350;
	const float NorthstarHoverDuration = 3.2f;
	const float RoninPhaseDuration = 1.15f;
	const float ThermalDrainPerSecond = 0.25f;
	const float ThermalRechargePerSecond = 0.14f;
	const float ThermalRechargeDelay = 0.6f;

	[Sync( SyncFlags.FromHost )] public float UtilityRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float HoverRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float PhaseRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool LegionLongRangeMode { get; private set; }
	[Sync( SyncFlags.FromHost )] public float ThermalShieldMeter { get; private set; } = 1f;
	[Sync( SyncFlags.FromHost )] public bool RoninBlocking { get; private set; }
	[Sync( SyncFlags.FromHost )] public float SiphonSlowRemaining { get; private set; }
	public string LastUtilityFailure { get; private set; }

	bool _holdDefenseHeld;
	GameObject _holdDefensePilot;
	float _thermalRechargeDelay;
	bool _wasPhasingHost;
	bool _wasPhasingVisual;
	CameraComponent _roninPhaseCamera;

	public string UtilityName => Kit switch
	{
		Chassis.Monarch => "Rearm",
		Chassis.Ion => "Laser Tripwire",
		Chassis.Legion => LegionLongRangeMode ? "Long-Range" : "Close-Range",
		Chassis.Tone => "Sonar Lock",
		Chassis.Scorch => "Incendiary Trap",
		Chassis.Northstar => "VTOL Hover",
		Chassis.Ronin => "Phase Dash",
		_ => "Tactical"
	};

	public float UtilityCooldownSeconds => Kit switch
	{
		Chassis.Monarch => 17f,
		Chassis.Ion => 10f,
		Chassis.Legion => 1.0f,
		Chassis.Tone => 14f,
		Chassis.Scorch => 10f,
		Chassis.Northstar => 10f,
		Chassis.Ronin => 12.5f,
		_ => 12f
	};

	public bool CanUseUtility => Ready && UtilityRemaining <= 0
		&& (Kit != Chassis.Ion || SharedEnergy >= IonTripwireEnergyCost);

	public bool IsHovering => HoverRemaining > 0;
	public bool IsPhasing => PhaseRemaining > 0;
	public float CurrentMoveSpeedMultiplier =>
		(SiphonSlowRemaining > 0f ? 0.55f : 1f) * (RoninBlocking ? 0.65f : 1f);

	internal void ApplySiphonSlow( float durationSeconds )
	{
		if ( !Networking.IsHost || !float.IsFinite( durationSeconds ) || durationSeconds <= 0f ) return;
		SiphonSlowRemaining = MathF.Max( SiphonSlowRemaining, Math.Clamp( durationSeconds, 0.2f, 4f ) );
	}

	/// <summary>Host cockpit hold input for Scorch Thermal Shield and Ronin Sword Block.</summary>
	internal void SetHoldDefenseInput( PlayerController pilot, Ray aim, bool held )
	{
		if ( !Ready || !pilot.IsValid() || pilot.IsProxy || Actor.Seat?.GetOccupant() != pilot ) return;
		if ( Kit is not (Chassis.Scorch or Chassis.Ronin) ) return;
		_holdDefensePilot = pilot.GameObject;
		_holdDefenseHeld = held;

		if ( Kit == Chassis.Ronin )
		{
			// Retail Ronin tech: holding Sword Block during Phase Dash braces against Titan exit-overlap recoil.
			RoninBlocking = held;
			GuardRemaining = RoninBlocking ? 0.25f : 0f;
			return;
		}

		// Scorch Thermal Shield: hold to project heat shield while meter lasts.
		if ( held && ThermalShieldMeter > 0.05f && Actor.GetComponent<Titanfall2TitanCore>() is not { BlocksPrimary: true } )
		{
			if ( !DefenseVolumeActive )
			{
				BeginDefenseVolume();
				PlayUtilitySoundRpc( "heat_shield", Eye );
			}
			GuardRemaining = 0.25f;
		}
		else if ( DefenseVolumeActive )
		{
			ClearDefenseVolume();
			GuardRemaining = 0f;
		}
	}

	internal void ClearHoldDefenseInput()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		_holdDefensePilot = null;
		_holdDefenseHeld = false;
		RoninBlocking = false;
		if ( Kit == Chassis.Scorch && DefenseVolumeActive )
		{
			ClearDefenseVolume();
			GuardRemaining = 0f;
		}
		else if ( Kit == Chassis.Ronin )
		{
			GuardRemaining = 0f;
		}
	}

	void TickUtility()
	{
		var dt = Time.Delta;
		UtilityRemaining = MathF.Max( 0f, UtilityRemaining - dt );
		HoverRemaining = MathF.Max( 0f, HoverRemaining - dt );
		SiphonSlowRemaining = MathF.Max( 0f, SiphonSlowRemaining - dt );

		if ( PhaseRemaining > 0f )
		{
			_wasPhasingHost = true;
			PhaseRemaining = MathF.Max( 0f, PhaseRemaining - dt );
			if ( PhaseRemaining <= 0f )
			{
				_wasPhasingHost = false;
				ResolveRoninPhaseExitHost();
			}
		}
		else if ( _wasPhasingHost )
		{
			_wasPhasingHost = false;
			ResolveRoninPhaseExitHost();
		}

		var pilotObj = Actor.Seat?.GetOccupant()?.GameObject;
		if ( _holdDefensePilot is not null && (!_holdDefensePilot.IsValid() || _holdDefensePilot != pilotObj) )
			ClearHoldDefenseInput();

		if ( Kit == Chassis.Scorch )
		{
			if ( DefenseVolumeActive && _holdDefenseHeld && _holdDefensePilot.IsValid() )
			{
				ThermalShieldMeter = MathF.Max( 0f, ThermalShieldMeter - ThermalDrainPerSecond * dt );
				_thermalRechargeDelay = ThermalRechargeDelay;
				GuardRemaining = ThermalShieldMeter > 0f ? 0.25f : 0f;
				if ( ThermalShieldMeter <= 0f ) ClearDefenseVolume();
			}
			else if ( !DefenseVolumeActive )
			{
				if ( _thermalRechargeDelay > 0f ) _thermalRechargeDelay = MathF.Max( 0f, _thermalRechargeDelay - dt );
				else ThermalShieldMeter = MathF.Min( 1f, ThermalShieldMeter + ThermalRechargePerSecond * dt );
			}
		}
		else if ( Kit == Chassis.Ronin && RoninBlocking )
		{
			if ( !_holdDefenseHeld || !_holdDefensePilot.IsValid() )
			{
				RoninBlocking = false;
				GuardRemaining = 0f;
			}
			else
			{
				GuardRemaining = 0.25f;
			}
		}
	}

	void ClearUtilityState()
	{
		HoverRemaining = 0f;
		PhaseRemaining = 0f;
		SiphonSlowRemaining = 0f;
		RoninBlocking = false;
		_holdDefenseHeld = false;
		_holdDefensePilot = null;
		_wasPhasingHost = false;
	}

	void UpdatePhaseVisual()
	{
		var phasing = IsPhasing;
		var localPiloted = Actor.IsValid() && Actor.Seat?.LocalPilot is not null;
		Titanfall2PhaseDimension.SetCameraPhaseIsolation( Scene, ref _roninPhaseCamera, active: phasing && localPiloted );

		if ( phasing == _wasPhasingVisual ) return;
		_wasPhasingVisual = phasing;
		if ( !Actor.IsValid() ) return;
		Titanfall2PhaseDimension.MarkRealspace( Actor.GameObject, inRealspace: !phasing );
		if ( Actor.Renderer.IsValid() ) Actor.Renderer.RenderOptions.Game = !phasing;
		Actor.ShowCorePrimaryWeapon( !phasing );
	}

	void ReleaseRoninPhaseCamera() =>
		Titanfall2PhaseDimension.SetCameraPhaseIsolation( Scene, ref _roninPhaseCamera, active: false );

	void ResolveRoninPhaseExitHost()
	{
		if ( !Networking.IsHost || !Actor.IsValid() || !Scene.IsValid() ) return;
		Titanfall2PhaseDimension.MarkRealspace( Actor.GameObject, inRealspace: true );
		Titanfall2PhaseDimension.ResolveRoninExitHost( Actor, this );
		SpawnRoninPhaseRiftRpc( Actor.WorldPosition + Vector3.Up * 90f );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnRoninPhaseRiftRpc( Vector3 origin ) =>
		Titanfall2PhaseDimension.SpawnPhaseRiftVisual( Scene, origin, large: true );

	/// <summary>Activates the chassis-specific Slot 3 Utility/Tactical ability on the host.</summary>
	public bool TryUtility( Ray aim, GameObject attacker )
	{
		LastUtilityFailure = null;
		if ( !Ready || !ValidAim( aim ) ) { LastUtilityFailure = "机体无法使用辅助技能"; return false; }
		if ( UtilityRemaining > 0 ) { LastUtilityFailure = "辅助技能冷却中"; return false; }

		var source = attacker.IsValid() ? attacker : Actor.GameObject;
		switch ( Kit )
		{
			case Chassis.Monarch:
				OffenseRemaining = 0f;
				DefenseRemaining = 0f;
				RefreshAllDashes();
				Actor.InstantRearmReload();
				UtilityRemaining = UtilityCooldownSeconds;
				PlayUtilitySoundRpc( "rearm", Eye );
				return true;

			case Chassis.Ion:
				if ( !_ionEnergy.Spend( IonTripwireEnergyCost ) )
				{
					LastUtilityFailure = "共享能量不足";
					return false;
				}
				SharedEnergy = _ionEnergy.Count;
				UtilityRemaining = UtilityCooldownSeconds;
				DeployIonTripwire( aim, source );
				PlayUtilitySoundRpc( "tripwire", Eye );
				return true;

			case Chassis.Legion:
				LegionLongRangeMode = !LegionLongRangeMode;
				UtilityRemaining = UtilityCooldownSeconds;
				PlayUtilitySoundRpc( "mode_switch", Eye );
				return true;

			case Chassis.Tone:
				UtilityRemaining = UtilityCooldownSeconds;
				TriggerToneSonarPulse( aim );
				PlayUtilitySoundRpc( "sonar", Eye );
				return true;

			case Chassis.Scorch:
				UtilityRemaining = UtilityCooldownSeconds;
				LaunchScorchCanister( aim, source );
				PlayUtilitySoundRpc( "canister", Eye );
				return true;

			case Chassis.Northstar:
				UtilityRemaining = UtilityCooldownSeconds;
				HoverRemaining = NorthstarHoverDuration;
				PlayUtilitySoundRpc( "hover", Eye );
				return true;

			case Chassis.Ronin:
				UtilityRemaining = UtilityCooldownSeconds;
				PhaseRemaining = RoninPhaseDuration;
				Titanfall2PhaseDimension.EnsurePhysicsRules( Scene );
				Titanfall2PhaseDimension.MarkRealspace( Actor.GameObject, inRealspace: false );
				Titanfall2PhaseDimension.CleanseStuckProjectiles( Scene, Actor.GameObject );
				// Retail Ronin Phase Dash cleanses enemy tether traps and Tone tracker locks.
				foreach ( var trap in Scene.GetAllComponents<Titanfall2TetherTrap>().ToArray() )
					if ( trap.IsValid() && trap.Attached && trap.Target == Actor ) trap.Release();
				foreach ( var enemyKit in Scene.GetAllComponents<Titanfall2TitanKit>() )
					if ( enemyKit.IsValid() && enemyKit != this ) enemyKit.ClearTrackerOnTarget( Actor );
				SpawnRoninPhaseRiftRpc( Actor.WorldPosition + Vector3.Up * 90f );
				PlayUtilitySoundRpc( "phase", Eye );
				return true;

			default:
				return false;
		}
	}

	void DeployIonTripwire( Ray aim, GameObject attacker )
	{
		var targetHit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, aim.Position + aim.Forward.Normal * 720f )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
		var dropOrigin = targetHit.Hit ? targetHit.EndPosition + targetHit.Normal * 12f : targetHit.EndPosition;
		var floor = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( dropOrigin + Vector3.Up * 32f, dropOrigin - Vector3.Up * 420f )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
		var pos = floor.Hit ? floor.EndPosition + Vector3.Up * 12f : dropOrigin;

		using var scope = Scene.Push();
		var go = new GameObject( false, "TF2 Ion Laser Tripwire" ) { WorldPosition = pos };
		go.Flags |= GameObjectFlags.NotSaved;
		var mine = go.AddComponent<Titanfall2IonTripwire>();
		mine.OwnerTitan = Actor;
		mine.Attacker = attacker;
		mine.Weapon = GameObject;
		mine.MountIdent = Actor.MountIdent ?? "titanfall2";
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
	}

	void TriggerToneSonarPulse( Ray aim )
	{
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, aim.Position + aim.Forward.Normal * 1800f )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		var center = hit.EndPosition;
		const float sonarRadius = 560f;
		var sonarRegistry = Scene.GetSystem<Titanfall2SonarRegistry>();

		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>().ToArray() )
		{
			if ( !npc.IsValid() || npc == Actor || npc.Dead || !Actor.IsHostileToFaction( npc.Faction ) ) continue;
			if ( npc.WorldPosition.Distance( center ) > sonarRadius ) continue;
			sonarRegistry?.Reveal( npc.GameObject, 6.0f );
			if ( !_trackers.ContainsKey( npc ) && _trackers.Count >= 64 ) continue;
			_trackers[npc] = _trackers.GetValueOrDefault( npc ).Add( Time.Now );
			ShowTrackedTarget( npc );
		}

		SpawnSonarVisualRpc( center );
	}

	void LaunchScorchCanister( Ray aim, GameObject attacker )
	{
		Titanfall2GasCanister.Launch( Actor, aim, attacker );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnSonarVisualRpc( Vector3 center )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var go = new GameObject( Scene, false, "Tone Sonar Pulse" ) { WorldPosition = center };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( go, new Color( 1f, 0.55f, 0.18f ), 24, 90, 26, 1.0f );
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1.2f;
		go.Enabled = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayUtilitySoundRpc( string kind, Vector3 point )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		var sound = kind switch
		{
			"rearm" => "Titan_Monarch_Smart_Core_Activated_3P",
			"tripwire" => "Weapon_ShoulderLaser_Fire_3P",
			"mode_switch" => "Weapon_Predator_WindUp_3P",
			"sonar" => "Titan_Tone_SonarLock_Fired_ShoulderPod_3P",
			"canister" => "incendiary_trap_deploy_3p",
			"heat_shield" => "heat_shield_3p_loop",
			"hover" => "Jumpjet_Jump_Start_3P",
			"phase" => "Jumpjet_Wallrun_Start_3P",
			_ => null
		};
		if ( !string.IsNullOrWhiteSpace( sound ) )
			Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent, sound, point );
	}
}

/// <summary>Proximity laser mine deployed by Ion's Slot 3 Utility ability (Laser Tripwire).</summary>
[Hide]
public sealed class Titanfall2IonTripwire : Component
{
	const float ArmDelay = 0.4f;
	const float TriggerRadius = 150f;
	const float BlastRadius = 260f;
	const float Lifetime = 10f;

	[Property] public Titanfall2Npc OwnerTitan { get; set; }
	[Property] public GameObject Attacker { get; set; }
	[Property] public GameObject Weapon { get; set; }
	[Property] public string MountIdent { get; set; } = "titanfall2";

	TimeUntil _armed;
	TimeUntil _expiry;
	bool _detonated;

	protected override void OnStart()
	{
		_armed = ArmDelay;
		_expiry = Lifetime;
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		var light = GameObject.AddComponent<PointLight>();
		light.LightColor = new Color( 1f, 0.25f, 0.15f ) * 2.5f;
		light.Radius = 120f;
		light.Shadows = false;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy || _detonated ) return;
		if ( _expiry <= 0 ) { Detonate(); return; }
		if ( _armed > 0 || !OwnerTitan.IsValid() ) return;

		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>() )
		{
			if ( !npc.IsValid() || npc == OwnerTitan || npc.Dead || !OwnerTitan.IsHostileToFaction( npc.Faction ) ) continue;
			if ( npc.WorldPosition.Distance( WorldPosition ) <= TriggerRadius )
			{
				Detonate();
				return;
			}
		}
	}

	void Detonate()
	{
		if ( _detonated ) return;
		_detonated = true;

		var attacker = Attacker.IsValid() ? Attacker : OwnerTitan?.GameObject;
		var weapon = Weapon.IsValid() ? Weapon : GameObject;
		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>().ToArray() )
		{
			if ( !npc.IsValid() || npc == OwnerTitan || npc.Dead ) continue;
			if ( OwnerTitan.IsValid() && !OwnerTitan.IsHostileToFaction( npc.Faction ) ) continue;
			var dist = npc.WorldPosition.Distance( WorldPosition );
			if ( dist > BlastRadius ) continue;
			var falloff = 1f - 0.5f * (dist / BlastRadius);
			var dmg = (npc.IsTitan ? 1500f : 220f) * falloff;
			var info = new DamageInfo( dmg, attacker, weapon ) { Origin = WorldPosition, Position = npc.WorldPosition + Vector3.Up * 36f };
			info.Tags.Add( "blast" );
			Titanfall2HostDamage.Deal( (IDamageable)npc, info );
		}

		PlayBlastRpc();
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBlastRpc()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var go = new GameObject( Scene, false, "Ion Tripwire Blast" ) { WorldPosition = WorldPosition };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( go, new Color( 1f, 0.3f, 0.15f ), 36, 180, 28, 0.9f, smoke: true );
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1.2f;
		go.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, "Weapon_ShoulderLaser_Fire_3P", WorldPosition );
	}
}
