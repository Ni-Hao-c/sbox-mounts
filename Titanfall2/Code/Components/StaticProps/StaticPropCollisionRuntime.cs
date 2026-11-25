using System.Diagnostics;
using System.Runtime.InteropServices;
using Titanfall2;

public sealed partial class Titanfall2StaticPropStreamer
{
	// Nested to consume the scene's private instance records without exposing an engine API.
	// This worker owns physics scheduling and readiness; render ownership is independent.
	sealed class CollisionRuntime( Titanfall2StaticPropStreamer owner )
	{
		const float CollisionChunkSize = 512f;
		const string CollisionCacheKind = "propcol";
		const uint CollisionCacheMagic = 0x43503254; // "T2PC"
		const int CollisionCacheVersion = 1;
		readonly record struct ChunkKey( int X, int Y );

		sealed record CachedChunk( Vector3[] Vertices, uint[] Indices, byte[] TriangleSurfaces );
		sealed record CachedGroup( bool HasContents, uint Contents, int PropCount, string[] SurfaceKeys, CachedChunk[] Chunks );
		sealed record CachedCollisionPayload( int Expected, int Created, int Filtered, CachedGroup[] Groups );

		sealed class SpatialMeshChunk
		{
			public readonly List<Vector3> Vertices = new();
			public readonly List<uint> Indices = new();
			public readonly List<byte> TriangleSurfaces = new();
		}

		sealed class MergedContentsGroup
		{
			public readonly Dictionary<ChunkKey, SpatialMeshChunk> Chunks = new();
			public readonly List<Surface> Surfaces = new();
			public readonly Dictionary<Surface, PhysicsBodyBuilder> FallbackHullBodies = new();
			public PhysicsBodyBuilder DefaultFallbackHullBody;
			public readonly List<(PropInstance Instance, Model Model)> Members = new();
			public int PropCount => Members.Count;

			public PhysicsBodyBuilder GetFallbackHullBody( Surface surface )
			{
				if ( surface is null )
					return DefaultFallbackHullBody ??= new PhysicsBodyBuilder();
				if ( !FallbackHullBodies.TryGetValue( surface, out var body ) )
					FallbackHullBodies[surface] = body = new PhysicsBodyBuilder { Surface = surface };
				return body;
			}

			public byte GetSurfaceIndex( Surface surface )
			{
				var resolved = surface ?? Surface.FindByName( "default" );
				if ( resolved is null ) return 0;
				var index = Surfaces.IndexOf( resolved );
				if ( index >= 0 ) return (byte)index;
				if ( Surfaces.Count > byte.MaxValue ) return 0;
				Surfaces.Add( resolved );
				return (byte)(Surfaces.Count - 1);
			}

			public SpatialMeshChunk GetChunk( ChunkKey key )
			{
				if ( !Chunks.TryGetValue( key, out var chunk ) )
					Chunks[key] = chunk = new SpatialMeshChunk();
				return chunk;
			}
		}

		public readonly List<PropCell> Queue = new();
		readonly List<GameObject> _groupedObjects = new();
		readonly List<PhysicsGroupDescription> _groupedPhysics = new();
		public bool Dirty;
		public int Cursor;
		public int Expected;
		public int Created;
		public int MergedBodyCount;
		public int Failures;
		public int Filtered;
		public int Released;
		public readonly Stopwatch Probe = new();
		public readonly Stopwatch Timer = new();
		public int Changes;
		public int PreloadAttempts;
		public int Deferred;
		public int Resumes;
		public bool ResidentReady;
		public bool LoadedFromCache;
		public bool HasPending => Created + Filtered + Failures < Expected;
		public bool NeedsSpatialWork => !owner.KeepAllCollisions || HasPending || Dirty;
		public int ResidentBodyCount => owner.KeepAllCollisions ? MergedBodyCount : Created;

		public void BeginPopulate() => PreloadAttempts = 0;

		public void Clear()
		{
			foreach ( var obj in _groupedObjects )
			{
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Destroy();
			}
			_groupedObjects.Clear();
			_groupedPhysics.Clear();
			Queue.Clear();
			Cursor = 0;
			Expected = Created = MergedBodyCount = Filtered = Failures = 0;
			ResidentReady = Dirty = LoadedFromCache = false;
		}

		public void Register( PropInstance instance, Model model, Stopwatch timer, float budgetMilliseconds )
		{
			if ( !instance.Source.Collidable ) return;
			Expected++;
			// Resident maps merge all static prop hulls/meshes per (cluster, contents) during
			// Preload instead of spawning thousands of individual ModelCollider GameObjects.
			if ( owner.KeepAllCollisions || Application.IsDedicatedServer ) return;
			if ( PreloadAttempts < PreloadCollisionBodiesPerYield && timer.Elapsed.TotalMilliseconds < budgetMilliseconds )
			{
				PreloadAttempts++;
				EnsureCollisionState( instance, owner._lastAnchor, model );
			}
			else Deferred++;
		}

		public void Refresh()
		{
			if ( !owner.KeepAllCollisions ) RebuildCollisionQueue();
		}

