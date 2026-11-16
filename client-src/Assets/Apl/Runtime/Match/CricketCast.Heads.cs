using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The 3D head made to match the player's card (rating r8: "Bhumrah's card has a
    /// dark beard; the model is a clean-shaven, light-skinned man"): the head texture
    /// is re-baked per look (Apl/HeadBlit) with the portrait's skin tone on the skin
    /// and his beard — stubble, short, full or a moustache — in his hair colour,
    /// through the body's beard mask (tools/gear/beard_masks.py). Bodies are also
    /// picked by hair style (<see cref="HairOf"/>). One texture per distinct look, shared.
    /// </summary>
    public sealed partial class CricketCast
    {
        private const int HeadSize = 768;
        private static Material? _headBlit;
        private readonly Dictionary<string, RenderTexture> _heads = new Dictionary<string, RenderTexture>();

        /// <summary>Which bodies' hair suits a portrait hair style (picked first among near skin tones).</summary>
        private static readonly Dictionary<string, string[]> HairOf = new Dictionary<string, string[]>
        {
            ["curly"] = new[] { "Male_Adult_06", "Male_Adult_01" },
            ["spiky"] = new[] { "Male_Adult_08", "Male_Adult_16" },
            ["quiff"] = new[] { "Male_Adult_08", "Male_Adult_10" },
            ["side"] = new[] { "Male_Adult_10", "Business_Male_06", "Sports_Male_04" },
            ["short"] = new[] { "Male_Adult_11", "Sports_Male_02", "Sports_Male_04", "Male_Adult_16" },
            ["buzz"] = new[] { "Male_Adult_11", "Sports_Male_02" },
            ["bald"] = new[] { "Male_Adult_11" },
        };

        /// <summary>Score bonus (lower is better) when the body's hair suits the player's.</summary>
        private float HairScore(Player p, int model)
        {
            if (!Portraits.Looks.TryGetValue(p.Id, out var look) || !HairOf.TryGetValue(look.hair, out var bodies)) return 0;
            return System.Array.IndexOf(bodies, _refs.CastModels[model].name) >= 0 ? -1.6f : 0;
        }

        private static readonly Dictionary<string, Color> BeardByHair = new Dictionary<string, Color>
        {
            ["black"] = new Color(0.05f, 0.04f, 0.035f),
            ["darkbrown"] = new Color(0.11f, 0.075f, 0.055f),
            ["brown"] = new Color(0.2f, 0.13f, 0.08f),
            ["blond"] = new Color(0.55f, 0.42f, 0.24f),
            ["ginger"] = new Color(0.45f, 0.2f, 0.08f),
            ["salt"] = new Color(0.42f, 0.4f, 0.38f),
            ["grey"] = new Color(0.62f, 0.6f, 0.58f),
        };

        /// <summary>Scalp hair colours (sRGB) by the portrait's hair colour.</summary>
        private static readonly Dictionary<string, Color> HairDye = new Dictionary<string, Color>
        {
            ["black"] = new Color(0.08f, 0.07f, 0.065f),
            ["darkbrown"] = new Color(0.2f, 0.13f, 0.08f),
            ["brown"] = new Color(0.38f, 0.25f, 0.15f),
            ["blond"] = new Color(0.8f, 0.64f, 0.38f),
            ["ginger"] = new Color(0.66f, 0.32f, 0.12f),
            ["salt"] = new Color(0.52f, 0.5f, 0.48f),
            ["grey"] = new Color(0.72f, 0.72f, 0.72f),
        };

        /// <summary>Skin tone and beard on <paramref name="body"/>'s head for player <paramref name="p"/>.</summary>
        private void Face(Animator body, int model, Player p)
        {
            _headBlit ??= MakeHeadBlit();
            var name = _refs.CastModels[model].name;
            var mask = Resources.Load<Texture2D>("Heads/" + name + "_beard");
            if (_headBlit == null) return;
            Portraits.Looks.TryGetValue(p.Id, out var look);
            var skin = Tint(ToneOf(p), _refs.CastTones[model]);
            var beard = look?.beard ?? "none";
            if (mask == null) beard = "none";
            var amount = beard switch
            {
                "stubble" => new Vector4(0.8f, 0.35f, 1, 0),
                "short" => new Vector4(0.97f, 1f, 0, 0), // the card paints "short" over the whole jaw at 85 % (AvatarPainter): a beard, not stubble (r10 `06`)
                "full" => new Vector4(1f, 1f, 0, 0),
                "moustache" => new Vector4(0f, 1f, 0, 0),
                _ => Vector4.zero,
            };
            var colour = look != null && BeardByHair.TryGetValue(look.hair_color, out var bc) ? bc : BeardByHair["black"];
            // Seniors only flecked with grey: at 50 % a dark short beard read as stubble on Rogit (r10 `06`).
            if (look != null && look.age == "senior" && look.hair_color != "salt" && look.hair_color != "grey") colour = Color.Lerp(colour, BeardByHair["salt"], 0.2f);
            // The scalp hair in the card's colour too (r9 `374`: a blond card on a brown-haired head).
            var hairColour = look != null && HairDye.TryGetValue(look.hair_color, out var hd) ? hd : HairDye["black"];
            if (look != null && look.age == "senior" && look.hair_color != "salt" && look.hair_color != "grey") hairColour = Color.Lerp(hairColour, HairDye["salt"], 0.45f);
            var hair = new Color(hairColour.r, hairColour.g, hairColour.b, look != null ? 1f : 0f);
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.name.ToLowerInvariant().Contains("head")) continue;
                    var src = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                    if (src == null) continue;
                    var key = $"{name}|{ColorUtility.ToHtmlStringRGB(skin)}|{beard}|{ColorUtility.ToHtmlStringRGB(colour)}|{ColorUtility.ToHtmlStringRGBA(hair)}";
                    if (!_heads.TryGetValue(key, out var rt))
                    {
                        rt = new RenderTexture(HeadSize, HeadSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                        {
                            useMipMap = true,
                            autoGenerateMips = true,
                            wrapMode = TextureWrapMode.Clamp,
                            name = "Head_" + name,
                        };
                        _headBlit.SetTexture("_Beard", mask != null ? mask : Texture2D.blackTexture);
                        _headBlit.SetColor("_SkinMul", skin);
                        _headBlit.SetColor("_BeardColor", colour);
                        _headBlit.SetVector("_Amount", amount);
                        _headBlit.SetColor("_Hair", hair);
                        _headBlit.SetVector("_Detail", new Vector4(1f / HeadSize, 0.55f, 0, 0));
                        Graphics.Blit(src, rt, _headBlit);
                        _heads[key] = rt;
                    }
                    m.SetTexture("_BaseMap", rt);
                    // The tone is baked into the texture now.
                    m.SetColor("_BaseColor", Color.white);
                }
            }
        }

        private static Material? MakeHeadBlit()
        {
            var shader = Resources.Load<Shader>("Shaders/HeadBlit") ?? Shader.Find("Apl/HeadBlit");
            if (shader != null) return new Material(shader) { name = "HeadBlit" };
            Debug.LogWarning("[APL] Apl/HeadBlit shader missing: heads keep their own skin and no beards");
            return null;
        }
    }
}
