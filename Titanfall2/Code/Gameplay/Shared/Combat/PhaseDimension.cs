using Sandbox.Npcs;
using Sandbox.Physics;

/// <summary>
/// Shared Titanfall 2 Phase Dimension ("Other Dimension") system used by both
/// Pilot Phase Shift and Ronin Phase Dash:
/// 1. Physics collision isolation via CollisionRules (pass through NPCs/Titans/projectiles, keep world geometry).
/// 2. Bidirectional camera visibility isolation via RenderExcludeTags (hide realspace actors/projectiles while phased).
/// 3. Status cleanse on entry (detach stuck projectiles/tethers/tracker locks).
/// 4. Exit overlap arbitration (Pilot humanoid Telefrag, Ronin crush + Sword-Block recoil, anti-stuck escape).
/// </summary>
internal static class Titanfall2PhaseDimension
{
	internal const string PhasedTag = "tf2-phased";
	internal const string RealspaceTag = "tf2-realspace";

	static readonly Dictionary<CameraComponent, int> _cameraPhaseRefCount = [];
	static readonly Dictionary<CameraComponent, (bool Realspace, bool Projectile)> _cameraOriginalExclusions = [];

	internal static void ClearCameraRefs()
	{
		foreach ( var camera in _cameraPhaseRefCount.Keys ) RestoreCameraExclusions( camera );
		_cameraPhaseRefCount.Clear();
		_cameraOriginalExclusions.Clear();
	}

	/// <summary>
	/// Ensures physics collision pairs ignore contacts between phased entities and realspace actors/projectiles
	/// while preserving all solid world/terrain collisions.
	/// </summary>
	internal static void EnsurePhysicsRules( Scene scene )
	{
		var rules = scene?.PhysicsWorld?.CollisionRules;
		if ( rules?.Pairs is null ) return;

		SetIgnore( rules, PhasedTag, RealspaceTag );
		SetIgnore( rules, PhasedTag, "npc" );
		SetIgnore( rules, PhasedTag, "titan" );
		SetIgnore( rules, PhasedTag, "projectile" );
		SetIgnore( rules, PhasedTag, "tether-trap" );
	}

	static void SetIgnore( CollisionRules rules, string left, string right )
	{
		var pair = new CollisionRules.Pair( left, right );
		if ( !rules.Pairs.TryGetValue( pair, out var existing ) || existing != CollisionRules.Result.Ignore )
			rules.Pairs[pair] = CollisionRules.Result.Ignore;
	}

	/// <summary>Marks a scene actor as belonging to realspace (visible & solid to normal world, hidden from phased cameras).</summary>
	internal static void MarkRealspace( GameObject obj, bool inRealspace )
	{
		if ( !obj.IsValid() ) return;
		if ( inRealspace )
		{
			obj.Tags.Add( RealspaceTag );
			obj.Tags.Remove( PhasedTag );
		}
		else
		{
			obj.Tags.Remove( RealspaceTag );
			obj.Tags.Add( PhasedTag );
		}
	}

	/// <summary>
	/// Tags all active non-phased NPCs in the scene with RealspaceTag so both TF2 NPCs and vanilla Sandbox NPCs
	/// vanish cleanly when the local camera enters the Phase Dimension.
	/// </summary>
	internal static void RefreshRealspaceTags( Scene scene )
	{
		if ( !scene.IsValid() ) return;
		foreach ( var npc in scene.GetAllComponents<BaseNpc>() )
		{
			if ( !npc.IsValid() ) continue;
			if ( npc.GameObject.Tags.Has( PhasedTag ) )
				npc.GameObject.Tags.Remove( RealspaceTag );
			else
				npc.GameObject.Tags.Add( RealspaceTag );
		}
	}

	/// <summary>Ref-counted local camera phase-dimension filter so overlapping Pilot/Ronin states never leak RenderExcludeTags.</summary>
	internal static void SetCameraPhaseIsolation( Scene scene, ref CameraComponent trackedCamera, bool active )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;

