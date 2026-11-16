// Team kit: recolours a Rocketbox body texture (the blit source) where its
// kit mask says shirt (R), sleeves (G), trousers (B) or shoes (A), keeping
// the original's shading — folds and seams — as a brightness on the new colour.
Shader "Apl/KitBlit"
{
    Properties
    {
        _MainTex ("Body", 2D) = "white" {}
        _Mask ("Kit mask", 2D) = "black" {}
        _Shirt ("Shirt", Color) = (0.1, 0.3, 0.8, 1)
        _Sleeve ("Sleeves", Color) = (0.95, 0.75, 0.2, 1)
        _Trousers ("Trousers", Color) = (0.1, 0.2, 0.5, 1)
        _Shoes ("Shoes", Color) = (0.95, 0.95, 0.95, 1)
        _SkinTint ("Skin tint (unpainted parts)", Color) = (1, 1, 1, 1)
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

            sampler2D _MainTex;
            sampler2D _Mask;
            fixed4 _Shirt, _Sleeve, _Trousers, _Shoes, _SkinTint;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 body = tex2D(_MainTex, i.uv);
                fixed4 mask = tex2D(_Mask, i.uv);
                // Shading from the original cloth: mid grey keeps the colour, darker folds darken it.
                float lum = dot(body.rgb, float3(0.3, 0.59, 0.11));
                // Round 13 (r12 `P393`: the shirts' own plackets, buttons and pockets made them read as
                // casual shirts): the shirt and sleeves keep only half the original's shading — the
                // folds stay, the printed-in details fade — trousers and shoes keep it all.
                float top = saturate(mask.r + mask.g);
                float shade = lerp(1.0, clamp(lum / 0.42, 0.45, 1.35), lerp(0.7, 0.32, top));
                float total = saturate(mask.r + mask.g + mask.b + mask.a);
                fixed3 kit = (_Shirt.rgb * mask.r + _Sleeve.rgb * mask.g + _Trousers.rgb * mask.b + _Shoes.rgb * mask.a) / max(total, 0.001);
                // What isn't kit is mostly skin (hands, neck): shade it to the player's tone.
                return fixed4(lerp(body.rgb * _SkinTint.rgb, kit * shade, total), body.a);
            }
            ENDCG
        }
    }
}
