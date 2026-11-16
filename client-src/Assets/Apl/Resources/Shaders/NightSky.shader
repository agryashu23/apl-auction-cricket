// The night sky round the ground (StadiumBuilder.NightSky), on a big sphere at
// the origin: a deep navy zenith, the venue's sky colour in the middle, a warm
// glow of city and floodlight on the horizon, thin clouds lit from below, and
// stars that fade toward the bright horizon. No fog (the fog colour is matched
// to the horizon instead).
Shader "Apl/NightSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.02, 0.03, 0.08, 1)
        _Mid ("Mid sky", Color) = (0.08, 0.1, 0.24, 1)
        _Horizon ("Horizon glow", Color) = (0.36, 0.28, 0.3, 1)
        _Cloud ("Cloud", Color) = (0.2, 0.2, 0.28, 1)
        _Stars ("Stars", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry-50" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            fixed4 _Zenith, _Mid, _Horizon, _Cloud;
            float _Stars;

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.dir = mul(unity_ObjectToWorld, float4(vertex.xyz, 0)).xyz;
                return o;
            }

            float hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = hash(float3(i, 0)), b = hash(float3(i + float2(1, 0), 0));
                float c = hash(float3(i + float2(0, 1), 0)), d = hash(float3(i + float2(1, 1), 0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += a * noise(p); p *= 2.03; a *= 0.5; }
                return s;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float e = d.y;
                // Gradient: horizon glow up to ~12 degrees, the venue's sky, navy overhead.
                float3 col = lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0.0, 0.22, e));
                col = lerp(col, _Zenith.rgb, smoothstep(0.25, 0.85, e));
                // Below the horizon: the glow sinks into the dark land.
                col = lerp(col, _Horizon.rgb * 0.45, smoothstep(0.0, -0.08, e));
                // Thin clouds, projected on a flat layer, lit from below by the city.
                float2 uv = d.xz / max(e + 0.18, 0.05) * 1.6;
                float cl = smoothstep(0.52, 0.82, fbm(uv + float2(3.1, 7.7)));
                float lit = 1 - smoothstep(0.05, 0.6, e);
                col = lerp(col, lerp(_Cloud.rgb, _Horizon.rgb * 1.1, lit), cl * 0.55 * smoothstep(-0.02, 0.06, e));
                // Stars: a sparse grid of tiny points, hidden by cloud and the horizon glow.
                float3 g = floor(d * 1400);
                float h = hash(g);
                float star = step(0.9988, h) * smoothstep(0.12, 0.4, e) * (1 - cl) * _Stars;
                col += star * (0.5 + 0.5 * frac(h * 91.7));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
