using System.Diagnostics;
using Titanfall2;

/// <summary>
/// Prepares the particle systems referenced by a map's *_fx.ent partition during
/// scene loading. Instances are retained; distant systems are
/// suspended instead of destroyed so moving through a map does not reveal gaps.
/// </summary>
[Library, Hide]
public sealed class Titanfall2ParticleStreamer : Component, Component.DontExecuteOnServer
{
	static readonly Sandbox.Diagnostics.Logger Log = new( "Titanfall2Particles" );
	const int DataMagic = 0x58465054; // TPFX
	const int DataVersion = 4;
	const int MaximumEntityCount = 100_000;
	// PCF libraries and map effects are created in two bounded stages so hundreds of
	// small effects never become one long loading or movement stall.
	const int LibrariesPerFrame = 8;
	const float LibraryFrameBudgetMilliseconds = 2f;
	const int InstancesPerFrame = 4;
	const float InstanceFrameBudgetMilliseconds = 2f;
	const float ActivationRadius = 4096f;
	// A suspended looping effect is stepped forward this long on waking so it reappears filled.
	const float WakeWarmupSeconds = 1f;
	const float WakeWarmupBudgetMilliseconds = 1.5f;

	[Property, Hide]
	public string MountIdent { get; set; }

	[Property, Hide]
	public string MapPath { get; set; }

	[Property, Hide]
	public Vector3 FallbackAnchor { get; set; }

	[Property, Hide]
	public string EncodedEntities { get; set; }

	readonly List<RuntimeEntity> _pending = [];
	readonly List<SpawnedEntity> _spawned = [];
	readonly List<InitialWarmup> _initialWarmups = [];
	// Reusable budget/probe timers: this path runs every frame and Stopwatch.StartNew()
	// allocates, so they are created once and restarted in place.
	readonly Stopwatch _spawnTimer = new();
	readonly Stopwatch _spawnProbe = new();
	readonly Stopwatch _warmupProbe = new();
	// Frustum planes are tested against the effect's influence sphere grown by this
	// margin, which covers sprites past the estimate and the camera turning between frames.
	const float SleepFrustumMargin = 256f;
	const float WakeWarmupStep = 1f / 15f;
	// Milliseconds spent warming woken effects in the current frame.
	double _warmupMilliseconds;
	bool _initialized;
	bool _catalogResolved;
	bool _preparing;
	int _failed;
	int _failureLogs;
	/// <summary>All requested particle resources were prepared successfully during scene load.</summary>
	public bool ResourcesPrepared { get; private set; }
	public int PendingSystemCount => _pending.Count;
	public int FailedSystemCount => _failed;

	internal void Configure( string mountIdent, string mapPath, Vector3 fallbackAnchor,
		IReadOnlyList<Titanfall2ParticleMapEntity> entities )
	{
		MountIdent = mountIdent;
		MapPath = mapPath;
		FallbackAnchor = fallbackAnchor;
		EncodedEntities = Encode( entities );
	}

	protected override void OnAwake() => InitializeEntities();

	protected override void OnEnabled() => RefreshActivation();

	protected override void OnDisabled()
	{
		foreach ( var entity in _spawned )
			if ( entity.GameObject.IsValid() ) entity.GameObject.Enabled = false;
	}