		public void Tick()
		{
			if ( owner.KeepAllCollisions )
			{
				if ( Dirty && !owner._releasing )
				{
					Dirty = false;
					RebuildMergedResidentCollisions();
				}
				return;
			}
			if ( Dirty )
			{
				Dirty = false;
				RebuildCollisionQueue();
			}
			if ( Queue.Count > 0 ) ProcessCollisionQueue();
		}
		/// <summary>
		/// Applies collision state on its own schedule. One collider costs a synchronous model
		/// load plus a native physics body (2-22 ms), so this pass is capped per frame and
		/// resumes the cell it stopped on. It is separate from the state pass precisely because
		/// stopping early is normal for it.
		/// </summary>
		public void ProcessCollisionQueue()
		{
			if ( Cursor >= Queue.Count ) return;
			Timer.Restart();
			Changes = 0;
			while ( Cursor < Queue.Count )
			{
				if ( Changes >= CollisionInstancesPerFrame ) break;
				var cell = Queue[Cursor];
				if ( !UpdateCellCollisionState( cell, owner._lastAnchor, Timer ) )
				{
					// The budget stopped this cell partway. Leave the cursor on it so the next frame
					// resumes its own instance cursor exactly where it stopped.
					Resumes++;
					break;
				}

				Cursor++;
				if ( Timer.Elapsed.TotalMilliseconds >= CollisionFrameBudgetMilliseconds ) break;
			}

			if ( Cursor < Queue.Count ) return;
			Queue.Clear();
			Cursor = 0;
		}

		/// <summary>
		/// Applies the collision state for one cell. Returns false when the per-frame cap
		/// or the frame budget stopped the pass early, in which case the collision queue
		/// resumes this cell on a later frame. A single collider is cheap in isolation
		/// (0.02-0.69 ms measured), but a dense cell holds many and movement used to churn
		/// them at 90-130 per second, so the pass needs both a count cap and a time cap.
		/// The budget timer is supplied by the caller because the collision pass has its own
		/// per-frame budget; sharing the state pass's timer would abort immediately.
		/// </summary>
		public bool UpdateCellCollisionState( PropCell cell, Vector3 anchor, Stopwatch budgetTimer )
		{
			var frameBudget = CollisionFrameBudgetMilliseconds;
			var instanceLimit = CollisionInstancesPerFrame;

			// Resume from this cell's own cursor. Restarting the scan from the first
			// instance every frame would re-spend the whole per-frame cap on the same
			// leading instances and the tail of a dense cell would never be reached.
			while ( cell.CollisionCursor < cell.Instances.Count )
			{
				if ( Changes >= instanceLimit )
					return false;

				// Checked before every collider rather than after a batch of them, so one slow
				// activation cannot carry the frame past the budget. Returning here leaves the
				// cursor on this instance: it is retried next frame with a fresh budget, so the
				// pass still makes progress even when it stops immediately every frame.
				if ( budgetTimer.Elapsed.TotalMilliseconds >= frameBudget )
					return false;

				var instance = cell.Instances[cell.CollisionCursor];
				// Only a body that was actually created or destroyed counts against the cap. Counting
				// every check spent the default cap of 2 on instances whose state was already right,
				// so the queue advanced ~100 instances/s and colliders lagged the player in dense
				// areas. Pure checks stay bounded by the frame budget above.
				if ( instance.Source.Collidable && !instance.CollisionUnavailable
					&& EnsureCollisionState( instance, anchor, fallbackModel: null ) )
					Changes++;

				cell.CollisionCursor++;
			}

			return true;
		}

