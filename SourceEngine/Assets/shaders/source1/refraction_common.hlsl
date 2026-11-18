#define S_TRANSLUCENT 1
#define CUSTOM_MATERIAL_INPUTS
#include "common/shared.hlsl"

#ifndef S_SOURCE_SCREENSPACE
	#define S_SOURCE_SCREENSPACE 0
#endif

struct VertexInput
{
	#if S_SOURCE_SCREENSPACE
		float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	#else
	#include "common/vertexinput.hlsl"
	float4 vSourceColor : COLOR0 < Semantic( Color ); >;
	#endif
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vSourceColor : COLOR0;
	nointerpolation float flSourceAlpha : TEXCOORD15;
	nointerpolation float flSourceFrame : TEXCOORD16;
	nointerpolation float3 vSourceOriginWs : TEXCOORD17;
};
