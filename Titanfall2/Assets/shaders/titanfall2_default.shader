// T2_MATERIAL_DECLARATIONS: ordinary and GPU-table paths share the same shading functions.

HEADER
{
	Description = "Titanfall 2 default PBR shader";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_T2_PROP_AGGREGATE, 0..1, "Static prop aggregate material and probe table" );
	Feature( F_T2_FAST_NORMAL, 0..1, "Equivalent tangent-frame normalization" );
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	StaticCombo( S_T2_PROP_AGGREGATE, F_T2_PROP_AGGREGATE, Sys( ALL ) );
	#define D_T2_MATERIAL_TABLE S_T2_PROP_AGGREGATE
#if S_T2_PROP_AGGREGATE
	#define T2_MATERIAL_BUFFER_ATTRIBUTE "T2PropMaterials"
#endif
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 0
	#endif
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 0
	#endif
	
	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
	uint nT2InstanceIndex : SV_InstanceID;
#if S_T2_PROP_AGGREGATE
	float2 vT2PropIds : TEXCOORD9;
#endif
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vProbeTint : COLOR1;
	nointerpolation uint nT2ProbeInstance : TEXCOORD12;
	nointerpolation uint nT2MaterialId : TEXCOORD13;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vVertexColor *= float4( SrgbGammaToLinear( v.vColor.rgb ), v.vColor.a );
		i.vPositionOs = v.vPositionOs.xyz;
#if S_T2_PROP_AGGREGATE
		i.vProbeTint = float4( 1, 1, 1, 1 );
		i.nT2MaterialId = (uint)(v.vT2PropIds.x + 0.5);
		i.nT2ProbeInstance = (uint)(v.vT2PropIds.y + 0.5);
#else
		i.vProbeTint = GetExtraPerInstanceShaderData( v.nInstanceTransformID ).vTint;
		i.nT2ProbeInstance = v.nT2InstanceIndex;
		i.nT2MaterialId = 0;
