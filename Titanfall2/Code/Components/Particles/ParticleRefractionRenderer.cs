using Sandbox.Rendering;
using Titanfall2;

/// <summary>Refract particles displace scene color; their normal map is never drawn as sprite color.</summary>
[Library, Hide]
public sealed class Titanfall2ParticleRefractionRenderer : ParticleRenderer
{
	public Texture NormalMap { get; private set; }
	public Texture OpacityMap { get; private set; }
	public float RefractAmount { get; private set; }
	public float BlurAmount { get; private set; }
	public int DrawSerial { get; private set; }
	public int RenderedParticles { get; private set; }
	readonly object _sync = new();
	readonly List<(Vector3 Position, Vector3 Size, Angles Angles, float Alpha)> _particles = [];
	readonly VertexBuffer _vertices = new();
	SceneCustomObject _object;
	Material _material;
	int _orientation;
	float _materialAlpha, _depthFeather;

	internal bool Configure( Titanfall2Mount mount, Titanfall2ParticleDefinition definition, Titanfall2VmtDefinition material )
	{
		NormalMap = Read( material.GetTexture( "$normalmap", "$bumpmap" ) );
		var opacity = material.GetTexture( "$opacitymap", "$opacitytexture", "$alphamask" );
		OpacityMap = string.IsNullOrWhiteSpace( opacity ) ? Texture.White : Read( opacity );
		if ( NormalMap is null || OpacityMap is null ) return false;
		RefractAmount = Math.Clamp( material.GetFloat( "$refractamount", 1 ), 0, 64 );
		BlurAmount = Math.Clamp( material.GetFloat( "$bluramount", 0 ), 0, 16 );
		_materialAlpha = Math.Clamp( material.GetFloat( "$alpha", 1 ), 0, 1 );
		_orientation = definition.OrientationType; _depthFeather = MathF.Max( 0, definition.DepthFeather );
		_material = Material.FromShader( "shaders/titanfall2_particle_refraction.shader" );
		EnsureObject(); return _material is not null && _object.IsValid();
		Texture Read( string name )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) return null;
			var texture = Texture.Load( $"mount://{mount.Ident}/{Titanfall2LegacyPath.NormalizeTextureName( name )}.vtex", false );
			return texture is { IsValid: true, IsError: false } ? texture : null;
		}
	}
	void EnsureObject()
	{
		if ( _object.IsValid() || _material is null || Scene?.SceneWorld is null ) return;
		_object = new SceneCustomObject( Scene.SceneWorld ) { RenderOverride = Render, RenderingEnabled = false };
		_object.Flags.IsOpaque = false; _object.Flags.IsTranslucent = true; _object.Flags.CastShadows = false;
		_object.Flags.WantsFrameBufferCopy = true;
	}
	protected override void OnPreRender()
	{
		GetComponent<Titanfall2ParticleGeometry>()?.RefreshControlPointLocks(); EnsureObject();
		if ( !_object.IsValid() || !ParticleEffect.IsValid() ) return;
		RenderOptions.Apply( _object );
		_object.Tags.SetFrom( GameObject.Tags );
		_object.Flags.WantsFrameBufferCopy = true;
		lock ( _sync )
		{
			_particles.Clear();
			var min = new Vector3( float.MaxValue ); var max = new Vector3( float.MinValue );
			foreach ( var p in ParticleEffect.Particles )
			{
				if ( !p.Position.IsFinite || !p.Size.IsFinite ) continue;
				var alpha = Math.Clamp( p.Alpha * p.Color.a * _materialAlpha, 0, 1 );
				if ( alpha <= 0 ) continue;
				var size = p.Size * MathF.Abs( WorldScale.x );
				_particles.Add( (p.Position, size, p.Angles, alpha) );
				var radius = MathF.Max( MathF.Abs( size.x ), MathF.Abs( size.y ) ) * 1.5f;
				min = Vector3.Min( min, p.Position - radius ); max = Vector3.Max( max, p.Position + radius );
			}
			_object.RenderingEnabled = _particles.Count > 0;
			RenderedParticles = _particles.Count;
			if ( _particles.Count > 0 ) _object.Bounds = new BBox( min, max );
		}
	}
	void Render( SceneObject sceneObject )
	{
		// Both world and GameOverlay color passes are Translucent. The opaque
		// refraction-stencil pass has no color target and this shader has no
		// depth mode. Never grab or persist a framebuffer texture from it.
		if ( Graphics.LayerType != SceneLayerType.Translucent ) return;
		lock ( _sync )
		{
			if ( _particles.Count == 0 ) return;
			_vertices.Init( false );
			foreach ( var particle in _particles.OrderByDescending( x => x.Position.DistanceSquared( Graphics.CameraPosition ) ) )
			{
				var right = Graphics.CameraRotation.Right; var up = Graphics.CameraRotation.Up;
				if ( _orientation is 2 or 3 ) { var rotation = particle.Angles.ToRotation(); right = rotation.Left; up = rotation.Up; }
				else if ( _orientation == 1 ) { up = Vector3.Up; right = Vector3.Cross( Graphics.CameraRotation.Forward.WithZ( 0 ).Normal, up ); }
				if ( _orientation is 0 or 1 )
				{
					var angle = -particle.Angles.roll * MathF.PI / 180; var original = right;
					right = original * MathF.Cos( angle ) + up * MathF.Sin( angle ); up = up * MathF.Cos( angle ) - original * MathF.Sin( angle );
				}
				Vertex Corner( float x, float y ) => new() { Position = particle.Position + right * ((x - 0.5f) * 2 * particle.Size.x) + up * ((0.5f - y) * 2 * particle.Size.y),
					Normal = right, Tangent = new Vector4( up.x, up.y, up.z, 0 ), TexCoord0 = new Vector4( x, y, particle.Alpha, 0 ), Color = Color.White };
				var a = Corner( 0, 0 ); var b = Corner( 1, 0 ); var c = Corner( 1, 1 ); var d = Corner( 0, 1 );
				_vertices.Add( a ); _vertices.Add( b ); _vertices.Add( c ); _vertices.Add( a ); _vertices.Add( c ); _vertices.Add( d );
			}
			var attributes = sceneObject.Attributes;
			// WantsFrameBufferCopy supplies a fresh copy in the active translucent
			// layer. Keep that per-view binding out of persistent object attributes.
			attributes.Set( "T2RefractNormal", NormalMap ); attributes.Set( "T2RefractOpacity", OpacityMap );
			attributes.Set( "T2RefractAmount", RefractAmount ); attributes.Set( "T2RefractBlur", BlurAmount ); attributes.Set( "T2RefractDepthFeather", _depthFeather );
			_vertices.Draw( _material, attributes ); RenderedParticles = _particles.Count; DrawSerial++;
		}
	}
	protected override void OnDisabled()
	{
		base.OnDisabled(); lock ( _sync ) { _object?.Delete(); _object = null; _particles.Clear(); RenderedParticles = 0; }
	}
}