	protected override async Task OnLoad( LoadingContext context )
	{
		ResourcesPrepared = false;
		if ( string.IsNullOrWhiteSpace( EncodedEntities ) )
		{
			ResourcesPrepared = true;
			return;
		}
		if ( !_initialized ) InitializeEntities();
		if ( !_initialized ) return;
		_preparing = true;
		try
		{
			while ( _pending.Count > 0 || _initialWarmups.Count > 0 )
			{
				if ( !this.IsValid() || !GameObject.IsValid() || GameObject.IsDestroyed ) return;
				var mount = Sandbox.Mounting.Directory.Get( MountIdent ) as Titanfall2Mount;
				if ( mount is null || !mount.IsMounted ) return;
				// Catalog and creation have separate bounds. A yield never publishes readiness.
				if ( !mount.IsParticleCatalogComplete ) mount.IndexParticleFiles( LibrariesPerFrame, LibraryFrameBudgetMilliseconds );
				// Child definitions can live in a later PCF. Creating a parent against a partial
				// catalog silently loses those children for the lifetime of the map.
				if ( !mount.IsParticleCatalogComplete ) { await Task.Yield(); continue; }
				SpawnAvailable( mount );
				ResolveMissingDefinitions( mount );
				AdvanceInitialWarmups();
				context.Title = $"Preparing Titanfall 2 particles ({_spawned.Count} created, {_pending.Count} pending, {_initialWarmups.Count} warming)";
				if ( _pending.Count > 0 || _initialWarmups.Count > 0 ) await Task.Yield();
			}
			ResourcesPrepared = _failed == 0;
			Log.Info( $"Titanfall 2 particle preparation complete during scene loading: {_spawned.Count} map FX retained, "
				+ $"{_failed} skipped ({MapPath})." );
		}
		finally { _preparing = false; }
	}

	void ResolveMissingDefinitions( Titanfall2Mount mount )
	{
		if ( _pending.Count > 0 && mount.IsParticleCatalogComplete && !_catalogResolved )
		{
			_catalogResolved = true;
			// A completed catalog only proves which names are missing. Valid
			// systems can still be queued behind this frame's creation budget.
			for ( var index = _pending.Count - 1; index >= 0; --index )
			{
				var entity = _pending[index];
				if ( mount.TryGetParticleDefinition( entity.EffectName, out _ ) ) continue;
				_pending.RemoveAt( index );
				_failed++;
				if ( _failureLogs++ < 16 )
					Titanfall2Log.Warning( $"Titanfall 2 particle definition '{entity.EffectName}' was not found for '{MapPath}'." );
			}
		}
	}

	protected override void OnUpdate()
	{
		if ( !_initialized || _preparing ) return;
		// Every frame: a woken effect must be enabled before it reaches the screen, and
		// the pass is one distance and one frustum test per retained system.
		RefreshActivation();
	}

	protected override void OnDestroy()
	{
		ResourcesPrepared = false;
		var released = _spawned.Count;
		foreach ( var entity in _spawned )
			if ( entity.GameObject.IsValid() && !entity.GameObject.IsDestroyed ) entity.GameObject.Destroy();
		_spawned.Clear();
		_pending.Clear();
		_initialWarmups.Clear();
		Log.Info( $"Titanfall 2 particle scene resources released: {released} systems ({MapPath})." );
	}

	void InitializeEntities()
	{
		if ( _initialized || string.IsNullOrWhiteSpace( EncodedEntities ) ) return;
		_initialized = true;
		try
		{
			_pending.AddRange( Decode( EncodedEntities ) );
			var anchor = GetAnchor();
			_pending.Sort( ( left, right ) => left.Position.DistanceSquared( anchor ).CompareTo( right.Position.DistanceSquared( anchor ) ) );
			Log.Info( $"Titanfall 2 particle streamer ready: {_pending.Count} active map FX; "
				+ $"{LibrariesPerFrame} PCFs/frame, "
				+ $"{InstancesPerFrame} systems/frame ({MapPath})." );
		}
		catch ( Exception exception )
		{
			_pending.Clear();
			_failed++;
			Log.Warning( $"Failed to initialize Titanfall 2 particle streaming for '{MapPath}': {exception.Message}" );
		}
	}

	void SpawnAvailable( Titanfall2Mount mount )
	{
		if ( _pending.Count == 0 ) return;
		_spawnTimer.Restart();
		var created = 0;
		for ( var index = 0; index < _pending.Count
			&& created < InstancesPerFrame
			&& _spawnTimer.Elapsed.TotalMilliseconds < InstanceFrameBudgetMilliseconds; )
		{
			var entity = _pending[index];
			if ( !mount.TryGetParticleDefinition( entity.EffectName, out var definition ) )
			{
				index++;
				continue;
			}

			_pending.RemoveAt( index );
			// One system creation can decode a VTF sprite sheet and build materials and
			// models, so it is measured on its own rather than relying on the budget
			// check above, which only runs between entities.
			_spawnProbe.Restart();
			if ( SpawnEntity( mount, entity, definition ) ) created++;
			else _failed++;
			Titanfall2Log.ReportSpike( _spawnProbe, "particle system '{0}'", entity.EffectName );
		}
	}

