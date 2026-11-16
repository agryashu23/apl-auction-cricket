using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The Blender gear (GearMeshes) on the cast, in named groups — each its own
    /// skinned renderer on the body, three shared materials: the helmet, the
    /// pads and gloves, a team cap, a wide-brim hat. Hair cards hide only while
    /// something sits on the head.
    /// </summary>
    public sealed partial class CricketCast
    {
        public const string HelmetGear = "Gear_helmet", PadGear = "Gear_pads", CapGear = "Gear_cap", HatGear = "Gear_hat";

        private static readonly string[] HelmetSet = { "helmet_shell", "helmet_vents", "helmet_rim", "helmet_peak", "helmet_peak_under", "helmet_grille" };
        private static readonly string[] PadSet = { "pad_L", "pad_R", "pad_trim_L", "pad_trim_R", "pad_straps_L", "pad_straps_R", "glove_L", "glove_R", "glove_cuff_L", "glove_cuff_R" };

        private static readonly string[] KeeperSet =
        {
            "k_pad_L", "k_pad_R", "k_pad_trim_L", "k_pad_trim_R", "k_pad_straps_L", "k_pad_straps_R",
            "k_glove_L", "k_glove_R", "k_glove_palm_L", "k_glove_palm_R", "k_glove_web_L", "k_glove_web_R", "k_glove_cuff_L", "k_glove_cuff_R",
        };

        private static readonly string[] CapSet = { "cap_crown", "cap_peak", "cap_seams", "cap_badge" };

        /// <summary>
        /// Bodies with hair modelled into the head mesh (buns, quiffs, afros) that
        /// would poke 2–7 cm through a close cap (tools/gear/fit_heads.py →
        /// build/gear/head_fit.txt): no cap for them — bareheaded, or the sun hat.
        /// </summary>
        public static readonly HashSet<string> NoCap = new HashSet<string>
        {
            "Sports_Male_03", "Male_Adult_04", "Wood_Male_01", "Male_Adult_17", "Male_Adult_18", "Male_Adult_10",
        };
        private static readonly string[] HatSet = { "hat_crown", "hat_band", "hat_brim" };

        /// <summary>Bodies whose head mesh (hair modelled in) is bigger than the helmet's lining: x, y, z scale.</summary>
        private static readonly Dictionary<string, Vector3> HeadFit = new Dictionary<string, Vector3>
        {
            ["Sports_Male_03"] = new Vector3(1.16f, 1.03f, 1.07f),
            ["Male_Adult_04"] = new Vector3(1.07f, 1.1f, 1.04f),  // the umpire: his hair (in the head mesh) stands 4.5 cm above the rest
        };

        /// <summary>
        /// How far each body's scalp top falls short of the helmet's lining
        /// (build/gear/head_fit.txt "top" − 0.228 m, capped at 1.2 cm so the
        /// brow never covers the eyes): the helmet comes down by that much.
        /// </summary>
        private static readonly Dictionary<string, float> HelmetDrop = new Dictionary<string, float>
        {
            ["Male_Adult_11"] = -0.012f, ["Male_Adult_02"] = -0.006f, ["Male_Adult_16"] = -0.008f, ["Male_Adult_09"] = -0.011f,
            ["Sports_Male_04"] = -0.009f, ["Sports_Male_02"] = -0.012f, ["Male_Adult_05"] = -0.012f, ["Male_Adult_14"] = -0.012f,
            ["Male_Adult_01"] = -0.001f, ["Male_Adult_20"] = -0.011f, ["Male_Adult_08"] = -0.009f, ["Male_Adult_06"] = -0.004f,
            ["Male_Adult_03"] = -0.012f, ["Male_Adult_13"] = -0.012f, ["Male_Adult_07"] = -0.008f, ["Wood_Male_01"] = -0.008f,
            ["Male_Adult_17"] = -0.007f, ["Male_Adult_12"] = -0.01f,
            ["Sports_Male_01"] = -0.012f, ["Business_Male_02"] = -0.008f, ["Business_Male_04"] = -0.012f, ["Business_Male_05"] = -0.012f,
            ["Business_Male_06"] = -0.007f, ["Business_Male_07"] = -0.012f,
        };

        private readonly Dictionary<string, Material> _gearTeam = new Dictionary<string, Material>();
        private Material? _gearWhite, _gearDark, _gearPad;

        /// <summary>Team colour (helmet shell, caps, pad and glove trims), white padding, dark steel/straps.</summary>
        private Material[] GearSlots(Franchise? team) => GearSlots(team != null ? Color.Lerp(UiKit.ParseHex(team.PrimaryColor), Color.black, 0.1f) : new Color(0.12f, 0.16f, 0.35f));

        private Material[] GearSlots(Color colour)
        {
            var id = ColorUtility.ToHtmlStringRGB(colour);
            if (!_gearTeam.TryGetValue(id, out var shell)) _gearTeam[id] = shell = Mat(colour, 0.72f);
            _gearWhite ??= Mat(new Color(0.95f, 0.95f, 0.93f), 0.22f);
            if (_gearDark == null)
            {
                _gearDark = Mat(new Color(0.11f, 0.11f, 0.13f), 0.55f);
                _gearDark.SetFloat("_Metallic", 0.45f);
            }
            if (_gearPad == null)
            {
                // The pads' leather: canes shaded into their grooves, stitching, knee rolls (Gear/pad.png).
                _gearPad = Mat(new Color(0.97f, 0.97f, 0.95f), 0.2f);
                var pad = Resources.Load<Texture2D>("Gear/pad");
                if (pad != null) _gearPad.SetTexture("_BaseMap", pad);
            }
            // Slots: team, white, dark, (crest: caps only), pad leather.
            return new[] { shell, _gearWhite, _gearDark, _gearWhite, _gearPad };
        }

        private readonly Dictionary<string, Material[]> _caps = new Dictionary<string, Material[]>();

        /// <summary>A cap's materials: matte team cloth (not the helmet's gloss), the button, the crest.</summary>
        private Material[] CapSlots(Franchise? team, Color? cloth = null)
        {
            var id = (team?.Id ?? "") + (cloth.HasValue ? ColorUtility.ToHtmlStringRGB(cloth.Value) : "");
            if (_caps.TryGetValue(id, out var slots)) return slots;
            var c = cloth ?? (team != null ? UiKit.ParseHex(team.PrimaryColor) : new Color(0.12f, 0.16f, 0.35f));
            var fabric = Mat(c, 0.12f);
            fabric.SetTexture("_BaseMap", CapCloth());
            var crest = team != null ? Resources.Load<Texture2D>("Crests/" + team.Id) : null;
            Material badge;
            if (crest != null && _refs.CutoutTemplate != null)
            {
                badge = new Material(_refs.CutoutTemplate);
                badge.SetColor("_BaseColor", Color.white);
                badge.SetTexture("_BaseMap", crest);
                badge.SetColor("_EmissionColor", Color.black);
            }
            else
            {
                badge = Mat(team != null ? UiKit.ParseHex(team.SecondaryColor) : Color.white, 0.2f);
            }
            var basic = GearSlots(team);
            return _caps[id] = new[] { fabric, basic[1], Mat(c * 0.6f, 0.1f), badge };
        }

        private bool Capped(Animator a) => !(_modelOf.TryGetValue(a, out var model) && NoCap.Contains(model));

        /// <summary>
        /// The bowler bowls bareheaded: his cap or sun hat comes off (hair back) and
        /// goes back on when he returns to the field. Keepers and batters are left alone.
        /// </summary>
        public static void Bareheaded(Animator? a, bool bare)
        {
            if (a == null || Groups(a, PadGear).Any()) return;
            foreach (var r in Groups(a, CapGear, HatGear)) r.gameObject.SetActive(!bare);
            Hair(a);
        }

        /// <summary>Puts a group of parts on a body; false if the gear assets aren't there.</summary>
        private bool Wear(Animator a, string label, string[] set, Material[] slots)
        {
            if (!GearMeshes.Ready) return false;
            _modelOf.TryGetValue(a, out var model);
            var fit = model != null && HeadFit.TryGetValue(model, out var f) ? f : Vector3.one;
            var drop = label == HelmetGear && model != null && HelmetDrop.TryGetValue(model, out var dy) ? new Vector3(0, dy, 0) : Vector3.zero;
            var ok = GearMeshes.Dress(a, set, slots, fit, label, drop) != null;
            Hair(a);
            return ok;
        }

        private static IEnumerable<SkinnedMeshRenderer> Groups(Animator a, params string[] labels) =>
            a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => labels.Contains(r.name));

        /// <summary>
        /// Batting / keeping gear (helmet, pads, gloves) off or back on — a
        /// captain at the toss is in his kit only. Caps and hats stay as they are.
        /// </summary>
        public static void ShowGear(Animator a, bool on)
        {
            foreach (var r in Groups(a, HelmetGear, PadGear)) r.enabled = on;
            Hair(a);
        }

        /// <summary>The keeper's head each over: helmet up to the stumps for spin, team cap standing back to pace.</summary>
        public static void KeeperHeadgear(Animator? a, bool spin)
        {
            if (a == null) return;
            foreach (var r in Groups(a, HelmetGear)) r.gameObject.SetActive(spin);
            foreach (var r in Groups(a, CapGear)) r.gameObject.SetActive(!spin);
            Hair(a);
        }

        /// <summary>Fielders' heads: about a third in the team cap, one in six in a sun hat (stable per player).</summary>
        private void FieldHeadwear(Dictionary<string, Animator> side, Franchise team)
        {
            var cap = CapSlots(team);
            foreach (var kv in side)
            {
                var roll = Mathf.Abs(kv.Key.Aggregate(41, (h, c) => h * 37 + c)) % 100;
                if (roll < 35 && Capped(kv.Value)) Wear(kv.Value, CapGear, CapSet, cap);
                else if (roll < 52) Wear(kv.Value, HatGear, HatSet, cap);
            }
        }

        private static readonly Dictionary<Material, (float alpha, float clip, float cutoff, bool keyword)> HairWas =
            new Dictionary<Material, (float, float, float, bool)>();

        /// <summary>The hair cards (the bodies' "opacity" material) drawn clear while a helmet, cap or hat is on.</summary>
        /// <summary>(batting) Hair shown or hidden again after a helmet came off or went back on (BatterRig.Celebration.cs).</summary>
        public static void RefreshHair(Animator a) => Hair(a);

        private static void Hair(Animator a)
        {
            var covered = Groups(a, HelmetGear, CapGear, HatGear).Any(r => r.enabled && r.gameObject.activeSelf);
            foreach (var r in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.name.ToLowerInvariant().Contains("opacity") || !m.HasProperty("_BaseColor")) continue;
                    var c = m.GetColor("_BaseColor");
                    if (!HairWas.TryGetValue(m, out var was))
                    {
                        was = (c.a, m.HasProperty("_AlphaClip") ? m.GetFloat("_AlphaClip") : 0, m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f, m.IsKeywordEnabled("_ALPHATEST_ON"));
                        HairWas[m] = was;
                    }
                    c.a = covered ? 0 : was.alpha;
                    m.SetColor("_BaseColor", c);
                    m.SetFloat("_AlphaClip", covered ? 1 : was.clip);
                    m.SetFloat("_Cutoff", covered ? 0.5f : was.cutoff);
                    if (covered || was.keyword) m.EnableKeyword("_ALPHATEST_ON");
                    else m.DisableKeyword("_ALPHATEST_ON");
                }
            }
        }
    }
}
