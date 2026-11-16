using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// How a fielding clip moves him before the ball meets his hands, which
    /// decides how it is turned to the real ball: standing (or a step), a dive
    /// across the line, running in at it, running back under it, chasing it
    /// towards the rope, across, or the leap-and-toss at the rope.
    /// </summary>
    public enum FieldStyle { Stand, Dive, RunIn, RunBack, Chase, Lateral, Boundary }

    /// <summary>
    /// One fielding clip and its moments (Resources/Data/cricket/fielding and
    /// fielding_sequences): the ball meets the hand on <see cref="EventTime"/>
    /// (catch, stop, pick-up) and leaves it on <see cref="ReleaseTime"/> (NaN: the
    /// clip has no throw; ground clips then hand over to a throw clip at
    /// <see cref="ChainAt"/>). Directions are in the fielder's own frame (Unity
    /// local: +z the way he faces at the start, +x his right).
    /// </summary>
    public sealed class SeqClip
    {
        public string Name = "", Kind = "";
        public FieldStyle Style;
        public float EventTime, ReleaseTime = float.NaN, Length, ThrowSpeed = 25f;

        /// <summary>Where the clip throws to, from where he starts.</summary>
        public Vector3 Target = new Vector3(0, 0, 30);

        /// <summary>The hand that meets the ball (neither: both, the ball between them).</summary>
        public bool LeftHand, RightHand;

        /// <summary>Ground clips with no throw of their own: when the throw clip takes over.</summary>
        public float ChainAt = float.NaN;

        /// <summary>Throw clips that say so (owner-side metadata): the ball's secure in the throwing hand by here.</summary>
        public float GatherEnd = float.NaN;

        /// <summary>The boundary catch: tossed up before he crosses the rope, caught again inside.</summary>
        public float TossAt = float.NaN, RecatchAt = float.NaN;

        /// <summary>The dropped catch: it pops out, he picks it up.</summary>
        public float SpillAt = float.NaN, PickupAt = float.NaN;

        /// <summary>The flick back from the rope: released with this velocity (his frame).</summary>
        public float FlickAt = float.NaN;
        public Vector3 FlickVelocity;

        /// <summary>The other half of a two-fielder play, and when the ball reaches the partner's hands (his clip's time).</summary>
        public string Partner = "";
        public float PartnerEvent = float.NaN;

        /// <summary>A shy at the stumps (underarm, backhand flick, on the run, spin throw).</summary>
        public bool Direct;

        /// <summary>Only ever played as half of a pair.</summary>
        public bool PartnerOnly;

        public bool IsCatch => Kind == "catch";
        public bool Collects => Kind == "stop" || Kind == "pickup";
        public bool HasThrow => !float.IsNaN(ReleaseTime);
        public bool Pair => Partner.Length > 0;

        private const float Fps = 30f;
        private static List<SeqClip>? _all;

        /// <summary>Every fielding clip (catches, stops, pick-ups, the throws, the drop and the pairs).</summary>
        public static IReadOnlyList<SeqClip> All
        {
            get
            {
                if (_all != null) return _all;
                _all = new List<SeqClip>();
                LoadSingles(_all);
                LoadSequences(_all);
                return _all;
            }
        }

        public static SeqClip? Named(string name) => All.FirstOrDefault(c => c.Name == name);

        private static float T(JToken? frame) => frame == null ? float.NaN : ((int)frame - 1) / Fps;
        private static float T(int frame) => (frame - 1) / Fps;

        private static string Stem(string file) => file.EndsWith(".fbx") ? file.Substring(0, file.Length - 4) : file;

        /// <summary>The single clips (catches 01–11, ground fielding 12–17, throws 18–19).</summary>
        private static void LoadSingles(List<SeqClip> all)
        {
            var text = Resources.Load<TextAsset>("Data/cricket/fielding");
            if (text == null) return;
            foreach (JObject c in (JArray)JObject.Parse(text.text)["clips"]!)
            {
                var name = Stem((string)c["file"]!);
                var type = (string?)c["type"] ?? "";
                // (Area 3: the owner-side throws — crow hop, side-arm snap, turn-and-throw — give release_frame, not event_frame.)
                var ev = c["event_frame"] ?? (type == "throw" ? c["release_frame"] : null);
                if (ev == null) continue;
                var clip = new SeqClip
                {
                    Name = name,
                    EventTime = T(ev),
                    Length = ((int?)c["frames"] ?? 30) / Fps,
                };
                if (type == "throw")
                {
                    clip.Kind = "throw";
                    clip.ReleaseTime = clip.EventTime;
                    clip.ThrowSpeed = (float?)c["throw_speed_m_s"] ?? (name.Contains("underarm") ? 12f : 28f);
                    if (c["gather_end_frame"] != null) clip.GatherEnd = T(c["gather_end_frame"]);
                }
                else if (type == "catch")
                {
                    clip.Kind = "catch";
                    clip.Style = name.StartsWith("catch_05") || name.StartsWith("catch_06") ? FieldStyle.Dive
                        : name.StartsWith("catch_07") || name.StartsWith("catch_08") ? FieldStyle.RunIn
                        : name.StartsWith("catch_09") ? FieldStyle.RunBack
                        : name.StartsWith("catch_11") ? FieldStyle.Boundary
                        : FieldStyle.Stand;
                    if (c["toss"] is JObject toss)
                    {
                        clip.TossAt = T(toss["release_frame"]);
                        clip.RecatchAt = T(c["recatch_frame"]);
                    }
                }
                else if (type == "ground fielding")
                {
                    clip.Kind = name.Contains("pickup") ? "pickup" : "stop";
                    clip.Style = name.Contains("dive") ? FieldStyle.Dive
                        : name.Contains("slide") ? FieldStyle.Chase
                        : name.Contains("pickup") ? FieldStyle.RunIn
                        : FieldStyle.Stand;
                    // Up and throwing once he's got hold of it (frames from the clips' review sheets).
                    clip.ChainAt = name.StartsWith("field_12") ? T(70)
                        : name.StartsWith("field_13") ? T(55)
                        : name.StartsWith("field_14") ? T(45)
                        : name.StartsWith("field_17") ? T(100)
                        : T(95);
                }
                else continue;
                Hands(clip);
                all.Add(clip);
            }
        }

        /// <summary>The whole-play sequences (20–37), including the two-fielder flick-back and relay.</summary>
        private static void LoadSequences(List<SeqClip> all)
        {
            var text = Resources.Load<TextAsset>("Data/cricket/fielding_sequences");
            if (text == null) return;
            foreach (JObject c in (JArray)JObject.Parse(text.text)["clips"]!)
            {
                var name = Stem((string)c["file"]!);
                var clip = new SeqClip
                {
                    Name = name,
                    Length = (int)c["frames"]! / Fps,
                    ReleaseTime = T(c["release_frame"]),
                    ThrowSpeed = (float?)c["throw_speed_m_s"] ?? 25f,
                    Target = Local(c["throw_target_m"]),
                };
                clip.Target.y = 0;
                if (c["catch_attempt_frame"] != null)
                {
                    clip.Kind = "drop";
                    clip.EventTime = T(c["catch_attempt_frame"]);
                    clip.SpillAt = T(c["ball_spills_frame"]);
                    clip.PickupAt = T(c["pickup_frame"]);
                    clip.Style = FieldStyle.Stand;
                }
                else if (c["flick_release_frame"] != null)
                {
                    // seq_29 and 30a: the same slide-and-flick (30a has no collect frame of its own; 29's is 54).
                    clip.Kind = "flick";
                    clip.EventTime = c["collect_frame"] != null ? T(c["collect_frame"]) : T(54);
                    clip.FlickAt = T(c["flick_release_frame"]);
                    clip.FlickVelocity = Local(c["flick_velocity_m_s"]);
                    clip.Style = FieldStyle.Chase;
                    clip.ReleaseTime = float.NaN;
                    if (name.StartsWith("seq_30a"))
                    {
                        clip.Partner = "seq_30b_teammate_pickup_throw_fielder2";
                        clip.PartnerEvent = T(88);
                    }
                    else clip.PartnerOnly = true;
                }
                else
                {
                    var ev = c["pickup_frame"] ?? c["stop_frame"] ?? c["catch_frame"];
                    if (ev == null) continue;
                    clip.EventTime = T(ev);
                    clip.Kind = c["catch_frame"] != null ? "catch" : c["stop_frame"] != null ? "stop" : "pickup";
                    clip.Style = name.Contains("dive") || name.Contains("diving") ? FieldStyle.Dive
                        : name.Contains("chase_back") || name.Contains("back_running") ? FieldStyle.RunBack
                        : name.Contains("chase_left") || name.Contains("chase_right") ? FieldStyle.Lateral
                        : name.Contains("slide") ? FieldStyle.Chase
                        : name.Contains("long_barrier") ? FieldStyle.Stand
                        : FieldStyle.RunIn;
                    clip.Direct = name.Contains("at_stumps") || name.Contains("on_the_run") || name.Contains("spin_throw");
                    if (c["role"] != null)
                    {
                        // The relay: the deep fielder leads, the middle fielder is placed in the lead's frame.
                        if (name.StartsWith("seq_31a"))
                        {
                            clip.Partner = "seq_31b_relay_middle_fielder";
                            clip.PartnerEvent = T(98);
                        }
                        else clip.PartnerOnly = true;
                    }
                }
                Hands(clip);
                all.Add(clip);
            }
        }

        /// <summary>A sequence-frame point (x = his left, forward is negative y, z up) in Unity local.</summary>
        private static Vector3 Local(JToken? p) => p == null
            ? new Vector3(0, 0, 30)
            : new Vector3(-(float)p["x"]!, p["z"] != null ? (float)p["z"]! : 0, -(float)p["y"]!);

        /// <summary>One-handed and diving clips meet the ball with the hand on their side; pick-ups on the run with the right.</summary>
        private static void Hands(SeqClip c)
        {
            var n = c.Name;
            c.LeftHand = n.StartsWith("catch_03") || n.StartsWith("catch_05") || n.StartsWith("field_15") || n.StartsWith("seq_25");
            c.RightHand = n.StartsWith("catch_04") || n.StartsWith("catch_06") || n.StartsWith("field_16") || n.StartsWith("seq_26")
                || n.StartsWith("field_14") || n.StartsWith("field_17") || (n.StartsWith("seq_") && c.Kind == "pickup");
        }
    }

    /// <summary>What the planner knows about the ball and the play around it.</summary>
    public sealed class FieldBall
    {
        /// <summary>World position of the ball on its own, t s after the hit.</summary>
        public Func<float, Vector3> At = null!;

        /// <summary>Metres inside the rope (negative: over it) of a ground point.</summary>
        public Func<Vector3, float> RopeGap = null!;

        /// <summary>Seconds from the hit for this fielder to run a distance.</summary>
        public Func<float, float> RunTime = null!;

        /// <summary>Where it was hit from, and when the engine has it fielded / caught.</summary>
        public Vector3 From;
        public float Due;

        /// <summary>Catches: when it would come down, and its highest point.</summary>
        public float Hang, Apex;

        /// <summary>Where the throw goes, and whether it's a tight run (have a shy at the stumps).</summary>
        public Vector3 Target;
        public bool Tight;

        /// <summary>The engine had him stop it with a dive.</summary>
        public bool Dived;

        /// <summary>When the engine has the throw leave his hand (s after the hit; NaN: not known) — area 3, MatchDirector.Gather.</summary>
        public float ReleaseBy = float.NaN;

        /// <summary>The engine has a batter out on this throw: a release later than its moment costs far more (area 3, run-outs).</summary>
        public bool RunOut;

        /// <summary>0–1: picks among near-equal options so the same ball isn't always fielded the same way.</summary>
        public float Variety;
    }

    /// <summary>
    /// Plans and plays a piece of fielding: picks the clip whose run-in, facing
    /// and throw best fit the real ones, turns and places it so the collecting
    /// hand is on the ball at the event frame (sampled on this body), runs the
    /// fielder (in code) to where the clip takes over, then carries the ball in
    /// his hand(s) — through a drop, a toss at the rope, a flick back or a relay
    /// to a team-mate — to the release, and throws it.
    /// </summary>
    public sealed partial class FieldPlay
    {
        private const float Join = 0.55f;

        public Animator Fielder = null!;
        public SeqClip Clip = null!;

        /// <summary>Seconds after the hit the hand meets the ball (the ball is timed to it), and where.</summary>
        public float EventAt;
        public Vector3 BallAt;

        /// <summary>Where the clip is played from, and where / when (s after the hit) his run hands over to it.</summary>
        public Vector3 Place;
        public Quaternion Turn = Quaternion.identity;
        public Vector3 JoinAt;
        public float JoinTime, JoinClip;

        /// <summary>The clip joins him mid-stride (keep running into it, don't pull up).</summary>
        public bool Moving;

        /// <summary>The team-mate in a flick-back or relay (his clip starts with the lead's).</summary>
        public FieldPlay? Partner;

        /// <summary>Where the throw goes (world), and whoever holds the ball now.</summary>
        public Vector3 Target;
        public Animator Thrower = null!;

        public float Score;

        /// <summary>A catch: the ball's own flight time (s after the hit) at <see cref="BallAt"/>, the planned meeting point.</summary>
        public float BallTime = float.NaN;

        /// <summary>A catch plan he can't make in time (the best of the late ones): by this much (s). 0 = in time.</summary>
        public float LateBy;

        /// <summary>A catch plan: the time he has in hand at the take with this clip (s), for the clip-choice log.</summary>
        public float Spare;
        public bool Done, Thrown;

        /// <summary>Catches: the director takes the ball back (the celebration) when this is set.</summary>
        public bool LetGo;

        /// <summary>
        /// The boundary catch passed on: instead of catching his own toss he lobs it in to a
        /// team-mate, whose hands it reaches (<see cref="TossTo"/>) at <see cref="TossCaughtAt"/> s after the hit.
        /// </summary>
        public Func<Vector3>? TossTo;
        public float TossCaughtAt;
        public Transform? TossHand;

        /// <summary>Where the throw will actually go, asked live (the end and the man taking it can change after the plan).</summary>
        public Func<Vector3>? Aim;

        /// <summary>
        /// Area 3 (gather and throw). <see cref="ReleaseBy"/>: when the engine has the throw leave his hand (s after the
        /// hit; NaN: not known) — a throw clip is started so its release frame lands there, never before the gather.
        /// Set as it happens, for the log: the throw clip, when the throwing man's hands met it and when the ball was
        /// in his throwing hand (s after the hit).
        /// </summary>
        public float ReleaseBy = float.NaN;
        public string ThrowClip = "";
        public float TakenAt = float.NaN, GatherDone = float.NaN, ReleaseEta = float.NaN;

        /// <summary>A clip with its own throw: its playback rate per frame from the take to the release (snap turns slowed), and the time that adds (s).</summary>
        private float[]? _pace;
        public float PaceDelay;

        /// <summary>Seconds after the hit for a time in this clip.</summary>
        public float HitTime(float clipTime) => EventAt + clipTime - Clip.EventTime;

        /// <summary>Seconds after the hit the ball leaves the last hand (NaN: it doesn't).</summary>
        public float ReleaseAt => Partner != null && Partner.Clip.HasThrow ? Partner.HitTime(Partner.Clip.ReleaseTime) : Clip.HasThrow ? HitTime(Clip.ReleaseTime) : float.NaN;

        /// <summary>Everyone who touches the ball in this play.</summary>
        public IEnumerable<Animator> Fielders => Partner != null ? new[] { Fielder, Partner.Fielder } : new[] { Fielder };

        // ------------------------------------------------------------ planning

        /// <summary>
        /// The best catch for this fielder: the clip, turn and place where his
        /// hands meet the falling ball at the hands' own height, in time (or, if
        /// none gets him there, the nearest miss — he runs a little quicker).
        /// <paramref name="clips"/> narrows the choice (the drop is its own).
        /// </summary>
        /// <summary>Area 3, dev 2: seconds in hand a catch plan needs when he has a run to make first (the reaction turn and start-up).</summary>
        public const float CatchRunSlack = 0.35f;

        public static FieldPlay? PlanCatch(Animator a, FieldBall b, IEnumerable<SeqClip>? clips = null)
        {
            var from = Flat(a.transform.position);
            FieldPlay? best = null, latest = null;
            var lateBy = float.MaxValue;
            foreach (var clip in clips ?? SeqClip.All.Where(c => c.IsCatch && !c.PartnerOnly))
            {
                if (!Has(a, clip)) continue;
                var hand = Hand(Sample(a, clip, clip.EventTime), clip);
                var meet = Descent(b, hand.y);
                if (meet == null) continue;
                var (t, ball) = meet.Value;
                var faceYaw = Yaw(b.From - ball);
                var approach = Flat(ball) - from;
                var run = ClipRun(a, clip);
                var yaws = new List<float> { faceYaw };
                if (run.magnitude > 0.5f && approach.magnitude > 1f) yaws.Add(Yaw(approach) - Yaw(run));
                foreach (var yaw in yaws)
                {
                    var p = Fit(a, from, clip, ball, yaw, b.RunTime);
                    var margin = t - p.EventAt;
                    p.Score = CatchCost(clip, b, ball, approach, yaw, faceYaw, run, margin, a);
                    p.Spare = margin;
                    p.EventAt = t;
                    p.BallTime = t;
                    p.BallAt = ball;
                    p.JoinTime = t - (clip.EventTime - p.JoinClip);
                    p.Target = b.Target;
                    // (Area 3, dev 2 — build 5 real play: a dive planned with 0.0 s to spare started after the catch, the
                    // run there arriving 0.3 s late (the turn and start-up the planner's sprint time leaves out), and the
                    // ball was dragged 1.37 m to his hands. A plan with a run to make keeps CatchRunSlack in hand.)
                    var need = Vector3.Distance(from, p.JoinAt) > 2f ? CatchRunSlack : -0.05f;
                    if (margin >= need)
                    {
                        if (best == null || p.Score < best.Score) best = p;
                    }
                    else if (need - margin < lateBy)
                    {
                        lateBy = need - margin;
                        p.LateBy = need - margin;
                        latest = p;
                    }
                }
            }
            return best ?? latest;
        }

        /// <summary>
        /// The best way to collect a ball along the ground: the earliest time
        /// (from about when the engine has it fielded) that a clip gets his hand
        /// to it, weighed for how well its run, facing and throw fit.
        /// </summary>
        public static FieldPlay? PlanGround(Animator a, FieldBall b, IEnumerable<SeqClip>? clips = null)
        {
            var from = Flat(a.transform.position);
            var options = (clips ?? SeqClip.All.Where(c => c.Collects && !c.PartnerOnly && !c.Pair)).Where(c => Has(a, c)).ToList();
            FieldPlay? best = null, nearest = null;
            var found = new HashSet<(string, int)>();
            // The engine has his hands on it at Due: no later (the ball mustn't run on past him).
            for (var t = Mathf.Max(0.2f, b.Due - 0.4f); t <= b.Due + 0.15f; t += 0.05f)
            {
                var ball = b.At(t);
                if (b.RopeGap(ball) < 0.2f) break;
                var vel = Flat(b.At(t + 0.05f) - ball) / 0.05f;
                var approach = Flat(ball) - from;
                var faceYaw = vel.magnitude > 0.5f ? Yaw(-vel) : Yaw(b.Target - ball);
                foreach (var clip in options)
                {
                    var run = ClipRun(a, clip);
                    var throwYaw = Yaw(b.Target - ball) - Yaw(ThrowDir(a, clip));
                    var runYaw = run.magnitude > 0.5f && approach.magnitude > 1f ? Yaw(approach) - Yaw(run) : faceYaw;
                    var yaws = clip.Style == FieldStyle.Stand || clip.Style == FieldStyle.Dive
                        ? new[] { faceYaw, Mid(faceYaw, throwYaw) }
                        : new[] { runYaw, Mid(runYaw, throwYaw), Mid(runYaw, faceYaw) };
                    for (var i = 0; i < yaws.Length; i++)
                    {
                        if (found.Contains((clip.Name, i))) continue;
                        var p = Fit(a, from, clip, ball, yaws[i], b.RunTime);
                        if (p.EventAt > t + 0.02f)
                        {
                            // Not in time with this one: keep the closest miss at the engine's moment. (Area 3, build 4: most
                            // seq_23 picks and 15/16 engine-dived balls came through HERE, where no throw-fit cost applied — the
                            // quickest clip to the ball won. Now the miss is weighed with the same throw fit as an in-time pick.)
                            var missCost = (p.EventAt - t) * (ThrowCraft.On ? 60f : 1f)
                                + (ThrowCraft.On ? ThrowFitCost(clip, b, t, Vector3.Distance(Flat(ball), Flat(b.Target)), clip.HasThrow ? Mathf.Abs(Mathf.DeltaAngle(yaws[i], throwYaw)) : 0f) : 0f);
                            if (Mathf.Abs(t - b.Due) < 0.03f && (nearest == null || missCost < nearest.Score))
                            {
                                p.Score = missCost;
                                p.PlanNote = $"late by {p.EventAt - t:0.00} s, cost {missCost:0}";
                                p.BallAt = ball;
                                nearest = p;
                            }
                            continue;
                        }
                        // The earliest moment this clip and turn get there is the one to judge.
                        found.Add((clip.Name, i));
                        var score = GroundCost(clip, b, ball, vel, approach, yaws[i], faceYaw, throwYaw, run, t, a);
                        if (best != null && score >= best.Score) continue;
                        p.EventAt = t;
                        p.BallAt = ball;
                        p.JoinTime = t - (clip.EventTime - p.JoinClip);
                        p.Score = score;
                        p.Target = b.Target;
                        p.PlanNote = $"in time, cost {score:0}";
                        best = p;
                    }
                }
            }
            if (best != null || nearest == null) return best;
            // Nothing quite gets there: the nearest, on time with the ball (he runs a little harder, the clip catches up).
            nearest.EventAt = b.Due;
            nearest.JoinTime = b.Due - (nearest.Clip.EventTime - nearest.JoinClip);
            nearest.Target = b.Target;
            nearest.Score = 200;
            return nearest;
        }

        /// <summary>
        /// A two-fielder play when one fits: the flick back from the rope (the
        /// ball run down within a stride or two of it) or the relay (collected a
        /// long way from the throw's end). The partner is whichever of
        /// <paramref name="mates"/> gets to his start in time.
        /// </summary>
        public static FieldPlay? PlanPair(Animator a, FieldBall b, IEnumerable<Animator> mates)
        {
            FieldPlay? best = null;
            foreach (var lead in SeqClip.All.Where(c => c.Pair))
            {
                var partnerClip = SeqClip.Named(lead.Partner);
                if (partnerClip == null || !Has(a, lead)) continue;
                var p = PlanGround(a, b, new[] { lead });
                if (p == null) continue;
                var flick = lead.Kind == "flick";
                if (flick ? b.RopeGap(p.BallAt) > 3.5f : Vector3.Distance(Flat(p.BallAt), Flat(b.Target)) < 55f) continue;
                if (!flick)
                {
                    // The relay is turned square to the line home (both throws go straight down it).
                    var q = Fit(a, Flat(a.transform.position), lead, p.BallAt, Yaw(b.Target - p.BallAt) - Yaw(ThrowDir(a, lead)), b.RunTime);
                    if (q.EventAt > p.EventAt + 0.02f) continue;
                    q.EventAt = p.EventAt;
                    q.BallAt = p.BallAt;
                    q.JoinTime = q.EventAt - (lead.EventTime - q.JoinClip);
                    q.Target = b.Target;
                    p = q;
                }
                var bestRun = float.MaxValue;
                foreach (var m in mates)
                {
                    if (m == a || !Has(m, partnerClip)) continue;
                    var start = Flat(p.Place + p.Turn * Sample(m, partnerClip, p.JoinClip).Hips);
                    var dist = Vector3.Distance(Flat(m.transform.position), start);
                    if (b.RunTime(dist) > p.JoinTime + 0.15f || dist >= bestRun) continue;
                    bestRun = dist;
                    var mate = new FieldPlay
                    {
                        Fielder = m,
                        Clip = partnerClip,
                        Place = p.Place,
                        Turn = p.Turn,
                        JoinAt = start,
                        JoinClip = p.JoinClip,
                        JoinTime = p.JoinTime,
                        Target = b.Target,
                        EventAt = p.HitTime(lead.PartnerEvent),
                    };
                    mate.BallAt = mate.World(Hand(Sample(m, partnerClip, lead.PartnerEvent), partnerClip));
                    p.Partner = mate;
                }
                if (p.Partner == null) continue;
                // The flick is to save four (always worth it); the relay for some of the long throws.
                p.Score = flick ? -100 : b.Variety < 0.65f ? -50 : 50;
                if (best == null || p.Score < best.Score) best = p;
            }
            return best;
        }

        /// <summary>Sample the clips this body may need, so the first plan doesn't stall a frame.</summary>
        public static void Warm(Animator a)
        {
            AnimTrace.Note(a, "FieldPlay.Warm"); // FLD-1 log only
            foreach (var c in SeqClip.All)
            {
                if (!Has(a, c)) continue;
                Sample(a, c, c.EventTime);
                Sample(a, c, JoinOf(c));
                Sample(a, c, Mathf.Max(0, JoinOf(c) - 0.1f));
            }
            WarmPace(a); // (area 3: the own-throw clips' snap pace, one clip a call — FieldPlay.Throw)
        }

        /// <summary>
        /// The way a clip's throw really goes, in his own frame: the throwing (right) hand's
        /// travel through the release frame, sampled on this body — not the metadata's
        /// target, which can be off (and is for the side-on crow hops).
        /// </summary>
        public static Vector3 ThrowDir(Animator a, SeqClip clip)
        {
            if (!clip.HasThrow) return clip.Target.normalized;
            var r1 = Sample(a, clip, clip.ReleaseTime).Right;
            var r0 = Sample(a, clip, Mathf.Max(0, clip.ReleaseTime - 0.1f)).Right;
            var d = Flat(r1 - r0);
            return d.magnitude > 0.15f ? d.normalized : clip.Target.normalized;
        }

        /// <summary>
        /// Planted, he turns his body (about his hips) so the throw he's about to make
        /// goes at <see cref="Aim"/>: a step of the turn left to make by <paramref name="by"/>
        /// (s after the hit). Call every frame from the take to the release.
        /// </summary>
        private void TurnToAim(Animator who, SeqClip clip, float now, float by)
        {
            if (Aim == null || !clip.HasThrow) return;
            var hips = who.GetBoneTransform(HumanBodyBones.Hips);
            var pivot = hips != null ? Flat(hips.position) : Flat(who.transform.position);
            var hand = who.GetBoneTransform(HumanBodyBones.RightHand);
            var from = hand != null ? Flat(hand.position) : pivot;
            var to = Flat(Aim() - from);
            if (to.sqrMagnitude < 1f) return;
            var dir = who.transform.rotation * ThrowDir(who, clip);
            var delta = Mathf.DeltaAngle(Yaw(dir), Yaw(to));
            var step = delta * Mathf.Clamp01(Time.deltaTime / Mathf.Max(Time.deltaTime, by - now));
            // Area 3: a body's pace — with the clip's own turn never over 650°/s together (build 3: stopping 0.08 s early
            // left seq_21/22 18–31° off; the clip's own whip now holds the code turn back instead) — about his planted foot.
            if (ThrowCraft.On) ThrowCraft.TurnCapped(who, step);
            else who.transform.RotateAround(pivot, Vector3.up, step);
        }

        /// <summary>Both hands at <paramref name="time"/> in any clip, in his own frame (sampled on this body, cached).</summary>
        public static (Vector3 left, Vector3 right) HandsAt(Animator a, string clip, float length, float time)
        {
            var p = Sample(a, new SeqClip { Name = clip, Length = length }, time);
            return (p.Left, p.Right);
        }

        private static float Mid(float a, float b) => a + Mathf.DeltaAngle(a, b) * 0.5f;

        /// <summary>When the falling ball is at <paramref name="height"/> (the hands' height on the catch frame), and where.</summary>
        private static (float t, Vector3 ball)? Descent(FieldBall b, float height)
        {
            var hi = b.Hang;
            var lo = b.Hang * 0.45f;
            if (b.At(lo).y < height) return null;
            for (var i = 0; i < 24; i++)
            {
                var mid = (lo + hi) / 2;
                if (b.At(mid).y >= height) lo = mid;
                else hi = mid;
            }
            return (lo, b.At(lo));
        }

        private static float CatchCost(SeqClip clip, FieldBall b, Vector3 ball, Vector3 approach, float yaw, float faceYaw, Vector3 run, float margin, Animator a)
        {
            var n = clip.Name;
            var toBatter = Flat(b.From - ball).normalized;
            // + he runs in towards the batter, − back towards the rope.
            var inward = Vector3.Dot(approach, toBatter);
            var steep = b.Apex > 14f;
            var flat = b.Apex < 6f;
            var score = Mathf.Abs(Mathf.DeltaAngle(yaw, faceYaw)) * (clip.Style == FieldStyle.Stand || clip.Style == FieldStyle.Dive ? 0.8f : 0.15f);
            if (run.magnitude > 0.5f && approach.magnitude > 1f) score += Mathf.Abs(Mathf.DeltaAngle(Yaw(approach), yaw + Yaw(run))) * 0.4f;
            switch (clip.Style)
            {
                case FieldStyle.Stand:
                    // Under it in time: the clean catch. Tight: something more athletic.
                    score += margin < 0.3f ? 30 : 0;
                    if (n.StartsWith("catch_02")) score += steep ? -6 : 4;
                    else if (n.StartsWith("catch_10")) score += flat ? 8 : 90;
                    else if (n.StartsWith("catch_03") || n.StartsWith("catch_04")) score += flat ? 6 : 22;
                    // (Area 3, dev 2: the low cup takes it at his belly, where the camera loses the ball even from the
                    // front — review b3. A take higher up is preferred when one fits.)
                    else if (n.StartsWith("catch_01")) score += 10;
                    // (Review b4: the skier's cup takes it upright at his hip — a body hit by eye. Its reverse-cup twin, hands up.)
                    else if (n.StartsWith("catch_15_skier_judged_cup")) score += 40;
                    else score += steep ? 2 : 0;
                    break;
                case FieldStyle.Dive:
                    score += margin > 0.6f ? 45 : 18;
                    if (clip.HasThrow) score += b.Variety < 0.5f ? -4 : 4;
                    break;
                case FieldStyle.RunIn:
                    score += inward > 3f ? 8 : 70;
                    if (n.StartsWith("catch_07")) score += margin > 0.5f ? 30 : 4;
                    break;
                case FieldStyle.RunBack:
                    score += inward < -3f ? 8 : 70;
                    if (clip.HasThrow) score += b.Variety < 0.5f ? -3 : 3;
                    break;
                case FieldStyle.Boundary:
                    // At the rope, his momentum carrying him back over it.
                    score += b.RopeGap(ball) < 2.5f && inward < 0 ? -10 : 200;
                    break;
                default:
                    score += 40;
                    break;
            }
            return score + LongDiveCost(n, margin, inward) + SpeedCost(a, clip, approach.magnitude);
        }

        // Final rating 2026-11-14 (Fielding #2, owner "can make a little difference"): catch_13_sprint_dive_forward_long took 5
        // of 7 field catches — styled Stand by its name, it cost next to nothing against the standing takes. The full-length
        // dive is now for a ball that needs one (little time in hand, running in at it), and not on catch after catch.

        /// <summary>The sprinting full-length forward dive.</summary>
        private static bool LongDive(string clip) => clip.StartsWith("catch_13");

        /// <summary>The last few field catches: was each the long dive (the cap on how often it's seen).</summary>
        private static readonly Queue<bool> RecentCatches = new Queue<bool>();

        /// <summary>A field catch was chosen (MatchDirector.Fielding): counted for the long-dive cap.</summary>
        public static void NoteCatch(string clip)
        {
            RecentCatches.Enqueue(LongDive(clip));
            while (RecentCatches.Count > 4) RecentCatches.Dequeue();
        }

        /// <summary>How many of the last few field catches were the long dive, of how many (the log).</summary>
        public static (int dives, int of) LongDiveShare => (RecentCatches.Count(d => d), RecentCatches.Count);

        private static float LongDiveCost(string clip, float margin, float inward)
        {
            if (!LongDive(clip)) return 0f;
            // Time in hand: he takes it on his feet (a cup / reverse cup / one-hander at the height the ball comes in).
            // (A plan with a run already keeps CatchRunSlack in hand: "tight" is measured above that.)
            var cost = margin > CatchRunSlack + 0.55f ? 50f : margin > CatchRunSlack + 0.25f ? 25f : 0f;
            // A forward dive is a run IN at a dropping ball, not one he has to turn or go back for.
            if (inward < 2f) cost += 40f;
            // The cap: each long dive in the last few catches makes the next one dearer (still taken when nothing else gets there).
            cost += RecentCatches.Count(d => d) * 20f;
            return cost;
        }

        private static float GroundCost(SeqClip clip, FieldBall b, Vector3 ball, Vector3 vel, Vector3 approach, float yaw, float faceYaw, float throwYaw, Vector3 run, float t, Animator a)
        {
            var speed = vel.magnitude;
            var dist = approach.magnitude;
            var toTarget = Vector3.Distance(Flat(ball), Flat(b.Target));
            var chasing = speed > 1f && Vector3.Dot(vel, approach) > 0 && dist > 3f;
            // Later than the engine's moment costs; then how well it fits.
            var score = Mathf.Max(0, t - b.Due) * 30f;
            var faceW = clip.Style switch { FieldStyle.Stand => 0.8f, FieldStyle.Dive => 0.8f, FieldStyle.Lateral => 0.3f, FieldStyle.Chase => 0f, _ => 0.25f };
            score += Mathf.Abs(Mathf.DeltaAngle(yaw, faceYaw)) * faceW;
            if (run.magnitude > 0.5f && dist > 2f && clip.Style != FieldStyle.Stand) score += Mathf.Abs(Mathf.DeltaAngle(Yaw(approach), yaw + Yaw(run))) * 0.5f;
            if (clip.HasThrow)
            {
                score += Mathf.Abs(Mathf.DeltaAngle(yaw, throwYaw)) * 0.5f;
                // Area 3 (owner: "throws facing the opposite direction"): a turn he can't make by the clip's release at a
                // body's pace (ThrowCraft.TurnRate) leaves him throwing across himself — weigh what's left over hard.
                if (ThrowCraft.On)
                {
                    var can = ThrowCraft.TurnRate * Mathf.Max(0f, clip.ReleaseTime - clip.EventTime - 0.08f);
                    score += Mathf.Max(0f, Mathf.Abs(Mathf.DeltaAngle(yaw, throwYaw)) - can) * 1.5f;
                }
                // Short flicks for short throws, crow hops for long ones.
                score += Mathf.Abs(Mathf.Log(Mathf.Max(2f, toTarget) / Mathf.Max(2f, clip.Target.magnitude))) * 20f;
            }
            else score += 14;
            switch (clip.Style)
            {
                case FieldStyle.Dive:
                    // Dives are for a hard-hit ball just out of reach (the engine says when he dived).
                    score += b.Dived ? -40 : speed > 7f && dist > 0.8f && dist < 4.5f ? 0 : 80;
                    break;
                case FieldStyle.Stand:
                    // The long barrier: coming straight at him.
                    score += dist < 2.5f && speed > 3f ? 4 : 50;
                    break;
                case FieldStyle.Chase:
                    // Slides: running it down near the rope.
                    score += chasing && b.RopeGap(ball) < 9f ? -4 : 60;
                    break;
                case FieldStyle.RunBack:
                    score += chasing ? 2 : 60;
                    break;
                case FieldStyle.Lateral:
                    score += dist > 3f ? 4 : 40;
                    break;
                default:
                    score += chasing ? 40 : 0;
                    break;
            }
            // A shy at the stumps only for a tight run, and from close enough. (Area 3, Unity review r1 item 6: the
            // on-the-run shy was a third of all plays off this bonus — a smaller one; ThrowFitCost weighs the rest.)
            if (clip.Direct) score += b.Tight && toTarget < clip.Target.magnitude * 1.6f + 4f ? (ThrowCraft.On ? -4 : -12) : 90;
            if (ThrowCraft.On) score += ThrowFitCost(clip, b, t, toTarget, clip.HasThrow ? Mathf.Abs(Mathf.DeltaAngle(yaw, throwYaw)) : 0f);
            else if (b.Tight && toTarget < 16f) score += 8;
            score += b.Variety * 6f * Stable(clip.Name);
            return score + SpeedCost(a, clip, dist);
        }

        /// <summary>0–1 from a name, the same on every run.</summary>
        private static float Stable(string s)
        {
            var h = 7;
            foreach (var ch in s) h = (h * 31 + ch) & 0xffff;
            return (h % 97) / 96f;
        }

        /// <summary>The clip joins him mid-stride: it mustn't be running faster than he can be by then.</summary>
        private static float SpeedCost(Animator a, SeqClip clip, float dist)
        {
            var join = JoinOf(clip);
            var clipSpeed = Flat(Sample(a, clip, join).Hips - Sample(a, clip, Mathf.Max(0, join - 0.1f)).Hips).magnitude / 0.1f;
            var canBe = Mathf.Min(7.5f, Mathf.Sqrt(2 * 6f * Mathf.Max(0, dist)));
            return Mathf.Max(0, clipSpeed - canBe - 1f) * 6f;
        }

        private static float JoinOf(SeqClip clip) => Mathf.Max(0, clip.EventTime - Join);

        /// <summary>The clip's hips from the join to the event (its run-in), on this body.</summary>
        private static Vector3 ClipRun(Animator a, SeqClip clip) => Flat(Sample(a, clip, clip.EventTime).Hips - Sample(a, clip, JoinOf(clip)).Hips);

        /// <summary>Turn the clip by <paramref name="yaw"/> and place it so the hand is on <paramref name="ball"/>; EventAt = the soonest he can be there.</summary>
        private static FieldPlay Fit(Animator a, Vector3 from, SeqClip clip, Vector3 ball, float yaw, Func<float, float> runTime)
        {
            var join = JoinOf(clip);
            var hipsJoin = Sample(a, clip, join).Hips;
            var hand = Hand(Sample(a, clip, clip.EventTime), clip);
            var turn = Quaternion.Euler(0, yaw, 0);
            var place = Flat(ball - turn * hand);
            var joinAt = Flat(place + turn * hipsJoin);
            var h0 = Sample(a, clip, Mathf.Max(0, join - 0.1f)).Hips;
            return new FieldPlay
            {
                Fielder = a,
                Clip = clip,
                Turn = turn,
                Place = place,
                JoinAt = joinAt,
                JoinClip = join,
                Moving = Flat(hipsJoin - h0).magnitude / 0.1f > 2f,
                EventAt = runTime(Vector3.Distance(from, joinAt)) + clip.EventTime - join,
            };
        }

        // ------------------------------------------------------------- playing

        private Vector3 World(Vector3 local) => Place + Turn * local;

        /// <summary>
        /// Run in, play the clip, take the ball on the event frame and carry it
        /// (through whatever the clip does with it) to the release, then throw.
        /// <paramref name="run"/> moves a fielder to a spot by a time after the hit
        /// (pulling up or running on into the clip); <paramref name="throwTo"/> flies
        /// the ball from a hand at a speed; <paramref name="throws"/>: after a
        /// collect, throw it in. <paramref name="atEvent"/> fires as the hand meets it.
        /// </summary>
        public IEnumerator Play(MonoBehaviour host, float hitAt, Transform ball, Func<Animator, Vector3, float, bool, Quaternion, IEnumerator> run,
            Func<FieldPlay, Animator, Vector3, float, IEnumerator> throwTo, bool throws, Action<FieldPlay>? atEvent = null)
        {
            var a = Fielder;
            float Now() => Time.time - hitAt;
            Thrower = a;
            Aim ??= () => Target;
            var mate = Partner != null ? host.StartCoroutine(Partner.MateRoutine(hitAt, run)) : null;
            // The run turns him into the clip's facing over its last strides (no snap at the join).
            yield return run(a, JoinAt, JoinTime, !Moving, Turn);
            // Early (under a catch, waiting for it): turn to face the way the clip starts.
            if (!Moving && JoinTime - Now() > 0.15f)
            {
                var q0 = a.transform.rotation;
                var span = Mathf.Min(0.35f, JoinTime - Now());
                for (var t0 = Time.time; Time.time - t0 < span;)
                {
                    a.transform.rotation = Quaternion.Slerp(q0, Turn, (Time.time - t0) / span);
                    yield return null;
                }
            }
            while (Now() < JoinTime) yield return null;
            // Late (he couldn't quite get there): the clip keeps time with the ball.
            Start(a, Clip, JoinClip + Mathf.Max(0, Now() - JoinTime));
            // The last strides: whatever the plan's small errors (a pose, a late start), he's steered so his
            // hand arrives where the ball really is on the catch frame — he takes it where it is.
            while (Now() < EventAt)
            {
                var left = EventAt - Now();
                if (left < Steer)
                {
                    var miss = Flat(BallAt - HandNow());
                    // (Area 3, dev 2: with his hands reaching for it — CatchHands — the body is only steered by what the
                    // arms can't cover; it used to slide him the whole miss with his feet planted.)
                    if (a.GetComponent<CatchHands>() is CatchHands reach && reach.Reaching)
                        miss -= Vector3.ClampMagnitude(miss, CatchHands.FreeReach);
                    var step = miss * Mathf.Clamp01(Time.deltaTime / Mathf.Max(Time.deltaTime, left));
                    a.transform.position += step;
                    Corrected += step.magnitude;
                }
                yield return null;
            }
            atEvent?.Invoke(this);
            // His from here: the director stops flying it; it's eased from wherever it is into his hand(s).
            HasBall = true;
            // (Area 3, dev 2: a catch's flight is steered to end in his palms on this frame — when this runs before the
            // director's last flight frame, the ball's still where it was a frame ago, ~25 cm out at catch pace, and
            // the ease below showed it hanging in front of his hands — review b3, seq_36. A catch takes it in the cup.)
            var takeFrom = CupTake(Clip) ? HandNow() : ball.position;
            var takeAt = Time.time;

            // In his hands from here, moving to the throwing hand before it goes.
            var takeHand = HandBones(a, Clip);
            var right = a.GetBoneTransform(HumanBodyBones.RightHand);
            var release = ReleaseAt;
            Vector3 Held()
            {
                // (Area 3: a ground collect sits in the palms' cup — HandPoint, the one helper — not between the wrists.)
                var at = ThrowCraft.On && !Clip.IsCatch && Clip.Kind != "drop" ? HandPoint.Cup(a, Clip.LeftHand, Clip.RightHand) : CupTake(Clip) ? HandNow() : HandPos(takeHand);
                // (Area 3: to the palm of the throwing hand, where BallView then holds it — not the wrist.)
                if (right != null && !(Clip.IsCatch && !Clip.HasThrow)) at = Vector3.Lerp(at, ThrowCraft.On ? ThrowCraft.Palm(a) : right.position, Mathf.SmoothStep(0, 1, (Now() - EventAt) / 0.35f));
                // The first moment of the take: from where the ball really was, never a jump.
                var ease = (Time.time - takeAt) / 0.12f;
                return ease < 1f ? Vector3.Lerp(takeFrom, at, Mathf.SmoothStep(0f, 1f, ease)) : at;
            }

            if (Clip.Kind == "drop")
            {
                yield return HoldUntil(ball, Held, hitAt, HitTime(Clip.SpillAt));
                // Out of his hands, a bounce, and down where his hand picks it up.
                var down = World(Hand(Sample(a, Clip, Clip.PickupAt), Clip)) + Vector3.up * 0.02f;
                yield return Lob(ball, ball.position, down, hitAt, HitTime(Clip.PickupAt), 0.55f);
                yield return HoldUntil(ball, () =>
                {
                    TurnToAim(a, Clip, Now(), release - 0.08f);
                    return right != null ? right.position : HandPos(takeHand);
                }, hitAt, release);
                Thrown = true;
                yield return throwTo(this, a, ball.position, Clip.ThrowSpeed);
            }
            else if (!float.IsNaN(Clip.TossAt))
            {
                // The boundary catch: up before he crosses, caught again coming back in — or lobbed in to a team-mate.
                yield return HoldUntil(ball, HandNow, hitAt, HitTime(Clip.TossAt));
                if (TossTo != null && TossHand != null)
                {
                    yield return Lob(ball, ball.position, TossTo(), hitAt, TossCaughtAt, -1f);
                    var mateHold = TossHand;
                    yield return HoldUntil(ball, () => mateHold.position, hitAt, HitTime(Clip.Length) + 1f, () => LetGo);
                    Done = true;
                    yield break;
                }
                var again = World(Hand(Sample(a, Clip, Clip.RecatchAt), Clip));
                yield return Lob(ball, ball.position, again, hitAt, HitTime(Clip.RecatchAt), -1f);
                yield return HoldUntil(ball, HandNow, hitAt, HitTime(Clip.Length), () => LetGo);
            }
            else if (Partner != null && Clip.Kind == "flick")
            {
                yield return HoldUntil(ball, Held, hitAt, HitTime(Clip.FlickAt));
                // Flicked back inside; it lands and sits for the team-mate.
                yield return Flight(ball, Turn * Clip.FlickVelocity, hitAt, Partner.EventAt - 0.12f);
                var mateHand = HandBones(Partner.Fielder, Partner.Clip);
                var from = ball.position;
                for (var t0 = Time.time; Now() < Partner.EventAt;)
                {
                    ball.position = Vector3.Lerp(from, HandPos(mateHand), Mathf.Clamp01((Time.time - t0) / 0.12f));
                    yield return null;
                }
                Thrower = Partner.Fielder;
                var mateRight = Partner.Fielder.GetBoneTransform(HumanBodyBones.RightHand);
                var mateRelease = Partner.HitTime(Partner.Clip.ReleaseTime);
                yield return HoldUntil(ball, () =>
                {
                    TurnToAim(Partner.Fielder, Partner.Clip, Now(), mateRelease - 0.08f);
                    return mateRight == null ? HandPos(mateHand) : ThrowCraft.On ? ThrowCraft.Palm(Partner.Fielder) : mateRight.position;
                }, hitAt, mateRelease);
                Thrown = true;
                yield return throwTo(this, Partner.Fielder, ball.position, Partner.Clip.ThrowSpeed);
            }
            else if (Partner != null)
            {
                // The relay: flat into the middle man's hands on his catch frame, then on to the stumps.
                yield return HoldUntil(ball, Held, hitAt, HitTime(Clip.ReleaseTime));
                yield return Lob(ball, ball.position, Partner.BallAt, hitAt, Partner.EventAt, -1f);
                var mateHand = HandBones(Partner.Fielder, Partner.Clip);
                var mateRight = Partner.Fielder.GetBoneTransform(HumanBodyBones.RightHand);
                Thrower = Partner.Fielder;
                var caughtAt = Partner.EventAt;
                yield return HoldUntil(ball, () =>
                {
                    TurnToAim(Partner.Fielder, Partner.Clip, Now(), Partner.HitTime(Partner.Clip.ReleaseTime) - 0.08f);
                    var at = HandPos(mateHand);
                    return mateRight == null ? at : Vector3.Lerp(at, ThrowCraft.On ? ThrowCraft.Palm(Partner.Fielder) : mateRight.position, Mathf.SmoothStep(0, 1, (Now() - caughtAt) / 0.35f));
                }, hitAt, Partner.HitTime(Partner.Clip.ReleaseTime));
                Thrown = true;
                yield return throwTo(this, Partner.Fielder, ball.position, Partner.Clip.ThrowSpeed);
            }
            else if (ThrowCraft.On && Clip.HasThrow && throws && !Clip.IsCatch)
            {
                // Area 3: its own throw, released on the clip's release frame, snap turns slowed, the ball in his hand (FieldPlay.Throw).
                yield return ThrowOwn(a, ball, hitAt, Held, throwTo);
            }
            else if (ThrowCraft.On && throws && !Clip.IsCatch)
            {
                // Area 3: up from the stop / pick-up — gather, set, the throw for the distance timed to the engine (FieldPlay.Throw).
                yield return SetAndThrow(a, ball, hitAt, EventAt, ThrowCraft.GatherFor(Clip, a), Held, throwTo);
                yield break;
            }
            else if (Clip.HasThrow && (throws || Clip.IsCatch))
            {
                // Planted, he turns his body to the man taking it as he gets ready to let it go.
                yield return HoldUntil(ball, () =>
                {
                    TurnToAim(a, Clip, Now(), release - 0.08f);
                    return Held();
                }, hitAt, release, Clip.IsCatch ? () => LetGo : (Func<bool>?)null);
                if (!LetGo)
                {
                    Thrown = true;
                    yield return throwTo(this, a, ball.position, Clip.ThrowSpeed);
                }
            }
            else if (throws && !Clip.IsCatch)
            {
                // Up from the stop / pick-up and into a throw clip turned at the target.
                var chain = float.IsNaN(Clip.ChainAt) ? Clip.Length : Clip.ChainAt;
                yield return HoldUntil(ball, Held, hitAt, HitTime(chain));
                var underarm = Vector3.Distance(Flat(a.transform.position), Flat(Aim?.Invoke() ?? Target)) < 12f;
                var throwClip = SeqClip.Named(underarm ? "throw_19_underarm_throw" : "throw_18_overarm_throw");
                if (throwClip != null && Has(a, throwClip))
                {
                    Body.Rebase(a);
                    a.speed = 1f;
                    AnimTrace.Note(a, "FieldPlay play"); // FLD-1 log only
                    a.Play(throwClip.Name, 0, 0f);
                    var t0 = Now();
                    var go = t0 + throwClip.ReleaseTime;
                    // Round to the man taking it through the crow hop (the clip's own throw line, measured), the ball in his throwing hand.
                    yield return HoldUntil(ball, () =>
                    {
                        if (Aim != null) TurnToAim(a, throwClip, Now(), go - 0.08f);
                        return right != null ? right.position : HandPos(takeHand);
                    }, hitAt, go);
                    Thrown = true;
                    yield return throwTo(this, a, ball.position, throwClip.ThrowSpeed);
                    var settle = Time.time + Mathf.Max(0, Mathf.Min(1.6f, throwClip.Length - throwClip.ReleaseTime - 0.05f) - (Time.time - hitAt - go));
                    while (Time.time < settle) yield return null;
                    Settle(a);
                    Done = true;
                    yield break;
                }
                Thrown = true;
                yield return throwTo(this, a, ball.position, 25f);
            }
            else
            {
                // A catch: his until the celebration takes it (area 3, dev 2: in the palm cup, not between the wrists).
                yield return HoldUntil(ball, HandNow, hitAt, HitTime(Clip.Length), () => LetGo);
            }

            // Let the follow-through play, then stand where the clip left him.
            var until = Mathf.Min(HitTime(Clip.Length) - 0.05f, Now() + 2.5f);
            while (Now() < until && !(Clip.IsCatch && LetGo)) yield return null;
            if (!(Clip.IsCatch && LetGo)) Settle(a);
            Done = true;
        }

        /// <summary>The partner's part: in position by the start, his clip with the lead's, and back to ready.</summary>
        private IEnumerator MateRoutine(float hitAt, Func<Animator, Vector3, float, bool, Quaternion, IEnumerator> run)
        {
            var a = Fielder;
            yield return run(a, JoinAt, JoinTime, true, Turn);
            while (Time.time - hitAt < JoinTime) yield return null;
            Start(a, Clip, JoinClip + Mathf.Max(0, Time.time - hitAt - JoinTime));
            var left = Clip.Length - JoinClip;
            if (Clip.HasThrow) left = Mathf.Min(left, Clip.ReleaseTime - JoinClip + 0.9f);
            var end = hitAt + JoinTime + left;
            while (Time.time < end) yield return null;
            Settle(a);
        }

        /// <summary>His clip is playing (the hands it takes the ball with are where the plan put them).</summary>
        public bool Started;

        /// <summary>Seconds before the catch frame he's steered onto the ball, and how far (m) it took (test runs log it).</summary>
        private const float Steer = 0.3f;
        public float Corrected;

        /// <summary>His hands have it (the director stops flying the ball on its own).</summary>
        public bool HasBall;

        /// <summary>Where the hand(s) that take the ball are right now.</summary>
        public Vector3 HandNow() => CupTake(Clip) ? HandPoint.Cup(Fielder, Clip.LeftHand, Clip.RightHand) : HandPos(HandBones(Fielder, Clip));

        private void Start(Animator a, SeqClip clip, float at)
        {
            if (a == Fielder) Started = true;
            a.speed = 1f;
            a.SetBool(MirrorParam, false);
            a.transform.SetPositionAndRotation(Place, Turn);
            AnimTrace.Note(a, "FieldPlay play"); // FLD-1 log only
            a.Play(clip.Name, 0, at / LengthOf(a, clip));
        }

        /// <summary>Stand where the clip left him, in the ready stance (no cross-fade: the old pose was off his transform).</summary>
        private static void Settle(Animator a)
        {
            Body.Rebase(a);
            a.speed = 1f;
            AnimTrace.Note(a, "FieldPlay.Settle Play Ready"); // FLD-1 log only
            a.Play("Ready", 0, 0f);
        }

        private static IEnumerator HoldUntil(Transform ball, Func<Vector3> at, float hitAt, float until, Func<bool>? stop = null)
        {
            while (Time.time - hitAt < until && (stop == null || !stop()))
            {
                ball.position = at();
                yield return null;
            }
            if (stop == null || !stop()) ball.position = at();
        }

        /// <summary>From one point to another by a time (s after the hit) under gravity; <paramref name="hop"/> ≥ 0 sets the arc's height instead.</summary>
        private static IEnumerator Lob(Transform ball, Vector3 from, Vector3 to, float hitAt, float by, float hop)
        {
            var t0 = Time.time;
            // Never a flash: if it's late leaving, it's a little late arriving.
            var span = Mathf.Max(0.25f, by - (t0 - hitAt));
            var g = -Physics.gravity.y;
            while (Time.time - t0 < span)
            {
                var t = Time.time - t0;
                var p = Vector3.Lerp(from, to, t / span);
                p.y += hop >= 0 ? hop * 4 * (t / span) * (1 - t / span) : 0.5f * g * t * (span - t);
                ball.position = p;
                yield return null;
            }
            ball.position = to;
        }

        /// <summary>Free flight from where it is with <paramref name="v"/>, until it's down (then it rolls to a stop) or the time's up.</summary>
        private static IEnumerator Flight(Transform ball, Vector3 v, float hitAt, float until)
        {
            var p = ball.position;
            while (Time.time - hitAt < until)
            {
                if (p.y > 0.04f || v.y > 0)
                {
                    v += Physics.gravity * Time.deltaTime;
                    p += v * Time.deltaTime;
                    if (p.y < 0.04f)
                    {
                        // One small bounce, then it rolls.
                        p.y = 0.04f;
                        v = new Vector3(v.x * 0.5f, Mathf.Abs(v.y) * 0.25f, v.z * 0.5f);
                        if (v.y < 0.6f) v.y = 0;
                    }
                }
                else
                {
                    v = Vector3.MoveTowards(new Vector3(v.x, 0, v.z), Vector3.zero, 4f * Time.deltaTime);
                    p += v * Time.deltaTime;
                }
                ball.position = p;
                yield return null;
            }
        }

        // ---------------------------------------------------------- the body

        private static readonly int MirrorParam = Animator.StringToHash("Mirror");

        public static bool Has(Animator a, SeqClip c) => a.runtimeAnimatorController != null && a.HasState(0, Animator.StringToHash(c.Name));

        /// <summary>Hips and both hands, with him standing at the origin facing +z.</summary>
        private struct Pose
        {
            public Vector3 Hips, Left, Right;

            /// <summary>Which way his chest faces (yaw, deg, his own frame).</summary>
            public float Facing;

            /// <summary>Area 3 (dev 2, catches): the palm points (HandPoint), where a caught ball sits — not the wrists.</summary>
            public Vector3 LeftPalm, RightPalm;
        }

        /// <summary>Area 3 (dev 2): a catch is planned and taken on the palm cup (HandPoint), not between the wrists (the ball sat on the belly).</summary>
        // (Area 3, dev 1, build 3: pick-ups and stops too — planned, steered and taken on the palms, not the wrists.)
        private static bool CupTake(SeqClip c) => c.IsCatch && !c.PartnerOnly || ThrowCraft.On && c.Collects;

        private static Vector3 Hand(Pose p, SeqClip c) =>
            CupTake(c) ? (c.LeftHand ? p.LeftPalm : c.RightHand ? p.RightPalm : HandPoint.Cup(p.LeftPalm, p.RightPalm))
            : c.LeftHand ? p.Left : c.RightHand ? p.Right
            : Vector3.Distance(p.Left, p.Right) < 0.45f ? (p.Left + p.Right) / 2 : p.Right;

        /// <summary>The live bones of the hand(s) that take the ball.</summary>
        private static (Transform? a, Transform? b) HandBones(Animator a, SeqClip c)
        {
            var l = a.GetBoneTransform(HumanBodyBones.LeftHand);
            var r = a.GetBoneTransform(HumanBodyBones.RightHand);
            return c.LeftHand ? (l, null) : c.RightHand ? (r, null) : (l, r);
        }

        private static Vector3 HandPos((Transform? a, Transform? b) h)
        {
            if (h.a != null && h.b != null)
            {
                // Together: between them; apart (one reached), the right.
                return Vector3.Distance(h.a.position, h.b.position) < 0.45f ? (h.a.position + h.b.position) / 2 : h.b.position;
            }
            return h.a != null ? h.a.position : h.b != null ? h.b.position : Vector3.zero;
        }

        private static Pose Sample(Animator a, SeqClip clip, float time)
        {
            var key = (a.GetInstanceID(), clip.Name, Mathf.RoundToInt(time * 30));
            if (Cache.TryGetValue(key, out var hit)) return hit;
            AnimTrace.Note(a, $"FieldPlay.Sample {clip.Name}@{time:0.00}"); // FLD-1 log only
            var pos = a.transform.position;
            var rot = a.transform.rotation;
            var state = a.GetCurrentAnimatorStateInfo(0);
            var speed = a.speed;
            var mirror = a.GetBool(MirrorParam);
            var culling = a.cullingMode;
            // Evaluated even off screen (a culled animator would hand back its last pose, metres off for a dive).
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.speed = 1f;
            a.SetBool(MirrorParam, false);
            a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            a.Play(clip.Name, 0, time / LengthOf(a, clip));
            a.Update(0f);
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var l = a.GetBoneTransform(HumanBodyBones.LeftHand);
            var r = a.GetBoneTransform(HumanBodyBones.RightHand);
            var result = new Pose
            {
                Hips = hips != null ? hips.position : Vector3.zero,
                Left = l != null ? l.position : new Vector3(-0.3f, 1f, 0.3f),
                Right = r != null ? r.position : new Vector3(0.3f, 1f, 0.3f),
                Facing = ThrowCraft.ChestYaw(a),
                LeftPalm = HandPoint.Palm(a, true),
                RightPalm = HandPoint.Palm(a, false),
            };
            a.transform.SetPositionAndRotation(pos, rot);
            a.SetBool(MirrorParam, mirror);
            a.Play(state.shortNameHash, 0, state.normalizedTime);
            a.speed = speed;
            a.Update(0f);
            a.cullingMode = culling;
            Cache[key] = result;
            return result;
        }

        private static readonly Dictionary<string, float> Lengths = new Dictionary<string, float>();

        /// <summary>
        /// The clip's real length as imported (s). Clip times (the metadata's frames, from frame 1
        /// at 30 fps) go to the animator as a share of THIS, not of the metadata's frame count — the
        /// clip runs in real seconds once playing, so a different length would put the hands of the
        /// sample and of the live clip apart.
        /// </summary>
        private static float LengthOf(Animator a, SeqClip clip)
        {
            if (Lengths.TryGetValue(clip.Name, out var len)) return len;
            var culling = a.cullingMode;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            AnimTrace.Note(a, "FieldPlay play"); // FLD-1 log only
            a.Play(clip.Name, 0, 0f);
            a.Update(0f);
            var info = a.GetCurrentAnimatorStateInfo(0);
            len = info.shortNameHash == Animator.StringToHash(clip.Name) && info.length > 0.05f ? info.length : Mathf.Max(0.01f, clip.Length);
            a.cullingMode = culling;
            Lengths[clip.Name] = len;
            return len;
        }

        private static readonly Dictionary<(int, string, int), Pose> Cache = new Dictionary<(int, string, int), Pose>();

        private static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);
        private static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
    }
}