	bool SpawnEntity( Titanfall2Mount mount, RuntimeEntity entity, Titanfall2ParticleDefinition definition )
	{
		var gameObject = new GameObject( GameObject, true,
			string.IsNullOrWhiteSpace( entity.TargetName ) ? entity.EffectName : entity.TargetName );
		gameObject.Flags |= GameObjectFlags.NotSaved;
		gameObject.WorldPosition = entity.Position;
		gameObject.WorldRotation = entity.Rotation;
		gameObject.WorldScale = Vector3.One * entity.Scale;
		var points = gameObject.AddComponent<Titanfall2ParticleControlPoints>();
		foreach ( var point in entity.ControlPoints ) points.SetControlPoint( point.Index, point.Position );
		bool createdSystem;
		int createdSystems;
		string error;
		var systems = new List<Titanfall2ParticleFactory.CreatedSystem>();
		try
		{
			createdSystem = Titanfall2ParticleFactory.TryCreate( gameObject, mount, definition, out createdSystems, out error, systems );
		}
		catch ( Exception exception )
		{
			// This runs from OnUpdate: a malformed PCF must not escape the frame callback.
			createdSystem = false;
			createdSystems = 0;
			error = exception.Message;
		}
		if ( !createdSystem )
		{
			gameObject.Destroy();
			if ( _failureLogs++ < 16 ) Titanfall2Log.Warning( $"Unable to create Titanfall 2 particle '{entity.EffectName}': {error}" );
			return false;
		}

		var alwaysActive = IsGlobalEffect( entity.EffectName ) || entity.WarmupAndPause;
		var influenceRadius = Titanfall2ParticleFactory.GetInfluenceRadius( definition ) * entity.Scale;
		// A system tree is drawn while any of its systems is, so the tree keeps the largest
		// draw distance and sleep time of its members.
		var drawDistance = 0f;
		var sleepTime = 0f;
		var warmup = new List<(ParticleEffect Effect, float Seconds)>();
		foreach ( var system in systems )
		{
			drawDistance = MathF.Max( drawDistance, system.Definition.MaxDrawDistance * entity.Scale );
			sleepTime = MathF.Max( sleepTime, system.Definition.SleepWhenNotDrawnTime );
			var seconds = system.Definition.Looping
				? MathF.Min( WakeWarmupSeconds,
					MathF.Max( system.Definition.WarmupTime, system.Definition.LifetimeMaximum ) )
				: 0f;
			if ( seconds > 0f && system.Effect.IsValid() ) warmup.Add( (system.Effect, seconds) );
		}
		var spawned = new SpawnedEntity( gameObject, entity.Position, alwaysActive, createdSystems, influenceRadius,
			drawDistance + influenceRadius, sleepTime, warmup.ToArray() );
		_spawned.Add( spawned );
		// Entity-level pre-roll is separate from the PCF default. Retail Beacon authors
		// 40 seconds on one smoke plume, so spread this work across loading frames.
		var initial = new Titanfall2ParticleWarmup( systems.Select( system => (system.Effect, system.Definition.WarmupTime) ),
			entity.WarmupTime, entity.WarmupAndPause );
		if ( !initial.Complete ) _initialWarmups.Add( new( spawned, initial ) );
		else ApplyActivation( spawned, GetAnchor(), GetViewFrustum(), Time.Now, false );
		return true;
	}

	void AdvanceInitialWarmups()
	{
		if ( _initialWarmups.Count == 0 ) return;
		var warmup = _initialWarmups[0];
		if ( !warmup.Work.Advance( 64, WakeWarmupBudgetMilliseconds ) ) return;
		_initialWarmups.RemoveAt( 0 );
		ApplyActivation( warmup.Entity, GetAnchor(), GetViewFrustum(), Time.Now, false );
	}

	void RefreshActivation()
	{
		_warmupMilliseconds = 0;
		var anchor = GetAnchor();
		var frustum = GetViewFrustum();
		var now = Time.Now;
		foreach ( var entity in _spawned ) ApplyActivation( entity, anchor, frustum, now, true );
	}

