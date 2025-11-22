Shader "Custom/UISpinner_Rotating"
{
    Properties
    {
        [Header(IMPORTANT This shader required Universal Render Pipeline)]
        [Space(2)]
        [Header(IMPORTANT Input texture must use FullRect MeshType in Sprite settings)]
        [Space(20)]
        
        [Header(Visual Settings)]
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Speed ("Rotation Speed", Float) = 500
        [Toggle(USE_UNSCALED_TIME)] _UseUnscaledTime ("Ignore Time Scale", Float) = 0

        [Header(System Stencil and Masking)]
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

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
            Name "UISpinner"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            
            // -------------------------------------
            // Material Keywords
            // -------------------------------------
            #pragma shader_feature_local USE_UNSCALED_TIME
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Speed;
                float4 _ClipRect;
            CBUFFER_END

            // Helper for RectMask2D clipping
            inline float UnityGet2DClipping(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position.xy) * step(position.xy, clipRect.zw);
                return inside.x * inside.y;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = mul(UNITY_MATRIX_M, IN.positionOS);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPosition.xyz);
                OUT.color = IN.color * _Color;

                // Calculate Rotation
                // Note: _Time.y is Time.time (scaled).
                // Real unscaled time requires passing Time.unscaledTime from C# to a uniform,
                // as standard URP global _Time is always scaled.
                float time = _Time.y;
                
                float angle = radians(_Speed * time);
                float sinAngle, cosAngle;
                sincos(angle, sinAngle, cosAngle);

                // Center of UV rotation
                // NOTE: This assumes the sprite is Full Rect or standalone. 
                // If used in a packed Atlas, 0.5 might not be the sprite center.
                float2 uvCenter = float2(0.5, 0.5);
                float2 uv = IN.uv - uvCenter;
                
                // Rotate
                OUT.uv.x = uv.x * cosAngle - uv.y * sinAngle + uvCenter.x;
                OUT.uv.y = uv.x * sinAngle + uv.y * cosAngle + uvCenter.y;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

                // UI Masking logic
                #ifdef UNITY_UI_CLIP_RECT
                    color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip (color.a - 0.001);
                #endif

                return color;
            }
            ENDHLSL
        }
    }
    FallBack "Sprites/Default"
}
