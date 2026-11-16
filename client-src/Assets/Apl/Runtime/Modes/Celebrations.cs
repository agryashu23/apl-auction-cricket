using System.Linq;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The batter's CELEBRATION (owner v26 #11: it was "fifty celebration"): one type the
    /// user picks in THE VAULT, played by his own batters at every milestone they reach
    /// (50, 150, 200 ...; a hundred-only pick — Milestone 100 — at the hundred, where everyone else draws from the
    /// hundred's set: MatchDirector.Milestone.cs PickCelebration) and, from v26, when one of
    /// them hits the winning runs. Each type is a clip stem that exists in BOTH hands'
    /// packs (rhb_ / lhb_ + stem, Resources/Data/cricket/celebrations.json).
    /// ADDING ONE (from a new mocap take): retarget the take as
    /// rhb_bat_&lt;name&gt; and lhb_bat_&lt;name&gt;, list both in celebrations.json, rebuild the
    /// controller, then add ONE row below (id, name, line, stem, unlock level) — the Vault,
    /// the save and the match pick it up from here; nothing else changes.
    /// </summary>
    public static class Celebrations
    {
        public sealed class Kind
        {
            public Kind(string id, string name, string line, string stem, int level, int milestone = 0)
            {
                Id = id;
                Name = name;
                Line = line;
                Stem = stem;
                Level = level;
                Milestone = milestone;
            }

            /// <summary>The cosmetic's id (saved in the career as the equipped Celebration).</summary>
            public string Id { get; }
            public string Name { get; }
            public string Line { get; }
            /// <summary>The clip without the hand prefix: BatterRig.Celebrate adds rhb_ / lhb_.</summary>
            public string Stem { get; }
            public int Level { get; }
            /// <summary>Which milestone it's for: 100 (a hundred only), 50 (fifty, 150, 200...), 0 (any but the hundred, as before).</summary>
            public int Milestone { get; }
        }

        /// <summary>Unlock order; the first is everyone's to start with. Only clips that are in the packs.</summary>
        public static readonly Kind[] All =
        {
            new Kind("celebrate_raise", "RAISE THE BAT", "The classic: bat up to the dressing room.", "bat_fifty_raise_bat", 1),
            new Kind("celebrate_salute", "CROWD SALUTE", "Turns and thanks all four stands with the bat.", "bat_acknowledge_applause", 3),
            new Kind("celebrate_wild", "WILD ONE", "Arms out, fists pumping: the full winning-runs roar.", "bat_winning_runs_celebration", 6),
            // Owner's milestone delivery (2026-11-12, build/motions/stage_milestone): the stems are the files' own names.
            // Generic names only: no real player is ever named in the game.
            new Kind("celebrate_signature", "SIGNATURE POSE", "Bat planted, chin up, hold it, then the bat to the dressing room.", "fifty_signature_pose", 2, 50),
            new Kind("celebrate_bat_drop", "BAT DROP", "Holds the bat out at arm's length and lets it fall. A hundred.", "century_bat_drop", 4, 100),
            new Kind("celebrate_leap_kisses", "LEAP & KISSES", "A big leap, then kisses to all four stands. A hundred.", "century_leap_and_kisses", 5, 100),
            new Kind("celebrate_chin_swipe", "LEAP & CHIN SWIPE", "A leap, then the back of the hand slow along the jaw. A hundred.", "century_leap_and_chin_swipe", 7, 100),
            new Kind("celebrate_roar_bow", "ROAR & BOW", "Roars, helmet off, a deep bow to the crowd. A hundred.", "century_roar_helmet_off_bow", 8, 100),
            new Kind("celebrate_meditation", "MEDITATION", "Sits cross-legged on the pitch, perfectly still. A hundred.", "century_meditation_pose", 9, 100),
            new Kind("celebrate_somersault", "SOMERSAULT", "Tosses the bat aside and flips. A hundred.", "century_somersault", 10, 100),
        };

        /// <summary>The celebration with this id or clip stem (null if none).</summary>
        public static Kind? Find(string idOrStem) => All.FirstOrDefault(k => k.Id == idOrStem || k.Stem == idOrStem);

        /// <summary>The clips anyone may celebrate a milestone with (the AI's batters, or the user's when his pick is for the other milestone): 100 or 50.</summary>
        public static string[] Defaults(int milestone) => milestone == 100
            ? new[] { "bat_hundred_leap_helmet_off" }.Concat(All.Where(k => k.Milestone == 100).Select(k => k.Stem)).ToArray()
            : new[] { "bat_fifty_raise_bat" }.Concat(All.Where(k => k.Milestone == 50).Select(k => k.Stem)).ToArray();

        /// <summary>The cosmetics for the Vault's CELEBRATION tab.</summary>
        public static Cosmetic[] Cosmetics() => All.Select(k => new Cosmetic
        {
            Id = k.Id, Slot = Slot.Celebration, Level = k.Level, Name = k.Name, Line = k.Line, Clip = k.Stem,
        }).ToArray();
    }
}