	/// <summary>
	/// The camera frustum for the sleep test, or null when sleeping is off or there is no
	/// camera to test against.
	/// </summary>
	Frustum? GetViewFrustum()
	{
		var camera = Scene?.Camera;
		return camera.IsValid() ? camera.GetFrustum() : null;
	}

	void ApplyActivation( SpawnedEntity entity, Vector3 anchor, Frustum? frustum, float now, bool warm )
	{
		if ( !entity.GameObject.IsValid() || entity.GameObject.IsDestroyed ) return;
		var wanted = entity.AlwaysActive || IsWanted( entity, anchor, frustum, now );
		if ( entity.GameObject.Enabled == wanted ) return;
		entity.GameObject.Enabled = wanted;
		if ( wanted && warm ) WarmUp( entity );
	}

	static bool IsWanted( SpawnedEntity entity, Vector3 anchor, Frustum? frustum, float now )
	{
		var radius = ActivationRadius + entity.InfluenceRadius;
		radius = MathF.Min( radius, entity.DrawDistance );
		if ( entity.Position.DistanceSquared( anchor ) > radius * radius ) return false;
		if ( frustum is not { } view ) return true;

		if ( view.IsInside( entity.Position, entity.InfluenceRadius + SleepFrustumMargin, true ) )
		{
			entity.LastVisibleTime = now;
			return true;
		}
		return now - entity.LastVisibleTime < entity.SleepTime;
	}

	/// <summary>
	/// Disabling an effect clears its particles, so a looping effect that wakes would refill
	/// from empty in plain view. It is stepped forward a little instead, within a per-frame
	/// budget so turning around does not stall on many wakes at once.
	/// </summary>
	void WarmUp( SpawnedEntity entity )
	{
		if ( entity.Warmup.Length == 0 ) return;
		if ( _warmupMilliseconds >= WakeWarmupBudgetMilliseconds ) return;
		_warmupProbe.Restart();
		foreach ( var (effect, seconds) in entity.Warmup )
		{
			if ( !effect.IsValid() || !effect.Active ) continue;
			for ( var remaining = seconds; remaining > 0f; remaining -= WakeWarmupStep )
				effect.Step( MathF.Min( remaining, WakeWarmupStep ) );
		}
		_warmupMilliseconds += _warmupProbe.Elapsed.TotalMilliseconds;
	}

	Vector3 GetAnchor() => Scene?.Camera.IsValid() == true ? Scene.Camera.WorldPosition : FallbackAnchor;

	static bool IsGlobalEffect( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		return name.Contains( "sun", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "star", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "sky", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "weather", StringComparison.OrdinalIgnoreCase );
	}

