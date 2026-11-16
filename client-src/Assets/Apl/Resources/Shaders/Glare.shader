// Floodlight glare and haze (StadiumBuilder.Sky): additive, the texture's
// alpha times its colour times _Color, no depth write, both faces. Edge-on
// quads fade out (|N.V|) so a crossed halo never shows as a line.
Shader "Apl/Glare"
{
    Properties
    {
        _MainTex ("Glow", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 0.95, 0.85, 1)
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float facing : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Intensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 viewDir = normalize(WorldSpaceViewDir(v.vertex));
                o.facing = abs(dot(n, viewDir));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                float edge = smoothstep(0.05, 0.5, i.facing);
                return fixed4(t.rgb * _Color.rgb * (t.a * _Intensity * edge), 1);
            }
            ENDCG
        }
    }
}
