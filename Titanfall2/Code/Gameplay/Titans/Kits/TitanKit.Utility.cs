/// <summary>Deterministic primary-weapon ADS zoom, FOV, and movement-scale rules for piloted Titans.</summary>
internal static class Titanfall2TitanAdsRules
{
	internal const float DefaultZoomSeconds = 0.2f;
	internal const float MinCockpitAdsFov = 42f;
	internal const float MaxCockpitAdsFov = 78f;

	internal static float StepZoom( float current, bool aiming, in Titanfall2SpreadProfile spread, float deltaTime )
	{
		if ( !float.IsFinite( current ) ) current = 0f;
		if ( !float.IsFinite( deltaTime ) || deltaTime <= 0f ) return Math.Clamp( current, 0f, 1f );
		if ( spread.Enabled ) return spread.AdvanceZoom( Math.Clamp( current, 0f, 1f ), aiming, deltaTime );
		var step = deltaTime / DefaultZoomSeconds;
		return aiming ? MathF.Min( 1f, current + step ) : MathF.Max( 0f, current - step );
	}

	internal static float ResolveCockpitAdsFov( float weaponAdsFov, Titanfall2TitanKit.Chassis kit, bool legionLongRange )
	{
		if ( kit == Titanfall2TitanKit.Chassis.Legion )
			return legionLongRange ? 46f : 72f;
		var raw = float.IsFinite( weaponAdsFov ) && weaponAdsFov > 0f ? weaponAdsFov : 55f;
		return Math.Clamp( raw, MinCockpitAdsFov, MaxCockpitAdsFov );
	}

	internal static float ComputeMoveSpeedScale( float weaponAdsMoveScale, float zoomFraction )
	{
		var clampedZoom = float.IsFinite( zoomFraction ) ? Math.Clamp( zoomFraction, 0f, 1f ) : 0f;
		if ( clampedZoom <= 0f ) return 1f;
		var clampedScale = float.IsFinite( weaponAdsMoveScale ) ? Math.Clamp( weaponAdsMoveScale, 0.2f, 1f ) : 1f;
		return float.Lerp( 1f, clampedScale, clampedZoom );
	}
}

/// <summary>
/// Slot 3 chassis-specific Utility/Tactical abilities for all 7 retail Titans:
/// Monarch Rearm, Ion Laser Tripwire, Legion Mode Switch, Tone Sonar Lock,
/// Scorch Incendiary Trap, Northstar VTOL Hover, and Ronin Phase Dash.
/// </summary>
public sealed partial class Titanfall2TitanKit
{
	const int IonTripwireEnergyCost = 350;
	const float SonarRadius = 560f;
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
	[Sync( SyncFlags.FromHost )] public float PrimaryAdsFraction { get; private set; }
	public float PrimaryZoomFraction => Kit == Chassis.Ion ? IonZoomFraction : PrimaryAdsFraction;
	public string LastUtilityFailure { get; private set; }

	bool _holdDefenseHeld;
	GameObject _holdDefensePilot;
	bool _primaryAdsHeld;
	GameObject _primaryAdsPilot;
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
		(SiphonSlowRemaining > 0f ? 0.55f : 1f)
		* (RoninBlocking ? 0.65f : 1f)
		* Titanfall2TitanAdsRules.ComputeMoveSpeedScale( Actor.IsValid() ? Actor.AdsMoveSpeedScale : 1f, PrimaryZoomFraction );

	bool CanPrimaryAds => Ready && !IsPhasing && !RoninBlocking
		&& !(Kit == Chassis.Scorch && DefenseVolumeActive)
		&& !Actor.Reloading
		&& Actor.GetComponent<Titanfall2TitanCore>() is not { BlocksPrimary: true };

	/// <summary>Host cockpit primary-weapon ADS input for non-Ion Titans (Ion owns ADS in TitanIon.cs).</summary>
	internal void SetPrimaryAdsInput( PlayerController pilot, bool ads )
	{
		if ( Kit == Chassis.Ion || !Ready || !pilot.IsValid() || pilot.IsProxy || Actor.Seat?.GetOccupant() != pilot ) return;
		if ( _primaryAdsPilot != pilot.GameObject ) ClearPrimaryAdsInput();
		_primaryAdsPilot = pilot.GameObject;
		_primaryAdsHeld = ads;
	}

