Shader "MyPipeLine/lit"
{
    Properties
    {
        _Color("Color",Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {	
            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment
			#include "../ShaderLibrary/lit.hlsl"
			ENDHLSL
        }
    }
}
