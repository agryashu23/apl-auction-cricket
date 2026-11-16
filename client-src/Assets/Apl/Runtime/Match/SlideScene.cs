using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 1 (sliding), LOG ONLY: which of the owner's scenarios a body is in, for the slide meter's per-scenario
    /// table ("[APL slidewatch] SCENARIO …"). The movers tag the body they move (<see cref="Tag"/>: a fielder's walk-in,
    /// a field change, a batter's walk-out…) and keep the tag alive while they move him; a body nobody tagged is
    /// classed from his role and the clip he's in (<see cref="Guess"/>: a bowling action is "bowler delivery", a run
    /// clip "batter running"…). No behaviour: nothing reads this but SlideProbe.
    /// </summary>
    public static class SlideScene
    {
        private static readonly Dictionary<int, (string scene, float until)> Tags = new Dictionary<int, (string, float)>();

        /// <summary>
        /// <paramref name="a"/> is in <paramref name="scene"/> for the next <paramref name="hold"/> s (a mover calling this
        /// every frame keeps it; a one-off play tags for its length). A tag already set is kept by <see cref="Keep"/>.
        /// </summary>
        public static void Tag(Animator? a, string scene, float hold = 0.25f)
        {
            if (a == null || !Debug.isDebugBuild) return;
            Tags[a.GetInstanceID()] = (scene, Time.time + hold);
        }

        /// <summary>The tag he has now (null: none, or it ran out).</summary>
        public static string? Of(Animator? a)
        {
            if (a == null || !Tags.TryGetValue(a.GetInstanceID(), out var t)) return null;
            return Time.time <= t.until ? t.scene : null;
        }

        /// <summary>For a shared mover (Strides.Walk, RunTo, Coast, a turn): keep the caller's tag alive, else tag <paramref name="fallback"/>.</summary>
        public static string? Keep(Animator? a, string? fallback)
        {
            var s = Of(a) ?? fallback;
            if (s != null) Tag(a, s);
            return s;
        }

        /// <summary>Each frame of a shared mover: the scenario it captured at its start (none: nothing tagged, the probe guesses).</summary>
        public static void Hold(Animator? a, string? scene)
        {
            if (scene != null) Tag(a, scene);
        }

        /// <summary>Untagged: the scenario from his role and his clip (top clip of the blend, either side of "&gt;").</summary>
        public static string Guess(string role, string state)
        {
            var s = state;
            bool Has(string k) => s.Contains(k);
            switch (role)
            {
                case "batter":
                    if (Has("walk_out")) return "batter walk-out / walk-in";
                    if (Has("walk_off") || Has("caught_reaction") || Has("bowled_reaction") || Has("lbw_reaction")) return "batter dismissed, walking off";
                    if (Has("fifty") || Has("century") || Has("celebrat")) return "batter milestone celebration";
                    if (Has("run_single") || Has("run_loop_with_bat") || Has("dive_into") || Has("drag_bat") || Has("cancel_run") || Has("settle_after") || Has("turn_180_after_run")) return "batter running between wickets";
                    if (Has("non_striker") || Has("backing_up")) return "batter non-striker";
                    if (Has("warmup") || Has("take_guard")) return "batter guard / warm-up";
                    return "batter at the crease (stance, shots)";
                case "bowler":
                    if (Has("walk_back") || Has("walkback")) return "bowler walk back";
                    if (Has("celebrat") || Has("appeal") || Has("hands_on_head")) return "bowler celebration / appeal";
                    if ((s.StartsWith("ra_") || s.StartsWith("la_") || Has(">ra_") || Has(">la_")) && !Has("intro_")) return "bowler run-up, delivery, follow-through";
                    if (Has("run_0")) return "bowler moving (run loops)";
                    return "bowler other";
                case "keeper":
                    return "keeper";
                case "fielder":
                    if (Has("warmup") || Has("stretch") || Has("jump_and_tap")) return "fielder warm-up";
                    if (Has("celebrat") || Has("applause") || Has("high_five") || Has("appeal") || Has("huddle")) return "fielder celebrating";
                    if (Has("catch_") || Has("field_") || Has("throw_") || Has("seq_") || Has("dive")) return "fielder fielding the ball";
                    if (Has("walk_out")) return "fielder taking the field";
                    if (Has("+turn") && !Has("run_01") && !Has("run_02") && !Has("run_03")) return "fielder turning on the spot";
                    if (Has("+walk")) return "fielder walk (Strides, untagged)";
                    if (Has("+feet")) return "fielder run (feet, untagged)";
                    if (Has("run_0")) return "fielder gait with no mover";
                    return "fielder standing";
                case "umpire":
                    return "umpire";
                default:
                    return "others (crowd, captains, staff)";
            }
        }
    }
}
