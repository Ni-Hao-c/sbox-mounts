using System.Collections.Concurrent;
using Sandbox.Rendering;

/// <summary>
/// Hosts Titanfall 2 vista models in the engine's native 3D skybox world. The
/// public Skybox3D component owns the native bridge and isolated-scene lifetime.
/// </summary>
[Library, Hide]
public sealed class Titanfall2Skybox3D : Component, Component.DontExecuteOnServer
{
	static readonly Sandbox.Diagnostics.Logger Log = new( "Titanfall2Skybox" );
	const int DataMagic = 0x324B5354; // TSK2
	const int DataVersion = 1;
	const int MaximumModelCount = 1024;

	[Property, Hide]
	public string MountIdent { get; set; }

	[Property, Hide]
	public string MapPath { get; set; }

	[Property, Hide]
	public string CameraName { get; set; }

	[Property, Hide]
	public Vector3 CameraOrigin { get; set; }

	[Property, Hide]
	public Angles CameraAngles { get; set; }

	[Property, Hide]
	public float SkyScale { get; set; } = 1000f;

	[Property, Hide] public Vector4 FogDensity { get; set; }
	[Property, Hide] public Vector4 FogBaseColor { get; set; }
	[Property, Hide] public Vector4 FogDirectionalColor { get; set; }
	[Property, Hide] public Vector4 FogDirection { get; set; }
	[Property, Hide] public Vector4 FogHeight { get; set; }
	[Property, Hide] public bool UseWorldFog { get; set; }
	Titanfall2HeightFog _worldFog;
	RealTimeSince _worldFogSearch = 10f;

	[Property, Hide]
	public string EncodedModels { get; set; }

	readonly List<Model> _retainedModels = new();
	GameObject _skyboxObject;
	SceneFile _skyboxFile;
	string _fogKey;
	internal static readonly ConcurrentDictionary<string, Titanfall2FogParameters> FogBySky = new();

	internal void Configure(
		string mountIdent,
		string mapPath,
		string cameraName,
		Vector3 cameraOrigin,
		Angles cameraAngles,
		float skyScale,
		Titanfall2SkyboxFog fog,
		IReadOnlyList<Titanfall2SkyboxModel> models )
	{
		MountIdent = mountIdent;
		MapPath = mapPath;
		CameraName = cameraName;
		CameraOrigin = cameraOrigin;
		CameraAngles = cameraAngles;
		SkyScale = Math.Clamp( skyScale, 1f, 100000f );
		FogDensity = fog.Parameters.Density;
		FogBaseColor = fog.Parameters.Color;
		FogDirectionalColor = fog.Parameters.DirectionalColor;
		FogDirection = fog.Parameters.Direction;
		FogHeight = fog.Parameters.Height;
		UseWorldFog = fog.UseWorldFog;
		EncodedModels = Encode( models );
	}

	protected override void OnStart()
	{
		CreateSkybox();
	}

	protected override void OnEnabled() => CreateSkybox();

	protected override void OnDisabled() => DestroySkybox();

	protected override void OnDestroy()
	{
		DestroySkybox();
	}

	protected override void OnPreRender()
	{
		if ( _skyboxObject is null ) return;
		// SetData allocates a dynamic constant buffer. Refresh it even when the
		// camera is stationary; a buffer from scene creation is not frame history.
		var fog = new Titanfall2FogParameters( FogDensity, FogBaseColor, FogDirectionalColor, FogDirection, FogHeight );
		if ( UseWorldFog )
		{
			// A map whose fog does not cover the origin has no Titanfall2HeightFog at all
			// (CreateEnvironment), so an unthrottled search walks the whole scene every frame.
			if ( (!_worldFog.IsValid() || !_worldFog.Active) && _worldFogSearch > 1f )
			{
				_worldFogSearch = 0;
				_worldFog = Scene.GetComponentsInChildren<Titanfall2HeightFog>().FirstOrDefault( f => f.Active );
			}
			if ( _worldFog.IsValid() && _worldFog.Active )
			{
				_worldFog.SelectForView();
				fog = _worldFog.Parameters;
			}
		}
		FogBySky[_fogKey] = fog;
		// The native bridge uses the final render view for each camera, including
		// transient weapon effects. Keep its authored origin fixed: cancelling a
		// different camera snapshot here introduces shaking and removes height parallax.
	}

