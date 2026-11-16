using System.Linq;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>What a cosmetic in THE VAULT dresses: the bat, the helmet, the pads and gloves, the CELEBRATION — or the player's title (round 12).</summary>
    public enum Slot { Bat, Helmet, Pads, Celebration, Title }

    /// <summary>The bat's sticker art (BatPaint), and the blade's finish.</summary>
    public enum BatPattern { Classic, Stripes, Chevron, Flame, Split }
    public enum BatFinish { Willow, Carbon, Ice, Gold }

    /// <summary>
    /// One unlockable for the user's own batters (Locker): a bat design (sticker art
    /// in three colours on a blade finish), a helmet or a pads-and-gloves colourway
    /// (main colour, trim, gloss / metal), or a celebration (a clip, Celebrations.cs).
    /// </summary>
    public sealed class Cosmetic
    {
        public string Id = "", Name = "", Line = "";
        public Slot Slot;

        /// <summary>The level that unlocks it (0: <see cref="Stars"/> challenge stars do).</summary>
        public int Level = 1;
        public int Stars;

        public BatPattern Pattern;
        public BatFinish Finish;
        /// <summary>Bat: sticker panel, bars, stripe. Helmet / pads: main, trim, padding.</summary>
        public Color A = Color.white, B = Color.white, C = Color.white;
        /// <summary>Helmet / pads: the team's own colour in place of <see cref="A"/>.</summary>
        public bool TeamMain;
        public float Smooth = 0.6f, Metal;

        /// <summary>Celebration: the clip played for his fifty (and a hundred).</summary>
        public string Clip = "";

        public bool Default => Level <= 1 && Stars == 0;
        public string Requirement => Level > 1 ? $"LEVEL {Level}" : Stars > 0 ? $"{Stars} STARS" : "";
    }

    /// <summary>The catalogue, by slot in unlock order; the first of each slot is the default.</summary>
    public static class Cosmetics
    {
        private static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;

        public static readonly Cosmetic[] All = Base().Concat(Celebrations.Cosmetics()).Concat(CosmeticsLadder.More).ToArray();

        private static Cosmetic[] Base() => new[]
        {
            // Bats: sticker art and blade finish.
            new Cosmetic { Id = "bat_classic", Slot = Slot.Bat, Name = "CLASSIC APL", Line = "The league's own stickers on natural willow.", Pattern = BatPattern.Classic, A = C("#141E52"), B = C("#D42A2A"), C = C("#E8B021") },
            new Cosmetic { Id = "bat_stripes", Slot = Slot.Bat, Level = 2, Name = "TWIN STRIPE", Line = "Racing stripes in crimson and black.", Pattern = BatPattern.Stripes, A = C("#B3121F"), B = C("#141414"), C = C("#F2C24F") },
            new Cosmetic { Id = "bat_chevron", Slot = Slot.Bat, Level = 3, Name = "CHEVRON", Line = "Emerald chevrons, gold edge.", Pattern = BatPattern.Chevron, A = C("#0E6A3C"), B = C("#F2F2F2"), C = C("#E8B021") },
            new Cosmetic { Id = "bat_carbon", Slot = Slot.Bat, Level = 4, Name = "CARBON", Line = "A black-finished blade with steel-grey stripes.", Pattern = BatPattern.Split, Finish = BatFinish.Carbon, A = C("#2B2F36"), B = C("#8E97A6"), C = C("#E8E8EC") },
            new Cosmetic { Id = "bat_flame", Slot = Slot.Bat, Level = 6, Name = "INFERNO", Line = "Flames licking up the sticker.", Pattern = BatPattern.Flame, A = C("#140A2A"), B = C("#E2541B"), C = C("#FFD23A") },
            new Cosmetic { Id = "bat_ice", Slot = Slot.Bat, Level = 7, Name = "ICE", Line = "A white-lacquered blade, ice-blue chevrons.", Pattern = BatPattern.Chevron, Finish = BatFinish.Ice, A = C("#0A6E9E"), B = C("#BFEFFF"), C = C("#0B2A44") },
            new Cosmetic { Id = "bat_gold", Slot = Slot.Bat, Level = 9, Name = "GOLD EDITION", Line = "A gold-leaf blade, black stickers.", Pattern = BatPattern.Classic, Finish = BatFinish.Gold, A = C("#121212"), B = C("#3B2600"), C = C("#FFF1B0") },
            new Cosmetic { Id = "bat_champion", Slot = Slot.Bat, Level = 0, Stars = 18, Name = "CHAMPION", Line = "For 18 challenge stars: royal purple and gold.", Pattern = BatPattern.Stripes, A = C("#3A1478"), B = C("#E8B021"), C = C("#FFFFFF") },
            // Helmets: shell colour and finish.
            new Cosmetic { Id = "helmet_team", Slot = Slot.Helmet, Name = "TEAM", Line = "Your side's colour.", TeamMain = true, Smooth = 0.72f },
            new Cosmetic { Id = "helmet_matte", Slot = Slot.Helmet, Level = 2, Name = "MATTE BLACK", Line = "Flat black shell.", A = C("#17181C"), Smooth = 0.15f },
            new Cosmetic { Id = "helmet_white", Slot = Slot.Helmet, Level = 4, Name = "PEARL WHITE", Line = "Gloss white shell.", A = C("#F2F2F0"), Smooth = 0.85f },
            new Cosmetic { Id = "helmet_chrome", Slot = Slot.Helmet, Level = 5, Name = "CHROME", Line = "Mirror-finish steel.", A = C("#B9C0CA"), Smooth = 0.92f, Metal = 0.9f },
            new Cosmetic { Id = "helmet_gold", Slot = Slot.Helmet, Level = 8, Name = "GOLD", Line = "A gold shell for the big nights.", A = C("#D4A017"), Smooth = 0.85f, Metal = 0.75f },
            // Pads and gloves: the trims, the padding.
            new Cosmetic { Id = "pads_classic", Slot = Slot.Pads, Name = "CLASSIC WHITE", Line = "White pads, team-colour trims.", TeamMain = true, B = C("#F2F2EF") },
            new Cosmetic { Id = "pads_black", Slot = Slot.Pads, Level = 3, Name = "BLACKOUT", Line = "Black pads and gloves, grey trims.", A = C("#5A5E66"), B = C("#1A1B1F") },
            new Cosmetic { Id = "pads_navy", Slot = Slot.Pads, Level = 5, Name = "NAVY", Line = "Navy pads, gold trims.", A = C("#E8B021"), B = C("#1B2A5E") },
            new Cosmetic { Id = "pads_team", Slot = Slot.Pads, Level = 6, Name = "TEAM COLOUR", Line = "Pads and gloves in your side's colour.", TeamMain = true, B = Color.clear },
            new Cosmetic { Id = "pads_gold", Slot = Slot.Pads, Level = 10, Name = "GOLD TRIM", Line = "White pads with gold trims.", A = C("#D4A017"), B = C("#F7F5EE"), Metal = 0.5f },
            // The CELEBRATION types come from Celebrations.cs (Base() is concatenated with them below).
            // Round 12: the ladder to level 50 and the star rewards (CosmeticsLadder.cs).
        };

        public static Cosmetic Default(Slot s) => All.First(c => c.Slot == s);

        public static Cosmetic? Find(string id) => All.FirstOrDefault(c => c.Id == id);
    }
}
