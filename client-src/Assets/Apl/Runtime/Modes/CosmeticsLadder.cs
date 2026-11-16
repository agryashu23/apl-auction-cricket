using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The unlock ladder past level 10 (round 12: at 18 the rewards screen said "everything's
    /// unlocked"): something every level or two up to 50 — bat designs, helmet and pad
    /// colourways, and titles (shown on the home page's profile and the rewards screen) —
    /// plus star rewards at 12, 24, 30 and 36 challenge stars.
    /// </summary>
    public static class CosmeticsLadder
    {
        private static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;

        private static Cosmetic Bat(string id, int level, string name, string line, BatPattern p, BatFinish f, string a, string b, string c, int stars = 0) =>
            new Cosmetic { Id = "bat_" + id, Slot = Slot.Bat, Level = level, Stars = stars, Name = name, Line = line, Pattern = p, Finish = f, A = C(a), B = C(b), C = C(c) };

        private static Cosmetic Helmet(string id, int level, string name, string line, string a, float smooth, float metal = 0f) =>
            new Cosmetic { Id = "helmet_" + id, Slot = Slot.Helmet, Level = level, Name = name, Line = line, A = C(a), Smooth = smooth, Metal = metal };

        private static Cosmetic Pads(string id, int level, string name, string line, string trim, string pad, float metal = 0f) =>
            new Cosmetic { Id = "pads_" + id, Slot = Slot.Pads, Level = level, Name = name, Line = line, A = C(trim), B = C(pad), Metal = metal };

        private static Cosmetic Title(string id, int level, string name, string line, int stars = 0) =>
            new Cosmetic { Id = "title_" + id, Slot = Slot.Title, Level = level, Stars = stars, Name = name, Line = line };

        public static readonly Cosmetic[] More =
        {
            // Titles: the first is everyone's to start with.
            Title("rookie", 1, "ROOKIE", "Everyone starts somewhere."),
            Title("club", 5, "CLUB CRICKETER", "Weekends at the club ground."),
            Title("state", 10, "STATE PLAYER", "Picked for the state side."),
            Title("regular", 15, "APL REGULAR", "A name on every team sheet."),
            Title("matchwinner", 18, "MATCH WINNER", "The one they turn to."),
            Title("finisher", 21, "FINISHER", "Takes it to the last over, and wins it."),
            Title("powerhitter", 24, "POWER HITTER", "Clears the rope at will."),
            Title("deathover", 27, "DEATH-OVER SPECIALIST", "Yorkers and nerve."),
            Title("captain", 31, "CAPTAIN MATERIAL", "Leads from the front."),
            Title("mvp", 36, "MVP", "Most valuable, every season."),
            Title("icon", 42, "ICON", "The face of the league."),
            Title("legend", 50, "APL LEGEND", "Level 50. Nothing left to prove."),
            Title("challenger", 0, "CHALLENGER", "12 challenge stars.", stars: 12),
            Title("starchaser", 0, "STAR CHASER", "24 challenge stars.", stars: 24),
            Title("perfectionist", 0, "PERFECTIONIST", "36 stars: every challenge, three stars.", stars: 36),
            // Bats.
            Bat("sapphire", 12, "SAPPHIRE", "Sapphire and white, split down the middle.", BatPattern.Split, BatFinish.Willow, "#0F4C9E", "#F2F2F2", "#7FB8FF"),
            Bat("tiger", 14, "TIGER", "Orange and black stripes.", BatPattern.Stripes, BatFinish.Willow, "#E86A10", "#121212", "#FFD23A"),
            Bat("midnight", 17, "MIDNIGHT", "Navy chevrons on a black blade.", BatPattern.Chevron, BatFinish.Carbon, "#1B2A5E", "#E8B021", "#F2F2F2"),
            Bat("aurora", 20, "AURORA", "Northern-lights flames on white lacquer.", BatPattern.Flame, BatFinish.Ice, "#1A0F3A", "#21C7A8", "#B88CFF"),
            Bat("ruby", 25, "RUBY", "The classic graphic in ruby and gold.", BatPattern.Classic, BatFinish.Willow, "#8E0E2A", "#E8B021", "#F2F2F2"),
            Bat("obsidian", 30, "OBSIDIAN", "Black blade, molten flames.", BatPattern.Flame, BatFinish.Carbon, "#0D0D0F", "#D4231B", "#FFB000"),
            Bat("sunrise", 35, "SUNRISE", "A gold blade, split amber and rose.", BatPattern.Split, BatFinish.Gold, "#F08A00", "#E2547A", "#FFFFFF"),
            Bat("legend", 40, "LEGEND", "Black and white stripes on gold leaf.", BatPattern.Stripes, BatFinish.Gold, "#111111", "#F7F7F7", "#D4A017"),
            Bat("halloffame", 50, "HALL OF FAME", "Purple and gold chevrons on gold leaf.", BatPattern.Chevron, BatFinish.Gold, "#3A1478", "#E8B021", "#FFFFFF"),
            Bat("starlight", 0, "STARLIGHT", "For 24 challenge stars.", BatPattern.Chevron, BatFinish.Ice, "#0B1E4A", "#E8E8FF", "#FFD23A", stars: 24),
            Bat("allstar", 0, "ALL-STAR", "For 30 challenge stars.", BatPattern.Stripes, BatFinish.Carbon, "#E8B021", "#1B1F2A", "#FFFFFF", stars: 30),
            Bat("perfect", 0, "PERFECT 36", "For every challenge on three stars.", BatPattern.Classic, BatFinish.Gold, "#3A1478", "#111111", "#FFF1B0", stars: 36),
            // Helmets.
            Helmet("red", 11, "RACING RED", "Gloss red shell.", "#C4121F", 0.85f),
            Helmet("navy", 16, "NAVY GLOSS", "Deep navy, high gloss.", "#13204A", 0.88f),
            Helmet("carbon", 22, "CARBON", "Dark carbon-weave sheen.", "#2A2D33", 0.7f, 0.5f),
            Helmet("emerald", 28, "EMERALD", "Metallic green.", "#0E6A3C", 0.8f, 0.55f),
            Helmet("rosegold", 33, "ROSE GOLD", "Rose-gold metal.", "#C98B7A", 0.88f, 0.8f),
            Helmet("platinum", 45, "PLATINUM", "Brushed platinum.", "#D9DDE3", 0.75f, 0.9f),
            // Pads and gloves.
            Pads("crimson", 13, "CRIMSON TRIM", "White pads, crimson trims.", "#B3121F", "#F5F4EE"),
            Pads("teal", 19, "TEAL", "Teal pads and gloves, white trims.", "#F2F2F2", "#137A7F"),
            Pads("carbon", 26, "CARBON BLACK", "Black pads, carbon-grey trims.", "#3A3D44", "#141518", 0.3f),
            Pads("platinum", 38, "PLATINUM TRIM", "White pads, platinum trims.", "#D9DDE3", "#F7F6F0", 0.8f),
        };
    }
}
