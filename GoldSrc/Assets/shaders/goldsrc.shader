
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
	
	Feature( F_CHROME, 0..1, "Chrome" );
	Feature( F_FULLBRIGHT, 0..1, "Fullbright" );
	Feature( F_ADDITIVE, 0..1, "Additive" );
	Feature( F_STUDIO_FADE, 0..1, "Translucent" );
}

MODES
{
    Forward();
    Depth();
}

COMMON
{
	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vChromeBone : TEXCOORD4 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	StaticCombo( S_CHROME, F_CHROME, Sys( ALL ) );
	#include "common/vertex.hlsl"
	float2 g_vChromeUvScale < Default2( 0.5, 0.5 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		#if S_CHROME
			// Studio chrome is projected per normal, using the camera and its animated bone pivot.
			float3x4 bone = GetTransformMatrix( v.nInstanceTransformID, (uint)v.vChromeBone.w );
			float3 pivot = mul( bone, float4( v.vChromeBone.xyz, 1 ) );
			float3 view = pivot - g_vCameraPositionWs;
			view = dot( view, view ) > 1e-8 ? normalize( view ) : g_vCameraDirWs;
			float3 right = cross( g_vCameraDirWs, g_vCameraUpDirWs );
			float3 up = cross( view, right );
			up = dot( up, up ) > 1e-8 ? normalize( up ) : normalize( cross( view, g_vCameraUpDirWs ) );
			right = normalize( cross( view, up ) );

			float3 bindNormal;
			float4 tangent;
			VS_DecodeObjectSpaceNormalAndTangent( v, bindNormal, tangent );
			float sign;
			float3 normal = TransformNormalInverseTranspose( bindNormal, (float3x3)bone, sign );
			i.vTextureCoords.xy = (float2( dot( normal, right ), dot( normal, up ) ) + 1) * g_vChromeUvScale;
		#endif
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	
	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	
	StaticCombo( S_FULLBRIGHT, F_FULLBRIGHT, Sys( ALL ) )
	StaticCombo( S_ADDITIVE, F_ADDITIVE, Sys( ALL ) )
	StaticCombo( S_STUDIO_FADE, F_STUDIO_FADE, Sys( ALL ) )
	float g_flStudioOpacity < Attribute( "StudioOpacity" ); Default( 1 ); >;

	#if S_ADDITIVE
		BoolAttribute( translucent, true );
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, ONE );
		RenderState( DstBlend, ONE );
		RenderState( BlendOp, ADD );
		RenderState( DepthWriteEnable, false );
	#elif S_STUDIO_FADE
		BoolAttribute( translucent, true );
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
		RenderState( DepthWriteEnable, false );
	#endif

	// Keep the pixel shader in depth/shadow passes so masked textures clip there too
	BoolAttribute( alphatest, true );
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 color = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );
		clip( color.a - 0.5 );

		// Match the GoldSrc texture and display gamma used for studio models.
		m.Albedo = SrgbGammaToLinear( pow( SrgbLinearToGamma( color.rgb ), 0.8 ) );
		m.Normal = normalize( i.vNormalWs );
		m.TextureCoords = i.vTextureCoords.xy;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = S_STUDIO_FADE ? g_flStudioOpacity : 1;
		m.Emission = 0;
		m.Transmission = 0;

		if ( S_FULLBRIGHT || S_ADDITIVE )
		{
			m.Emission = m.Albedo;
			m.Albedo = 0;
		}

		return ShadingModelStandard::Shade( i, m );
	}
}
