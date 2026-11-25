HEADER
{
	Description = "Titanfall 2 particle normal/opacity scene-color refraction";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
}
COMMON
{
	#define S_TRANSLUCENT 1
	#define S_RENDER_BACKFACES 1
	#include "common/shared.hlsl"
}
struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float3 Right : NORMAL < Semantic( None ); >;
	float4 Up : TANGENT < Semantic( None ); >;
	float4 UvAlpha : TEXCOORD0 < Semantic( None ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 Basis : TEXCOORD14;
};
VS
{
	PixelInput MainVs( VertexInput v )
	{
		PixelInput o = (PixelInput)0;
		o.vPositionPs = Position3WsToPs( v.Position ); o.vPositionWs = v.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		o.vTextureCoords.xy = v.UvAlpha.xy; o.vVertexColor = float4( 1, 1, 1, v.UvAlpha.z );
		o.Basis = float4( Vector3WsToVs( v.Right ).xy, Vector3WsToVs( v.Up.xyz ).xy );
		return o;
	}
}
PS
{
	#include "common/pixel.hlsl"
	Texture2D NormalMap < Attribute( "T2RefractNormal" ); SrgbRead( false ); >;
	Texture2D OpacityMap < Attribute( "T2RefractOpacity" ); SrgbRead( false ); >;
	Texture2D SceneColor < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;
	float4 CopyRectangle < Attribute( "FrameBufferCopyRectangle" ); Default4( 0, 0, 1, 1 ); >;
	float Amount < Attribute( "T2RefractAmount" ); Default( 1 ); >;
	float Blur < Attribute( "T2RefractBlur" ); Default( 0 ); >;
	float DepthFeather < Attribute( "T2RefractDepthFeather" ); Default( 0 ); >;
	SamplerState ClampSampler < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	RenderState( BlendEnable, true ); RenderState( SrcBlend, ONE ); RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthWriteEnable, false ); RenderState( CullMode, NONE );
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float coverage = saturate( OpacityMap.Sample( ClampSampler, i.vTextureCoords.xy ).r * i.vVertexColor.a );
		if ( DepthFeather > 0 )
		{
			float depth = dot( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs, g_vCameraDirWs );
			coverage *= saturate( (Depth::GetLinear( i.vPositionSs.xy ) - depth) / DepthFeather );
		}
		clip( coverage - 0.0001 );
		float2 normal = NormalMap.Sample( ClampSampler, i.vTextureCoords.xy ).rg * 2 - 1;
		if ( all( abs( normal ) <= (1.0 / 255.0 + 0.000001) ) ) normal = 0;
		// The mount adapter expresses amount/blur in render pixels. Keep data
		// separate from RGB artwork and fade displacement with the authored mask.
		float2 offset = (i.Basis.xy * normal.x + i.Basis.zw * normal.y) * float2( 1, -1 ) * Amount * coverage;
		if ( all( abs( offset ) < 0.000001 ) && Blur <= 0 ) return float4( 0, 0, 0, 0 );
		float2 uv = CalculateViewportUv( i.vPositionSs.xy );
		float2 halfTexel = g_vInvViewportSize * 0.5;
		float2 displaced = clamp( uv + offset * g_vInvViewportSize, halfTexel, 1 - halfTexel );
		float2 blur = g_vInvViewportSize * Blur * coverage;
		float3 color = SceneColor.SampleLevel( ClampSampler, displaced * CopyRectangle.zw, 0 ).rgb;
		if ( Blur > 0 )
		{
			color += SceneColor.SampleLevel( ClampSampler, clamp( displaced + float2( blur.x, 0 ), halfTexel, 1 - halfTexel ) * CopyRectangle.zw, 0 ).rgb;
			color += SceneColor.SampleLevel( ClampSampler, clamp( displaced - float2( blur.x, 0 ), halfTexel, 1 - halfTexel ) * CopyRectangle.zw, 0 ).rgb;
			color += SceneColor.SampleLevel( ClampSampler, clamp( displaced + float2( 0, blur.y ), halfTexel, 1 - halfTexel ) * CopyRectangle.zw, 0 ).rgb;
			color += SceneColor.SampleLevel( ClampSampler, clamp( displaced - float2( 0, blur.y ), halfTexel, 1 - halfTexel ) * CopyRectangle.zw, 0 ).rgb;
			color *= 0.2;
		}
		return float4( color * coverage, coverage );
	}
}