		/// <returns>True when a collision body was created, destroyed or attempted (the costly work).</returns>
		public bool EnsureCollisionState( PropInstance instance, Vector3 anchor, Model fallbackModel )
		{
			if ( !instance.Source.Collidable
				|| instance.CollisionUnavailable || instance.CollisionFiltered )
				return false;

			var collisionActive = instance.CollisionObject.IsValid() && !instance.CollisionObject.IsDestroyed;
			var shouldBeActive = owner.KeepAllCollisions || ShouldKeepCollision( instance.Position, instance.Radius, anchor, collisionActive );
			if ( shouldBeActive == collisionActive ) return false;
			// A collider destroyed by something else still holds its reference here. Releasing its
			// count first keeps Created from drifting upward when it is recreated below.
			if ( !collisionActive && instance.CollisionObject is not null )
			{
				instance.CollisionObject = null;
				Created = Math.Max( 0, Created - 1 );
			}
			if ( !shouldBeActive )
			{
				// Destroy rather than recycle. A collision-object pool was tried here and removed:
				// its release path cleared the instance reference without handing the object to the
				// pool, so released colliders were never torn down at all, and its measured reuse
				// rate was ~2%. At the ~15 activations per second the instance cap and the frame
				// budget leave, one owner.GameObject lifecycle is not worth that complexity or that risk.
				instance.CollisionObject.Destroy();
				instance.CollisionObject = null;
				Created = Math.Max( 0, Created - 1 );
				Released++;
				return true;
			}

			var prop = instance.Source;
			Probe.Restart();
			var resourcePath = $"mount://{owner.MountIdent}/{prop.ModelPath}.vmdl";
			var collision = owner.LoadCollisionModelWithRetryCache( resourcePath, prop.Skin );
			if ( collision.Status == Titanfall2CollisionModelStatus.Filtered )
			{
				instance.CollisionFiltered = true;
				Filtered++;
				return true;
			}
			if ( collision.Status == Titanfall2CollisionModelStatus.Empty )
			{
				instance.CollisionUnavailable = true;
				Failures++;
				if ( owner._failureLogs++ < 16 )
					Titanfall2Log.Warning( $"Titanfall 2 static prop imported without usable collision geometry: '{prop.ModelPath}' ({owner.MapPath})." );
				return true;
			}
			// An already loaded full model may recover a transient collision-only load failure,
			// but a model without physics must not turn a retryable failure into a terminal one.
			var resolvedModel = ModelLoader.IsUsableModel( collision.Model ) ? collision.Model
				: ModelLoader.IsUsableModel( fallbackModel ) && fallbackModel.Physics is { Parts.Count: > 0 } ? fallbackModel : null;
			if ( !ModelLoader.IsUsableModel( resolvedModel ) )
			{
				// A read/parse failure is transient, not proof that the prop has no collision.
				// Leaving CollisionUnavailable false lets a later refresh retry it (the model
				// retry cache bounds how often the file is read again) instead of permanently
				// blocking dedicated-server readiness on one hiccup.
				if ( owner._failureLogs++ < 16 )
					Titanfall2Log.Warning( $"Titanfall 2 static prop collision model is temporarily unavailable: "
						+ $"'{prop.ModelPath}' at {prop.Position} ({owner.MapPath})." );
				ReportSpike( Probe, "deferred collision body for '{0}'", prop.ModelPath );
				return true;
			}

			var data = resolvedModel.GetData<Titanfall2ModelCollisionData>();
			// Only a known v6 instance and known MDL contents can prove this object has
			// no collision for ANY retail query. Nonzero contents still need gameplay
			// query classification; never discard bullet-only props using a player mask.
			var hasContents = prop.SourceIndex >= 0 && data is not null;
			var contents = hasContents ? Titanfall2CollisionSource.StaticPropContents( data.ModelContents, prop.CollisionFlagsRemove ) : 0u;
			if ( hasContents && contents == 0 )
			{
				instance.CollisionFiltered = true;
				Filtered++;
				return true;
			}
			var collisionObject = new GameObject( owner.GameObject, true, System.IO.Path.GetFileNameWithoutExtension( prop.ModelPath ) );
			collisionObject.Flags |= GameObjectFlags.NotSaved;
			collisionObject.WorldPosition = prop.Position;
			collisionObject.WorldRotation = prop.Rotation;
			collisionObject.WorldScale = Vector3.One * MathF.Max( prop.Scale, 0.001f );
			collisionObject.IsStatic = true;
			if ( Titanfall2StreamingSettings.NavMeshStaticProps )
				collisionObject.Tags.Add( Titanfall2DeferredNavMeshBuilder.NavMeshBodyTag );
			var collider = collisionObject.AddComponent<ModelCollider>();
			collider.Model = resolvedModel;
			collider.Static = true;
			var source = collisionObject.AddComponent<Titanfall2CollisionSource>();
			source.MountIdent = owner.MountIdent;
			source.MapPath = owner.MapPath;
			source.ModelPath = prop.ModelPath;
			source.SourceIndex = prop.SourceIndex;
			source.HasInstanceMetadata = prop.SourceIndex >= 0;
			source.SolidType = prop.SolidType;
			source.ReservedCollisionData = prop.ReservedCollisionData;
			source.CollisionFlagsRemove = prop.CollisionFlagsRemove;
			source.HasEffectiveContents = hasContents;
			source.EffectiveContents = contents;
			if ( hasContents ) Titanfall2CollisionQueries.Classify( collisionObject, collider, contents );
			if ( ModelLoader.IsUsableModel( collider.Model )
				&& collider.Model.Physics is { Parts.Count: > 0 } )
			{
				instance.CollisionObject = collisionObject;
				source.CollisionDestroyed = OnResidentCollisionDestroyed;
				Created++;
				ReportSpike( Probe, "collision body for '{0}'", prop.ModelPath );
				return true;
			}

			Failures++;
			instance.CollisionUnavailable = true;
			if ( owner._failureLogs++ < 16 )
				Titanfall2Log.Warning( $"Titanfall 2 static prop has no usable collision bodies: "
					+ $"'{prop.ModelPath}' at {prop.Position} ({owner.MapPath})." );
			collisionObject.Destroy();
			ReportSpike( Probe, "failed collision body for '{0}'", prop.ModelPath );
			return true;
		}

