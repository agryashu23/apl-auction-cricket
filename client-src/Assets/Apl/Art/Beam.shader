// Visible light beam: additive, brightest at the lamp (uv.y = 0) and fading
// down the cone, soft at the silhouette edges so it reads as haze, not a solid.
Shader "Apl/Beam"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.9, 0.7, 0.35)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
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

            fixed4 _Color;

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
                float along = pow(saturate(1.0 - i.uv.y), 1.6);
                float edge = i.facing * i.facing;
                return _Color * (_Color.a * along * edge);
            }
            ENDCG
        }
    }
}