	void CreateSkybox()
	{
		if ( _skyboxObject is not null || string.IsNullOrWhiteSpace( EncodedModels ) ) return;
		var parentWorld = Scene?.SceneWorld;
		if ( parentWorld is null || !parentWorld.IsValid() ) return;

		try
		{
			var definitions = Decode( EncodedModels ).ToArray();
			if ( definitions.Length == 0 ) return;

			// Load every selected vista before attaching its world to the main scene.
			var loadedModels = new List<(Titanfall2SkyboxModel Definition, Model Model)>( definitions.Length );
			var failures = 0;
			foreach ( var definition in definitions )
			{
				var resourcePath = $"mount://{MountIdent}/{NormalizeModelPath( definition.ModelPath )}.vmdl";
				var model = Model.Load( resourcePath );
				if ( !ModelLoader.IsUsableModel( model ) )
				{
					failures++;
					Log.Warning( $"Unable to load Titanfall 2 skybox vista model '{resourcePath}'." );
					continue;
				}

				loadedModels.Add( (definition, model) );
			}

			if ( loadedModels.Count == 0 )
			{
				Log.Warning( $"Titanfall 2 3D skybox has no loadable vista models ({MapPath})." );
				return;
			}

			var scale = float.IsFinite( SkyScale ) ? Math.Clamp( SkyScale, 1f, 100000f ) : 1000f;
			_fogKey = Guid.NewGuid().ToString( "N" );
			FogBySky[_fogKey] = new( FogDensity, FogBaseColor, FogDirectionalColor, FogDirection, FogHeight );
			// Public Skybox3D owns the isolated scene and its native bridge.
			var content = new Scene();
			try
			{
				using ( content.Push() )
				{
					foreach ( var (definition, model) in loadedModels )
					{
						_retainedModels.Add( model );
						var obj = new GameObject( true, definition.ModelPath )
						{
							WorldTransform = new Transform( definition.Position, definition.Rotation.ToRotation(), MathF.Max( definition.Scale, 0.001f ) ),
							IsStatic = true
						};
						obj.AddComponent<ModelRenderer>().Model = model;
						obj.AddComponent<Titanfall2SkyboxDrawSettings>().FogKey = _fogKey;
					}
					var light = new GameObject( true, "Sky light" ).AddComponent<DirectionalLight>();
					light.WorldRotation = Rotation.From( 50f, -35f, 0f );
					light.LightColor = new Color( 1f, 0.95f, 0.86f );
					light.Shadows = false;
				}
				_skyboxFile = new SceneFile { GameObjects = content.Children.Select( obj => obj.Serialize() ).ToArray() };
			}
			finally { content.Destroy(); }
			_skyboxObject = new GameObject( true, "Titanfall 2 vista" )
			{
				Parent = GameObject,
				WorldPosition = -CameraOrigin * scale,
				WorldScale = new Vector3( scale )
			};
			_skyboxObject.Flags |= GameObjectFlags.NotSaved;
			var skybox = _skyboxObject.AddComponent<Skybox3D>( false );
			skybox.SkyboxScene = _skyboxFile;
			skybox.Enabled = true;
			Log.Info( $"Titanfall 2 3D skybox ready: {loadedModels.Count}/{definitions.Length} vistas using official Skybox3D ({MapPath})." );
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Unable to create Titanfall 2 3D skybox for '{MapPath}'." );
			DestroySkybox();
		}
	}

	void DestroySkybox()
	{
		_skyboxObject?.Destroy();
		_skyboxObject = null;
		_skyboxFile = null;
		if ( _fogKey is not null ) FogBySky.TryRemove( _fogKey, out _ );
		_fogKey = null;
		_retainedModels.Clear();
	}

	static string Encode( IReadOnlyList<Titanfall2SkyboxModel> source )
	{
		var models = source?.Where( static model => !string.IsNullOrWhiteSpace( model.ModelPath ) ).ToArray()
			?? Array.Empty<Titanfall2SkyboxModel>();
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, System.Text.Encoding.UTF8, leaveOpen: true ) )
		{
			writer.Write( DataMagic );
			writer.Write( DataVersion );
			writer.Write( models.Length );
			foreach ( var model in models )
			{
				writer.Write( NormalizeModelPath( model.ModelPath ) );
				writer.Write( model.Position.x );
				writer.Write( model.Position.y );
				writer.Write( model.Position.z );
				writer.Write( model.Rotation.pitch );
				writer.Write( model.Rotation.yaw );
				writer.Write( model.Rotation.roll );
				writer.Write( model.Scale );
			}
		}
		return Convert.ToBase64String( stream.GetBuffer(), 0, checked((int)stream.Length) );
	}

	static IEnumerable<Titanfall2SkyboxModel> Decode( string encoded )
	{
		using var stream = new MemoryStream( Convert.FromBase64String( encoded ), writable: false );
		using var reader = new BinaryReader( stream, System.Text.Encoding.UTF8, leaveOpen: false );
		if ( reader.ReadInt32() != DataMagic ) throw new InvalidDataException( "Skybox data has an invalid signature." );
		if ( reader.ReadInt32() != DataVersion ) throw new InvalidDataException( "Skybox data has an unsupported version." );
		var count = reader.ReadInt32();
		if ( count < 0 || count > MaximumModelCount ) throw new InvalidDataException( $"Invalid skybox model count {count}." );

		for ( var index = 0; index < count; index++ )
		{
			yield return new Titanfall2SkyboxModel(
				reader.ReadString(),
				new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
				new Angles( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
				reader.ReadSingle() );
		}
	}

	static string NormalizeModelPath( string path )
	{
		var normalized = path.Replace( '\\', '/' ).TrimStart( '/' );
		return normalized.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) ? normalized[..^5] : normalized;
	}
}

readonly record struct Titanfall2SkyboxModel( string ModelPath, Vector3 Position, Angles Rotation, float Scale );

/// <summary>sky_camera fog for the isolated skybox world; <see cref="UseWorldFog"/> follows the map's height fog instead.</summary>
readonly record struct Titanfall2SkyboxFog( Titanfall2FogParameters Parameters, bool UseWorldFog )
{
	public static Titanfall2SkyboxFog Disabled => default;
}

/// <summary>Draw attributes initialized inside the sky scene owned by the official component.</summary>
[Library, Hide]
public sealed class Titanfall2SkyboxDrawSettings : Component
{
	[Property, Hide] public string FogKey { get; set; }
	protected override void OnPreRender()
	{
		var renderer = GetComponent<ModelRenderer>();
		if ( !renderer.IsValid() || !renderer.SceneObject.IsValid() ) return;
		renderer.SceneObject.Attributes.Set( "T2SkyProjectionNear", 0.001f );
		if ( Titanfall2Skybox3D.FogBySky.TryGetValue( FogKey, out var fog ) ) fog.Apply( renderer.SceneObject.Attributes );
	}
}
