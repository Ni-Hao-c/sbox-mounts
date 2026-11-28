public sealed partial class Titanfall2PilotTactical
{
	GameObject _retailBodyEffect;
	GameObject _retailAudioRoot;
	Titanfall2SoundEventEmitter _retailLoop;
	int _retailState;
	bool _retailFirstPerson;
	bool _retailOutroPlayed;
	int _retailActivationSerial = -1;

	void UpdateRetailPresentation()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !_controller.IsValid() ) return;
		var state = IsStimActive ? 1 : IsPhaseActive ? 2 : IsGrappling ? 3 : 0;
		var firstPerson = LocalTacticalCamera().IsValid();
		if ( state != _retailState || firstPerson != _retailFirstPerson || ActivationSerial != _retailActivationSerial )
		{
			var sameActivation = state == _retailState && ActivationSerial == _retailActivationSerial;
			var outroPlayed = sameActivation && _retailOutroPlayed;
			StopRetailPresentation( playEndSound: !sameActivation );
			_retailState = state;
			_retailFirstPerson = firstPerson;
			_retailActivationSerial = ActivationSerial;
			_retailOutroPlayed = outroPlayed;
			if ( state == 0 ) return;
			var bodyOrigin = WorldPosition + Vector3.Up * 48f;
			if ( state == 1 )
			{
				if ( !firstPerson ) _retailBodyEffect = Titanfall2PilotTacticalEffects.Spawn( Scene, MountIdent, "P_pilot_stim_hld", bodyOrigin );
			}
			_retailAudioRoot = new GameObject( GameObject, false, "TF2 Tactical Audio" );
			_retailAudioRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_retailAudioRoot.LocalTransform = global::Transform.Zero;
			_retailLoop = _retailAudioRoot.AddComponent<Titanfall2SoundEventEmitter>();
			var sound = state switch
			{
				1 => firstPerson ? "pilot_stimpack_loop_1P" : "pilot_stimpack_loop_3P",
				2 => firstPerson ? "Pilot_PhaseShift_Loop_1P" : "Pilot_PhaseShift_Loop_3P",
				_ => firstPerson ? "pilot_grapple_traverse_1p" : "pilot_grapple_traverse_3p"
			};
			if ( !_retailLoop.Play( MountIdent, sound, firstPerson, WorldPosition, sustain: true ) )
				Titanfall2Log.Warning( $"Pilot tactical sound '{sound}' could not be started." );
			_retailAudioRoot.Enabled = true;
		}
		if ( _retailBodyEffect.IsValid() ) _retailBodyEffect.WorldPosition = WorldPosition + Vector3.Up * 48f;
		if ( !_retailOutroPlayed && ((state == 1 && StimRemaining <= 2f) || (state == 2 && PhaseRemaining <= 0.3f)) )
		{
			_retailOutroPlayed = true;
			if ( state == 1 ) PlayTacticalLocalSound( "pilot_stimpack_deactivate_1P", "pilot_stimpack_deactivate_3P", firstPerson );
			else PlayTacticalLocalSound( "Pilot_PhaseShift_WarningToEnd_1P", "Pilot_PhaseShift_WarningToEnd_3P", firstPerson );
		}
	}

	void PlayTacticalLocalSound( string first, string third, bool firstPerson ) =>
		Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent, firstPerson ? first : third, WorldPosition, firstPerson );

	void StopRetailPresentation( bool playEndSound = false )
	{
		if ( playEndSound && _retailState == 1 && !_retailOutroPlayed )
			PlayTacticalLocalSound( "pilot_stimpack_deactivate_1P", "pilot_stimpack_deactivate_3P", _retailFirstPerson );
		if ( playEndSound && _retailState == 2 )
			PlayTacticalLocalSound( "Pilot_PhaseShift_End_1P", "Pilot_PhaseShift_End_3P", _retailFirstPerson );
		_retailLoop?.StopAll();
		if ( _retailAudioRoot.IsValid() ) _retailAudioRoot.Destroy();
		if ( _retailBodyEffect.IsValid() ) _retailBodyEffect.Destroy();
		_retailAudioRoot = null; _retailLoop = null; _retailBodyEffect = null;
		_retailState = 0; _retailOutroPlayed = false; _retailActivationSerial = -1;
	}

	string GrappleSurfaceMaterial( in SceneTraceResult hit )
	{
		var npc = hit.GameObject?.GetComponentInParent<Titanfall2Npc>();
		var player = hit.GameObject?.GetComponentInParent<PlayerController>();
		var mount = Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) as Titanfall2Mount;
		return Titanfall2CombatEffects.ActorMaterial( npc?.ModelPath, npc?.IsTitan ?? false, player.IsValid() )
			?? mount?.CombatSurfaceMaterial( hit.Surface ) ?? "C";
	}

	// The authored Grapple impact tables use dotted names while general.mbnk uses underscores.
	// Only the caller's exact table event is normalized, and playback still requires that exact event to exist.
	internal static string GrappleImpactEvent( string authored ) => authored?.Replace( '.', '_' );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayGrappleContactRpc( Vector3 position, string material, string table )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer
			|| Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount ) return;
		var firstPerson = LocalTacticalCamera().IsValid();
		var authored = mount.CombatImpactValue( table, firstPerson ? "Sound_attacker" : "Sound", material );
		var sound = GrappleImpactEvent( authored );
		if ( string.IsNullOrEmpty( sound ) ) return;
		if ( !mount.TryGetSoundEvent( sound, out _ ) )
		{
			Titanfall2Log.Warning( $"Grapple impact sound '{authored}' ('{sound}') is unavailable." );
			return;
		}
		Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent, sound, position, firstPerson );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayGrappleResetRpc( bool retract )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !_controller.IsValid() ) return;
		var firstPerson = LocalTacticalCamera().IsValid();
		if ( retract )
		{
			// Stop the sustain immediately, even when state replication arrives a frame later.
			_retailLoop?.StopAll();
			PlayTacticalLocalSound( "pilot_grapple_retract_1p", "pilot_grapple_retract_3p", firstPerson );
		}
		Titanfall2DelayedWeaponSound.Queue( GameObject, MountIdent, firstPerson ? "pilot_grapple_reset" : "pilot_grapple_reset_3p",
			0.12f, firstPerson, WorldPosition );
	}
}

