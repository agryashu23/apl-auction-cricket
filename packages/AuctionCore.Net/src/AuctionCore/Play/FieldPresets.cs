using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// The named fields (Auto: the captain's own for the bowler and the phase). Append only — a saved match keeps
    /// the number — so the field rework (owner, 2026-11-16: "choose some good fields from the internet") kept the
    /// old member names and gave them the researched T20 fields below; each member's comment is what it now is.
    /// </summary>
    public enum FieldPreset
    {
        Auto,
        /// <summary>PACE – POWERPLAY ATTACK: two slips for the new ball, third man and fine leg back.</summary>
        Attacking,
        /// <summary>PACE – MIDDLE OVERS: five out, the ring saving the single.</summary>
        Balanced,
        /// <summary>PACE – DEFENSIVE SWEEPERS: five out, sweepers both sides (a chase running away).</summary>
        Defensive,
        /// <summary>PACE – DEATH, YORKERS: long-off, long-on, deep mid-wicket, deep square, fine leg back.</summary>
        Death,
        /// <summary>SPIN – CLOSE CATCHERS (user only): slip, short leg, silly point.</summary>
        SpinAttack,
        /// <summary>PACE – BOUNCER PLAN: fine leg and deep backward square back for the hook.</summary>
        ShortBall,
        /// <summary>PACE – 7-2 OFF-SIDE PACKED: bowl outside off.</summary>
        OffSideTrap,
        /// <summary>PACE – LEG-SIDE TRAP (user only): leg slip, short fine, square leg.</summary>
        LegSideTrap,
        /// <summary>SAVE THE SINGLE: all eight in a tight ring and a slip.</summary>
        SaveOne,
        /// <summary>PACE – DEATH, WIDE YORKER: third man, deep point, long-off, long-on, deep mid-wicket back.</summary>
        DeathOffSide,
        /// <summary>PACE – DEATH, SLOWER BALLS: the leg side and straight on the rope.</summary>
        DeathLegSide,
        /// <summary>LEG-SPIN – MIDDLE OVERS.</summary>
        SpinSweeper,
        /// <summary>OFF-SPIN – MIDDLE OVERS.</summary>
        SpinStraight,
        /// <summary>PACE – MIDDLE, HARD LENGTHS: four out.</summary>
        PaceSquare,
        /// <summary>PACE – POWERPLAY, ONE SLIP: the textbook new-ball field.</summary>
        PowerplayClassic,
        /// <summary>PACE – POWERPLAY, CONTAIN: third man and fine leg up, deep point and deep square back.</summary>
        PowerplayLegSide,
        /// <summary>OFF-SPIN – POWERPLAY.</summary>
        PowerplayStraight,
        /// <summary>ALL IN – CLOSE CATCHERS (user only): slips, gully, short leg, nobody out.</summary>
        AllInAttack,
        /// <summary>LEG-SPIN – POWERPLAY.</summary>
        PaceSlip,
        Custom1, Custom2, Custom3,
        /// <summary>LEFT-ARM SPIN – POWERPLAY.</summary>
        LeftArmPowerplay,
        /// <summary>LEFT-ARM SPIN – MIDDLE OVERS.</summary>
        LeftArmMiddle,
    }

    public sealed partial class Field
    {
        /// <summary>Outside the ring: a field may have two out in the powerplay, five after it.</summary>
        public static int MaxOutside(int phase) => phase == 0 ? 2 : 5;

        /// <summary>T20 playing conditions: never more than five fielders on the leg side.</summary>
        public const int MaxLegSide = 5;

        /// <summary>"PACE – POWERPLAY ATTACK", … for the field buttons and the logs.</summary>
        public static string PresetName(FieldPreset p) => p switch
        {
            FieldPreset.Auto => "AUTO",
            FieldPreset.Attacking => "PACE – POWERPLAY ATTACK",
            FieldPreset.PowerplayClassic => "PACE – POWERPLAY, ONE SLIP",
            FieldPreset.PowerplayLegSide => "PACE – POWERPLAY, CONTAIN",
            FieldPreset.Balanced => "PACE – MIDDLE OVERS",
            FieldPreset.PaceSquare => "PACE – MIDDLE, HARD LENGTHS",
            FieldPreset.Defensive => "PACE – DEFENSIVE SWEEPERS",
            FieldPreset.ShortBall => "PACE – BOUNCER PLAN",
            FieldPreset.OffSideTrap => "PACE – 7-2 OFF-SIDE PACKED",
            FieldPreset.Death => "PACE – DEATH, YORKERS",
            FieldPreset.DeathOffSide => "PACE – DEATH, WIDE YORKER",
            FieldPreset.DeathLegSide => "PACE – DEATH, SLOWER BALLS",
            FieldPreset.SaveOne => "SAVE THE SINGLE",
            FieldPreset.PowerplayStraight => "OFF-SPIN – POWERPLAY",
            FieldPreset.SpinStraight => "OFF-SPIN – MIDDLE OVERS",
            FieldPreset.PaceSlip => "LEG-SPIN – POWERPLAY",
            FieldPreset.SpinSweeper => "LEG-SPIN – MIDDLE OVERS",
            FieldPreset.LeftArmPowerplay => "LEFT-ARM SPIN – POWERPLAY",
            FieldPreset.LeftArmMiddle => "LEFT-ARM SPIN – MIDDLE OVERS",
            FieldPreset.SpinAttack => "SPIN – CLOSE CATCHERS",
            FieldPreset.AllInAttack => "ALL IN – CLOSE CATCHERS",
            FieldPreset.LegSideTrap => "PACE – LEG-SIDE TRAP",
            FieldPreset.Custom1 => "CUSTOM 1",
            FieldPreset.Custom2 => "CUSTOM 2",
            FieldPreset.Custom3 => "CUSTOM 3",
            _ => p.ToString().ToUpperInvariant(),
        };

        /// <summary>The fielding side's fielding (0–100, its XI's average) and its keeper's keeping: direct hits, fumbles, stumpings.</summary>
        public double Fielding { get; set; } = 70;
        public double Keeping { get; set; } = 70;

        // ------------------------------------------------------------- where the men stand

        /// <summary>Boundary riders stand this far inside the rope (the owner: 3–6 m).</summary>
        public const double RopeGap = 3.5;

        /// <summary>Where the keeper stands behind the striker's stumps (m): back to pace, up to spin (KeeperRig's spots).</summary>
        public const double KeeperBackDepth = 4.5, KeeperUpDepth = 0.62;

        /// <summary>A boundary rider at this angle, <see cref="RopeGap"/> inside the standard oval's rope (a venue keeps the gap).</summary>
        public static Fielder Deep(string name, double angleDeg) => new Fielder(name, angleDeg, Math.Round(Boundary(angleDeg) - RopeGap, 1));

        /// <summary>A man at (x across, + off side; f up the pitch from the striker's stumps), batter frame.</summary>
        public static Fielder At(string name, double x, double f) => new Fielder(name, Math.Atan2(x, f) * 180 / Math.PI, Math.Round(Math.Sqrt(x * x + f * f), 2));

        /// <summary>The catchers who stand beside the keeper (slips, gully, leg slip) — their spot follows his depth.</summary>
        public static bool BesideKeeper(string name) => name == "slip" || name == "second slip" || name == "gully" || name == "leg slip";

        /// <summary>
        /// Where a catcher stands for this keeper: slips level with the keeper's gloves and a long stride apart, gully
        /// squarer and a touch in front, leg slip on the keeper's other side; short leg and silly point a bat's reach
        /// and a step in front of the batter. Null: not a close catcher (he keeps his own spot).
        /// </summary>
        public static Fielder? Catcher(string name, bool keeperUp) => (name, keeperUp) switch
        {
            ("slip", false) => At(name, 1.9, -KeeperBackDepth - 0.5),
            ("second slip", false) => At(name, 4.0, -KeeperBackDepth - 0.1),
            ("gully", false) => At(name, 7.5, -KeeperBackDepth + 2.0),
            ("leg slip", false) => At(name, -2.2, -KeeperBackDepth - 0.5),
            ("slip", true) => At(name, 1.5, -1.4),
            ("second slip", true) => At(name, 3.5, -1.8),
            ("gully", true) => At(name, 5.5, -0.6),
            ("leg slip", true) => At(name, -1.6, -1.6),
            ("short leg", _) => At(name, -5.0, 0.9),
            ("silly point", _) => At(name, 5.0, 1.2),
            _ => null,
        };

        /// <summary>Every catcher moved to his spot for this keeper (presets and custom fields alike).</summary>
        private static List<Fielder> ForKeeper(List<Fielder> f, bool keeperUp) => f.Select(x => Catcher(x.Name, keeperUp) ?? x).ToList();

        /// <summary>
        /// A named field as it plays this phase (nine; the keeper and the bowler are extra). A field with more men
        /// out than the phase allows brings the nearest of them up to the ring.
        /// </summary>
        private static List<Fielder> Preset(FieldPreset p, int phase, bool spin, bool keeperUp)
        {
            var f = ForKeeper(Layout(p, spin), keeperUp);
            var outside = f.Where(OutsideRing).OrderBy(x => FromPitch(x.AngleDeg, x.Radius)).ToList();
            for (var i = 0; i < outside.Count - MaxOutside(phase); i++)
            {
                var k = f.IndexOf(outside[i]);
                f[k] = BroughtUp(f, k, outside[i]);
            }
            return f;
        }

        /// <summary>
        /// A deep man brought up to the ring at his angle, or a few degrees round it if a ring man already
        /// stands there (long-off up to the ring would otherwise land on mid-off), named for where he ends up.
        /// Never round behind square on the leg side, and never across to the other side of the wicket.
        /// </summary>
        private static Fielder BroughtUp(List<Fielder> f, int k, Fielder deep)
        {
            Fielder? best = null;
            var bestGap = -1.0;
            foreach (var turn in new[] { 0, 10, -10, 20, -20, 30, -30 })
            {
                var a = deep.AngleDeg + turn;
                if (LegBehindSquare(a) && !LegBehindSquare(deep.AngleDeg)) continue;
                if (Math.Sign(a) != Math.Sign(deep.AngleDeg)) continue;
                var at = Ring(Ringed(deep.Name), a);
                var (x, y) = at.Position;
                var gap = double.MaxValue;
                for (var j = 0; j < f.Count; j++)
                {
                    if (j == k) continue;
                    var (ox, oy) = f[j].Position;
                    gap = Math.Min(gap, Math.Sqrt((x - ox) * (x - ox) + (y - oy) * (y - oy)));
                }
                if (gap >= 8) return at;
                if (gap > bestGap) { bestGap = gap; best = at; }
            }
            return best ?? Ring(Ringed(deep.Name), deep.AngleDeg);
        }

        // The ring and the rope (angles: 0 straight past the bowler, + the batter's off side).
        private static Fielder MidOff => Ring("mid-off", 15);
        private static Fielder ExtraCover => Ring("extra cover", 35);
        private static Fielder Cover => Ring("cover", 55);
        private static Fielder Point => Ring("point", 92);
        private static Fielder ShortThird => Ring("short third", 128);
        private static Fielder MidOn => Ring("mid-on", -15);
        private static Fielder MidWicket => Ring("mid-wicket", -50);
        private static Fielder SquareLeg => Ring("square leg", -88);
        private static Fielder ShortFine => Ring("short fine", -140);
        private static Fielder LongOff => Deep("long-off", 12);
        private static Fielder DeepCover => Deep("deep cover", 50);
        private static Fielder DeepPoint => Deep("deep point", 95);
        private static Fielder ThirdMan => Deep("third man", 145);
        private static Fielder LongOn => Deep("long-on", -12);
        private static Fielder CowCorner => Deep("cow corner", -35);
        private static Fielder DeepMidWicket => Deep("deep mid-wicket", -58);
        private static Fielder DeepSquare => Deep("deep square leg", -86);
        private static Fielder DeepBackSquare => Deep("deep backward square", -110);
        private static Fielder FineLeg => Deep("fine leg", -148);
        // Close catchers: placed by the keeper's depth (Catcher); these are the keeper-back spots.
        private static Fielder Slip => Catcher("slip", false)!;
        private static Fielder SecondSlip => Catcher("second slip", false)!;
        private static Fielder Gully => Catcher("gully", false)!;
        private static Fielder LegSlip => Catcher("leg slip", false)!;
        private static Fielder ShortLeg => Catcher("short leg", false)!;
        private static Fielder SillyPoint => Catcher("silly point", false)!;

        /// <summary>SAVE THE SINGLE's ring: this far from the pitch, a few steps inside the usual ring spot.</summary>
        private const double SaveGap = 22;

        /// <summary>
        /// The researched T20 fields, for a right-hander (a left-hander's is the mirror image: Field.LeftHanded).
        /// Sources: the textbook new-ball field (slip, point, cover, mid-off, mid-on, square leg; third man and fine
        /// leg the two out), the T20 death fields (yorkers: long-off, long-on, deep mid-wicket, deep square, fine leg;
        /// wide yorkers: deep point, third man, long-off, long-on, deep mid-wicket with mid-off and extra cover up),
        /// and the spinners' fields after the powerplay (long-on, long-off, deep mid-wicket, deep square, sweeper cover
        /// back; point, cover, short third / mid-wicket and short fine in the ring).
        /// </summary>
        private static List<Fielder> Layout(FieldPreset p, bool spin) => p switch
        {
            // ---------------------------------------------------------------- pace, powerplay (two out)
            FieldPreset.Attacking => new List<Fielder> { Slip, SecondSlip, Point, Cover, MidOff, MidOn, MidWicket, ThirdMan, FineLeg },
            FieldPreset.PowerplayClassic => new List<Fielder> { Slip, Point, Cover, MidOff, MidOn, MidWicket, SquareLeg, ThirdMan, FineLeg },
            FieldPreset.PowerplayLegSide => new List<Fielder> { ShortThird, Point, Cover, MidOff, MidOn, MidWicket, ShortFine, DeepPoint, DeepSquare },
            // ---------------------------------------------------------------- pace, middle overs
            FieldPreset.Balanced => new List<Fielder> { Point, Cover, MidOff, MidOn, DeepPoint, LongOff, LongOn, DeepMidWicket, FineLeg },
            FieldPreset.PaceSquare => new List<Fielder> { ShortThird, Point, Cover, MidOff, MidWicket, DeepPoint, LongOn, DeepSquare, FineLeg },
            FieldPreset.Defensive => new List<Fielder> { Point, Cover, MidWicket, ShortFine, DeepPoint, DeepCover, LongOff, LongOn, DeepMidWicket },
            FieldPreset.ShortBall => new List<Fielder> { Point, Cover, MidOff, MidWicket, ThirdMan, FineLeg, DeepBackSquare, DeepMidWicket, LongOn },
            FieldPreset.OffSideTrap => new List<Fielder> { ShortThird, Point, Cover, MidOff, DeepPoint, DeepCover, LongOff, MidOn, FineLeg },
            // ---------------------------------------------------------------- pace, death (five out)
            FieldPreset.Death => new List<Fielder> { ShortThird, Cover, MidOff, MidOn, LongOff, LongOn, DeepMidWicket, DeepSquare, FineLeg },
            FieldPreset.DeathOffSide => new List<Fielder> { Point, ExtraCover, MidOff, ShortFine, ThirdMan, DeepPoint, LongOff, LongOn, DeepMidWicket },
            FieldPreset.DeathLegSide => new List<Fielder> { ShortThird, Point, Cover, MidOff, FineLeg, DeepSquare, DeepMidWicket, CowCorner, LongOn },
            FieldPreset.SaveOne => new List<Fielder>
            {
                Ring("short third", 128, SaveGap), Ring("point", 92, SaveGap), Ring("cover", 52, SaveGap),
                Ring("mid-off", 15, SaveGap), Ring("mid-on", -15, SaveGap), Ring("mid-wicket", -52, SaveGap),
                Ring("square leg", -88, SaveGap), Ring("short fine", -140, SaveGap), Slip,
            },
            // ---------------------------------------------------------------- spin (the keeper up: the catchers follow him)
            FieldPreset.PowerplayStraight => new List<Fielder> { ShortThird, Point, Cover, MidOff, ShortFine, MidWicket, MidOn, DeepMidWicket, LongOn },
            FieldPreset.SpinStraight => new List<Fielder> { Point, Cover, MidOn, ShortFine, DeepCover, LongOff, LongOn, DeepMidWicket, DeepSquare },
            FieldPreset.PaceSlip => new List<Fielder> { Slip, Point, Cover, MidOff, MidOn, MidWicket, SquareLeg, LongOff, DeepMidWicket },
            FieldPreset.SpinSweeper => new List<Fielder> { ShortThird, Point, Cover, ShortFine, DeepCover, LongOff, LongOn, DeepMidWicket, DeepSquare },
            FieldPreset.LeftArmPowerplay => new List<Fielder> { Slip, Point, Cover, ExtraCover, MidOff, MidOn, MidWicket, LongOff, DeepMidWicket },
            FieldPreset.LeftArmMiddle => new List<Fielder> { Point, Cover, MidOff, ShortFine, DeepCover, LongOff, LongOn, DeepMidWicket, DeepSquare },
            // ---------------------------------------------------------------- close catchers (the user's to pick, never the AI's)
            FieldPreset.SpinAttack => new List<Fielder> { Slip, ShortLeg, SillyPoint, Cover, MidOff, MidOn, MidWicket, LongOff, DeepMidWicket },
            FieldPreset.AllInAttack => new List<Fielder> { Slip, SecondSlip, Gully, Point, Cover, MidOff, MidOn, MidWicket, ShortLeg },
            FieldPreset.LegSideTrap => new List<Fielder> { LegSlip, ShortFine, SquareLeg, MidWicket, DeepMidWicket, MidOff, Cover, Point, LongOff },
            // ---------------------------------------------------------------- the user's own (an empty slot plays as PACE – MIDDLE OVERS)
            _ when IsCustom(p) && Custom(p) is IReadOnlyList<Fielder> mine => mine.ToList(),
            _ => Layout(FieldPreset.Balanced, spin),
        };

        /// <summary>The ring position for a deep fielder brought up.</summary>
        private static string Ringed(string name) => name switch
        {
            "deep point" => "point",
            "deep cover" => "cover",
            "long-off" => "mid-off",
            "long-on" => "mid-on",
            "deep mid-wicket" => "mid-wicket",
            "deep square" => "square leg",
            "deep square leg" => "square leg",
            "deep backward square" => "backward square",
            "third man" => "short third",
            "fine leg" => "short fine",
            "cow corner" => "mid-wicket",
            _ => name.StartsWith("deep ") ? name.Substring(5) : name,
        };
    }
}
