/// <summary>
/// Thrown Pilot Pulse Blade kunai: 1-hit kills hostile infantry on impact, sticks to walls or Titans,
/// and emits periodic sonar pulses revealing hostile enemies through walls via <see cref="Titanfall2SonarRegistry"/>.
/// </summary>
[Hide]
public sealed class Titanfall2PulseBlade : Component
{
	[Property] public PlayerController OwnerPlayer { get; set; }
	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public Titanfall2PilotTacticalSettings Settings { get; set; } = Titanfall2PilotTacticalSettings.RetailDefault;
	public Vector3 Velocity { get; set; }

	[Sync( SyncFlags.FromHost )] public bool Stuck { get; private set; }
	[Sync( SyncFlags.FromHost )] public int PulseCount { get; private set; }

	GameObject _stuckTarget;
	Transform _stuckLocal;
	TimeUntil _nextPulse;
	TimeUntil _expiry;

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

		var light = GameObject.AddComponent<PointLight>();
		light.LightColor = new Color( 1f, 0.48f, 0.12f ) * 3f;
		light.Radius = 90f;
		light.Shadows = false;
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
		_nextPulse = 0f;

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

			if ( npc is not { Dead: true } )
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
			registry?.Reveal( npc.GameObject, revealDuration );
		}

		SpawnPulseWaveRpc( WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnPulseWaveRpc( Vector3 origin )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var fx = new GameObject( Scene, false, "TF2 Pulse Blade Sonar" ) { WorldPosition = origin };
		fx.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( fx, new Color( 1f, 0.52f, 0.14f ), 24, 140f, 18f, 0.9f );
		fx.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1.1f;
		fx.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent ?? "titanfall2", "Titan_Tone_SonarLock_Fired_ShoulderPod_3P", origin );
	}
}