		static bool ShouldKeepCollision( Vector3 position, float radius, Vector3 anchor, bool currentlyActive )
		{
			if ( Titanfall2StreamingSettings.NavMeshStaticProps ) return true;
			var configuredDistance = currentlyActive ? CollisionDisableDistance : CollisionEnableDistance;
			var distance = configuredDistance + radius;
			return position.DistanceSquared( anchor ) <= distance * distance;
		}
		public void RebuildCollisionQueue()
		{
			Queue.Clear();
			Queue.AddRange( owner._cells.Values );
			Queue.Sort( static ( left, right ) => left.DistanceSquared.CompareTo( right.DistanceSquared ) );
			Cursor = 0;
			// A refresh re-evaluates every cell from the start, so any collision pass that
			// was interrupted by the per-frame cap begins again rather than resuming.
			foreach ( var cell in Queue ) cell.CollisionCursor = 0;
		}

		void OnResidentCollisionDestroyed()
		{
			if ( owner._releasing || !owner.KeepAllCollisions ) return;
			ResidentReady = false;
			Dirty = true;
		}


		public bool HasDiskCache()
		{
			var identity = BuildCollisionCacheIdentity();
			if ( !Titanfall2StreamingSettings.ProcessedCache || string.IsNullOrEmpty( identity ) ) return false;
			try { return Titanfall2Storage.FileExists( Titanfall2ProcessedCache.GetPath( CollisionCacheKind, identity ) ); }
			catch ( Exception ) { return false; }
		}

		string BuildCollisionCacheIdentity()
		{
			if ( string.IsNullOrWhiteSpace( owner.MapPath ) || string.IsNullOrWhiteSpace( owner.MountIdent ) ) return null;
			if ( Sandbox.Mounting.Directory.Get( owner.MountIdent ) is not Titanfall2Mount mount ) return null;
			return $"{mount.ContentFingerprint}|{owner.MapPath}|propcol-addmesh-ccw-v{CollisionCacheVersion}";
		}

		public async Task Preload( LoadingContext context )
		{
			// Merge all resident static prop collisions globally by EffectiveContents and split
			// triangles into 512u spatial AddMesh chunks (identical to Map.Collision.cs). This turns
			// ~18,000 individual broadphase hull shapes into ~14 bodies / ~500 mesh chunks while
			// preserving per-triangle Surface and retail EffectiveContents classification.
			if ( await TryPreloadFromCacheAsync( context ) ) return;
			await BuildMergedResidentCollisionsAsync( context );
		}

		public async Task<bool> TryPreloadFromCacheAsync( LoadingContext context )
		{
			var identity = BuildCollisionCacheIdentity();
			if ( !Titanfall2StreamingSettings.ProcessedCache || string.IsNullOrEmpty( identity ) ) return false;
			try
			{
				if ( !Titanfall2Storage.FileExists( Titanfall2ProcessedCache.GetPath( CollisionCacheKind, identity ) ) ) return false;
			}
			catch ( Exception ) { return false; }

			context.Title = "Loading cached Titanfall 2 prop collision";
			var timer = Stopwatch.StartNew();
			var payload = await Titanfall2Workers.Run( () => ReadCollisionCache( identity ) );
			if ( payload is null || payload.Groups.Length == 0 || !owner.IsValid() || !owner.GameObject.IsValid() )
				return false;

			DestroyGroupedObjects();
			Created = MergedBodyCount = Filtered = Failures = 0;
			ResidentReady = false;
			LoadedFromCache = false;

			foreach ( var group in payload.Groups )
			{
				if ( group.Chunks.Length == 0 ) continue;
				var surfaces = new List<Surface>( group.SurfaceKeys.Length );
				foreach ( var key in group.SurfaceKeys )
					surfaces.Add( Surface.FindByName( key ) ?? Surface.FindByName( "default" ) );

				var meshBody = new PhysicsBodyBuilder();
				var hasSurfaces = surfaces.Count > 0;
				var addedChunk = false;
				foreach ( var chunk in group.Chunks )
				{
					if ( chunk.Vertices.Length < 3 || chunk.Indices.Length < 3 ) continue;
					var triSurfaces = hasSurfaces && chunk.TriangleSurfaces.Length == chunk.Indices.Length / 3
						? chunk.TriangleSurfaces.AsSpan()
						: Span<byte>.Empty;
					meshBody.AddMesh( chunk.Vertices.AsSpan(), chunk.Indices.AsSpan(), triSurfaces );
					addedChunk = true;
				}
				if ( !addedChunk ) continue;

				var tagSuffix = group.HasContents ? group.Contents.ToString( "x8" ) : "raw";
				var name = $"{owner.MapPath}/staticprops_contents_{tagSuffix}.vphys";
				var physics = PhysicsGroupDescription.Create( name, [meshBody], hasSurfaces ? surfaces : null );
				if ( !physics.IsValid() || physics.Parts.Count == 0 )
				{
					DestroyGroupedObjects();
					return false;
				}

				AttachMergedPhysicsGroup( physics, tagSuffix, group.HasContents, group.Contents, group.PropCount );
			}

			Expected = payload.Expected;
			Filtered = payload.Filtered;
			if ( Created + Filtered != Expected || MergedBodyCount == 0 )
			{
				DestroyGroupedObjects();
				Expected = Created = MergedBodyCount = Filtered = 0;
				return false;
			}

			LoadedFromCache = true;
			ResidentReady = true;
			Queue.Clear();
			Cursor = 0;
			Log.Info( $"Titanfall 2 static prop collision cache hit: {Created}/{Expected} colliders in {MergedBodyCount} merged bodies restored in {timer.Elapsed.TotalSeconds:0.00}s ({owner.MapPath})." );
			return true;
		}

