Shader "Custom/UISpinner_Rotating"
{
    Properties
    {
        [Header(IMPORTANT This shader required Universal Render Pipeline)]
        [Space(2)]
        [Header(IMPORTANT Input texture must use FullRect MeshType in Sprite settings)]
        [Space(20)]
        [PerRendererData]
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Speed ("Rotation Speed", Float) = 500
        _UseUnscaledTime ("Ignore Time Scale", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            float _Speed;
            float _UseUnscaledTime;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color * _Color;

                // calculate rotate
                float time = _UseUnscaledTime > 0.5 ? _Time.y : _Time.x;
                float angle = radians(_Speed * time);
                float sinAngle, cosAngle;
                sincos(angle, sinAngle, cosAngle);

                // apply to uv
                float2 uvCenter = float2(0.5, 0.5);
                float2 uv = IN.uv - uvCenter;
                float2 rotatedUV = float2(
                uv.x * cosAngle - uv.y * sinAngle,
                uv.x * sinAngle + uv.y * cosAngle
                );

                // remapping
                OUT.uv = rotatedUV + uvCenter;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // output with Wrap Mode
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                return color;
            }
            ENDHLSL
        }
    }
    FallBack "Sprites/Default"
}