	internal static string Encode( IReadOnlyList<Titanfall2ParticleMapEntity> entities )
	{
		var valid = entities?.Where( static entity => !string.IsNullOrWhiteSpace( entity.EffectName ) ).ToArray()
			?? Array.Empty<Titanfall2ParticleMapEntity>();
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, System.Text.Encoding.UTF8, true ) )
		{
			writer.Write( DataMagic );
			writer.Write( DataVersion );
			writer.Write( valid.Length );
			foreach ( var entity in valid )
			{
				writer.Write( entity.EffectName );
				writer.Write( entity.TargetName ?? string.Empty );
				writer.Write( entity.Position.x );
				writer.Write( entity.Position.y );
				writer.Write( entity.Position.z );
				writer.Write( entity.Rotation.pitch );
				writer.Write( entity.Rotation.yaw );
				writer.Write( entity.Rotation.roll );
				writer.Write( entity.Scale );
				var points = entity.ControlPoints ?? Array.Empty<Titanfall2ParticleMapControlPoint>();
				writer.Write( points.Count );
				foreach ( var point in points )
				{
					writer.Write( point.Index );
					writer.Write( point.Position.x ); writer.Write( point.Position.y ); writer.Write( point.Position.z );
				}
				writer.Write( entity.WarmupTime );
				writer.Write( entity.WarmupAndPause );
			}
		}
		return Convert.ToBase64String( stream.GetBuffer(), 0, checked((int)stream.Length) );
	}

	internal static IEnumerable<RuntimeEntity> Decode( string encoded )
	{
		using var stream = new MemoryStream( Convert.FromBase64String( encoded ), false );
		using var reader = new BinaryReader( stream, System.Text.Encoding.UTF8, false );
		if ( reader.ReadInt32() != DataMagic ) throw new InvalidDataException( "Particle entity data has an invalid signature." );
		var version = reader.ReadInt32();
		if ( version is < 2 or > DataVersion ) throw new InvalidDataException( "Particle entity data has an unsupported version." );
		var count = reader.ReadInt32();
		if ( count < 0 || count > MaximumEntityCount ) throw new InvalidDataException( $"Invalid particle entity count {count}." );
		for ( var index = 0; index < count; index++ )
		{
			var effectName = reader.ReadString();
			var targetName = reader.ReadString();
			var position = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
			var rotation = new Angles( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
			var scale = Math.Clamp( reader.ReadSingle(), 0.001f, 1000f );
			var points = Array.Empty<Titanfall2ParticleMapControlPoint>();
			if ( version >= 3 )
			{
				var pointCount = reader.ReadInt32();
				if ( pointCount is < 0 or > 64 ) throw new InvalidDataException( "Particle control point count is invalid." );
				points = new Titanfall2ParticleMapControlPoint[pointCount];
				for ( var point = 0; point < pointCount; ++point )
				{
					var id = reader.ReadInt32();
					var location = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
					if ( id is < 0 or > 63 || !Titanfall2ParticleControlPoints.IsFinite( location ) )
						throw new InvalidDataException( "Particle control point is invalid." );
					points[point] = new( id, location );
				}
			}
			var warmup = version >= 4 ? reader.ReadSingle() : 0f;
			var pause = version >= 4 && reader.ReadBoolean();
			if ( !float.IsFinite( warmup ) || !Titanfall2ParticleControlPoints.IsFinite( position )
				|| !float.IsFinite( rotation.pitch ) || !float.IsFinite( rotation.yaw ) || !float.IsFinite( rotation.roll ) || !float.IsFinite( scale ) )
				throw new InvalidDataException( "Particle entity transform or warmup is invalid." );
			yield return new RuntimeEntity( effectName, targetName, position, rotation, scale, points, Math.Clamp( warmup, 0f, 60f ), pause );
		}
	}

	internal readonly record struct RuntimeEntity( string EffectName, string TargetName, Vector3 Position, Angles Rotation, float Scale,
		IReadOnlyList<Titanfall2ParticleMapControlPoint> ControlPoints, float WarmupTime, bool WarmupAndPause );
	sealed record InitialWarmup( SpawnedEntity Entity, Titanfall2ParticleWarmup Work );
	sealed class SpawnedEntity( GameObject gameObject, Vector3 position, bool alwaysActive, int systemCount, float influenceRadius,
		float drawDistance, float sleepTime, (ParticleEffect Effect, float Seconds)[] warmup )
	{
		public GameObject GameObject { get; } = gameObject;
		public Vector3 Position { get; } = position;
		bool _initialAlwaysActive = alwaysActive;
		public bool AlwaysActive { get => _initialAlwaysActive; }
		int _initialSystemCount = systemCount;
		public int SystemCount { get => _initialSystemCount; }
		float _initialInfluenceRadius = influenceRadius;
		public float InfluenceRadius { get => _initialInfluenceRadius; }
		/// <summary>Distance from <see cref="Position"/> beyond which no system in the tree is drawn.</summary>
		float _initialDrawDistance = drawDistance;
		public float DrawDistance { get => _initialDrawDistance; }
		float _initialSleepTime = sleepTime;
		public float SleepTime { get => _initialSleepTime; }
		public (ParticleEffect Effect, float Seconds)[] Warmup { get; } = warmup;
		/// <summary>Starts at creation, so a new system gets its full sleep time before it can sleep.</summary>
		float _initialLastVisibleTime = Time.Now;
		public float LastVisibleTime { get => _initialLastVisibleTime; set => _initialLastVisibleTime = value; }
	}
}

