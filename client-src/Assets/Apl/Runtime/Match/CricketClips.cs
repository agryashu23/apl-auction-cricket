using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>A batting clip: which way it sends the ball, and when the bat meets it.</summary>
    public sealed class ShotClip
    {
        public string Name = "";

        /// <summary>0 = past the bowler, clockwise from above with the bowler at the top: 0–180 off side.</summary>
        public double Angle;
        public bool Lofted;
        public string Family = "";

        /// <summary>Seconds into the clip that the bat meets the ball, and the clip's length.</summary>
        public float ContactTime, Length;

        /// <summary>The middle of the bat at contact, in the batter's frame (x off side, y up, z toward the bowler); zero if the pack didn't say.</summary>
        public Vector3 Sweet;
    }

    /// <summary>A bowling clip: where the run-up starts and when the ball leaves the hand.</summary>
    public sealed class BowlClip
    {
        public string Name = "", Action = "", Arm = "", Variant = "";
        public bool OverTheWicket;

        /// <summary>A real accelerating run-up (the fast_bowling_actions pack) — played as it is, not sped up.</summary>
        public bool Sprints;
        public float ReleaseTime, Length;

        /// <summary>Start mark: metres behind the bowler's popping crease, and to the bowler's left.</summary>
        public float StartBehind, StartLeft;
    }

    /// <summary>A running clip and the moment the bat is grounded over the crease.</summary>
    public sealed class RunClip
    {
        public string Name = "";
        public float EventTime, Length;
    }

    /// <summary>
    /// A batting clip's authored bat path (Resources/Data/cricket/bat_tracks, written
    /// by tools/motions/export_bat_tracks.py from the generator's own bat channels):
    /// per frame the grip point (the bottom hand on the handle), the axis down the
    /// blade and the face normal, in the generator's frame — metres, Z up, bowler
    /// +X, a right-hander's chest and off side −Y — and whether the top hand is on
    /// the handle. Frame i is clip time i/30 s.
    /// </summary>
    public sealed class BatTrack
    {
        public Vector3[] Grip = System.Array.Empty<Vector3>(), Axis = System.Array.Empty<Vector3>(), Face = System.Array.Empty<Vector3>();
        public bool[] TopHand = System.Array.Empty<bool>();
        public int Frames => Grip.Length;
    }

    /// <summary>A batter's reaction (beaten, struck, dismissed): when its ball event is, and whether it loops.</summary>
    public sealed class ReactClip
    {
        public string Name = "";
        public float EventTime, Length;
        public bool Loop;
    }

    /// <summary>A fielding clip and the moment of the catch / pick-up / release.</summary>
    public sealed class FieldClip
    {
        public string Name = "", Type = "";
        public float EventTime, Length;
        public float BallUp;
    }

    /// <summary>
    /// The batting, bowling and fielding clips' metadata (Resources/Data/cricket),
    /// and the rules for picking one: a shot for the player's input and the
    /// ball, an action for the bowler, a catch for the height of the ball.
    /// </summary>
    public static class CricketClips
    {
        private const float Fps = 30f;
        private static List<ShotClip>? _shots;
        private static List<BowlClip>? _bowls;
        private static List<FieldClip>? _fielding;
        private static List<RunClip>? _runs;
        private static List<ReactClip>? _reactions;
        private static Dictionary<string, BatTrack>? _batTracks;

        /// <summary>The bat path of a batting clip ("lhb_" clips share the right-hander's, mirrored by the rig), or null.</summary>
        public static BatTrack? BatTrackFor(string clip)
        {
            if (_batTracks == null)
            {
                _batTracks = new Dictionary<string, BatTrack>();
                var text = Resources.Load<TextAsset>("Data/cricket/bat_tracks");
                if (text != null)
                {
                    foreach (var kv in (JObject)JObject.Parse(text.text)["clips"]!)
                    {
                        var rows = (JArray)kv.Value!;
                        var t = new BatTrack { Grip = new Vector3[rows.Count], Axis = new Vector3[rows.Count], Face = new Vector3[rows.Count], TopHand = new bool[rows.Count] };
                        for (var i = 0; i < rows.Count; i++)
                        {
                            var r = (JArray)rows[i];
                            float F(int k) => (float)(double)r[k]!;
                            t.Grip[i] = new Vector3(F(0), F(1), F(2));
                            t.Axis[i] = new Vector3(F(3), F(4), F(5));
                            t.Face[i] = new Vector3(F(6), F(7), F(8));
                            t.TopHand[i] = r.Count < 10 || F(9) > 0.5f;
                        }
                        _batTracks[kv.Key] = t;
                    }
                }
            }
            return _batTracks.TryGetValue(clip.StartsWith("lhb_") ? clip.Substring(4) : clip, out var track) ? track : null;
        }
        private static HashSet<string>? _umpire;

        /// <summary>The batter's reactions (Resources/Data/cricket/reactions), per hand ("rhb_" / "lhb_").</summary>
        public static IReadOnlyList<ReactClip> Reactions
        {
            get
            {
                if (_reactions != null) return _reactions;
                _reactions = new List<ReactClip>();
                var text = Resources.Load<TextAsset>("Data/cricket/reactions");
                if (text == null) return _reactions;
                foreach (JObject c in (JArray)JObject.Parse(text.text)["clips"]!)
                {
                    _reactions.Add(new ReactClip
                    {
                        Name = Stem((string)c["file"]!),
                        EventTime = (((int?)c["event_frame"] ?? 1) - 1) / Fps,
                        Length = ((int?)c["frames"] ?? 30) / Fps,
                        Loop = (bool?)c["loop"] ?? false,
                    });
                }
                return _reactions;
            }
        }

        /// <summary>Reaction clip by its name without the hand prefix, e.g. "play_and_miss_drive".</summary>
        public static ReactClip? Reaction(string name, bool leftHanded) => Reactions.FirstOrDefault(r => r.Name == (leftHanded ? "lhb_" : "rhb_") + name);

        /// <summary>Running between the wickets, per hand ("rhb_" / "lhb_" prefixes).</summary>
        public static IReadOnlyList<RunClip> Runs
        {
            get
            {
                if (_runs != null) return _runs;
                _runs = new List<RunClip>();
                var text = Resources.Load<TextAsset>("Data/cricket/running");
                if (text == null) return _runs;
                foreach (JObject c in (JArray)JObject.Parse(text.text)["clips"]!)
                {
                    var ev = (int?)c["event_frame"] ?? 1;
                    _runs.Add(new RunClip { Name = Stem((string)c["file"]!), EventTime = (ev - 1) / Fps, Length = ((int?)c["frames"] ?? 30) / Fps });
                }
                return _runs;
            }
        }

        /// <summary>Running clip by its name without the hand prefix, e.g. "run_single_crease_to_crease".</summary>
        public static RunClip? Run(string name, bool leftHanded) => Runs.FirstOrDefault(r => r.Name == (leftHanded ? "lhb_" : "rhb_") + name);

        /// <summary>Umpire clip names present (signals, idles).</summary>
        public static bool HasUmpire(string name)
        {
            if (_umpire == null)
            {
                _umpire = new HashSet<string>();
                var text = Resources.Load<TextAsset>("Data/cricket/umpire");
                if (text != null)
                {
                    foreach (JObject c in (JArray)JObject.Parse(text.text)["clips"]!) _umpire.Add(Stem((string)c["file"]!));
                }
            }
            return _umpire.Contains(name);
        }

        public static IReadOnlyList<ShotClip> Shots => Load()._shots!;

        /// <summary>The edge / miss clips (78 play-and-miss off, 79 inside edge onto pad, 80 bat-pad pop, 81 chopped on): never picked for an aim, swung when the engine says edge or miss.</summary>
        public static IReadOnlyList<ShotClip> Edges { get { Load(); return _edges; } }
        private static readonly List<ShotClip> _edges = new List<ShotClip>();

        /// <summary>
        /// Round 12: the clip for a ball he got a thin piece of, or missed — instead of the full stroke he asked for
        /// (the rater: "BACKWARD SQUARE DRIVE · EDGED" played the whole drive). Off-side edges flying behind square and
        /// misses outside off: the play-and-miss (the bat goes past the outside edge); inside edges and misses at the
        /// body: onto the pad; an edge into the stumps: chopped on. Null keeps the stroke (a leading / top edge that
        /// balloons is a full swing).
        /// </summary>
        public static ShotClip? EdgeClip(bool edge, bool miss, bool playedOn, bool skied, double exitAngle, bool offSideBall)
        {
            string? name = null;
            if (playedOn) name = "81_chopped_on";
            else if (edge && !skied) name = exitAngle >= 100 ? "78_play_and_miss_off" : exitAngle <= -100 ? "79_inside_edge_onto_pad" : null;
            else if (miss) name = offSideBall ? "78_play_and_miss_off" : "79_inside_edge_onto_pad";
            return name == null ? null : Edges.FirstOrDefault(e => e.Name == name);
        }
        public static IReadOnlyList<BowlClip> Bowls => Load()._bowls!;
        public static IReadOnlyList<FieldClip> Fielding => Load()._fielding!;

        private static (List<ShotClip>? _shots, List<BowlClip>? _bowls, List<FieldClip>? _fielding) Load()
        {
            if (_shots != null) return (_shots, _bowls, _fielding);
            _shots = new List<ShotClip>();
            _edges.Clear();
            _bowls = new List<BowlClip>();
            _fielding = new List<FieldClip>();
            var bat = Resources.Load<TextAsset>("Data/cricket/batting");
            if (bat != null)
            {
                foreach (JObject c in (JArray)JObject.Parse(bat.text)["clips"]!)
                {
                    // The stance idle loop is in the list too; it isn't a shot.
                    if (c["contact_frame"] == null || c["angle_deg"] == null) continue;
                    // The edge / miss clips (78–81) and the footwork moves (82–84) aren't shots to pick for an aim.
                    var family = (string?)c["family"] ?? "";
                    if (family == "footwork") continue;
                    (family == "edge / miss" ? _edges : _shots).Add(new ShotClip
                    {
                        Name = Stem((string)c["file"]!),
                        Angle = (double)c["angle_deg"]!,
                        Sweet = c["sweet_spot_at_contact_m"] is JObject sw
                            ? new Vector3((float)(double)sw["toward_off_side"]!, (float)(double)sw["up"]!, (float)(double)sw["toward_bowler"]!)
                            : Vector3.zero,
                        Lofted = (bool)c["lofted"]!,
                        Family = (string)c["family"]!,
                        ContactTime = ((int)c["contact_frame"]! - 1) / Fps,
                        Length = (int)c["frames"]! / Fps,
                    });
                }
            }
            var bowl = Resources.Load<TextAsset>("Data/cricket/bowling");
            if (bowl != null)
            {
                foreach (JObject c in (JArray)JObject.Parse(bowl.text)["clips"]!)
                {
                    _bowls.Add(new BowlClip
                    {
                        Name = Stem((string)c["file"]!),
                        Action = (string)c["bowler_action"]!,
                        Arm = (string)c["arm"]!,
                        Variant = (string)c["release_variant"]!,
                        OverTheWicket = ((string?)c["side"] ?? "").StartsWith("over"),
                        ReleaseTime = ((int)c["release_frame"]! - 1) / Fps,
                        Length = (int)c["frames"]! / Fps,
                        StartBehind = (float)(double)c["start_position_m"]!["behind_crease"]!,
                        StartLeft = (float)(double)c["start_position_m"]!["left"]!,
                    });
                }
            }
            // The sprint actions (side-on, front-on, slinger): a jog-run-sprint run-up, stock deliveries.
            var sprint = Resources.Load<TextAsset>("Data/cricket/bowling_fast");
            if (sprint != null)
            {
                foreach (JObject c in (JArray)JObject.Parse(sprint.text)["clips"]!)
                {
                    _bowls.Add(new BowlClip
                    {
                        Name = Stem((string)c["file"]!),
                        Action = "fast_" + (string)c["action"]!,
                        Arm = ((string)c["arm"]!).StartsWith("left") ? "left" : "right",
                        Variant = "stock",
                        OverTheWicket = true,
                        Sprints = true,
                        ReleaseTime = ((int)c["release_frame"]! - 1) / Fps,
                        Length = (int)c["frames"]! / Fps,
                        StartBehind = (float)(double)c["start_position_m"]!["behind_crease"]!,
                        StartLeft = (float)(double)c["start_position_m"]!["left"]!,
                    });
                }
            }
            var field = Resources.Load<TextAsset>("Data/cricket/fielding");
            if (field != null)
            {
                foreach (JObject c in (JArray)JObject.Parse(field.text)["clips"]!)
                {
                    var ev = (int?)c["event_frame"] ?? 1;
                    _fielding.Add(new FieldClip
                    {
                        Name = Stem((string)c["file"]!),
                        Type = (string?)c["type"] ?? "",
                        EventTime = (ev - 1) / Fps,
                        Length = ((int?)c["frames"] ?? 30) / Fps,
                        BallUp = (float?)(double?)c["ball_at_event_m"]?["up"] ?? 1f,
                    });
                }
            }
            return (_shots, _bowls, _fielding);
        }

        private static string Stem(string file) => file.EndsWith(".fbx") ? file.Substring(0, file.Length - 4) : file;

        /// <summary>Shots kept for later special-shot buttons (switch hits, reverses, scoops, ramps).</summary>
        private static readonly HashSet<string> Specials = new HashSet<string>
        {
            "26_paddle_sweep", "27_reverse_sweep", "28_fine_reverse_sweep", "29_lofted_reverse_sweep",
            "50_scoop_dilscoop", "51_ramp_keeper", "52_ramp_slips", "53_lap", "54_reverse_lap", "55_reverse_scoop",
            "56_switch_hit_cover", "57_switch_hit_point", "58_switch_hit_third_man", "71_falling_scoop", "72_no_look_ramp",
        };

        private static bool BackFoot(string family) =>
            family.StartsWith("back-foot") || family.StartsWith("cut");

        /// <summary>The clip is played off the back foot (the cut family's 31 is the front-foot cut).</summary>
        public static bool IsBackFoot(ShotClip s) => BackFoot(s.Family) && s.Name != "31_front_foot_cut";

        /// <summary>
        /// Focus batch 1 (owner, BAT-5 "back-foot inputs play sweeps and drives"): the candidates a shot may come
        /// from. The foot is a hard rule — back foot: cuts, pulls, hooks, punches, the back-foot defence and glances;
        /// front foot: everything else — and so is LOFT (lofted clips only) against STROKE / PUSH (ground clips
        /// only); direction then picks within. Only if a rule leaves nothing is it relaxed (loft first, then foot).
        /// Trick shots (SPECIAL) keep their own set.
        /// </summary>
        private static System.Collections.Generic.List<ShotClip> Candidates(ShotType type, bool back, bool special)
        {
            var all = Shots.Where(s => Specials.Contains(s.Name) == special && s.Family != "leave").ToList();
            if (special) return all;
            var foot = all.Where(s => IsBackFoot(s) == back).ToList();
            var both = foot.Where(s => s.Lofted == (type == ShotType.Loft)).ToList();
            return both.Count > 0 ? both : foot.Count > 0 ? foot : all;
        }

        /// <summary>The soft, guided shots a PUSH plays: vertical bat, dead or angled, into a gap.</summary>
        private static readonly HashSet<string> Pushes = new HashSet<string>
        {
            "01_forward_defence", "19_leg_glance", "30_steer_dab", "32_back_foot_defence", "34_back_foot_straight_punch",
            "35_back_foot_off_punch", "36_back_foot_cover_punch", "39_late_cut", "47_back_foot_clip", "48_back_foot_glance",
            "49_nudge_nurdle", "73_soft_hands_drop_off_side", "74_soft_hands_drop_leg_side",
        };

        /// <summary>
        /// The clip for a shot: lofted or not to match the type, front or back
        /// foot to suit the length (or the foot he chose; sweeps and charges only
        /// to spin), the closest direction to the aim. Defend and leave have their
        /// own clips; <paramref name="special"/> picks among the trick shots.
        /// </summary>
        public static ShotClip? PickShot(ShotType type, double engineAngle, Length length, bool spin, Rng? rng = null, bool? backFoot = null, bool special = false, float? ballHeight = null, bool? charged = null)
        {
            if (Shots.Count == 0) return null;
            var back = backFoot ?? length >= Length.Short;
            var want = engineAngle < 0 ? engineAngle + 360 : engineAngle;
            ShotClip? best = null;
            var bestScore = double.MaxValue;
            foreach (var s in Candidates(type, back, special))
            {
                // Direction first: the ball leaves along the clip's own line, so a clip far off the aim
                // is a bat swinging one way and the ball going another. The foot, the push/stroke set
                // and the lofted set only break near-ties (a straight punch for a short ball aimed
                // straight, not a pull to midwicket).
                var diff = Math.Abs(want - s.Angle) % 360;
                if (diff > 180) diff = 360 - diff;
                var score = diff;
                if (BackFoot(s.Family) != back) score += backFoot != null ? 45 : 20;
                if (!special && Pushes.Contains(s.Name) != (type == ShotType.Push)) score += 18;
                if (!special && s.Lofted != (type == ShotType.Loft)) score += 45;
                // Down the track: only when he's charging (batting-feel's footwork; null = the old rule, spin only).
                if (charged is bool c ? (s.Family == "down the track") != (c && type == ShotType.Loft) : s.Family == "down the track" && !spin) score += 80;
                if (s.Family.StartsWith("sweep") && (!spin || charged == true)) score += 80;
                if (s.Family == "helicopter" && length > Length.Full) score += 60;
                // The ball's height where he meets it against the clip's sweet spot: a chest-high punch to a
                // head-high bouncer misses it by the length of a forearm.
                if (ballHeight != null) score += 50 * Mathf.Abs(ballHeight.Value - s.Sweet.y);
                // A little variety between near-equal shots.
                if (rng != null) score += rng.NextDouble() * 6;
                if (score < bestScore)
                {
                    best = s;
                    bestScore = score;
                }
            }
            return best;
        }

        public static ShotClip? Find(string name) => Shots.FirstOrDefault(s => s.Name == name) ?? Edges.FirstOrDefault(s => s.Name == name);

        /// <summary>
        /// The action for a bowler (right-arm; pace by skill, spin by type) and
        /// the variant for this ball (bouncer, yorker, swing, cutter, stock).
        /// </summary>
        public static BowlClip? PickBowl(Player bowler, Delivery d, Rng rng)
        {
            if (Bowls.Count == 0) return null;
            // The arm and the side of the wicket are the engine's (la_ clips for left-armers, *_around_* round the wicket).
            var arm = d.LeftArm ? "left" : "right";
            var over = !d.AroundWicket;
            // Quick bowlers charge in: the sprint actions, one per bowler (side-on, front-on or slinger; over the wicket only).
            var pace = Bowling.PaceOf(bowler);
            // Express and fast men charge in on the sprint actions; the rest by their pace class.
            if (!d.IsSpin && (pace == PaceClass.Express || pace == PaceClass.Fast) && over && (d.Variation == Variation.Stock || d.Variation == Variation.Inswinger || d.Variation == Variation.Outswinger))
            {
                var sprint = Bowls.Where(b => b.Sprints && b.Arm == arm).OrderBy(b => b.Name, StringComparer.Ordinal).ToList();
                if (sprint.Count > 0) return sprint[Stable(bowler.Id) % sprint.Count];
            }
            string action;
            if (d.Kind == DeliveryKind.OffSpin) action = "off_spin";
            else if (d.Kind == DeliveryKind.LegSpin) action = "leg_spin";
            else action = pace switch { PaceClass.Express => "express_fast", PaceClass.Fast => "express_fast", PaceClass.FastMedium => "fast_medium", PaceClass.Medium => "medium", _ => "slow_medium" };
            // Left-armers have the quick, fast-medium and both spin actions: the nearest one.
            if (d.LeftArm && (action == "medium" || action == "slow_medium")) action = "fast_medium";
            var mine = Bowls.Where(b => !b.Sprints && b.Action == action && b.Arm == arm && b.OverTheWicket == over).ToList();
            if (mine.Count == 0) mine = Bowls.Where(b => !b.Sprints && b.Action == action && b.Arm == arm).ToList();
            if (mine.Count == 0) mine = Bowls.Where(b => !b.Sprints && b.Arm == arm && b.OverTheWicket == over && b.Action.Contains(d.IsSpin ? "spin" : "fast")).ToList();
            if (mine.Count == 0) mine = Bowls.Where(b => b.Arm == "right" && b.OverTheWicket).ToList();
            string[] wanted = d.Variation switch
            {
                Variation.Bouncer => new[] { "bouncer" },
                Variation.Yorker => new[] { "yorker" },
                Variation.Slower => new[] { "knuckle", "back_of_hand" },
                Variation.OffCutter => new[] { "off_cutter" },
                Variation.LegCutter => new[] { "leg_cutter" },
                Variation.Inswinger => new[] { "inswinger" },
                Variation.Outswinger => new[] { "outswinger" },
                Variation.ArmBall => new[] { "arm_ball" },
                Variation.Wrong => new[] { "googly", "doosra" },
                Variation.Flipper => new[] { "flipper" },
                Variation.TopSpinner => new[] { "top_spinner" },
                Variation.Carrom => new[] { "carrom" },
                _ when d.Length == Length.Bouncer => new[] { "bouncer" },
                _ when d.Length == Length.Yorker => new[] { "yorker" },
                _ when d.IsSpin => d.Kind == DeliveryKind.OffSpin ? new[] { "off_break" } : new[] { "leg_break" },
                _ => new[] { "stock" },
            };
            var pool = mine.Where(b => wanted.Contains(b.Variant)).ToList();
            if (pool.Count == 0) pool = mine.Where(b => b.Variant == "stock" || b.Variant == "off_break" || b.Variant == "leg_break").ToList();
            if (pool.Count == 0) pool = mine;
            return pool[rng.NextInt(pool.Count)];
        }

        /// <summary>A hash that's the same every run (string.GetHashCode isn't), so a bowler keeps his action.</summary>
        private static int Stable(string s)
        {
            var h = 7;
            foreach (var ch in s) h = (h * 31 + ch) & 0x7fffffff;
            return h;
        }

        /// <summary>A catch for the ball's height at the fielder and how far he had to go.</summary>
        public static FieldClip? PickCatch(float height, float ran)
        {
            string name = ran > 6 ? "catch_08_front_running_catch"
                : height > 2.2f ? "catch_10_jumping_catch"
                : height > 1.4f ? "catch_02_reverse_cup_catch"
                : ran > 2.5f ? "catch_07_diving_forward"
                : "catch_01_cup_catch";
            return Fielding.FirstOrDefault(f => f.Name == name) ?? Fielding.FirstOrDefault(f => f.Type == "catch");
        }

        public static FieldClip? FieldClipNamed(string name) => Fielding.FirstOrDefault(f => f.Name == name);
    }
}