		static CachedCollisionPayload ReadCollisionCache( string identity )
		{
			CachedCollisionPayload parsed = null;
			var hit = Titanfall2ProcessedCache.TryRead( CollisionCacheKind, identity, reader =>
			{
				if ( reader.ReadUInt32() != CollisionCacheMagic || reader.ReadInt32() != CollisionCacheVersion )
					throw new InvalidDataException( "Collision cache header mismatch." );

				var expected = reader.Count( 1_000_000 );
				var created = reader.Count( 1_000_000 );
				var filtered = reader.Count( 1_000_000 );
				var groupCount = reader.Count( 4096 );
				var groups = new CachedGroup[groupCount];
				for ( var g = 0; g < groupCount; g++ )
				{
					var hasContents = reader.ReadBoolean();
					var contents = reader.ReadUInt32();
					var propCount = reader.Count( 1_000_000 );
					var surfCount = reader.Count( 512 );
					var surfKeys = new string[surfCount];
					for ( var s = 0; s < surfCount; s++ ) surfKeys[s] = reader.Text();
					var chunkCount = reader.Count( 65536 );
					var chunks = new CachedChunk[chunkCount];
					for ( var c = 0; c < chunkCount; c++ )
					{
						var verts = reader.Array<Vector3>( 4_000_000 );
						var idxs = reader.Array<uint>( 12_000_000 );
						var tris = reader.Array<byte>( 4_000_000 );
						chunks[c] = new CachedChunk( verts, idxs, tris );
					}
					groups[g] = new CachedGroup( hasContents, contents, propCount, surfKeys, chunks );
				}
				parsed = new CachedCollisionPayload( expected, created, filtered, groups );
			} );
			return hit ? parsed : null;
		}

		void WriteCollisionCache( Dictionary<(bool HasContents, uint Contents), MergedContentsGroup> groups )
		{
			var identity = BuildCollisionCacheIdentity();
			if ( !Titanfall2StreamingSettings.ProcessedCache || string.IsNullOrEmpty( identity ) ) return;
			if ( Failures > 0 || Created + Filtered != Expected ) return;
			// Only cache pure AddMesh builds; fallback hull groups are rare and stay on the live builder.
			foreach ( var g in groups.Values )
				if ( g.DefaultFallbackHullBody is not null || g.FallbackHullBodies.Count > 0 ) return;

			var cachedGroups = new List<CachedGroup>( groups.Count );
			foreach ( var ((hasContents, contents), group) in groups )
			{
				if ( group.PropCount == 0 || group.Chunks.Count == 0 ) continue;
				var surfKeys = new string[group.Surfaces.Count];
				for ( var i = 0; i < group.Surfaces.Count; i++ )
				{
					var s = group.Surfaces[i];
					surfKeys[i] = !string.IsNullOrWhiteSpace( s?.ResourcePath ) && s.ResourcePath.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase )
						? s.ResourcePath
						: s?.ResourceName ?? "default";
				}
				var chunks = new List<CachedChunk>( group.Chunks.Count );
				foreach ( var chunk in group.Chunks.Values )
				{
					if ( chunk.Vertices.Count < 3 || chunk.Indices.Count < 3 ) continue;
					chunks.Add( new CachedChunk( chunk.Vertices.ToArray(), chunk.Indices.ToArray(), chunk.TriangleSurfaces.ToArray() ) );
				}
				if ( chunks.Count > 0 )
					cachedGroups.Add( new CachedGroup( hasContents, contents, group.PropCount, surfKeys, chunks.ToArray() ) );
			}
			if ( cachedGroups.Count == 0 ) return;

			var payload = new CachedCollisionPayload( Expected, Created, Filtered, cachedGroups.ToArray() );
			var timer = Stopwatch.StartNew();
			var written = Titanfall2ProcessedCache.Write( CollisionCacheKind, identity, writer =>
			{
				writer.Write( CollisionCacheMagic );
				writer.Write( CollisionCacheVersion );
				writer.Write( payload.Expected );
				writer.Write( payload.Created );
				writer.Write( payload.Filtered );
				writer.Write( payload.Groups.Length );
				foreach ( var g in payload.Groups )
				{
					writer.Write( g.HasContents );
					writer.Write( g.Contents );
					writer.Write( g.PropCount );
					writer.Write( g.SurfaceKeys.Length );
					foreach ( var key in g.SurfaceKeys ) Titanfall2ProcessedCache.WriteString( writer, key );
					writer.Write( g.Chunks.Length );
					foreach ( var c in g.Chunks )
					{
						Titanfall2ProcessedCache.WriteArray( writer, c.Vertices );
						Titanfall2ProcessedCache.WriteArray( writer, c.Indices );
						Titanfall2ProcessedCache.WriteArray( writer, c.TriangleSurfaces );
					}
				}
			} );
			if ( written )
				Log.Info( $"Titanfall 2 static prop collision cache written ({payload.Groups.Length} bodies) in {timer.Elapsed.TotalSeconds:0.00}s ({owner.MapPath})." );
		}

