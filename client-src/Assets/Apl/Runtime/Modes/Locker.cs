using System.Collections.Generic;
using Apl.Client.Match;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The user's batters wear what's equipped in THE VAULT (Progression; the class keeps its
    /// old name "Locker"): the helmet colourway, the pads-and-gloves colourway and the bat design — the
    /// user's side only (MatchLaunch.MyTeam); everyone else stays as the kit makes
    /// them. Hooked into CricketCast.BatterGear (the gear's material slots: team
    /// colour, white, dark, cap crest, pad leather) and, for the bat, a small
    /// component that paints it once the batter's rig has made it.
    /// </summary>
    public static class Locker
    {
        /// <summary>The user's own side: what the locker dresses.</summary>
        public static bool Mine(string teamId) => teamId == MatchLaunch.MyTeam;

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        private static Material Tinted(Material template, string key, Color c, float smooth, float metal)
        {
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(template) { name = key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return Mats[key] = m;
        }

        /// <summary>The helmet's slots for <paramref name="team"/>: the shell in the equipped colourway (the team's own colour for TEAM).</summary>
        public static Material[] Helmet(Franchise team, Material[] slots)
        {
            var h = Progression.Equipped(Slot.Helmet);
            if (!Mine(team.Id) || slots.Length == 0) return slots;
            var c = h.TeamMain ? slots[0].GetColor("_BaseColor") : h.A;
            var copy = (Material[])slots.Clone();
            copy[0] = Tinted(slots[0], $"helmet_{h.Id}_{ColorUtility.ToHtmlStringRGB(c)}", c, h.Smooth, h.Metal);
            return copy;
        }

        /// <summary>The pads' and gloves' slots: trims (slot 0), padding (slot 1) and the pad leather (slot 4) in the colourway.</summary>
        public static Material[] Pads(Franchise team, Material[] slots)
        {
            var p = Progression.Equipped(Slot.Pads);
            if (!Mine(team.Id) || p.Default || slots.Length < 5) return slots;
            var teamColour = slots[0].GetColor("_BaseColor");
            var trim = p.TeamMain ? teamColour : p.A;
            var pad = p.B.a <= 0f ? Color.Lerp(teamColour, Color.white, 0.15f) : p.B;
            var copy = (Material[])slots.Clone();
            copy[0] = Tinted(slots[0], $"padtrim_{p.Id}_{ColorUtility.ToHtmlStringRGB(trim)}", trim, 0.6f, p.Metal);
            copy[1] = Tinted(slots[1], $"padding_{p.Id}_{ColorUtility.ToHtmlStringRGB(pad)}", pad, 0.25f, 0f);
            copy[4] = Tinted(slots[4], $"padleather_{p.Id}_{ColorUtility.ToHtmlStringRGB(pad)}", pad, 0.2f, 0f);
            return copy;
        }

        /// <summary>From BatterGear: this body's bat gets the user's design, or goes back to the plain one.</summary>
        public static void Dress(Animator a, Franchise team)
        {
            var paint = a.GetComponent<LockerBat>();
            if (paint == null) paint = a.gameObject.AddComponent<LockerBat>();
            paint.Mine = Mine(team.Id);
            paint.Apply();
        }

        /// <summary>The material for the user's bat design (from the plain bat's own material).</summary>
        public static Material? BatMaterial(Material plain)
        {
            var d = Progression.Equipped(Slot.Bat);
            if (d.Default) return null;
            var key = "bat_" + d.Id;
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            var tex = BatPaint.For(d);
            if (tex == null) return null;
            m = new Material(plain) { name = key };
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetFloat("_Smoothness", d.Finish == BatFinish.Gold ? 0.72f : d.Finish == BatFinish.Willow ? 0.42f : 0.6f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", d.Finish == BatFinish.Gold ? 0.6f : 0f);
            return Mats[key] = m;
        }

        /// <summary>
        /// The clip for a celebration by one of the user's batters (a milestone, the winning runs): the
        /// CELEBRATION equipped in THE VAULT (Celebrations.cs), else the director's own.
        /// </summary>
        public static string CelebrationClip(string teamId, string usual)
        {
            if (!Mine(teamId)) return usual;
            var clip = Progression.Equipped(Slot.Celebration).Clip;
            Debug.Log($"[APL vault] celebration: {clip}");
            return string.IsNullOrEmpty(clip) ? usual : clip;
        }
    }
}