/// <summary>Uses mounted PCF graphs and their original materials; unsupported PCF features remain converter limitations.</summary>
static class Titanfall2PilotTacticalEffects
{
	internal static GameObject Spawn( Scene scene, string ident, string name, Vector3 position, bool firstPerson = false, float lifetime = 0 )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !scene.IsValid()
			|| Sandbox.Mounting.Directory.Get( ident ?? "titanfall2" ) is not Titanfall2Mount mount ) return null;
		if ( !mount.TryPrepareWeaponParticle( name, out var definition ) )
		{
			Titanfall2Log.Warning( $"Pilot tactical particle '{name}' is unavailable in mount '{ident}'." );
			return null;
		}
		var go = new GameObject( scene, false, name ) { WorldPosition = position };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.AddComponent<Titanfall2ParticleControlPoints>();
		if ( !Titanfall2ParticleFactory.TryCreate( go, mount, definition, out _, out var error ) )
		{
			Titanfall2Log.Warning( $"Pilot tactical particle '{name}': {error}" );
			go.Destroy(); return null;
		}
		foreach ( var renderer in go.GetComponentsInChildren<ParticleSpriteRenderer>( true ) )
		{
			renderer.RenderOptions.Game = !firstPerson; renderer.RenderOptions.Overlay = firstPerson;
			if ( firstPerson ) { renderer.CameraFadeNear = 0; renderer.CameraFadeFar = 0; renderer.DepthFeather = 0; }
		}
		foreach ( var renderer in go.GetComponentsInChildren<ParticleModelRenderer>( true ) )
		{ renderer.RenderOptions.Game = !firstPerson; renderer.RenderOptions.Overlay = firstPerson; }
		foreach ( var renderer in go.GetComponentsInChildren<Titanfall2ParticleRefractionRenderer>( true ) )
		{ renderer.RenderOptions.Game = !firstPerson; renderer.RenderOptions.Overlay = firstPerson; }
		if ( lifetime > 0 )
		{
			var cleanup = go.AddComponent<TemporaryEffect>(); cleanup.DestroyAfterSeconds = lifetime; cleanup.WaitForChildEffects = false;
		}
		go.Enabled = true;
		return go;
	}
}