		void AttachMergedPhysicsGroup( PhysicsGroupDescription physics, string tagSuffix, bool hasContents, uint contents, int propCount )
		{
			_groupedPhysics.Add( physics );
			var obj = new GameObject( owner.GameObject, owner.Active, $"t2_prop_col_{tagSuffix}" );
			obj.Flags |= GameObjectFlags.NotSaved;
			obj.IsStatic = true;
			if ( Titanfall2StreamingSettings.NavMeshStaticProps )
				obj.Tags.Add( Titanfall2DeferredNavMeshBuilder.NavMeshBodyTag );

			var collider = obj.AddComponent<PhysicsCollider>();
			collider.Physics = physics;
			collider.Static = true;

			var source = obj.AddComponent<Titanfall2CollisionSource>();
			source.MountIdent = owner.MountIdent;
			source.MapPath = owner.MapPath;
			source.GeometrySource = "merged static prop mesh";
			source.HasEffectiveContents = hasContents;
			source.EffectiveContents = contents;
			source.CollisionDestroyed = OnResidentCollisionDestroyed;

			if ( hasContents ) Titanfall2CollisionQueries.Classify( obj, collider, contents );
			_groupedObjects.Add( obj );
			MergedBodyCount++;
			Created += propCount;
		}

		void RebuildMergedResidentCollisions()
		{
			DestroyGroupedObjects();
			Created = MergedBodyCount = Filtered = Failures = 0;
			ResidentReady = false;
			var groups = new Dictionary<(bool HasContents, uint Contents), MergedContentsGroup>();
			foreach ( var cluster in owner._clusters.Values )
			{
				if ( !owner.IsValid() || !owner.GameObject.IsValid() ) return;
				CollectClusterProps( cluster, groups );
			}
			CreateMergedGroups( groups );
			ResidentReady = owner._failedProps == 0 && Failures == 0
				&& Created + Filtered == Expected;
		}

		async Task BuildMergedResidentCollisionsAsync( LoadingContext context )
		{
			DestroyGroupedObjects();
			Created = MergedBodyCount = Filtered = Failures = 0;
			ResidentReady = false;
			var groups = new Dictionary<(bool HasContents, uint Contents), MergedContentsGroup>();
			var collected = 0;
			Timer.Restart();

			foreach ( var cluster in owner._clusters.Values )
			{
				if ( !owner.IsValid() || !owner.GameObject.IsValid() ) return;
				collected += CollectClusterProps( cluster, groups );
				context.Title = $"Preparing Titanfall 2 prop collision ({collected}/{Expected})";
				if ( Timer.Elapsed.TotalMilliseconds >= PreloadBudgetMilliseconds )
				{
					await owner.Task.Yield();
					Timer.Restart();
				}
			}

			CreateMergedGroups( groups );
			context.Title = $"Loading Titanfall 2 prop collision ({Created}/{Expected})";
			ResidentReady = owner._failedProps == 0 && Failures == 0
				&& Created + Filtered == Expected;
			if ( ResidentReady ) WriteCollisionCache( groups );
			Queue.Clear();
			Cursor = 0;
		}

		void DestroyGroupedObjects()
		{
			foreach ( var obj in _groupedObjects )
			{
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Destroy();
			}
			_groupedObjects.Clear();
			_groupedPhysics.Clear();
		}

		int CollectClusterProps( PropCluster cluster, Dictionary<(bool HasContents, uint Contents), MergedContentsGroup> groups )
		{
			var visited = 0;
			foreach ( var cell in cluster.Cells )
			{
				foreach ( var instance in cell.Instances )
				{
					if ( !instance.Source.Collidable ) continue;
					visited++;
					var prop = instance.Source;
					var resourcePath = $"mount://{owner.MountIdent}/{prop.ModelPath}.vmdl";
					var collision = owner.LoadCollisionModelWithRetryCache( resourcePath, prop.Skin );
					if ( collision.Status == Titanfall2CollisionModelStatus.Filtered )
					{
						instance.CollisionFiltered = true;
						Filtered++;
						continue;
					}
					if ( collision.Status == Titanfall2CollisionModelStatus.Empty )
					{
						instance.CollisionUnavailable = true;
						Failures++;
						if ( owner._failureLogs++ < 16 )
							Titanfall2Log.Warning( $"Titanfall 2 static prop imported without usable collision geometry: '{prop.ModelPath}' ({owner.MapPath})." );
						continue;
					}
					var resolvedModel = ModelLoader.IsUsableModel( collision.Model ) ? collision.Model : null;
					if ( !ModelLoader.IsUsableModel( resolvedModel ) || resolvedModel.Physics is not { Parts.Count: > 0 } )
					{
						Failures++;
						instance.CollisionUnavailable = true;
						if ( owner._failureLogs++ < 16 )
							Titanfall2Log.Warning( $"Titanfall 2 static prop collision model is unavailable: '{prop.ModelPath}' at {prop.Position} ({owner.MapPath})." );
						continue;
					}

					var data = resolvedModel.GetData<Titanfall2ModelCollisionData>();
					var hasContents = prop.SourceIndex >= 0 && data is not null;
					var contents = hasContents ? Titanfall2CollisionSource.StaticPropContents( data.ModelContents, prop.CollisionFlagsRemove ) : 0u;
					if ( hasContents && contents == 0 )
					{
						instance.CollisionFiltered = true;
						Filtered++;
						continue;
					}

					var key = (hasContents, contents);
					if ( !groups.TryGetValue( key, out var group ) )
						groups[key] = group = new MergedContentsGroup();

					var scale = MathF.Max( prop.Scale, 0.001f );
					if ( !AppendPropGeometry( group, resolvedModel, prop.Position, prop.Rotation, scale ) )
					{
						// Models whose compiled PhysicsGroupDescription has zero actual shapes
						// (e.g. degenerate garbage bag hulls) have no native physics body.
						instance.CollisionFiltered = true;
						Filtered++;
						continue;
					}
					group.Members.Add( (instance, resolvedModel) );
				}
			}
			return visited;
		}

