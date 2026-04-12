Shader "Sprites/FlipbookRemap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0

        [PerRendererData] _FlipbookBaseRect ("Flipbook Base Rect", Vector) = (0,0,1,1)
        [PerRendererData] _FlipbookTargetRect ("Flipbook Target Rect", Vector) = (0,0,1,1)
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _FlipbookBaseRect;
            float4 _FlipbookTargetRect;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;

                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float4 baseRect = _FlipbookBaseRect;
                float4 targetRect = _FlipbookTargetRect;

                float2 baseMin = baseRect.xy;
                float2 baseSize = max(baseRect.zw, float2(1e-5, 1e-5));
                float2 targetMin = targetRect.xy;
                float2 targetSize = targetRect.zw;

                float2 normalizedWithinBase = (IN.texcoord - baseMin) / baseSize;
                float2 remappedUV = targetMin + normalizedWithinBase * targetSize;

                fixed4 color = tex2D(_MainTex, remappedUV) * IN.color;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