#endif
		VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentUOs_flTangentVSign );
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_T2_FAST_NORMAL, F_T2_FAST_NORMAL, Sys( ALL ) );
	DynamicCombo( D_T2_SIMPLE_SURFACE, 0..1, Sys( ALL ) );
	#include "common/pixel.hlsl"
	#include "titanfall2_gpu_material_table.hlsl"

	CreateInputTexture2D( Albedo, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( Normal, Linear, 8, "None", "_norm", ",0/,0/0", Default4( 0.50, 0.50, 1.00, 1.00 ) );
	CreateInputTexture2D( Gloss, Linear, 8, "None", "_gloss", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 1.00 ) );
	CreateInputTexture2D( Specular, Linear, 8, "None", "_specular", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 1.00 ) );
	CreateInputTexture2D( AO, Linear, 8, "None", "_ao", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( Opacity, Linear, 8, "None", "_opacity", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( Emissive, Srgb, 8, "None", "_emissive", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 1.00 ) );

#if !D_T2_MATERIAL_TABLE
Texture2D g_tAlbedo < Channel( RGBA, Box( Albedo ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
#endif

#if !D_T2_MATERIAL_TABLE
Texture2D g_tNormal < Channel( RGBA, Box( Normal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif

#if !D_T2_MATERIAL_TABLE
Texture2D g_tGloss < Channel( RGBA, Box( Gloss ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif

#if !D_T2_MATERIAL_TABLE
Texture2D g_tSpecular < Channel( RGBA, Box( Specular ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif

#if !D_T2_MATERIAL_TABLE
Texture2D g_tAO < Channel( RGBA, Box( AO ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif

#if !D_T2_MATERIAL_TABLE
Texture2D g_tOpacity < Channel( RGBA, Box( Opacity ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif

#if !D_T2_MATERIAL_TABLE
Texture2D g_tEmissive < Channel( RGBA, Box( Emissive ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
#endif
	// Optional Titanfall 2 MATL layers. Kept inline because mount shader include roots are isolated.
	CreateInputTexture2D( Detail, Linear, 8, "None", "_detail", ",0/,0/0", Default4( 0.50, 0.50, 0.50, 1.00 ) );
	CreateInputTexture2D( Distortion, Linear, 8, "None", "_distortion", ",0/,0/0", Default4( 0.50, 0.50, 0.00, 1.00 ) );

#if !D_T2_MATERIAL_TABLE
Texture2D g_tDetail < Channel( RGBA, Box( Detail ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif

#if !D_T2_MATERIAL_TABLE
Texture2D g_tDistortion < Channel( RGBA, Box( Distortion ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif
	CreateInputTextureCube( Environment, Srgb, 8, "None", "_cube", "Reflection", Default4( 0, 0, 0, 1 ) );
	CreateTextureCube( g_tEnvironment ) < Channel( RGBA, Box( Environment ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;

#if !D_T2_MATERIAL_TABLE
float g_flT2GlossScale < Default( 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float4 g_vT2SpecularTint < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float g_flT2EmissiveStrength < Default( 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float g_flT2DetailBlend < Default( 0.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float4 g_vT2UvDistortion < Default4( 0.0, 0.0, 0.0, 0.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float g_flT2HasEnvironment < Default( 0.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float g_flT2EnvironmentIntensity < Default( 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float g_flT2FresnelStrength < Default( 0.0 ); >;
#endif
	#include "titanfall2_optional_surface.hlsl"
	float T2Roughness( float gloss ) { return 1.0 - saturate( gloss * max( 0.0, g_flT2GlossScale ) ); }
	float T2Metalness( float3 specular ) { float3 tinted = specular * g_vT2SpecularTint.rgb; return saturate( max( tinted.r, max( tinted.g, tinted.b ) ) * g_vT2SpecularTint.a ); }
	float3 T2Environment( float3 worldPosition, float3 worldNormal, float roughness )
	{
		if ( g_flT2HasEnvironment < 0.5 ) return float3( 0.0, 0.0, 0.0 );
		float3 viewDirection = normalize( g_vCameraPositionWs - worldPosition );
		float3 reflectionDirection = reflect( -viewDirection, normalize( worldNormal ) );
		float fresnel = pow( 1.0 - saturate( dot( normalize( worldNormal ), viewDirection ) ), 5.0 );
		float strength = g_flT2EnvironmentIntensity * lerp( 0.04, 1.0, max( fresnel, saturate( g_flT2FresnelStrength ) ) );
		return g_tEnvironment.SampleLevel( g_sAniso, reflectionDirection, saturate( roughness ) * 5.0 ).rgb * strength;
	}

#if !D_T2_MATERIAL_TABLE
float4 g_vT2Uv1RotScale < Default4( 1.0, 0.0, 0.0, 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float4 g_vT2Uv1Translate < Default4( 0.0, 0.0, 0.0, 0.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float4 g_vT2AlbedoTint < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float4 g_vT2EmissiveTint < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float g_flT2MaterialOpacity < Default( 1.0 ); >;
#endif

#if !D_T2_MATERIAL_TABLE
float g_flT2AlphaTestReference < Default( 0.5 ); >;
#endif
	float2 T2Uv1( float2 uv )
	{
		float2 transformed = float2( dot( uv, g_vT2Uv1RotScale.xy ), dot( uv, g_vT2Uv1RotScale.zw ) );
		return transformed + g_vT2Uv1Translate.xy * lerp( 1.0, g_flTime, saturate( g_vT2Uv1Translate.z ) );
	}

#if !D_T2_MATERIAL_TABLE
float g_flUseVertexColor < Default( 0 ); >;
#endif
	#define T2_HAS_PROBE_INDEX
	#include "titanfall2_surface.hlsl"

	float4 MainPs( PixelInput i ) : SV_Target0
	{
#if D_T2_MATERIAL_TABLE
		g_nT2GpuMaterialId = i.nT2MaterialId;
#endif
		float2 uv = T2ApplyDistortion( T2Uv1( i.vTextureCoords.xy ) );
		if ( DepthNormals::WantsDepthNormals() )
		{
			float gloss;
			float3 normal;
			T2ReadDepthSurface( i, uv, gloss, normal );
			Material depth = Material::Init();
			depth.Normal = normal;
			depth.Roughness = T2Roughness( gloss );
			depth.Opacity = 1.0;
			depth.TextureCoords = uv;
			return T2DepthNormals( i, depth );
		}
		Material m = Material::Init();
		m.Albedo = float3( 1, 1, 1 );
		// Keep initialization finite; the decoded normal is assigned before lighting.
		m.Normal = normalize( i.vNormalWs );
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = float3( 0, 0, 0 );
		m.Transmission = 0;

		float4 albedo; float gloss; float3 specular; float3 surfaceNormal;
		T2ReadSurface( i, uv, albedo, gloss, specular, surfaceNormal );
		float2 rawUv = i.vTextureCoords.xy;
#if D_T2_MATERIAL_TABLE
		float ao = 1.0;
		[branch] if ( (g_nT2TextureMask & 4) != 0 )
			ao = Tex2DS( g_tAO, g_sAniso, T2SurfaceUv( rawUv, uv, g_vT2AuxUvChannels.z ) ).r;
#else
		float ao = Tex2DS( g_tAO, g_sAniso, T2SurfaceUv( rawUv, uv, g_vT2AuxUvChannels.z ) ).r;
#endif
		float3 emissive = T2ReadEmissive( rawUv, uv );

		m.Normal = surfaceNormal;
		m.Albedo = T2ApplyDetail( albedo.xyz, uv, g_flT2SpecularWorkflow ) * g_vT2AlbedoTint.rgb * lerp( float3( 1, 1, 1 ), i.vVertexColor.rgb, g_flUseVertexColor ) * i.vProbeTint.rgb;
		m.Roughness = T2Roughness( gloss );
		m.Metalness = T2Metalness( specular );
		m.AmbientOcclusion = ao;
		m.Opacity = 1.0;
		m.Emission = emissive.xyz * g_vT2EmissiveTint.rgb * g_flT2EmissiveStrength * i.vProbeTint.rgb;
		m.Emission += T2Environment( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz, m.Normal, m.Roughness );

		m.TextureCoords = uv;

		float4 shade = T2ShadeSurface( i, m, specular * g_vT2SpecularTint.rgb );
		return shade;
	}
}
