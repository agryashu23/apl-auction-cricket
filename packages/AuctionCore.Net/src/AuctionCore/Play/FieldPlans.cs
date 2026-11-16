using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>The kinds of bowler a captain sets a field for (left-arm wrist spin turns into a right-hander, like off-spin).</summary>
    public enum FieldBowler { Pace, OffSpin, LegSpin, LeftArmSpin }

    public sealed partial class Field
    {
        /// <summary>The keeper's up to the stumps (always to spin; now and then to a medium pacer): stumpings are on.</summary>
        public bool KeeperUp { get; set; }

        /// <summary>The field screen's tabs: a label and the fields under it (Auto first; the CUSTOM slots apart, <see cref="CustomSlots"/>).</summary>
        public static readonly (string label, FieldPreset[] fields)[] Groups =
        {
            ("AUTO", new[] { FieldPreset.Auto }),
            ("PACE PP", new[] { FieldPreset.Attacking, FieldPreset.PowerplayClassic, FieldPreset.PowerplayLegSide }),
            ("PACE MID", new[] { FieldPreset.Balanced, FieldPreset.PaceSquare, FieldPreset.Defensive, FieldPreset.ShortBall, FieldPreset.OffSideTrap }),
            ("DEATH", new[] { FieldPreset.Death, FieldPreset.DeathOffSide, FieldPreset.DeathLegSide, FieldPreset.SaveOne }),
            ("SPIN", new[] { FieldPreset.PowerplayStraight, FieldPreset.SpinStraight, FieldPreset.PaceSlip, FieldPreset.SpinSweeper }),
            ("LEFT-ARM", new[] { FieldPreset.LeftArmPowerplay, FieldPreset.LeftArmMiddle }),
            ("CATCHERS", new[] { FieldPreset.SpinAttack, FieldPreset.AllInAttack, FieldPreset.LegSideTrap }),
        };

        /// <summary>Every named field the user can pick (Auto first), in the field screen's order.</summary>
        public static readonly FieldPreset[] Presets = Groups.SelectMany(g => g.fields).ToArray();

        /// <summary>The close-catcher fields: the user's to pick, never the AI captain's (owner, build 6).</summary>
        public static bool UserOnly(FieldPreset p) => p == FieldPreset.SpinAttack || p == FieldPreset.AllInAttack || p == FieldPreset.LegSideTrap;

        /// <summary>One line on what the field is for (the field screen's card).</summary>
        public static string PresetNote(FieldPreset p) => p switch
        {
            FieldPreset.Auto => "The captain's field for the bowler and the phase",
            FieldPreset.Attacking => "New ball: two slips, third man and fine leg back",
            FieldPreset.PowerplayClassic => "New ball: a slip, third man and fine leg back",
            FieldPreset.PowerplayLegSide => "Third man and fine leg up, deep point and deep square back",
            FieldPreset.Balanced => "Five out: deep point, long-off, long-on, deep square, fine leg",
            FieldPreset.PaceSquare => "Four out: hit the pitch hard, deep point and deep square back",
            FieldPreset.Defensive => "Five out, sweepers both sides, the single given",
            FieldPreset.ShortBall => "Bouncers: fine leg and deep backward square back for the hook",
            FieldPreset.OffSideTrap => "Seven on the off side: bowl outside off",
            FieldPreset.Death => "Yorkers: long-off, long-on, deep mid-wicket, deep square, fine leg",
            FieldPreset.DeathOffSide => "Wide yorkers: third man, deep point, long-off, long-on, deep mid-wicket",
            FieldPreset.DeathLegSide => "Slower balls: the leg side and straight on the rope",
            FieldPreset.SaveOne => "Everyone in the ring for a last-ball single",
            FieldPreset.PowerplayStraight => "Turning in: leg side sealed, long-on and deep mid-wicket back",
            FieldPreset.SpinStraight => "Five out: long-off, long-on, sweeper, deep mid-wicket, deep square",
            FieldPreset.PaceSlip => "A slip for the leg-break, long-off and deep mid-wicket back",
            FieldPreset.SpinSweeper => "Five out, short third and short fine in the ring",
            FieldPreset.LeftArmPowerplay => "Turning away: a slip, the off side packed, two back",
            FieldPreset.LeftArmMiddle => "Five out, mid-off up for the arm ball",
            FieldPreset.SpinAttack => "Attacking close field: slip, short leg, silly point",
            FieldPreset.AllInAttack => "Attacking close field, nobody out: slips, gully, short leg",
            FieldPreset.LegSideTrap => "Bowl at the pads: leg slip, short fine and square leg",
            _ => IsCustom(p) ? (Custom(p) == null ? "Empty: set it on the field" : "Your own field") : "",
        };

        /// <summary>The kind of bowler a field is set for: pace, or which way his stock ball turns into a right-hander.</summary>
        public static FieldBowler KindOf(Player bowler)
        {
            if (bowler.BowlType != BowlType.Spin) return FieldBowler.Pace;
            var wrist = Bowling.KindFor(bowler) == DeliveryKind.LegSpin;
            var leftArm = Bowling.LeftArm(bowler);
            if (leftArm) return wrist ? FieldBowler.OffSpin : FieldBowler.LeftArmSpin;
            return wrist ? FieldBowler.LegSpin : FieldBowler.OffSpin;
        }

        /// <summary>The captain's standard field for this kind of bowler in this phase (what AUTO plays).</summary>
        public static FieldPreset PlanFor(FieldBowler bowler, int phase) => (bowler, phase) switch
        {
            (FieldBowler.Pace, 0) => FieldPreset.PowerplayLegSide,
            (FieldBowler.Pace, 1) => FieldPreset.Balanced,
            (FieldBowler.Pace, _) => FieldPreset.Death,
            (FieldBowler.OffSpin, 0) => FieldPreset.PowerplayStraight,
            (FieldBowler.OffSpin, _) => FieldPreset.SpinStraight,
            (FieldBowler.LegSpin, 0) => FieldPreset.PaceSlip,
            (FieldBowler.LegSpin, _) => FieldPreset.SpinSweeper,
            (FieldBowler.LeftArmSpin, 0) => FieldPreset.LeftArmPowerplay,
            _ => FieldPreset.LeftArmMiddle,
        };

        /// <summary>The AI captain's field for an over (old callers: pace or off-spin).</summary>
        public static FieldPreset AiPreset(in Situation s, bool spin, int seed) => AiPreset(s, spin ? FieldBowler.OffSpin : FieldBowler.Pace, seed);

        /// <summary>
        /// The AI captain's field for an over (the user batting), chosen once at the start of the over — the match keeps
        /// it for all six balls (QuickMatch.FieldPresetNow), so it changes only with a new over / bowler / phase; a
        /// left-hander gets the same field mirrored. Everyone up to save a last-ball single; sweepers when the chase runs
        /// away; else the researched field for the bowler and the phase, with the new-ball, hard-length, bouncer and
        /// death variations now and then (<paramref name="seed"/> varies it over to over). Never a close-catcher field.
        /// </summary>
        public static FieldPreset AiPreset(in Situation s, FieldBowler bowler, int seed)
        {
            if (s.Target is int t && s.BallsLeft <= 1 && t - s.Runs <= 1) return FieldPreset.SaveOne;
            var plan = PlanFor(bowler, s.Phase);
            var roll = (seed * 2654435761u) % 100;
            if (bowler != FieldBowler.Pace) return plan;
            if (s.Phase == 2) return roll < 65 ? FieldPreset.Death : roll < 90 ? FieldPreset.DeathOffSide : FieldPreset.DeathLegSide;
            if (s.Target != null && s.RequiredRate > 11) return FieldPreset.Defensive;
            if (s.Phase == 0)
            {
                if (s.BatterBalls < 3 && roll < 40) return FieldPreset.Attacking;
                return roll < 50 ? plan : FieldPreset.PowerplayLegSide;
            }
            return roll < 50 ? plan : roll < 85 ? FieldPreset.PaceSquare : FieldPreset.ShortBall;
        }
    }
}
