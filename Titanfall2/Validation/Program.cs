using Sandbox;
using Sandbox.Mounting;
using Sandbox.Rendering;
using Sandbox.Engine;
using NativeEngine;
using System.Reflection;
using System.Text.Json;
using Titanfall2;

// Optional native lifecycle fixture. Run in its own process with FACEPUNCH_ENGINE
// pointing at the same installation used by SboxEngineRoot. Never deploys a DLL.
static class Program
{
	const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	static readonly List<object> Results = [];
	static string _reportDirectory;
	sealed record ConfigSnapshot( string Path, byte[] Bytes, DateTime LastWriteUtc );

	static ConfigSnapshot[] SaveConfig()
	{
		var engine = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ) ?? throw new InvalidOperationException( "Set FACEPUNCH_ENGINE." );
		return new[] { "core/cfg/video.txt", "cfg/video.txt", "config/video.json" }.Select( relative =>
		{
			var path = Path.Combine( engine, relative );
			return new ConfigSnapshot( path, File.Exists( path ) ? File.ReadAllBytes( path ) : null, File.GetLastWriteTimeUtc( path ) );
		} ).ToArray();
	}

	static void RestoreConfig( ConfigSnapshot[] snapshots )
	{
		foreach ( var snapshot in snapshots )
		{
			if ( snapshot.Bytes is null ) { if ( File.Exists( snapshot.Path ) ) File.Delete( snapshot.Path ); continue; }
			File.WriteAllBytes( snapshot.Path, snapshot.Bytes );
			File.SetLastWriteTimeUtc( snapshot.Path, snapshot.LastWriteUtc );
		}
	}

	sealed class InstalledGame( string directory ) : ISteamIntegration
	{
		public BaseFileSystem GetFileSystem( long id ) => id == 1237970 ? new LocalFileSystem( directory, true ) : null;
		public bool IsAppInstalled( long id ) => id == 1237970;
		public bool IsAppOwned( long id ) => id == 1237970;
		public bool IsDlcInstalled( long id ) => false;
	}

	static void Check( bool pass, string message )
	{
		if ( !pass ) throw new InvalidOperationException( message );
	}

	static Titanfall2StaticPropRenderResources Resources( Titanfall2StaticPropStreamer component ) =>
		(Titanfall2StaticPropRenderResources)typeof( Titanfall2StaticPropStreamer ).GetField( "_renderResources", PrivateInstance )!.GetValue( component )!;

	static void Main( string[] args )
	{
		var report = Path.GetFullPath( args[0] );
		_reportDirectory = Path.GetDirectoryName( report );
		var config = SaveConfig();
		try
		{
			var app = new TestAppSystem();
			app.Init();
			// TestAppSystem has no game package data directory. Supply one inside
			// this run's output so derived-cache behavior is exercised as in a game.
			var dataDirectory = Path.Combine( _reportDirectory, "data" );
			System.IO.Directory.CreateDirectory( dataDirectory );
			GlobalContext.Current.FileData = new LocalFileSystem( dataDirectory );
			CheckDeploymentRegistration();
			CheckMountTypesAfterGameLibraryReset();
			var assets = Path.Combine( Path.GetDirectoryName( typeof( Titanfall2Mount ).Assembly.Location )!, "Assets" );
			if ( System.IO.Directory.Exists( assets ) ) EngineFileSystem.AddAssetPath( "tf2_validation", assets );
			var module = typeof( Titanfall2Mount ).Assembly;
			foreach ( var other in AppDomain.CurrentDomain.GetAssemblies().Where( a => a != module && a.GetName().Name == module.GetName().Name ) )
				Game.TypeLibrary.RemoveAssembly( other );
			Game.TypeLibrary.AddAssembly( module, false );
			var worker = Titanfall2Workers.Run( () => 42 );
			Check( worker.Wait( TimeSpan.FromSeconds( 10 ) ) && worker.Result == 42, "CPU worker join required a main-thread continuation." );
			Results.Add( new { Test = "OfficialWorkerSynchronousJoin" } );
			CheckDataOnlyAwakeAndClone();
			CheckRenderOwnerLifecycle();
			CheckPublicSkyboxLifecycle();
			CheckOfficialSceneUnload();
			CheckRejectedLoadingTransaction();
			if ( Graphics.IsAvailable ) CheckCubemapLifecycle();
			if ( args.Length > 1 ) CheckMapReload( args[1], args.Length > 2 ? args[2] : "sp_boomtown_start" );
			if ( args.Length > 1 )
			{
				var cache = Titanfall2ProcessedCache.GetStatistics();
				Results.Add( new { Test = "DerivedCacheWrites", Snapshot = cache } );
				Check( cache.Writes > 0 && cache.WriteFailures == 0, "Derived cache did not write successfully." );
			}
			var commandType = typeof( CommandList );
			Results.Add( new
			{
				OfficialApi = typeof( Scene ).Assembly.Location,
				PlaybackPublic = commandType.GetMethod( "ExecuteOnRenderThread", PrivateInstance )?.IsPublic ?? false,
				RecorderPublic = commandType.GetMethod( "AddAction", PrivateInstance )?.IsPublic ?? false,
				ClutterBatchPublic = typeof( Scene ).Assembly.GetType( "Sandbox.Clutter.ClutterBatchSceneObject" )?.IsPublic ?? false
			} );
			File.WriteAllText( report, JsonSerializer.Serialize( new { success = true, mvid = module.ManifestModule.ModuleVersionId, Results }, new JsonSerializerOptions { WriteIndented = true } ) );
			Console.WriteLine( "STATIC_PROP_LIFECYCLE_PASS" );
			RestoreConfig( config );
			EngineGlobal.Plat_ExitProcess( 0 );
		}
		catch ( Exception exception )
		{
			File.WriteAllText( report, JsonSerializer.Serialize( new { success = false, error = exception.ToString(), Results }, new JsonSerializerOptions { WriteIndented = true } ) );
			Console.Error.WriteLine( exception );
			RestoreConfig( config );
			EngineGlobal.Plat_ExitProcess( 1 );
		}
	}

	static void CheckCubemapLifecycle()
	{
		var bytes = new byte[80 + 6 * 8];
		void U32( int offset, uint value ) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );
		void U16( int offset, ushort value ) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset ), value );
		U32( 0, 0x00465456 ); U32( 4, 7 ); U32( 8, 2 ); U32( 12, 80 );
		U16( 16, 4 ); U16( 18, 4 ); U32( 20, 0x4000 ); U16( 24, 1 ); U16( 26, ushort.MaxValue );
		U32( 52, 13 ); bytes[56] = 1;
		for ( var face = 0; face < 6; face++ ) { U16( 80 + face * 8, 0xF800 ); U16( 82 + face * 8, 0xF800 ); }
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			Check( VtfTextureDecoder.TryCreateCubeFrames( bytes, out var textures, out var error ), error );
			var controller = scene.CreateObject().AddComponent<Titanfall2CubemapController>();
			controller.ConfigureCustom( textures, [Vector3.Zero], BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 256 ) ), bytes );
			Check( controller.RuntimeTextureCount == 1 && controller.RuntimeProbeCount == 1, "Initial cubemap preparation failed." );
			Check( !controller.GameObject.Serialize().ToJsonString().Contains( "titanfall2_cubemap_000" ), "Runtime probe helper was serialized." );
			controller.Enabled = false;
			Check( controller.RuntimeTextureCount == 0 && controller.RuntimeProbeCount == 0, "Disabled cubemap retained GPU resources." );
			controller.Enabled = true;
			Check( controller.RuntimeTextureCount == 1 && controller.RuntimeProbeCount == 1, "Re-enabled cubemap did not restore source data." );
			var clone = controller.GameObject.Clone().GetComponent<Titanfall2CubemapController>();
			for ( var i = 0; i < 60 && clone.RuntimeTextureCount == 0; i++ )
			{
				scene.GameTick( 1.0 / 60 ); EngineLoop.RunAsyncTasks();
			}
			Check( clone.RuntimeTextureCount == 1 && clone.RuntimeProbeCount == 1, "Cloned cubemap did not own exactly one helper." );
			var field = typeof( Titanfall2CubemapController ).GetField( "_ownedTextures", PrivateInstance );
			Check( !ReferenceEquals( ((List<Texture>)field.GetValue( controller ))[0], ((List<Texture>)field.GetValue( clone ))[0] ), "Cubemap clones shared a disposable texture." );
			controller.GameObject.DestroyImmediate();
			Check( clone.RuntimeTextureCount == 1 && clone.RuntimeProbeCount == 1, "Destroying one cubemap instance affected its clone." );
			Results.Add( new { Test = "CubemapDisableRestoreAndClone" } );
		}
		finally { scene.Destroy(); }
	}

	static void CheckMountTypesAfterGameLibraryReset()
	{
		var assembly = Sandbox.Mounting.Directory.Get( "titanfall2" ).GetType().Assembly;
		Game.InitTypeLibrary();
		var names = new[] { "Titanfall2WorldMaterialTableController", "Titanfall2HeightFog", "Titanfall2StaticPropStreamer", "Titanfall2SkyboxDrawSettings" };
		var missing = names.Where( name => Game.TypeLibrary.GetType( assembly.GetType( name, true ) ) is null ).ToArray();
		Results.Add( new { Test = "MountTypesAfterGameLibraryReset", Missing = missing } );
		Check( missing.Length == 0, "Game type-library reset lost mount components: " + string.Join( ", ", missing ) );
	}

	static void CheckDeploymentRegistration()
	{
		try { _ = Sandbox.Mounting.Directory.GetAll(); }
		catch ( NullReferenceException ) { Sandbox.Mounting.Directory.LoadSources(); }
		var mount = Sandbox.Mounting.Directory.Get( "titanfall2" );
		Check( mount is not null, "The engine did not discover the deployed Titanfall 2 mount." );
		var expected = typeof( Titanfall2Mount ).Assembly.ManifestModule.ModuleVersionId;
		var actual = mount.GetType().Assembly.ManifestModule.ModuleVersionId;
		Check( actual == expected, "The engine discovered a different Titanfall 2 assembly." );
		Results.Add( new { Test = "DeployedMountDiscovery", mount.Ident, mount.IsInstalled, mount.IsMounted, ActualMvid = actual } );
	}

	static void CheckDataOnlyAwakeAndClone()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var template = scene.CreateObject().AddComponent<Titanfall2StaticPropStreamer>();
			var payload = Titanfall2StaticPropSceneData.Compile( [new() { ModelPath = "models/test.mdl" }], null, null, null );
			template.ConfigureCompiled( "titanfall2", "test", Vector3.Zero, payload, Color.White );
			template.EncodedLocalLights = Convert.ToBase64String( new byte[80] );
			typeof( Titanfall2StaticPropStreamer ).GetMethod( "InitializeCells", PrivateInstance )!.Invoke( template, null );
			var owner = Resources( template );
			Check( !owner.IsPrepared && owner.Lights is null, "Data-only restoration allocated render resources." );
			Results.Add( new { Test = "DataOnlyAwake", Snapshot = template.GetCompiledSceneStatistics() } );

			var empty = scene.CreateObject().AddComponent<Titanfall2StaticPropStreamer>();
			empty.KeepAllCollisions = true;
			empty.ConfigureCompiled( "titanfall2", "empty", Vector3.Zero, Titanfall2StaticPropSceneData.Compile( [], null, null, null ), Color.White );
			var clone = empty.GameObject.Clone().GetComponent<Titanfall2StaticPropStreamer>();
			Check( clone.PreparationComplete && clone.ResidentCollisionsReady && clone.ResourcesPrepared, "Empty clone lost readiness semantics." );
			Check( !ReferenceEquals( Resources( empty ), Resources( clone ) ), "Scene clone shared render ownership." );
			empty.GameObject.Destroy();
			Check( clone.ResourcesPrepared, "Destroying template affected clone." );
			Results.Add( new { Test = "IndependentClone", Snapshot = clone.GetCompiledSceneStatistics() } );
		}
		finally { scene.Destroy(); }
	}

	static void CheckRenderOwnerLifecycle()
	{
		var encoded = Convert.ToBase64String( new byte[80] );
		using var first = new Titanfall2StaticPropRenderResources();
		using var second = new Titanfall2StaticPropRenderResources();
		first.Prepare( encoded );
		second.Prepare( encoded );
		var scene = new Scene();
		try
		{
			var renderObject = new SceneObject( scene.SceneWorld, Model.Cube );
			first.Track( renderObject );
			Check( first.NativeObjectCount == 1 && renderObject.IsValid(), "Native render ownership was not registered." );
			first.Dispose();
			Check( first.NativeObjectCount == 0 && !renderObject.IsValid(), "Render owner did not delete its native object." );
		}
		finally { scene.Destroy(); }
		// A second pair covers repeated preparation independently of the native-object teardown.
		using var prepared = new Titanfall2StaticPropRenderResources();
		prepared.Prepare( encoded );
		Check( prepared.IsPrepared && second.IsPrepared, "Render preparation did not finish." );
		if ( Graphics.IsAvailable )
		{
			Check( prepared.Lights is not null && second.Lights is not null && !ReferenceEquals( prepared.Lights, second.Lights ), "Scenes shared local-light buffers." );
			var lights = prepared.Lights;
			prepared.Prepare( encoded );
			Check( ReferenceEquals( lights, prepared.Lights ), "Repeated preparation uploaded lights twice." );
		}
		else Check( prepared.Lights is null && second.Lights is null, "Headless preparation allocated light buffers." );
		first.Dispose();
		first.Dispose();
		Check( !first.IsPrepared && first.Lights is null && second.IsPrepared, "Render teardown leaked or affected another scene." );
		try { first.Prepare( encoded ); throw new InvalidOperationException( "Disposed owner accepted preparation." ); }
		catch ( ObjectDisposedException ) { }
		Results.Add( new { Test = "RenderOwnerLifetime", Graphics.IsAvailable } );
	}

	static void CheckMapReload( string gameDirectory, string map )
	{
		using var host = new MountHost( new Configuration { SteamIntegration = new InstalledGame( gameDirectory ) } );
		typeof( Sandbox.Mounting.Directory ).GetField( "_system", BindingFlags.Static | BindingFlags.NonPublic )!.SetValue( null, host );
		// The host writes mount thumbnails into the project filesystem. Game files
		// supplied by InstalledGame remain read-only; only this fixture output is writable.
		host.Initialize( typeof( Titanfall2Mount ), new LocalFileSystem( Path.GetDirectoryName( typeof( Titanfall2Mount ).Assembly.Location )! ) );
		host.Mount( "titanfall2" ).GetAwaiter().GetResult();
		var mount = (Titanfall2Mount)host.GetSource( "titanfall2" );
		Check( mount is not null && mount.IsInstalled && mount.IsMounted, "Fixture mount did not initialize/mount the installed game." );
		var loader = mount.GetByPath( $"mount://titanfall2/maps/{map}.bsp.world.scene" );
		Check( loader is not null, "Mounted map scene was not indexed." );
		var file = (SceneFile)loader.GetOrCreate().GetAwaiter().GetResult();
		Results.Add( new { Test = "MapImportStatistics", Snapshot = ((MapLoader)loader).LastBuildStatistics } );
		int? instances = null, collisions = null;
		for ( var cycle = 0; cycle < 2; ++cycle )
		{
			var scene = new Scene();
			Titanfall2StaticPropRenderResources owner = null;
			try
			{
				using var scope = scene.Push();
				var options = new SceneLoadOptions { ShowLoadingScreen = false };
				options.SetScene( file );
				Check( scene.Load( options ), "Map scene load failed." );
				var props = scene.GetComponentsInChildren<Titanfall2StaticPropStreamer>().Single();
				owner = Resources( props );
				var loadTimer = System.Diagnostics.Stopwatch.StartNew();
				while ( !props.PreparationComplete && loadTimer.Elapsed.TotalMinutes < 10 )
				{
					EngineGlobal.SourceEnginePanelAppFrame();
					scene.GameTick( 1.0 / 60 );
					EngineLoop.RunAsyncTasks();
					EngineLoop.DrainFrameEndDisposables();
				}
				Results.Add( new { Test = "MapLoadingOutcome", cycle, Snapshot = props.GetCompiledSceneStatistics() } );
				Check( props.ResourcesPrepared && props.ResidentCollisionsReady, "Loaded map is not ready." );
				var tables = scene.GetAllComponents<Titanfall2WorldMaterialTableController>().ToArray();
				Check( tables.Length > 0 && tables.All( table => typeof( Titanfall2WorldMaterialTableController ).GetField( "_buffer", PrivateInstance ).GetValue( table ) is not null ),
					"World material table did not restore its GPU buffer." );
				Check( owner.NativeObjectCount > 0, "Loaded scene has no native prop objects." );
				Check( instances is null || instances == owner.NativeObjectCount, "Reload changed native prop object count." );
				Check( collisions is null || collisions == props.ResidentCollisionCount, "Reload changed collision count." );
				instances = owner.NativeObjectCount;
				collisions = props.ResidentCollisionCount;
				props.Enabled = false;
				Check( !props.Enabled, "Component did not disable." );
				props.Enabled = true;
				Check( props.Enabled, "Component did not reenable." );
				var camera = scene.CreateObject().AddComponent<CameraComponent>();
				camera.CustomSize = new( 640, 360 );
				camera.ZFar = 50000;
				camera.WorldPosition = map == "sp_boomtown_start" ? new( 12027.547f, -8760.884f, 9795.224f ) : new( 2284, -1766, 210 );
				camera.WorldRotation = map == "sp_boomtown_start" ? new Angles( -16.276112f, -487.74136f, 0 ) : new Angles( -3, 76, 0 );
				using var target = Texture.CreateRenderTarget().WithSize( 640, 360 ).Create( "static-prop-lifecycle" );
				for ( var frame = 0; frame < 4; ++frame )
				{
					EngineGlobal.SourceEnginePanelAppFrame();
					scene.GameTick( 1.0 / 60 );
					scene.PreRender();
					Check( camera.RenderToTexture( target, new ViewSetup { EnablePostprocessing = true, EnableUI = false } ), "Map render failed." );
					g_pRenderDevice.Present( IntPtr.Zero );
					g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
					EngineLoop.DrainFrameEndDisposables();
					RenderTarget.EndOfFrame();
					EngineLoop.RunAsyncTasks();
				}
				Results.Add( new { Test = "MapLoadRenderReload", cycle, Snapshot = props.GetCompiledSceneStatistics() } );
				using var bitmap = target.GetBitmap();
				var screenshot = Path.Combine( _reportDirectory, $"{map}-cycle-{cycle}.png" );
				File.WriteAllBytes( screenshot, bitmap.ToPng() );
				Results.Add( new { Test = "MapScreenshot", cycle, screenshot, WorldMaterialTables = tables.Length } );
			}
			finally { scene.Destroy(); }
			Check( owner is not null && !owner.IsPrepared && owner.Lights is null && owner.NativeObjectCount == 0,
				"Scene destruction retained render resources." );
		}
	}

	sealed class UnloadLoader : SceneLoader<Titanfall2Mount>
	{
		internal int Builds, Releases;
		protected override void BuildScene() { Builds++; new GameObject( true, "Unload fixture" ); }
		protected override void OnSceneUnloaded() => Releases++;
	}

	static void CheckOfficialSceneUnload()
	{
		var loader = new UnloadLoader();
		loader.InitializeInternal( ResourceType.Scene, "maps/official-lifetime-fixture.scene", new Titanfall2Mount() );
		var first = (SceneFile)loader.GetOrCreate().GetAwaiter().GetResult();
		Check( first.IsValid && loader.Builds == 1 && loader.Releases == 0, "Temporary build scene released its registered resources." );
		first.DestroyInternal();
		Check( loader.Releases == 1, "Official scene unload hook was not called." );
		var second = (SceneFile)loader.GetOrCreate().GetAwaiter().GetResult();
		Check( second.IsValid && !ReferenceEquals( first, second ) && loader.Builds == 2, "Destroyed registered scene was reused." );
		loader.ShutdownInternal();
		Check( !second.IsValid && loader.Releases == 2, "Shutdown did not release the rebuilt scene." );
		Results.Add( new { Test = "OfficialSceneUnloadAndRebuild", loader.Builds, loader.Releases } );
	}

	static void CheckPublicSkyboxLifecycle()
	{
		var content = new Scene { Enabled = false };
		SceneFile file;
		try
		{
			using var scope = content.Push();
			content.CreateObject().AddComponent<ModelRenderer>().Model = Model.Cube;
			file = new SceneFile { GameObjects = content.Children.Select( obj => obj.Serialize() ).ToArray() };
		}
		finally { content.Destroy(); }
		var scene = new Scene();
		Scene skyScene = null;
		try
		{
			using var scope = scene.Push();
			var skybox = scene.CreateObject().AddComponent<Skybox3D>();
			skybox.SkyboxScene = file;
			scene.GameTick( 1.0 / 60 );
			skyScene = (Scene)typeof( Skybox3D ).GetField( "skyboxScene", PrivateInstance )!.GetValue( skybox );
			Check( skyScene.IsValid(), "Public Skybox3D did not create an isolated scene from the generated SceneFile." );
			Check( skyScene.GetAllComponents<ModelRenderer>().Count() == 1, "Skybox lost the serialized vista renderer." );
		}
		finally { scene.Destroy(); }
		Check( !skyScene.IsValid(), "Destroying the host scene retained its public skybox scene." );
		Results.Add( new { Test = "PublicSkyboxLifecycle" } );
	}

	static void CheckRejectedLoadingTransaction()
	{
		// Non-rendering scenes deliberately do not read or allocate the render light table.
		if ( !Graphics.IsAvailable ) return;
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var props = scene.CreateObject().AddComponent<Titanfall2StaticPropStreamer>();
			props.KeepAllCollisions = true;
			props.ConfigureCompiled( "titanfall2", "invalid-lights", Vector3.Zero,
				Titanfall2StaticPropSceneData.Compile( [new() { ModelPath = "models/test.mdl" }], null, null, null ), Color.White );
			props.EncodedLocalLights = "invalid!";
			var method = typeof( Titanfall2StaticPropStreamer ).GetMethod( "OnLoad", PrivateInstance, [typeof( LoadingContext )] )!;
			try
			{
				((Task)method.Invoke( props, [new LoadingContext()] )!).GetAwaiter().GetResult();
				throw new InvalidOperationException( "Invalid light data did not fail the loading transaction." );
			}
			catch ( FormatException ) { }
			var owner = Resources( props );
			Check( !props.ResourcesPrepared && !props.ResidentCollisionsReady && props.ResidentCollisionCount == 0,
				"Failed loading transaction published readiness." );
			Check( !owner.IsPrepared && owner.Lights is null && owner.NativeObjectCount == 0,
				"Failed loading transaction retained render resources." );
			Results.Add( new { Test = "RejectedLoadingTransaction", Snapshot = props.GetCompiledSceneStatistics() } );
		}
		finally { scene.Destroy(); }
	}
}