		if ( active )
		{
			var cam = scene?.Camera;
			if ( !cam.IsValid() ) { ReleaseCamera( ref trackedCamera ); return; }
			if ( trackedCamera == cam && _cameraPhaseRefCount.ContainsKey( cam ) ) return;
			ReleaseCamera( ref trackedCamera );
			RefreshRealspaceTags( scene );

			trackedCamera = cam;
			var count = _cameraPhaseRefCount.GetValueOrDefault( cam ) + 1;
			if ( count == 1 )
				_cameraOriginalExclusions[cam] = (cam.RenderExcludeTags.Has( RealspaceTag ), cam.RenderExcludeTags.Has( "projectile" ));
			_cameraPhaseRefCount[cam] = count;
			cam.RenderExcludeTags.Add( RealspaceTag );
			cam.RenderExcludeTags.Add( "projectile" );
		}
		else
		{
			ReleaseCamera( ref trackedCamera );
		}
	}

	static void ReleaseCamera( ref CameraComponent trackedCamera )
	{
		if ( trackedCamera is null ) return;
		var cam = trackedCamera;
		trackedCamera = null;

		if ( !cam.IsValid() )
		{
			_cameraPhaseRefCount.Remove( cam );
			_cameraOriginalExclusions.Remove( cam );
			return;
		}
		// A mount reset already restored and cleared this reference; do not touch host tags.
		if ( !_cameraPhaseRefCount.TryGetValue( cam, out var count ) ) return;

		var next = Math.Max( 0, count - 1 );
		if ( next == 0 )
		{
			_cameraPhaseRefCount.Remove( cam );
			RestoreCameraExclusions( cam );
			_cameraOriginalExclusions.Remove( cam );
		}
		else
		{
			_cameraPhaseRefCount[cam] = next;
		}
	}

	static void RestoreCameraExclusions( CameraComponent camera )
	{
		if ( !camera.IsValid() || !_cameraOriginalExclusions.TryGetValue( camera, out var original ) ) return;
		if ( !original.Realspace ) camera.RenderExcludeTags.Remove( RealspaceTag );
		if ( !original.Projectile ) camera.RenderExcludeTags.Remove( "projectile" );
	}

	/// <summary>Detaches and destroys any sticky projectiles (Firestar, Arc Star, Satchel) attached to a phasing entity.</summary>
	internal static int CleanseStuckProjectiles( Scene scene, GameObject target )
	{
		if ( !Networking.IsHost || !scene.IsValid() || !target.IsValid() ) return 0;
		var removed = 0;
		foreach ( var proj in scene.GetAllComponents<Titanfall2Projectile>().ToArray() )
		{
			if ( !proj.IsValid() || !proj.StuckTarget.IsValid() ) continue;
			if ( proj.StuckTarget == target || target.IsDescendant( proj.StuckTarget ) )
			{
				proj.CleanseFromPhase();
				removed++;
			}
		}
		return removed;
	}

	/// <summary>Pure geometric cylinder-overlap test used for Phase exit telefrag and anti-stuck resolution.</summary>
	internal static bool AreCylindersOverlapping(
		Vector3 posA, float radiusA, float heightA,
		Vector3 posB, float radiusB, float heightB )
	{
		if ( !posA.IsFinite || !posB.IsFinite ) return false;
		var combinedRadius = MathF.Max( 4f, radiusA ) + MathF.Max( 4f, radiusB );
		var flatDistSq = (posA - posB).WithZ( 0 ).LengthSquared;
		if ( flatDistSq > combinedRadius * combinedRadius ) return false;

		var topA = posA.z + MathF.Max( 16f, heightA );
		var topB = posB.z + MathF.Max( 16f, heightB );
		return posA.z <= topB && posB.z <= topA;
	}

	/// <summary>
	/// Pilot Phase Shift exit arbitration:
	/// 1. Telefrags any overlapping hostile non-Titan NPC (instant lethal shatter).
	/// 2. Escapes cleanly if overlapping a Titan or solid map geometry.
	/// </summary>
	internal static int ResolvePilotExitHost( PlayerController pilot, string mountIdent )
	{
		if ( !Networking.IsHost || !pilot.IsValid() || !pilot.Scene.IsValid() ) return 0;

		var scene = pilot.Scene;
		var pilotRadius = MathF.Max( 12f, pilot.BodyRadius * 0.5f + 8f );
		var pilotHeight = pilot.IsDucking ? pilot.DuckedHeight : pilot.BodyHeight;
		var telefrags = 0;

		// 1. Telefrag overlapping hostile humanoids (retail TF2 signature mechanic).
		foreach ( var npc in scene.GetAllComponents<Titanfall2Npc>().ToArray() )
		{
			if ( !npc.IsValid() || npc.Dead || npc.IsTitan ) continue;
			if ( !npc.IsHostileToFaction( Sandbox.Npcs.Factions.Player ) ) continue;
			if ( npc.GameObject.Tags.Has( PhasedTag ) ) continue;
			if ( !AreCylindersOverlapping( pilot.WorldPosition, pilotRadius, pilotHeight, npc.WorldPosition, npc.BodyRadius, npc.BodyHeight ) )
				continue;

			var info = new DamageInfo( 10000f, pilot.GameObject, pilot.GameObject )
			{
				Origin = pilot.WorldPosition + Vector3.Up * 36f,
				Position = npc.WorldPosition + Vector3.Up * (npc.BodyHeight * 0.5f)
			};
			info.Tags.Add( "phase-telefrag" );
			info.Tags.Add( "melee" );
			Titanfall2HostDamage.Deal( (Component.IDamageable)npc, info );
			SpawnTelefragBurst( scene, npc.WorldPosition + Vector3.Up * 36f, mountIdent );
			telefrags++;
		}

		if ( telefrags > 0 )
			pilot.GameObject.GetComponent<Titanfall2PilotCombat>()?.NotifyTelefragKillHost();

		// 2. Ensure the Pilot never exits embedded inside a Titan or solid wall.
		var half = MathF.Max( 8f, pilot.BodyRadius * 0.5f );
		var hull = new BBox( new Vector3( -half, -half, 2f ), new Vector3( half, half, pilotHeight ) );
		var overlappingTitan = scene.GetAllComponents<Titanfall2Npc>().Any( t =>
			t.IsValid() && !t.Dead && t.IsTitan
			&& AreCylindersOverlapping( pilot.WorldPosition, half + 6f, pilotHeight, t.WorldPosition, t.BodyRadius, t.BodyHeight ) );
		var solidCheck = scene.Trace.Box( hull, pilot.WorldPosition, pilot.WorldPosition )
			.IgnoreGameObjectHierarchy( pilot.GameObject )
			.WithoutTags( "trigger", "projectile" )
			.Run();

		if ( !overlappingTitan && !solidCheck.StartedSolid ) return telefrags;

		Vector3[] offsets =
		[
			pilot.WorldRotation.Backward * 64f,
			pilot.WorldRotation.Right * 64f,
			pilot.WorldRotation.Left * 64f,
			pilot.WorldRotation.Forward * 64f,
			Vector3.Up * 48f,
			pilot.WorldRotation.Backward * 120f,
			pilot.WorldRotation.Right * 120f,
			pilot.WorldRotation.Left * 120f
		];
		foreach ( var offset in offsets )
		{
			var candidate = pilot.WorldPosition + offset;
			var probe = scene.Trace.Box( hull, candidate, candidate )
				.IgnoreGameObjectHierarchy( pilot.GameObject )
				.WithoutTags( "trigger", "projectile" )
				.Run();
			if ( !probe.StartedSolid )
			{
				pilot.WorldPosition = candidate;
				break;
			}
		}

		return telefrags;
	}

	/// <summary>
	/// Ronin Phase Dash exit arbitration:
	/// 1. Crushes/telefrags overlapping hostile infantry.
	/// 2. If overlapping another Titan, applies phase-collision recoil (reduced if Sword Blocking) and ejects to clear space.
	/// </summary>
	internal static int ResolveRoninExitHost( Titanfall2Npc ronin, Titanfall2TitanKit kit )
	{
		if ( !Networking.IsHost || !ronin.IsValid() || !ronin.Scene.IsValid() ) return 0;

		var scene = ronin.Scene;
		var telefrags = 0;
		Titanfall2Npc overlappedTitan = null;

		foreach ( var npc in scene.GetAllComponents<Titanfall2Npc>().ToArray() )
		{
			if ( !npc.IsValid() || npc == ronin || npc.Dead ) continue;
			if ( npc.GameObject.Tags.Has( PhasedTag ) ) continue;
			if ( !AreCylindersOverlapping( ronin.WorldPosition, ronin.BodyRadius, ronin.BodyHeight, npc.WorldPosition, npc.BodyRadius, npc.BodyHeight ) )
				continue;

			if ( npc.IsTitan )
			{
				overlappedTitan ??= npc;
				continue;
			}

			if ( !ronin.IsHostileToFaction( npc.Faction ) ) continue;
			var crush = new DamageInfo( 5000f, ronin.Seat?.GetOccupant()?.GameObject ?? ronin.GameObject, ronin.GameObject )
			{
				Origin = ronin.WorldPosition + Vector3.Up * 64f,
				Position = npc.WorldPosition + Vector3.Up * 36f
			};
			crush.Tags.Add( "phase-telefrag" );
			Titanfall2HostDamage.Deal( (Component.IDamageable)npc, crush );
			SpawnTelefragBurst( scene, npc.WorldPosition + Vector3.Up * 36f, ronin.MountIdent );
			telefrags++;
		}

		var radius = MathF.Max( 16f, ronin.BodyRadius - 2f );
		var hull = new BBox( new Vector3( -radius, -radius, 4f ), new Vector3( radius, radius, MathF.Max( 36f, ronin.BodyHeight - 4f ) ) );
		var check = scene.Trace.Box( hull, ronin.WorldPosition, ronin.WorldPosition )
			.IgnoreGameObjectHierarchy( ronin.GameObject )
			.WithoutTags( "trigger", "projectile", "tether-trap" )
			.Run();

		if ( overlappedTitan.IsValid() )
		{
			var recoilDmg = kit is { RoninBlocking: true } ? 500f : 1500f;
			var sourceObj = ronin.IsHostileToFaction( overlappedTitan.Faction ) ? overlappedTitan.GameObject : null;
			var recoil = new DamageInfo( recoilDmg, sourceObj, sourceObj )
			{
				Origin = overlappedTitan.WorldPosition,
				Position = ronin.WorldPosition + Vector3.Up * 96f
			};
			recoil.Tags.Add( "phase-recoil" );
			Titanfall2HostDamage.Deal( (Component.IDamageable)ronin, recoil );
		}

		if ( !overlappedTitan.IsValid() && !check.StartedSolid ) return telefrags;

		Vector3[] escapes =
		[
			ronin.WorldRotation.Backward * 120f,
			ronin.WorldRotation.Right * 120f,
			ronin.WorldRotation.Left * 120f,
			ronin.WorldRotation.Forward * 120f,
			ronin.WorldRotation.Backward * 200f,
			ronin.WorldRotation.Right * 200f,
			ronin.WorldRotation.Left * 200f
		];
		foreach ( var offset in escapes )
		{
			var candidate = ronin.WorldPosition + offset;
			var probe = scene.Trace.Box( hull, candidate, candidate )
				.IgnoreGameObjectHierarchy( ronin.GameObject )
				.WithoutTags( "trigger", "projectile", "tether-trap" )
				.Run();
			if ( !probe.StartedSolid )
			{
				ronin.WorldPosition = candidate;
				break;
			}
		}

		return telefrags;
	}

	internal static void SpawnPhaseRiftVisual( Scene scene, Vector3 origin, bool large = false )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !scene.IsValid() ) return;
		var go = new GameObject( scene, false, "TF2 Phase Rift" ) { WorldPosition = origin };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( go, new Color( 0.42f, 0.88f, 1f ), large ? 38 : 22, large ? 170f : 85f, large ? 30f : 14f, 0.55f );
		var light = go.AddComponent<PointLight>();
		light.LightColor = new Color( 0.35f, 0.82f, 1f ) * 3f;
		light.Radius = large ? 220f : 110f;
		light.Shadows = false;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.65f;
		go.Enabled = true;
	}

	static void SpawnTelefragBurst( Scene scene, Vector3 origin, string mountIdent )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !scene.IsValid() ) return;
		var go = new GameObject( scene, false, "TF2 Phase Telefrag" ) { WorldPosition = origin };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( go, new Color( 1f, 0.25f, 0.22f ), 34, 150f, 18f, 0.8f );
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.9f;
		go.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( scene, mountIdent ?? "titanfall2", "player_melee_impact_flesh_1p_vs_3p", origin );
	}
}
