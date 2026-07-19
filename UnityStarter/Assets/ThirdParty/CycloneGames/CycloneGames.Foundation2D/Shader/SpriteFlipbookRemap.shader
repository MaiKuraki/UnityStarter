Shader "Sprites/FlipbookRemap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
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
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnitySprites.cginc"

            float4 _FlipbookBaseRect;
            float4 _FlipbookTargetRect;

            v2f vert(appdata_t IN)
            {
                return SpriteVert(IN);
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

                fixed4 color = SampleSpriteTexture(remappedUV) * IN.color;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
