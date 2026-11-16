// The stadium crowd (StadiumBuilder.Crowd): one quad per fan, all animation
// in the vertex shader so a whole tier is a few static meshes.
//   POSITION  the fan's foot point (every corner of the quad carries it)
//   COLOR     rgb shirt colour, a = which fans (0 one side, 0.5 the other, 1 neutral)
//   TEXCOORD0 the sprite cell (idle frame)
//   TEXCOORD1 x, y corner offset in metres, z random phase 0-1, w kind (0 fan, 1 flag, 2 placard)
//   TEXCOORD2 xy offset to the arms-up frame, z brightness
// Quads turn about the vertical to face the camera. Globals set by
// StadiumBuilder.CrowdCheer: _AplCheer (strength, start time, hold seconds),
// _AplCheerTeams (how much each group joins in), _AplWave (strength, start, laps per second);
// StadiumBuilder.CrowdBanners / CrowdWicketBanners: _AplBanner (strength, start, bearing
// 0-1 or -1 for all round, kind). Placards are folded away (zero size) until a banner pulse
// near their bearing raises them, bobbing overhead.
// The sprites are rendered Rocketbox people (tools/gear/crowd_sprites.py): alpha is coverage;
// _MaskTex marks a team fan's top (and placard / flag colour), recoloured to the vertex colour
// with the cloth's shading kept. Neutral fans (colour alpha 1) keep their own clothes.
// _AplBanner.w picks the placard: 0 four, 1 six, 2 wicket (alt.x steps between them).
// TEXCOORD2.xy is the offset to the fan's excited frame pair (cheer / clap / wave), which flips.
Shader "Apl/Crowd"
{
    Properties
    {
        _MainTex ("Sprites", 2D) = "white" {}
        _MaskTex ("Team-colour mask", 2D) = "black" {}
        _CellU ("One cell's width in u", Float) = 0.0625
        _Bright ("Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 k : TEXCOORD1;
                float4 alt : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 col : TEXCOORD1;
                float recolour : TEXCOORD3;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _MainTex, _MaskTex;
            float _CellU;
            float _Bright;
            float4 _AplCheer, _AplCheerTeams, _AplWave, _AplBanner;

            v2f vert(appdata v)
            {
                v2f o;
                float3 c = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                float t = _Time.y;
                float ph = v.k.z;
                float flag = step(0.5, v.k.w) * step(v.k.w, 1.5);
                float placard = step(1.5, v.k.w);

                // How excited this fan is: the cheer (by side), or the wave passing.
                float team = v.color.a;
                float joins = team < 0.25 ? _AplCheerTeams.x : (team < 0.75 ? _AplCheerTeams.y : _AplCheerTeams.z);
                float ct = t - _AplCheer.y;
                float env = _AplCheer.x * saturate(ct * 5) * saturate(1 - (ct - _AplCheer.z) / 2.5) * joins;
                float up = step(ph, env * 1.15) * saturate(env * 3);
                float bearing = atan2(c.x, c.z) / 6.2831853 + 0.5;
                float wt = t - _AplWave.y;
                float head = frac(wt * _AplWave.z);
                float d = abs(frac(bearing - head + 0.5) - 0.5);
                float wave = _AplWave.x * saturate(1 - d / 0.03) * saturate(1 - wt / 16) * step(0, wt);
                up = max(up, wave);

                // Up on their feet and bouncing; at rest, a little fidget.
                float jump = up * (0.06 + 0.16 * abs(sin(t * (7 + ph * 5) + ph * 40))) + wave * 0.2;
                float bob = 0.012 * sin(t * (1.3 + ph * 1.7) + ph * 60) * (1 - up);
                float3 toCam = _WorldSpaceCameraPos - c;
                float3 right = normalize(float3(-toCam.z, 0, toCam.x) + float3(1e-5, 0, 0));
                float3 facing = normalize(float3(toCam.x, 0, toCam.z) + float3(0, 0, 1e-5));
                float2 k = v.k.xy;
                // Placards: up near where the ball went, held overhead and pumped up and down.
                float bt = t - _AplBanner.y;
                float benv = _AplBanner.x * saturate(bt * 3) * saturate(1 - (bt - 3.5)) * step(0, bt);
                float bd = abs(frac(bearing - _AplBanner.z + 0.5) - 0.5);
                float near = _AplBanner.z < 0 ? 1 : saturate(1 - bd / 0.13);  // bearing -1: all round
                float raise = placard * benv * near * joins * step(ph, 0.9);
                // Overhead: a standing fan's head is ~1.75 m above his row's floor.
                float held = 2.05 + 0.14 * sin(t * (5 + ph * 3) + ph * 20);
                k *= 1 - placard + raise;
                float3 p = c + right * k.x + float3(0, k.y + (bob + jump) * (1 - placard) + raise * held, 0);
                // A flag's cloth ripples, more toward the free end (k.x grows from the pole).
                float ripple = sin(t * (4.5 + ph * 2) + ph * 30 - k.x * 6) * 0.14 * saturate(k.x);
                p += facing * ripple * flag + right * (sin(t * 1.7 + ph * 9) * 0.06 * saturate(k.x)) * flag;

                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1));
                // Excited fans: their pair of frames flipping (arms pumping, clapping, waving).
                float active = step(0.5, up) * (1 - flag) * (1 - placard);
                float flip = step(0.5, frac(t * (2.2 + ph * 1.2) + ph * 7)) * active;
                o.uv = v.uv + v.alt.xy * (active + placard * _AplBanner.w) + float2(_CellU * flip, 0);
                o.recolour = max(step(team, 0.75), max(flag, placard));
                o.col = float4(v.color.rgb, v.alt.z * _Bright);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 s = tex2D(_MainTex, i.uv);
                clip(s.a - 0.3);
                float shirt = tex2D(_MaskTex, i.uv).r * i.recolour;
                float lum = dot(s.rgb, float3(0.3, 0.59, 0.11));
                float shade = lerp(1.0, clamp(lum / 0.42, 0.45, 1.35), 0.7);
                float3 col = lerp(s.rgb, i.col.rgb * shade, shirt) * i.col.a;
                fixed4 c = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
