Shader "UI/FlipbookRemap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        _FlipbookBaseRect ("Flipbook Base Rect", Vector) = (0,0,1,1)
        _FlipbookTargetRect ("Flipbook Target Rect", Vector) = (0,0,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 uv1      : TEXCOORD1;
                float4 uv2      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 baseRect : TEXCOORD1;
                float4 targetRect : TEXCOORD2;
                float4 worldPos : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float4 _FlipbookBaseRect;
            float4 _FlipbookTargetRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPos = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPos);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.baseRect = v.uv1;
                OUT.targetRect = v.uv2;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float4 baseRect = IN.baseRect;
                float4 targetRect = IN.targetRect;

                // 顶点通道未写入时回退到材质参数，便于兼容标准 Image 路径。
                if (baseRect.z <= 0.0 || baseRect.w <= 0.0)
                {
                    baseRect = _FlipbookBaseRect;
                }

                if (targetRect.z <= 0.0 || targetRect.w <= 0.0)
                {
                    targetRect = _FlipbookTargetRect;
                }

                float2 baseMin = baseRect.xy;
                float2 baseSize = max(baseRect.zw, float2(1e-5, 1e-5));
                float2 targetMin = targetRect.xy;
                float2 targetSize = targetRect.zw;

                float2 normalizedWithinBase = (IN.texcoord - baseMin) / baseSize;
                float2 remappedUV = targetMin + normalizedWithinBase * targetSize;

                fixed4 color = (tex2D(_MainTex, remappedUV) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
