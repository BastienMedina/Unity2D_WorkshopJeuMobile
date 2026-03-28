Shader "Custom/BackgroundFluid"
{
    Properties
    {
        _ColorA ("Color A", Color) = (0.1, 0.4, 0.6, 1)
        _ColorB ("Color B", Color) = (0.4, 0.1, 0.6, 1)
        _Speed ("Speed", Float) = 1.0
        _Scale ("Scale", Float) = 5.0
        _Intensity ("Intensity", Range(0, 1)) = 0.5
        _PixelSize ("Pixel Size", Float) = 128.0
        _PosterizeSteps ("Posterize Steps", Float) = 4.0
        
        // Required for UI
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            float4 _ColorA;
            float4 _ColorB;
            float _Speed;
            float _Scale;
            float _Intensity;
            float _PixelSize;
            float _PosterizeSteps;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 1. Pixelation: Quantize UVs
                float2 uv_pixel = floor(input.uv * _PixelSize) / _PixelSize;
                
                float2 uv = uv_pixel * _Scale;
                float time = _Time.y * _Speed;

                // 2. Balatro-style "Plasma" math
                for(int i = 1; i < 4; i++)
                {
                    uv.x += 0.3 / i * sin(i * 3.0 * uv.y + time + i);
                    uv.y += 0.3 / i * sin(i * 3.0 * uv.x + time + i);
                }

                // Create the "shapes"
                float strength = sin(uv.x + uv.y) * 0.5 + 0.5;
                
                // 3. Posterization: Cell-shaded look
                float factor = saturate(strength * _Intensity * 2.0);
                factor = floor(factor * _PosterizeSteps) / _PosterizeSteps;

                float4 finalColor = lerp(_ColorA, _ColorB, factor);
                finalColor.a *= input.color.a;
                
                return finalColor;
            }
            ENDHLSL
        }
    }
}
