#ifndef MYRP_UNLIT_INCLUDED
#define MYRP_UNLIT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerMaterial)
	float4 _Color;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
CBUFFER_END

struct VertexInput {
	float4 pos:POSITION;
};

struct VertexOutput {
	float4 clipPos:SV_POSITION;
};

VertexOutput UnlitPassVertex (VertexInput input) {
	VertexOutput output;
	float4 worldPos = mul(unity_ObjectToWorld, float4(input.pos.xyz, 1.0));
	output.clipPos = mul(unity_MatrixVP, worldPos);
	return output;
}
float4 UnlitPassFragment (VertexOutput input) : SV_TARGET {
	return _Color ;
}

#endif // MYRP_UNLIT_INCLUDED