		void CreateMergedGroups( Dictionary<(bool HasContents, uint Contents), MergedContentsGroup> groups )
		{
			foreach ( var ((hasContents, contents), group) in groups )
			{
				if ( group.PropCount == 0 ) continue;
				var bodies = new List<PhysicsBodyBuilder>( group.FallbackHullBodies.Count + 2 );
				if ( group.Chunks.Count > 0 )
				{
					var meshBody = new PhysicsBodyBuilder();
					var hasSurfaces = group.Surfaces.Count > 0;
					foreach ( var chunk in group.Chunks.Values )
					{
						if ( chunk.Indices.Count < 3 ) continue;
						var triSurfaces = hasSurfaces
							? CollectionsMarshal.AsSpan( chunk.TriangleSurfaces )
							: Span<byte>.Empty;
						meshBody.AddMesh(
							CollectionsMarshal.AsSpan( chunk.Vertices ),
							CollectionsMarshal.AsSpan( chunk.Indices ),
							triSurfaces );
					}
					bodies.Add( meshBody );
				}
				if ( group.DefaultFallbackHullBody is not null ) bodies.Add( group.DefaultFallbackHullBody );
				bodies.AddRange( group.FallbackHullBodies.Values );
				if ( bodies.Count == 0 ) continue;

				var tagSuffix = hasContents ? contents.ToString( "x8" ) : "raw";
				var name = $"{owner.MapPath}/staticprops_contents_{tagSuffix}.vphys";
				var surfaces = group.Surfaces.Count > 0 ? group.Surfaces : null;
				var physics = PhysicsGroupDescription.Create( name, bodies, surfaces );
				if ( !physics.IsValid() || physics.Parts.Count == 0 )
				{
					Log.Warning( $"Titanfall 2 failed to build merged static prop physics '{name}' for {group.PropCount} props; falling back to individual colliders ({owner.MapPath})." );
					foreach ( var (member, memberModel) in group.Members )
					{
						if ( !CreateFallbackResidentCollider( member, memberModel ) )
						{
							Failures++;
							member.CollisionUnavailable = true;
						}
					}
					continue;
				}

				AttachMergedPhysicsGroup( physics, tagSuffix, hasContents, contents, group.PropCount );
			}
		}

		static bool AppendPropGeometry(
			MergedContentsGroup group,
			Model resolvedModel,
			Vector3 position,
			Rotation rotation,
			float scale )
		{
			var fullTransform = new Transform( position, rotation, scale );
			if ( ModelLoader.TryGetCollisionMesh( resolvedModel, out var mesh )
				&& mesh.Vertices.Length >= 3 && mesh.Indices.Length >= 3 )
			{
				var surfaceIndex = group.GetSurfaceIndex( mesh.Surface );
				AppendTriangulatedMesh( group, mesh.Vertices, mesh.Indices, null, surfaceIndex, fullTransform );
				return true;
			}

			// Fallback when a model was created outside ModelLoader's triangulated collision path.
			var physics = resolvedModel.Physics;
			if ( physics is null ) return false;
			var placement = new Transform( position, rotation );
			var unitScale = MathF.Abs( scale - 1f ) < 0.0001f;
			var added = false;

			foreach ( var part in physics.Parts )
			{
				var bodyTransform = part.Transform;
				foreach ( var m in part.Meshes )
				{
					var verts = m.GetVertices();
					var indices = m.GetIndices();
					if ( verts.Length < 3 || indices.Length < 3 ) continue;
					for ( var i = 0; i < verts.Length; i++ )
						verts[i] = bodyTransform.PointToWorld( verts[i] );
					var defaultSurf = group.GetSurfaceIndex( m.Surface );
					AppendTriangulatedMesh( group, verts, indices, m.GetTriangleSurfaces(), defaultSurf, fullTransform );
					added = true;
				}
				foreach ( var hull in part.Hulls )
				{
					var pts = hull.GetPoints().ToArray();
					if ( pts.Length < 4 ) continue;
					for ( var i = 0; i < pts.Length; i++ )
						pts[i] = bodyTransform.PointToWorld( pts[i] );
					var body = group.GetFallbackHullBody( hull.Surface );
					body.AddHull( unitScale ? pts : ScalePoints( pts, scale ), placement );
					added = true;
				}
			}
			return added;
		}

