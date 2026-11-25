public enum Titanfall2TetherTrapState { Flying, Arming, Armed, Attached }

/// <summary>Retail Northstar proximity trap: one host projectile, one chassis endpoint, no damage aura.</summary>
public sealed class Titanfall2TetherTrap : Component, Component.IDamageable
{
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2Npc Source { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public GameObject Owner { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public string MountIdent { get; set; } = "titanfall2";
	[Sync( SyncFlags.FromHost )] public Titanfall2TetherTrapState State { get; private set; }
	[Sync( SyncFlags.FromHost )] public Titanfall2Npc Target { get; private set; }
	[Sync( SyncFlags.FromHost )] public float Health { get; private set; } = Titanfall2TetherRules.TrapHealth;
	[Sync( SyncFlags.FromHost )] public float TetherHealth { get; private set; }
	[Sync( SyncFlags.FromHost )] double PlantedAt { get; set; }
	[Sync( SyncFlags.FromHost )] double AttachedAt { get; set; }
	internal bool Attached => Active && State == Titanfall2TetherTrapState.Attached && TetherHealth > 0;
	internal Vector3 Velocity { get; private set; }
	string _faction;
	float _lifetime;
	double _createdAt;
	GameObject _surface;
	Transform _surfaceLocal;
	bool _wasParented;
	Titanfall2TetherConstraint _constraint;
	GameObject _light, _rope, _endpoint;
	ModelRenderer _model;
	bool _endpointFirstPerson;

	internal static Titanfall2TetherTrap Launch( Titanfall2Npc actor, Ray aim, GameObject owner, bool frontierDefense = false )
	{
		if ( !Networking.IsHost || !actor.IsValid() || actor.IsProxy || !actor.TitanCanAct || !owner.IsValid()
			|| !aim.Position.IsFinite || !aim.Forward.IsFinite || aim.Forward.LengthSquared < 0.25f ) return null;
		// The deployed cap belongs to the firing owner, not the current cockpit occupant.
		var previous = actor.Scene.GetAllComponents<Titanfall2TetherTrap>().Where( trap => trap.Owner == owner )
			.OrderBy( trap => trap._createdAt ).ToArray();
		for ( var i = 0; i <= previous.Length - Titanfall2TetherRules.MaximumDeployed; i++ ) previous[i].Release();
		var go = new GameObject( actor.Scene, false, "Northstar Tether Trap" );
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( "tether-trap" );
		go.WorldPosition = aim.Position;
		go.WorldRotation = Rotation.LookAt( aim.Forward );
		var trap = go.AddComponent<Titanfall2TetherTrap>();
		trap.Source = actor; trap.Owner = owner; trap.MountIdent = actor.MountIdent ?? "titanfall2";
		trap._faction = actor.Faction;
		trap.Velocity = aim.Forward.Normal * Titanfall2TetherRules.LaunchSpeed;
		trap._createdAt = Time.NowDouble;
		trap._lifetime = Titanfall2TetherRules.MineLifetime( owner.GetComponent<PlayerController>() is null, frontierDefense );
		if ( Networking.IsActive ) go.NetworkSpawn( true, null ); else go.Enabled = true;
		trap.PlaySound( "Wpn_TetherTrap_Deploy_3P" );
		return trap;
	}

	protected override void OnStart()
	{
		// A traceable hit proxy lets ordinary bullets and explosions destroy the planted trap.
		var collider = GameObject.AddComponent<BoxCollider>();
		collider.Scale = new Vector3( 12, 12, 10 );
		collider.Center = Vector3.Up * 4;
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		var model = Titanfall2GameplayVisuals.GetModel( $"mount://{MountIdent}/models/weapons/caber_shot/caber_shot_thrown_xl.mdl.visual.vmdl" );
		if ( model is { IsError: false } ) { _model = GameObject.AddComponent<ModelRenderer>(); _model.Model = model; }
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( !Owner.IsValid() || _wasParented && !_surface.IsValid() ) { Release(); return; }
		if ( _surface.IsValid() ) WorldTransform = _surface.WorldTransform.ToWorld( _surfaceLocal );
		if ( State == Titanfall2TetherTrapState.Flying ) { Fly(); return; }
		if ( State == Titanfall2TetherTrapState.Attached )
		{
			if ( !Target.IsValid() || !Target.TitanCanAct ) { Release(); return; }
			TetherHealth = MathF.Max( 0, Titanfall2TetherRules.TetherHealth - (float)(Time.NowDouble - AttachedAt) * Titanfall2TetherRules.HealthDrain );
			if ( TetherHealth <= 0 ) Release();
			return;
		}
		var armedAt = PlantedAt + Titanfall2TetherRules.ArmSeconds;
		if ( Time.NowDouble < armedAt ) return;
		if ( Time.NowDouble >= armedAt + _lifetime ) { Release(); return; }
		State = Titanfall2TetherTrapState.Armed;
		foreach ( var actor in Scene.GetAllComponents<Titanfall2Npc>().OrderByDescending( npc => npc.Seat?.IsOccupied == true ) )
		{
			if ( !CanTrigger( actor ) ) continue;
			var target = actor.WorldPosition + Vector3.Up * (actor.BodyHeight * 0.79f);
			var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( WorldPosition + Vector3.Up, target ).UseHitboxes()
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
			if ( hit.StartedSolid || !hit.Hit || hit.GameObject?.GetComponentInParent<Titanfall2Npc>() != actor ) continue;
			var constraint = actor.GameObject.GetOrAddComponent<Titanfall2TetherConstraint>();
			State = Titanfall2TetherTrapState.Attached;
			TetherHealth = Titanfall2TetherRules.TetherHealth;
			if ( !constraint.Attach( this ) ) { State = Titanfall2TetherTrapState.Armed; TetherHealth = 0; continue; }
			Target = actor; AttachedAt = Time.NowDouble; _constraint = constraint;
			PlaySound( "Wpn_TetherTrap_PopOpen_3p" );
			return;
		}
	}

	bool Hostile( string faction ) => !Titanfall2Npc.IsNeutralFaction( _faction ) && !Titanfall2Npc.IsNeutralFaction( faction )
		&& !(Titanfall2Npc.IsAllyFaction( _faction ) && Titanfall2Npc.IsAllyFaction( faction ))
		&& !string.Equals( _faction, faction, StringComparison.OrdinalIgnoreCase );
	internal bool CanTrigger( Titanfall2Npc actor ) => actor.IsValid() && actor != Source && actor.TitanCanAct && !actor.IsProxy
		&& (actor.IsTitan || actor.ModelPath?.Contains( "/robots/super_spectre/", StringComparison.OrdinalIgnoreCase ) == true)
		&& Hostile( actor.Faction ) && Titanfall2TetherRules.InTriggerRange( WorldPosition, actor.WorldPosition,
			actor.BodyHeight * 0.79f, actor.Seat?.IsOccupied == true );

	void Fly()
	{
		// Use the mount's grenade integration; wall/actor impacts bounce, only upward world surfaces plant.
		if ( Time.NowDouble - _createdAt > 60 ) { Release(); return; }
		var gravity = Vector3.Down * Titanfall2TetherRules.Gravity;
		var end = WorldPosition + Velocity * Time.Delta + gravity * (0.5f * Time.Delta * Time.Delta);
		var query = Scene.Trace.Ray( WorldPosition, end ).Radius( 3 ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile", "tether-trap" );
		if ( Source.IsValid() ) query = query.IgnoreGameObjectHierarchy( Source.GameObject );
		var hit = Titanfall2CollisionQueries.Run( query, Titanfall2TraceMask.Grenade );
		Velocity += gravity * Time.Delta;
		if ( !hit.Hit && !hit.StartedSolid ) { WorldPosition = end; return; }
		if ( hit.StartedSolid ) { Release(); return; }
		WorldPosition = hit.EndPosition + hit.Normal * 0.5f;
		var actor = hit.GameObject?.GetComponentInParent<Titanfall2Npc>();
		var player = hit.GameObject?.GetComponentInParent<PlayerController>();
		if ( hit.Normal.z < 0.7f || actor.IsValid() || player.IsValid()
			|| hit.GameObject?.GetComponentInParent<Rigidbody>() is { MotionEnabled: true } )
		{
			Velocity = Titanfall2Projectile.BounceVelocity( Velocity, hit.Normal );
			return;
		}
		Velocity = Vector3.Zero;
		WorldRotation = Rotation.LookAt( hit.Normal );
		State = Titanfall2TetherTrapState.Arming; PlantedAt = Time.NowDouble;
		_surface = hit.GameObject;
		if ( _surface.IsValid() ) { _surfaceLocal = _surface.WorldTransform.ToLocal( WorldTransform ); _wasParented = true; }
		PlaySound( "Wpn_TetherTrap_Land" );
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || IsProxy || State == Titanfall2TetherTrapState.Flying || damage is null
			|| !float.IsFinite( damage.Damage ) || damage.Damage <= 0 || !damage.Attacker.IsValid() || damage.Weapon == GameObject ) return;
		// noOwnerFriendlyFire: neither the owner nor allied actors can destroy this trap.
		var faction = damage.Attacker.GetComponentInParent<Titanfall2Npc>()?.Faction
			?? (damage.Attacker.GetComponentInParent<PlayerController>() is not null ? Sandbox.Npcs.Factions.Player : null);
		if ( damage.Attacker == Owner || faction is null || !Hostile( faction ) ) return;
		Health = MathF.Max( 0, Health - (int)MathF.Min( damage.Damage, 100000 ) );
		if ( Health <= 0 ) Release();
	}

	internal void Release()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		TetherHealth = 0;
		if ( _constraint.IsValid() ) _constraint.Detach( this );
		_constraint = null;
		GameObject.Destroy();
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaySound( string name )
	{
		if ( name == "Wpn_TetherTrap_Deploy_3P" && Owner?.GetComponent<PlayerController>() is { IsProxy: false } )
			name = "Wpn_TetherTrap_Deploy_1P";
		Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, name, WorldPosition );
	}

	protected override void OnUpdate()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( State != Titanfall2TetherTrapState.Flying && Time.NowDouble >= PlantedAt + 0.5 && !_light.IsValid() )
		{
			_light = new GameObject( GameObject, false, "Tether blinker" );
			_light.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			if ( _model.IsValid() && _model.Model.Attachments.GetTransform( "BLINKER" ) is { } attachment ) _light.LocalTransform = attachment;
			var fx = _light.AddComponent<Titanfall2PlasmaFlight>(); fx.MountIdent = MountIdent; fx.EffectName = "wpn_grenade_TT_mag";
			_light.Enabled = true;
		}
		if ( !Attached || !Target.IsValid() ) return;
		var pilot = Target.Seat?.LocalPilot;
		var firstPerson = pilot.IsValid();
		if ( !_endpoint.IsValid() || _endpointFirstPerson != firstPerson )
		{
			if ( _endpoint.IsValid() ) _endpoint.Destroy();
			if ( _rope.IsValid() ) _rope.Destroy();
			_endpointFirstPerson = firstPerson;
			_endpoint = new GameObject( GameObject, true, "Tether endpoint" );
			_endpoint.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var model = Titanfall2GameplayVisuals.GetModel( $"mount://{MountIdent}/models/weapons/caber_shot/{(firstPerson ? "caber_shot_tether_xl" : "caber_shot_thrown_xl")}.mdl.visual.vmdl" );
			if ( model is { IsError: false } ) _endpoint.AddComponent<ModelRenderer>().Model = model;
			_rope = new GameObject( GameObject, true, "Tether rope" );
			_rope.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var line = _rope.AddComponent<LineRenderer>(); line.Points = [GameObject, _endpoint]; line.Width = 2;
			line.Color = Color.White; line.CastShadows = false;
			line.Texturing = new() { Material = Material.Load( $"mount://{MountIdent}/cable/tether.vmat" ) };
		}
		var end = firstPerson ? pilot.EyeTransform.Position + pilot.EyeTransform.Rotation * new Vector3( 64, 24, -24 )
			: Target.WorldPosition + Vector3.Up * (Target.BodyHeight * 0.55f);
		_endpoint.WorldPosition = Vector3.Lerp( WorldPosition, end, Math.Clamp( (float)(Time.NowDouble - AttachedAt) / 0.3f, 0, 1 ) );
	}
	protected override void OnDisabled() { if ( _constraint.IsValid() ) _constraint.Detach( this ); }
	protected override void OnDestroy() { if ( _constraint.IsValid() ) _constraint.Detach( this ); }
}
