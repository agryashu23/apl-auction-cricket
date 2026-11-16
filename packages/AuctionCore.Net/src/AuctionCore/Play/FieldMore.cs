using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// The field's rules (the ring limit for the phase; at most five on the leg side; never more than two behind
    /// square on the leg side, Law 28.4; nobody within 2 m of another) and the CUSTOM FIELD: the nine set on the
    /// named spots of <see cref="Spots"/> by the user, kept in three slots (<see cref="SetCustom"/>; the client saves them).
    /// Angles: 0 straight past the bowler, + the batter's off side; radius metres from the batter.
    /// </summary>
    public sealed partial class Field
    {
        /// <summary>The user's own fields, in the order the field screen shows them.</summary>
        public static readonly FieldPreset[] CustomSlots = { FieldPreset.Custom1, FieldPreset.Custom2, FieldPreset.Custom3 };

        public static bool IsCustom(FieldPreset p) => p >= FieldPreset.Custom1 && p <= FieldPreset.Custom3;

        /// <summary>A man behind square on the leg side (Law 28.4: at most two there when the ball is bowled).</summary>
        public static bool LegBehindSquare(double angleDeg) => angleDeg < -90;

        /// <summary>A man on the leg side (T20 playing conditions: at most five there).</summary>
        public static bool LegSide(double angleDeg) => angleDeg < 0;

        /// <summary>
        /// Straight behind the stumps: within 25° of the line past the keeper. Nobody stands there short of the rope
        /// (no long stop) except the catchers beside the keeper.
        /// </summary>
        public static bool BehindKeeper(Fielder f) => 180 - Math.Abs(f.AngleDeg) < 25;

        /// <summary>The named spots of the custom field editor (~24), as a captain would call them (the catchers by the keeper back; Field.For moves them for a keeper up).</summary>
        public static readonly IReadOnlyList<Fielder> Spots = new List<Fielder>
        {
            // Close catchers.
            Slip, SecondSlip, Gully, LegSlip, ShortLeg, SillyPoint,
            // The ring.
            Point, Cover, ExtraCover, MidOff, MidOn, Ring("mid-wicket", -52), SquareLeg, ShortFine, ShortThird,
            // The deep.
            ThirdMan, DeepPoint, DeepCover, LongOff, LongOn, CowCorner, DeepMidWicket, DeepSquare, FineLeg,
        };

        /// <summary>A spot by its name (null: not one of the editor's). Old saves named deep square leg "deep square".</summary>
        public static Fielder? Spot(string name) => Spots.FirstOrDefault(s => s.Name == (name == "deep square" ? "deep square leg" : name));

        [ThreadStatic] private static Dictionary<FieldPreset, Fielder[]>? _custom;

        /// <summary>The user's field in a CUSTOM slot (null clears it; the client saves them, CustomFields.cs).</summary>
        public static void SetCustom(FieldPreset slot, IEnumerable<Fielder>? nine)
        {
            if (!IsCustom(slot)) throw new ArgumentException("Not a custom slot: " + slot);
            _custom ??= new Dictionary<FieldPreset, Fielder[]>();
            if (nine == null) _custom.Remove(slot);
            else _custom[slot] = nine.ToArray();
        }

        /// <summary>The nine in a CUSTOM slot, null if it's empty.</summary>
        public static IReadOnlyList<Fielder>? Custom(FieldPreset slot) => _custom != null && _custom.TryGetValue(slot, out var f) ? f : null;

        /// <summary>How many of <paramref name="nine"/> are outside the ring.</summary>
        public static int OutsideCount(IEnumerable<Fielder> nine) => nine.Count(OutsideRing);

        /// <summary>How many men a preset has out (its own layout, before any are brought up for the powerplay).</summary>
        public static int OutsideCount(FieldPreset p, bool spin = false) => p == FieldPreset.Auto ? -1 : OutsideCount(Layout(p, spin));

        /// <summary>A preset (or a custom slot) can be set in this phase as it is: nobody needs bringing up.</summary>
        public static bool LegalIn(FieldPreset p, int phase, bool spin = false)
        {
            if (p == FieldPreset.Auto) return true;
            if (IsCustom(p) && Custom(p) == null) return false;
            return Validate(Layout(p, spin), phase).Count == 0;
        }

        /// <summary>
        /// What's wrong with a field for <paramref name="phase"/> (none: it can be set): nine men, no more out than
        /// the phase allows, at most five on the leg side and two of them behind square, nobody on top of anyone else.
        /// </summary>
        public static List<string> Validate(IReadOnlyList<Fielder> nine, int phase)
        {
            var problems = new List<string>();
            if (nine.Count != 9) problems.Add($"Place nine fielders ({nine.Count} placed)");
            var outside = OutsideCount(nine);
            if (outside > MaxOutside(phase)) problems.Add(phase == 0 ? $"Powerplay: only two outside the ring ({outside} out)" : $"Only five outside the ring ({outside} out)");
            var leg = nine.Count(f => LegSide(f.AngleDeg));
            if (leg > MaxLegSide) problems.Add($"Only five on the leg side ({leg} there)");
            var legBehind = nine.Count(f => LegBehindSquare(f.AngleDeg));
            if (legBehind > 2) problems.Add($"Only two behind square on the leg side ({legBehind} there)");
            for (var i = 0; i < nine.Count; i++)
            for (var j = i + 1; j < nine.Count; j++)
            {
                var (ax, af) = nine[i].Position;
                var (bx, bf) = nine[j].Position;
                if ((ax - bx) * (ax - bx) + (af - bf) * (af - bf) < 4) problems.Add($"{nine[i].Name} and {nine[j].Name} stand on top of each other");
            }
            return problems;
        }
    }
}
