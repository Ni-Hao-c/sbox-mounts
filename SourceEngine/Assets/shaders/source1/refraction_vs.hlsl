#if !S_SOURCE_SCREENSPACE
	#include "common/vertex.hlsl"
#endif
#include "source1/alpha.hlsl"
#include "source1/frames.hlsl"

PixelInput MainVs( VertexInput v )
{
	#if S_SOURCE_SCREENSPACE
	PixelInput i = (PixelInput)0;
	i.vPositionPs = float4( v.vPositionOs.xy, 0, 1 );
	i.vTextureCoords.xy = v.vPositionOs.xy * float2( 0.5, -0.5 ) + 0.5;
	i.vSourceColor = 1;
	i.vNormalWs = float3( 0, 0, 1 );
	i.vTangentUWs = float3( 1, 0, 0 );
	i.vTangentVWs = float3( 0, 1, 0 );
	i.flSourceAlpha = SourceAlpha();
	i.flSourceFrame = SourceFrame();
	return i;
	#else
	PixelInput i = ProcessVertex( v );
	i.vSourceColor = v.vSourceColor;
	i.flSourceAlpha = SourceAlpha();
	i.flSourceFrame = SourceFrame();
	i.vSourceOriginWs = mul( GetTransformMatrix( v.nInstanceTransformID ), float4( 0, 0, 0, 1 ) ).xyz;
	return FinalizeVertex( i );
	#endif
}