		static void AppendTriangulatedMesh(
			MergedContentsGroup group,
			Vector3[] localVertices,
			int[] indices,
			Surface[] triangleSurfaces,
			byte defaultSurfaceIndex,
			Transform worldTransform )
		{
			var inverseChunk = 1f / CollisionChunkSize;
			var triCount = indices.Length / 3;
			for ( var t = 0; t < triCount; t++ )
			{
				var ia = indices[t * 3];
				var ib = indices[t * 3 + 1];
				var ic = indices[t * 3 + 2];
				if ( (uint)ia >= (uint)localVertices.Length
					|| (uint)ib >= (uint)localVertices.Length
					|| (uint)ic >= (uint)localVertices.Length ) continue;

				var a = worldTransform.PointToWorld( localVertices[ia] );
				var b = worldTransform.PointToWorld( localVertices[ib] );
				var c = worldTransform.PointToWorld( localVertices[ic] );
				var center = (a + b + c) * (1f / 3f);
				var chunk = group.GetChunk( new ChunkKey(
					(int)MathF.Floor( center.x * inverseChunk ),
					(int)MathF.Floor( center.y * inverseChunk ) ) );

				var baseVert = (uint)chunk.Vertices.Count;
				chunk.Vertices.Add( a );
				chunk.Vertices.Add( b );
				chunk.Vertices.Add( c );
				chunk.Indices.Add( baseVert );
				chunk.Indices.Add( baseVert + 1 );
				chunk.Indices.Add( baseVert + 2 );

				var surf = triangleSurfaces is not null && t < triangleSurfaces.Length
					? group.GetSurfaceIndex( triangleSurfaces[t] )
					: defaultSurfaceIndex;
				chunk.TriangleSurfaces.Add( surf );
			}
		}

		static Vector3[] ScalePoints( Vector3[] points, float scale )
		{
			var scaled = new Vector3[points.Length];
			for ( var i = 0; i < points.Length; i++ )
				scaled[i] = points[i] * scale;
			return scaled;
		}

		bool CreateFallbackResidentCollider( PropInstance instance, Model resolvedModel )
		{
			var prop = instance.Source;
			var data = resolvedModel.GetData<Titanfall2ModelCollisionData>();
			var hasContents = prop.SourceIndex >= 0 && data is not null;
			var contents = hasContents ? Titanfall2CollisionSource.StaticPropContents( data.ModelContents, prop.CollisionFlagsRemove ) : 0u;
			var collisionObject = new GameObject( owner.GameObject, owner.Active, System.IO.Path.GetFileNameWithoutExtension( prop.ModelPath ) );
			collisionObject.Flags |= GameObjectFlags.NotSaved;
			collisionObject.WorldPosition = prop.Position;
			collisionObject.WorldRotation = prop.Rotation;
			collisionObject.WorldScale = Vector3.One * MathF.Max( prop.Scale, 0.001f );
			collisionObject.IsStatic = true;
			if ( Titanfall2StreamingSettings.NavMeshStaticProps )
				collisionObject.Tags.Add( Titanfall2DeferredNavMeshBuilder.NavMeshBodyTag );
			var collider = collisionObject.AddComponent<ModelCollider>();
			collider.Model = resolvedModel;
			collider.Static = true;
			var source = collisionObject.AddComponent<Titanfall2CollisionSource>();
			source.MountIdent = owner.MountIdent;
			source.MapPath = owner.MapPath;
			source.ModelPath = prop.ModelPath;
			source.SourceIndex = prop.SourceIndex;
			source.HasInstanceMetadata = prop.SourceIndex >= 0;
			source.SolidType = prop.SolidType;
			source.ReservedCollisionData = prop.ReservedCollisionData;
			source.CollisionFlagsRemove = prop.CollisionFlagsRemove;
			source.HasEffectiveContents = hasContents;
			source.EffectiveContents = contents;
			if ( hasContents ) Titanfall2CollisionQueries.Classify( collisionObject, collider, contents );
			if ( ModelLoader.IsUsableModel( collider.Model ) && collider.Model.Physics is { Parts.Count: > 0 } )
			{
				source.CollisionDestroyed = OnResidentCollisionDestroyed;
				_groupedObjects.Add( collisionObject );
				MergedBodyCount++;
				Created++;
				return true;
			}
			collisionObject.Destroy();
			return false;
		}

		public void SetGroupedEnabled( bool enabled )
		{
			foreach ( var obj in _groupedObjects )
			{
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Enabled = enabled;
			}
		}

		public void SetEnabled( PropInstance instance, bool enabled )
		{
			if ( instance.CollisionObject.IsValid() ) instance.CollisionObject.Enabled = enabled;
		}

		public void Release( PropInstance instance )
		{
			if ( instance.CollisionObject.IsValid() && !instance.CollisionObject.IsDestroyed ) instance.CollisionObject.Destroy();
			instance.CollisionObject = null;
		}
	}
}