	internal void ClearPrimaryAdsInput()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		_primaryAdsPilot = null;
		_primaryAdsHeld = false;
		PrimaryAdsFraction = 0f;
		ClearChassisPrimaryAds();
	}

	void TickPrimaryAds()
	{
		if ( Kit == Chassis.Ion )
		{
			PrimaryAdsFraction = 0f;
			ClearChassisPrimaryAds();
			return;
		}
		var pilot = Actor.Seat?.GetOccupant()?.GameObject;
		if ( _primaryAdsPilot is not null && (!_primaryAdsPilot.IsValid() || _primaryAdsPilot != pilot) )
			ClearPrimaryAdsInput();
		var aiming = _primaryAdsHeld && pilot.IsValid() && CanPrimaryAds;
		PrimaryAdsFraction = Titanfall2TitanAdsRules.StepZoom( PrimaryAdsFraction, aiming, Actor.SpreadProfile, Time.Delta );
		TickChassisPrimaryAds();
	}

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
			if ( DefenseVolumeActive )
			{
				ThermalShieldMeter = MathF.Max( 0f, ThermalShieldMeter - ThermalDrainPerSecond * dt );
				_thermalRechargeDelay = ThermalRechargeDelay;
				if ( _holdDefenseHeld && _holdDefensePilot.IsValid() ) GuardRemaining = ThermalShieldMeter > 0f ? 0.25f : 0f;
				else if ( ThermalShieldMeter <= 0f ) GuardRemaining = 0f;
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
			if ( Actor.Seat is not { IsOccupied: true } && GuardRemaining > 0f )
			{
				// AI owns a bounded block; it uses the same blocking state as cockpit holds.
			}
			else if ( !_holdDefenseHeld || !_holdDefensePilot.IsValid() )
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
		RearmPresentedAt = -1;
		_rearmPilot = null;
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
		PlayUtilitySoundRpc( "phase_end", Eye );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnRoninPhaseRiftRpc( Vector3 origin ) =>
		Titanfall2PhaseDimension.SpawnPhaseRiftVisual( Scene, origin, large: true );

	/// <summary>Activates the chassis-specific Slot 3 Utility/Tactical ability on the host.</summary>
	public bool TryUtility( Ray aim, GameObject attacker )
	{
		LastUtilityFailure = null;
		if ( !Ready || !ValidAim( aim ) || !CanActivateOffhand( Titanfall2TitanAiAction.Utility ) ) { LastUtilityFailure = "机体无法使用辅助技能"; return false; }
		if ( UtilityRemaining > 0 ) { LastUtilityFailure = "辅助技能冷却中"; return false; }
		if ( Kit == Chassis.Northstar && AiAutonomous && !HasAiFlightClearance() )
		{ LastUtilityFailure = "悬浮起落空间不足"; return false; }

		var source = attacker.IsValid() ? attacker : Actor.GameObject;
		switch ( Kit )
		{
			case Chassis.Monarch:
				OffenseRemaining = 0f;
				DefenseRemaining = 0f;
				RefreshAllDashes();
				Actor.InstantRearmReload();
				UtilityRemaining = UtilityCooldownSeconds;
				BeginRearmPresentation();
				return true;

			case Chassis.Ion:
				if ( !_ionEnergy.Spend( IonTripwireEnergyCost, AiAutonomous ? AiProfile?.SharedEnergyRegenDelay ?? Titanfall2IonEnergy.RegenDelay : Titanfall2IonEnergy.RegenDelay ) )
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
				PlayUtilitySoundRpc( LegionLongRangeMode ? "mode_long" : "mode_short", Eye );
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
				PlayUtilitySoundRpc( "hover_start", Eye );
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
				PlayUtilitySoundRpc( "phase_start", Eye );
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
		var sonarRegistry = Scene.GetSystem<Titanfall2SonarRegistry>();

		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>().ToArray() )
		{
			if ( !npc.IsValid() || npc == Actor || npc.Dead || !Actor.IsHostileToFaction( npc.Faction ) ) continue;
			if ( npc.WorldPosition.Distance( center ) > SonarRadius ) continue;
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
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() || !Scene.IsValid() ) return;
		PlayUtilityCue( "sonar_hit", center );
		var go = new GameObject( Scene, false, "Tone Sonar Pulse" ) { WorldPosition = center };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		// Retail sh_sonar.gnut CP1 carries duration and scale, not an endpoint.
		var points = go.AddComponent<Titanfall2ParticleControlPoints>();
		points.SetControlPoint( 1, new Vector3( SonarRadius / 700f, SonarRadius / 192f, 0 ) );
		if ( Sandbox.Mounting.Directory.Get( Actor.MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount
			|| !mount.TryPrepareWeaponParticle( Titanfall2UtilityPresentation.SonarPulse, out var definition )
			|| !Titanfall2ParticleFactory.TryCreate( go, mount, definition, out _, out _ ) )
			Titanfall2CombatEffects.Burst( go, new Color( 1f, 0.55f, 0.18f ), 24, 90, 26, 1.0f );
		var cleanup = go.AddComponent<TemporaryEffect>();
		cleanup.DestroyAfterSeconds = 1.3f;
		cleanup.WaitForChildEffects = false;
		go.Enabled = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayUtilitySoundRpc( string kind, Vector3 point )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		PlayUtilityCue( kind, point );
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
		var visual = new GameObject( GameObject, false, "TF2 Ion Tripwire Pylon" );
		visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		visual.LocalTransform = global::Transform.Zero;
		var model = Titanfall2GameplayVisuals.GetModel( $"mount://{MountIdent}/{Titanfall2UtilityPresentation.TripwireModel}.visual.vmdl" );
		if ( model is not null && !model.IsError ) visual.AddComponent<ModelRenderer>().Model = model;
		var particle = visual.AddComponent<Titanfall2PlasmaFlight>();
		particle.MountIdent = MountIdent;
		particle.EffectName = Titanfall2UtilityPresentation.TripwireBase;
		visual.Enabled = true;
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
		go.WorldRotation = Rotation.LookAt( Vector3.Up );
		if ( !Titanfall2RetailWeaponEffects.Spawn( go, MountIdent, Titanfall2UtilityPresentation.TripwireBlast, firstPerson: false, lifetime: 2 ) )
			Titanfall2CombatEffects.Burst( go, new Color( 1f, 0.3f, 0.15f ), 36, 180, 28, 0.9f, smoke: true );
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 2.1f;
		go.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, Titanfall2UtilityPresentation.TripwireDestroyed, WorldPosition );
	}
}
