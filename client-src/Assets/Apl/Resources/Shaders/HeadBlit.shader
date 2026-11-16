// A player's head texture (CricketCast.Heads): the Rocketbox head recoloured to
// his portrait's skin tone (skin-coloured texels only, so eye whites, hair and
// lips keep theirs) and his beard painted on through the body's beard mask
// (Heads/<model>_beard: R full-beard area, G moustache + chin, B lips).
Shader "Apl/HeadBlit"
{
    Properties
    {
        _MainTex ("Head", 2D) = "white" {}
        _Beard ("Beard mask", 2D) = "black" {}
        _SkinMul ("Skin multiplier", Color) = (1, 1, 1, 1)
        _BeardColor ("Beard colour", Color) = (0.08, 0.06, 0.05, 1)
        _Amount ("Beard: full area, core, stubble (0/1), -", Vector) = (0, 0, 0, 0)
        _Hair ("Hair colour (a = how much)", Color) = (0, 0, 0, 0)
        _Detail ("Detail: texel size of the output, sharpening", Vector) = (0.0013, 0.55, 0, 0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _Beard;
            fixed4 _SkinMul, _BeardColor, _Hair;
            float4 _Detail;
            float4 _Amount;

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                // Round 12 (r11: "faces smooth and low-detail up close"): the 2048 head drawn down to 768
                // loses its pores, creases and brow hairs — an unsharp mask at the output's texel puts the
                // fine contrast back.
                float2 o = float2(_Detail.x, 0);
                float3 blur = (tex2D(_MainTex, i.uv + o.xy).rgb + tex2D(_MainTex, i.uv - o.xy).rgb
                             + tex2D(_MainTex, i.uv + o.yx).rgb + tex2D(_MainTex, i.uv - o.yx).rgb) * 0.25;
                c.rgb = max(0, c.rgb + (c.rgb - blur) * _Detail.y);
                float lum = dot(c.rgb, float3(0.3, 0.59, 0.11));
                // Skin (values are linear: the project is in linear space): warm — red well over blue
                // relative to its brightness — and not dark (hair). Eye whites and grey hair are neither.
                float warm = (c.r - c.b) / max(c.r, 0.001);
                float skin = smoothstep(0.2, 0.35, warm) * smoothstep(0.03, 0.08, lum);
                float3 col = c.rgb * lerp(float3(1, 1, 1), _SkinMul.rgb, skin);
                float4 m = tex2D(_Beard, i.uv);
                // The scalp's hair (mask A, minus skin) in the card's colour, keeping the strands' light and dark.
                float hairy = m.a * (1 - skin) * _Hair.a;
                float3 dyed = _Hair.rgb * clamp(lum / 0.05, 0.2, 1.6);
                col = lerp(col, dyed, saturate(hairy));
                // Hair strands: fine noise, a coarser clump pattern on top.
                float fine = hash(floor(i.uv * 900));
                float clump = hash(floor(i.uv * 140));
                float strands = 0.78 + 0.14 * fine + 0.08 * clump;
                float a = saturate(m.r * _Amount.x + m.g * _Amount.y) * (1 - m.b);
                // Stubble: a soft see-through shadow (speckle read as noise at 768 px); a beard: dense, strand-broken.
                float stubble = _Amount.z;
                a *= lerp(strands, 0.6 * (0.75 + 0.25 * fine), stubble);
                float3 beard = _BeardColor.rgb * (0.75 + 0.5 * lum);
                col = lerp(col, beard, saturate(a));
                return fixed4(col, c.a);
            }
            ENDCG
        }
    }
}
