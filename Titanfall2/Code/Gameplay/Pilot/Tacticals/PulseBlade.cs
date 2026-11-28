/// <summary>
/// Thrown Pilot Pulse Blade kunai: 1-hit kills hostile infantry on impact, sticks to walls or Titans,
/// and emits periodic sonar pulses revealing hostile enemies through walls via <see cref="Titanfall2SonarRegistry"/>.
/// </summary>
[Hide]
public sealed class Titanfall2PulseBlade : Component
{
	[Property, Sync( SyncFlags.FromHost )] public PlayerController OwnerPlayer { get; set; }
	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public Titanfall2PilotTacticalSettings Settings { get; set; } = Titanfall2PilotTacticalSettings.RetailDefault;
	public Vector3 Velocity { get; set; }

	[Sync( SyncFlags.FromHost )] public bool Stuck { get; private set; }
	[Sync( SyncFlags.FromHost )] public int PulseCount { get; private set; }

	GameObject _stuckTarget;
	Transform _stuckLocal;
	TimeUntil _nextPulse;
	TimeUntil _expiry;
	GameObject _trail;
	GameObject _impactEffect;
	bool _presentedImpact;

	protected override void OnStart()
	{
		_expiry = 12f;
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;

		var modelPath = $"mount://{MountIdent ?? "titanfall2"}/models/weapons/kunai/w_kunai_projectile.mdl.visual.vmdl";
		var model = Titanfall2GameplayVisuals.GetModel( modelPath );
		if ( model is { IsError: false } )
		{
			var mr = GameObject.AddComponent<ModelRenderer>();
			mr.Model = model;
			mr.RenderType = ModelRenderer.ShadowRenderType.Off;
		}

		_trail = Titanfall2PilotTacticalEffects.Spawn( Scene, MountIdent, "wpn_grenade_sonar", WorldPosition );
	}

	protected override void OnUpdate()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( Stuck && !_presentedImpact )
		{
			_presentedImpact = true;
			if ( _trail.IsValid() ) _trail.Destroy();
			_trail = null;
			_impactEffect = Titanfall2PilotTacticalEffects.Spawn( Scene, MountIdent, "wpn_grenade_sonar_impact", WorldPosition );
		}
		if ( _trail.IsValid() ) _trail.WorldTransform = WorldTransform;
		if ( _impactEffect.IsValid() ) _impactEffect.WorldTransform = WorldTransform;
	}

	protected override void OnDestroy()
	{
		if ( _trail.IsValid() ) _trail.Destroy();
		if ( _impactEffect.IsValid() ) _impactEffect.Destroy();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( _expiry <= 0f )
		{
			GameObject.Destroy();
			return;
		}

		if ( Stuck )
		{
			if ( _stuckTarget.IsValid() )
				WorldTransform = _stuckTarget.WorldTransform.ToWorld( _stuckLocal );
			if ( _nextPulse <= 0f )
			{
				_nextPulse = MathF.Max( 0.5f, Settings.PulseIntervalSeconds );
				EmitSonarPulseHost();
			}
			return;
		}

		var dt = Time.Delta;
		var gravity = Vector3.Down * (750f * Settings.PulseGravityScale);
		var start = WorldPosition;
		var end = start + Velocity * dt + gravity * (0.5f * dt * dt);

		var trace = Scene.Trace.Ray( start, end )
			.Radius( 3f )
			.UseHitboxes()
			.UseHitPosition()
			.WithoutTags( "trigger", "projectile" );
		if ( OwnerPlayer.IsValid() )
			trace = trace.IgnoreGameObjectHierarchy( OwnerPlayer.GameObject );

		var hit = Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Shot );
		if ( hit.Hit || hit.StartedSolid )
		{
			WorldPosition = hit.Hit ? hit.HitPosition : end;
			OnBladeImpactHost( hit );
			return;
		}

		WorldPosition = end;
		Velocity += gravity * dt;
		if ( Velocity.LengthSquared > 1f )
			WorldRotation = Rotation.LookAt( Velocity );
	}

	void OnBladeImpactHost( in SceneTraceResult hit )
	{
		Stuck = true;
		_expiry = MathF.Max( 1f, Settings.PulseSonarDuration );
		_nextPulse = MathF.Max( 0.5f, Settings.PulseIntervalSeconds );

		if ( hit.GameObject.IsValid() )
		{
			var npc = hit.GameObject.GetComponentInParent<Titanfall2Npc>();
			var damageable = hit.GameObject.GetComponentInParent<Component.IDamageable>();
			if ( damageable is not null && (!OwnerPlayer.IsValid() || hit.GameObject != OwnerPlayer.GameObject) )
			{
				var dmg = npc is { IsTitan: true } ? Settings.PulseTitanDamage : Settings.PulseDirectDamage;
				var info = new DamageInfo( dmg, OwnerPlayer.IsValid() ? OwnerPlayer.GameObject : GameObject, GameObject )
				{
					Origin = WorldPosition,
					Position = hit.HitPosition
				};
				info.Tags.Add( "bullet" );
				info.Tags.Add( "pulse-blade" );
				Titanfall2HostDamage.Deal( damageable, info );
			}

			// Retail never parents the blade to human-sized targets.
			if ( npc is not { Dead: true } && npc is not { IsTitan: false }
				&& !hit.GameObject.GetComponentInParent<PlayerController>().IsValid() )
			{
				_stuckTarget = hit.GameObject;
				_stuckLocal = _stuckTarget.WorldTransform.ToLocal( WorldTransform );
			}
		}

		EmitSonarPulseHost();
	}

	void EmitSonarPulseHost()
	{
		PulseCount++;
		var registry = Scene.GetSystem<Titanfall2SonarRegistry>();
		var radius = MathF.Max( 120f, Settings.PulseSonarRadius );
		var revealDuration = MathF.Max( 1.2f, Settings.PulseIntervalSeconds + 0.6f );

		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>() )
		{
			if ( !npc.IsValid() || npc.Dead || !npc.IsHostileToFaction( Sandbox.Npcs.Factions.Player ) ) continue;
			if ( npc.WorldPosition.Distance( WorldPosition ) > radius ) continue;
			if ( npc.GameObject.Tags.Has( Titanfall2PilotTactical.PhasedTag ) ) continue;
			registry?.Reveal( npc.GameObject, revealDuration );
			RevealTargetRpc( npc.GameObject, revealDuration );
		}

		SpawnPulseWaveRpc( WorldPosition, PulseCount == 1 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void RevealTargetRpc( GameObject target, float duration ) => Scene.GetSystem<Titanfall2SonarRegistry>()?.Reveal( target, duration );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnPulseWaveRpc( Vector3 origin, bool activation )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var firstPerson = OwnerPlayer.IsValid() && !OwnerPlayer.IsProxy && !OwnerPlayer.ThirdPerson;
		var sound = activation ? (firstPerson ? "Pilot_PulseBlade_Activated_1P" : "Pilot_PulseBlade_Activated_3P")
			: (firstPerson ? "Pilot_PulseBlade_Sonar_Pulse_1P" : "Pilot_PulseBlade_Sonar_Pulse_3P");
		Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent, sound, origin, firstPerson );
	}
}
