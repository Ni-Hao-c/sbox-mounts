HEADER
{
	Description = "Source 1 refraction and water";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_SOURCE_WATER, 0..1, "Source 1" );
	Feature( F_SOURCE_ALPHA_SINE, 0..1, "Source 1" );
	Feature( F_SOURCE_PROXIES, 0..1, "Source 1" );
	Feature( F_SOURCE_TEXTURE_ANIMATION, 0..1, "Source 1" );
}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	nointerpolation float flSourceAlpha : TEXCOORD15;
	nointerpolation float flSourceFrame : TEXCOORD16;
};

VS
{
	#include "common/vertex.hlsl"
	#include "source1/alpha.hlsl"
	#include "source1/frames.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.flSourceAlpha = SourceAlpha();
		i.flSourceFrame = SourceFrame();
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_SOURCE_WATER, F_SOURCE_WATER, Sys( ALL ) );
	DynamicCombo( D_SKYBOX, 0..1, Sys( PC ) );
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#include "common/pixel.hlsl"
	#include "source1/uv.hlsl"
	#include "source1/frames.hlsl"
	#include "common/classes/Depth.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	// The expensive water pass (or unblended cheap pass) writes the surface depth in GMod.
	RenderState( DepthWriteEnable, S_SOURCE_WATER != 0 );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sNormal < Attribute( "g_tNormalSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sNormal2 < Attribute( "g_tNormal2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSourceTint < Attribute( "g_tSourceTintSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSourceCube < Attribute( "g_tSourceEnvCubeSampler" ); Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
	SamplerState g_sSourceClamp < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
	Texture2D g_tSourceScene < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;
	Texture2D g_tSourceWaterReflection < Attribute( "SourceWaterReflection" ); SrgbRead( false ); >;
	Texture2D g_tSourceWaterRefraction < Attribute( "SourceWaterRefraction" ); SrgbRead( false ); >;
	Texture2D g_tSourceWaterRefractionDepth < Attribute( "SourceWaterRefractionDepth" ); SrgbRead( false ); >;
	bool g_bSourceWaterHasReflection < Attribute( "SourceWaterHasReflection" ); Default( 0 ); >;
	bool g_bSourceWaterHasRefraction < Attribute( "SourceWaterHasRefraction" ); Default( 0 ); >;
	float4 g_vSourceWaterPlane < Attribute( "SourceWaterPlane" ); Default4( 0, 0, 1, 0 ); >;
	float4 g_vSourceWaterReflectionViewport < Attribute( "SourceWaterReflectionViewport" ); Default4( 0, 0, 1, 1 ); >;
	float4 g_vSourceWaterRefractionViewport < Attribute( "SourceWaterRefractionViewport" ); Default4( 0, 0, 1, 1 ); >;
	float4 g_vSourceWaterRefractionDepthUvScaleBias < Attribute( "SourceWaterRefractionDepthUvScaleBias" ); Default4( 1, 1, 0, 0 ); >;
	float2 g_vSourceWaterRefractionDepthRange < Attribute( "SourceWaterRefractionDepthRange" ); Default2( 0, 1 ); >;
	float4x4 g_matSourceWaterRefractionProjectionToView < Attribute( "SourceWaterRefractionProjectionToView" ); >;

	CreateInputTexture2D( SourceNormal, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1.0, 1.0 ) );
	CreateInputTexture2D( SourceNormal2, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1.0, 1.0 ) );
	CreateInputTexture2D( SourceTint, Srgb, 8, "None", "_color", "Source 1", Default4( 1.0, 1.0, 1.0, 1.0 ) );
	CreateInputTextureCube( SourceEnvCube, Srgb, 8, "None", "_cube", "Source 1", Default4( 0, 0, 0, 1 ) );
	#if S_SOURCE_TEXTURE_ANIMATION
		Texture2DArray g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	#else
		Texture2D g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	#endif
	Texture2D g_tNormal2 < Channel( RGBA, Box( SourceNormal2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tSourceTint < Channel( RGBA, Box( SourceTint ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	#if S_SOURCE_WATER
		// GMod's water shaders sample envmaps without an sRGB read, including cheap water.
		CreateTextureCube( g_tSourceEnvCube ) < Channel( RGBA, Box( SourceEnvCube ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;
	#else
		CreateTextureCube( g_tSourceEnvCube ) < Channel( RGBA, Box( SourceEnvCube ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
	#endif

	float3 g_vSourceNormalTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceNormalTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceNormalScroll < Default3( 0, 0, 1 ); >;
	float4 g_vSourceNormalLayerScroll < Default4( 0, 0, 0, 0 ); >;
	float3 g_vSourceRefractTint < Default3( 1, 1, 1 ); >;
	float3 g_vSourceReflectTint < Default3( 1, 1, 1 ); >;
	float3 g_vSourceCheapReflectTint < Default3( 1, 1, 1 ); >;
	float2 g_vSourceCheapWaterDistances < Default2( 500, 1000 ); >;
	float3 g_vSourceFogColor < Default3( 0, 0, 0 ); >;
	float g_flSourceRefractAmount < Default( 0.02 ); >;
	float g_flSourceReflectAmount < Default( 1 ); >;
	float g_flSourceFogStart < Default( 0 ); >;
	float g_flSourceFogEnd < Default( 1000 ); >;
	float g_flSourceBlur < Default( 0 ); >;
	float g_flSourceHasNormal2 < Default( 0 ); >;
	float g_flSourceHasTint < Default( 0 ); >;
	float g_flSourceHasEnvCube < Default( 0 ); >;
	float g_flSourceSceneReflection < Default( 0 ); >;
	float g_flSourceEnvAlphaMask < Default( 0 ); >;
	float g_flSourceFresnel < Default( 1 ); >;
	float g_flSourceAboveWater < Default( 1 ); >;
	float g_flSourceCheapWater < Default( 0 ); >;
	float g_flSourceWaterBlend < Default( 1 ); >;
	float g_flSourceBlurRefract < Default( 0 ); >;
	float g_flSourceWaterMultiTexture < Default( 0 ); >;

	bool SourceCheapWater()
	{
		#if D_SKYBOX
			return true;
		#else
			return g_flSourceCheapWater > 0.5 || (!g_bSourceWaterHasReflection && !g_bSourceWaterHasRefraction);
		#endif
	}

	float SourceCheapWaterFade( float distanceToEye )
	{
		// water.cpp uploads reciprocal range and start/range separately. A zero
		// range (gm_fork authors 0,0) produces NaN in watercheap_ps2x; D3D9's
		// saturate maps that to zero. Keep Fresnel instead of forcing full reflection.
		float range = g_vSourceCheapWaterDistances.y - g_vSourceCheapWaterDistances.x;
		if ( range == 0 ) return 0;
		return saturate( (distanceToEye - g_vSourceCheapWaterDistances.x) / range );
	}

	float SourceCheapWaterFresnel( float3 eye, float3 normal )
	{
		if ( g_flSourceFresnel < 0.5 ) return g_flSourceWaterBlend;
		// The cheap shader uses the unnormalized normal and multiplies x^5.
		// pow() is undefined for negative x when a decoded normal exceeds unit length.
		float value = 1 - max( 0, dot( eye, normal ) );
		float square = value * value;
		return square * square * value;
	}

	float4 SourceWaterNormal( float2 uv, float2 rawUv, float frame )
	{
		float4 normal = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv, frame ) );
		if ( g_flSourceWaterMultiTexture > 0.5 )
		{
			float2 rotatedUv = float2( rawUv.x + rawUv.y, rawUv.y - rawUv.x ) * 0.1;
			float2 swappedUv = rawUv.yx * 0.45;
			normal = 0.33 * (normal
				+ g_tNormal.Sample( g_sNormal, SourceFrameUv( rotatedUv + g_vSourceNormalLayerScroll.xy * g_flTime, frame ) )
				+ g_tNormal.Sample( g_sNormal, SourceFrameUv( swappedUv + g_vSourceNormalLayerScroll.zw * g_flTime, frame ) ));
		}
		return normal;
	}

	float3 SourceRefractionFog( float3 color, float depth )
	{
		// GMod's above-water refraction view renders receivers with height fog
		// (common_ps_fxc::BlendPixelFog) before water_ps2x applies surface fog.
		// Our capture is unfogged, so reconstruct that first blend from its depth.
		return g_flSourceAboveWater > 0.5 && g_bSourceWaterHasRefraction
			? lerp( color, g_vSourceFogColor, depth ) : color;
	}

	float3 SourceSampleScene( float2 position )
	{
		#if S_SOURCE_WATER
			if ( g_bSourceWaterHasRefraction )
			{
				float2 captureUv = g_vSourceWaterRefractionViewport.xy + saturate( CalculateViewportUv( position ) ) * g_vSourceWaterRefractionViewport.zw;
				return g_tSourceWaterRefraction.SampleLevel( g_sSourceClamp, captureUv, 0 ).rgb;
			}
		#endif
		float2 uv = saturate( CalculateViewportUv( position ) ) * g_vFrameBufferCopyInvSizeAndUvScale.zw;
		return g_tSourceScene.SampleLevel( g_sSourceClamp, uv, 0 ).rgb;
	}

	bool SourceRefractionPositionAtPixel( int2 pixel, uint2 size, out float3 viewPosition )
	{
		viewPosition = 0;
		pixel = clamp( pixel, int2( 0, 0 ), int2( size ) - 1 );
		float depth = saturate( (g_tSourceWaterRefractionDepth.Load( int3( pixel, 0 ) ).r - g_vSourceWaterRefractionDepthRange.x)
			/ max( g_vSourceWaterRefractionDepthRange.y - g_vSourceWaterRefractionDepthRange.x, 0.000001 ) );
		if ( depth <= 0 ) return false;
		float2 uv = ((float2( pixel ) + 0.5) / float2( size ) - g_vSourceWaterRefractionViewport.xy) / g_vSourceWaterRefractionViewport.zw;
		float4 point = mul( float4( uv * float2( 2, -2 ) + float2( -1, 1 ), depth, 1 ), g_matSourceWaterRefractionProjectionToView );
		if ( point.w <= 0.000001 ) return false;
		viewPosition = point.xyz / point.w;
		return true;
	}

	bool SourceRefractionPosition( float2 position, out float3 viewPosition )
	{
		float2 colorUv = g_vSourceWaterRefractionViewport.xy + saturate( CalculateViewportUv( position ) ) * g_vSourceWaterRefractionViewport.zw;
		float2 uv = colorUv * g_vSourceWaterRefractionDepthUvScaleBias.xy + g_vSourceWaterRefractionDepthUvScaleBias.zw;
		uint width, height;
		g_tSourceWaterRefractionDepth.GetDimensions( width, height );
		return SourceRefractionPositionAtPixel( int2( uv * float2( width, height ) ), uint2( width, height ), viewPosition );
	}

	float SourceWaterFogAtPixel( int2 pixel, uint2 size )
	{
		float3 point;
		if ( !SourceRefractionPositionAtPixel( pixel, size, point ) ) return 1;
		float eyeDistance = dot( g_vCameraPositionWs.xyz, g_vSourceWaterPlane.xyz ) - g_vSourceWaterPlane.w;
		float receiverDistance = eyeDistance + dot( point, Vector3WsToVs( g_vSourceWaterPlane.xyz ) );
		float height = eyeDistance - receiverDistance;
		if ( abs( height ) < 0.0001 ) return 0;
		float fraction = saturate( -receiverDistance / height );
		return saturate( fraction * -point.z / max( g_flSourceFogEnd - g_flSourceFogStart, 0.001 ) );
	}

	float SourceSceneDepth( float2 position )
	{
		#if S_SOURCE_WATER
			if ( g_bSourceWaterHasRefraction )
			{
				float3 point;
				return SourceRefractionPosition( position, point ) ? -point.z : 1e20;
			}
		#endif
		position = clamp( position, 0, 1 / g_vInvViewportSize - 1 );
		#if D_SKYBOX
			return Depth::Linearize( 0, position );
		#else
			return Depth::Linearize( Depth::GetNormalized( position ), position );
		#endif
	}

	float SourceWaterDepth( float2 position, float waterHeight )
	{
		if ( g_flSourceAboveWater < 0.5 ) return 1;
		if ( g_bSourceWaterHasRefraction )
		{
			// GMod stores fog in refraction alpha and bilinearly filters it alongside
			// color. Filter reconstructed fog, not nonlinear hardware depth, to keep
			// clear texels and shallow receivers from making a false shoreline.
			float2 colorUv = g_vSourceWaterRefractionViewport.xy + saturate( CalculateViewportUv( position ) ) * g_vSourceWaterRefractionViewport.zw;
			float2 uv = colorUv * g_vSourceWaterRefractionDepthUvScaleBias.xy + g_vSourceWaterRefractionDepthUvScaleBias.zw;
			uint width, height;
			g_tSourceWaterRefractionDepth.GetDimensions( width, height );
			uint2 size = uint2( width, height );
			float2 texel = uv * float2( size ) - 0.5;
			int2 pixel = int2( floor( texel ) );
			float2 blend = frac( texel );
			return lerp( lerp( SourceWaterFogAtPixel( pixel, size ), SourceWaterFogAtPixel( pixel + int2( 1, 0 ), size ), blend.x ),
				lerp( SourceWaterFogAtPixel( pixel + int2( 0, 1 ), size ), SourceWaterFogAtPixel( pixel + int2( 1, 1 ), size ), blend.x ), blend.y );
		}
		#if D_SKYBOX
			return 1;
		#else
			if ( g_flSourceAboveWater < 0.5 ) return 1;
			position = clamp( position, 0, 1 / g_vInvViewportSize - 1 );
			if ( Depth::GetNormalized( position ) <= 0 ) return 1;
			float3 receiver = Depth::GetWorldPosition( position );
			float height = g_vCameraPositionWs.z - receiver.z;
			if ( abs( height ) < 0.0001 ) return 0;
			float fraction = saturate( (waterHeight - receiver.z) / height );
			return saturate( fraction * SourceSceneDepth( position ) / max( g_flSourceFogEnd - g_flSourceFogStart, 0.001 ) );
		#endif
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = SourceUv( i.vTextureCoords.xy, g_vSourceNormalTransformU, g_vSourceNormalTransformV, g_vSourceNormalScroll, g_vSourceNormalProxyU, g_vSourceNormalProxyV );
		#if S_SOURCE_WATER
			if ( SourceCheapWater() )
				uv = i.vTextureCoords.xy;
		#endif
		#if S_SOURCE_WATER
			float4 normalSample = SourceWaterNormal( uv, i.vTextureCoords.xy, i.flSourceFrame );
			float3 normalTs = normalSample.xyz * 2 - 1;
		#else
			float4 normalSample = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv + g_vSourceNormalLayerScroll.xy * g_flTime, i.flSourceFrame ) );
			float3 normalTs = normalize( normalSample.xyz * 2 - 1 );
		#endif
		if ( g_flSourceHasNormal2 > 0.5 )
		{
			float3 second = g_tNormal2.Sample( g_sNormal2, uv + g_vSourceNormalLayerScroll.zw * g_flTime ).xyz * 2 - 1;
			normalTs = normalize( float3( normalTs.xy + second.xy, normalTs.z * second.z ) );
		}

		#if S_SOURCE_WATER
			if ( SourceCheapWater() )
			{
				// watercheap_ps2x: fog/cubemap water. Never read the main-view
				// framebuffer or depth from a 3D skybox; its camera and units differ.
				float3 position = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
				float3 eye = normalize( g_vCameraPositionWs.xyz - position );
				float3 normal = normalTs.x * i.vTangentUWs + normalTs.y * i.vTangentVWs + normalTs.z * i.vNormalWs;
				float fresnel = SourceCheapWaterFresnel( eye, normal );
				if ( g_flSourceCheapWater < 0.5 )
					fresnel = saturate( fresnel + SourceCheapWaterFade( length( g_vCameraPositionWs.xyz - position ) ) );
				float3 direction = 2 * dot( normal, eye ) * normal - dot( normal, normal ) * eye;
				float3 reflection = 0;
				if ( g_flSourceHasEnvCube > 0.5 )
					reflection = g_tSourceEnvCube.Sample( g_sSourceCube, direction ).rgb;
				else if ( g_flSourceSceneReflection > 0.5 )
					reflection = EnvMap::From( position, i.vPositionSs, normalize( normal ), float2( 0.02, 0.02 ) );
				float3 color = lerp( g_vSourceFogColor, reflection * g_vSourceCheapReflectTint, fresnel );
				if ( g_bFogEnabled && g_flSourceAboveWater > 0.5 ) color = Fog::Apply( position, i.vPositionSs.xy, color );
				return float4( color, 1 );
			}
		#endif

		float alpha = saturate( i.flSourceAlpha );
		float3 positionWs = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 perturbation = Vector3WsToVs( normalTs.x * i.vTangentUWs + normalTs.y * i.vTangentVWs );
		float2 offset = float2( perturbation.x, -perturbation.y ) * g_flSourceRefractAmount * alpha;
		float unwarpedWaterDepth = 1;
		#if S_SOURCE_WATER
			unwarpedWaterDepth = SourceWaterDepth( i.vPositionSs.xy, positionWs.z );
			offset = normalTs.xy * normalSample.a * g_flSourceRefractAmount * alpha;
			if ( g_flSourceBlurRefract < 0.5 )
				offset *= unwarpedWaterDepth;
			alpha *= saturate( g_flSourceWaterBlend );
		#endif
		float2 distortedPosition = i.vPositionSs.xy + offset / g_vInvViewportSize;
		float surfaceDepth = Depth::Linearize( Depth::Normalize( i.vPositionSs.z ), i.vPositionSs.xy );
		float sceneDepth = SourceSceneDepth( distortedPosition );
		if ( sceneDepth < surfaceDepth )
		{
			distortedPosition = i.vPositionSs.xy;
			sceneDepth = SourceSceneDepth( distortedPosition );
		}

		float3 scene = SourceSampleScene( distortedPosition );
		#if S_SOURCE_WATER
			float waterDepth = SourceWaterDepth( distortedPosition, positionWs.z );
			scene = SourceRefractionFog( scene, waterDepth );
			if ( g_flSourceBlurRefract > 0.5 )
			{
				scene = 0;
				waterDepth = 0;
				for ( int x = -2; x <= 2; x++ )
					for ( int y = -2; y <= 2; y++ )
					{
						float2 samplePosition = distortedPosition + float2( x, y ) * 0.005 / g_vInvViewportSize;
						float sampleDepth = SourceWaterDepth( samplePosition, positionWs.z );
						scene += SourceRefractionFog( SourceSampleScene( samplePosition ), sampleDepth );
						waterDepth += sampleDepth;
					}
				scene /= 25;
				waterDepth /= 25;
			}
		#endif
		if ( g_flSourceBlur > 0 )
		{
			float blur = min( g_flSourceBlur, 8 );
			scene = (scene * 2
				+ SourceSampleScene( distortedPosition + float2( blur, 0 ) )
				+ SourceSampleScene( distortedPosition - float2( blur, 0 ) )
				+ SourceSampleScene( distortedPosition + float2( 0, blur ) )
				+ SourceSampleScene( distortedPosition - float2( 0, blur ) )) / 6;
		}
		float3 tint = g_vSourceRefractTint;
		if ( g_flSourceHasTint > 0.5 )
			tint *= g_tSourceTint.Sample( g_sSourceTint, uv ).rgb;
		float3 color = scene * tint;
		#if !S_SOURCE_WATER
			if ( g_bFogEnabled )
				color += Fog::Apply( positionWs, i.vPositionSs.xy, float3( 0, 0, 0 ) ) * (1 - tint);
		#endif

		#if S_SOURCE_WATER
			float fog = g_flSourceAboveWater > 0.5 ? saturate( waterDepth - 0.05 )
				: saturate( (surfaceDepth - g_flSourceFogStart) / max( g_flSourceFogEnd - g_flSourceFogStart, 0.001 ) );
			color = lerp( color, g_vSourceFogColor, fog );
		#endif

		bool planarReflection = false;
		#if S_SOURCE_WATER
			planarReflection = g_bSourceWaterHasReflection;
		#endif
		if ( planarReflection || g_flSourceHasEnvCube > 0.5 || g_flSourceSceneReflection > 0.5 )
		{
			float3 normalWs = Vec3TsToWsNormalized( normalTs, normalize( i.vNormalWs ), normalize( i.vTangentUWs ), normalize( i.vTangentVWs ) );
			float3 view = normalize( g_vCameraPositionWs.xyz - positionWs );
			#if S_SOURCE_WATER
				// watercheap_vs20 deliberately ignores BUMPTRANSFORM. Its overlay
				// samples a separate normal from the expensive refraction pass.
				float3 cheapNormalWs = normalWs;
				if ( !planarReflection )
				{
					float3 cheapNormalTs = SourceWaterNormal( i.vTextureCoords.xy, i.vTextureCoords.xy, i.flSourceFrame ).xyz * 2 - 1;
					cheapNormalWs = cheapNormalTs.x * i.vTangentUWs + cheapNormalTs.y * i.vTangentVWs + cheapNormalTs.z * i.vNormalWs;
				}
			#endif
			float fresnel = lerp( 1, 0.02 + 0.98 * pow( 1 - saturate( dot( view, normalWs ) ), 5 ), g_flSourceFresnel );
			#if S_SOURCE_WATER
				float3 viewTs = float3( dot( view, i.vTangentUWs ), dot( view, i.vTangentVWs ), dot( view, i.vNormalWs ) );
				fresnel = pow( 1 - saturate( dot( viewTs, normalTs ) ), 5 );
				fresnel *= saturate( (waterDepth - 0.05) * 20 );
			#endif
			float3 reflection;
			if ( planarReflection )
			{
				float2 reflectedOffset = normalTs.xy * normalSample.a * g_flSourceReflectAmount;
				if ( g_flSourceBlurRefract < 0.5 )
					reflectedOffset *= unwarpedWaterDepth;
				float2 reflectionUv = saturate( CalculateViewportUv( i.vPositionSs.xy ) + reflectedOffset );
				reflectionUv = g_vSourceWaterReflectionViewport.xy + reflectionUv * g_vSourceWaterReflectionViewport.zw;
				reflection = g_tSourceWaterReflection.SampleLevel( g_sSourceClamp, reflectionUv, 0 ).rgb;
			}
			else if ( g_flSourceHasEnvCube > 0.5 )
			{
				#if S_SOURCE_WATER
					float3 direction = 2 * dot( cheapNormalWs, view ) * cheapNormalWs - dot( cheapNormalWs, cheapNormalWs ) * view;
					reflection = g_tSourceEnvCube.Sample( g_sSourceCube, direction ).rgb;
				#else
					reflection = g_tSourceEnvCube.SampleLevel( g_sSourceCube, reflect( -view, normalWs ), 0 ).rgb;
				#endif
			}
			else
			{
				#if S_SOURCE_WATER
					reflection = EnvMap::From( positionWs, i.vPositionSs, normalize( cheapNormalWs ), float2( 0.02, 0.02 ) );
				#else
					reflection = EnvMap::From( positionWs, i.vPositionSs, normalWs, float2( 0.02, 0.02 ) );
				#endif
			}
			#if S_SOURCE_WATER
				if ( !planarReflection )
				{
					// GMod overlays DrawCheapWater on refract-only water. Its distance
					// alpha is additive with Fresnel, then faded at the shoreline.
					float distanceToEye = length( g_vCameraPositionWs.xyz - positionWs );
					float transition = SourceCheapWaterFade( distanceToEye );
					float cheapFresnel = SourceCheapWaterFresnel( view, cheapNormalWs );
					fresnel = saturate( cheapFresnel + transition ) * saturate( (unwarpedWaterDepth - 0.05) * 20 );
					reflection *= g_vSourceCheapReflectTint;
				}
				else
					reflection *= g_vSourceReflectTint;
			#else
				reflection *= g_vSourceReflectTint;
			#endif
			#if !S_SOURCE_WATER
				if ( g_bFogEnabled )
					reflection = Fog::Apply( positionWs, i.vPositionSs.xy, reflection );
			#endif
			float reflectionMask = lerp( 1, normalSample.a, g_flSourceEnvAlphaMask );
			#if S_SOURCE_WATER
				color = lerp( color, reflection, saturate( fresnel ) );
			#else
				color = lerp( color, reflection, saturate( fresnel * g_flSourceReflectAmount * reflectionMask ) );
			#endif
		}

		#if S_SOURCE_WATER
			// The underside already applies eye-to-surface water fog above. GMod's
			// water shader doesn't apply the camera's height fog a second time.
			if ( g_bFogEnabled && g_flSourceAboveWater > 0.5 )
				color = Fog::Apply( positionWs, i.vPositionSs.xy, color );
		#endif

		float2 screenUv = saturate( CalculateViewportUv( i.vPositionSs.xy ) ) * g_vFrameBufferCopyInvSizeAndUvScale.zw;
		float3 background = g_tSourceScene.SampleLevel( g_sSourceClamp, screenUv, 0 ).rgb;
		float4 output = float4( lerp( background, color, alpha ), 1 );
		return output;
	}
}
