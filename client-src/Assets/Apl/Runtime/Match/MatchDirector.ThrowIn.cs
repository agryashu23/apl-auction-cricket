using System.Collections;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The throw in, and it's always taken: into the keeper's gloves over the
    /// stumps (a take clip timed so the gloves meet it) or the hands of the bowler
    /// (or whoever's at that end) — where he'll be when it arrives — and it stays
    /// there. A shy at the stumps only when the engine's race has one (a direct
    /// hit, or a close call the TV umpire will look at); the odd real overthrow
    /// (the engine's extra run) runs on and the man backing up gathers it; a keeper's
    /// fumble squirts away and he gathers it again.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Where the receiver is heading (the stumps), and who's backing up.</summary>
        private Vector3? _receiverSpot;
        private Animator? _backer;

        /// <summary>The engine has a close run out at the stumps (out, or a TV not-out).</summary>
        private static bool CloseRunOut(BallResult r) => r.Running != null && (r.RunOut || r.Referral == TvCheck.RunOut);

        /// <summary>Which end that close call is at.</summary>
        private static bool CloseEnd(BallResult r) => r.RunOut ? r.RunOutAtKeeperEnd : r.Running?.ToKeeperEnd ?? true;

        private IEnumerator ThrowIn(FieldPlay play, Animator thrower, Vector3 from, float speed)
        {
            _throwHome = false;
            _throwStart = Time.time;
            _ballPhase = "throw";
            // Out of his hand (area 3: BallView had it in his palm since the transfer); the flight starts where it was.
            ThrowCraft.InHand(_ball, null);
            var r = _hitResult;
            var close = r != null && CloseRunOut(r);
            var (keeperEnd, receiver) = ThrowEnd(play, thrower);
            var stumpsAt = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            _receiver = receiver;
            var gloves = keeperEnd && receiver == Refs.Keeper && _keeper != null && _keeper.UsesClips;
            // (Dev 2, deep area 3: every engine direct hit is shown — a hit with the batter already home too, ~1.2 a match;
            // it was drawn as a plain take unless the call was close. Not-out ones arrive no sooner than the engine's Back, below.)
            var hitStumps = r?.Running != null && r.DirectHit && (close || r.Attempted > 0);
            // The engine's overthrow: a shy that missed and ran away (rare).
            var overthrow = !close && r?.Running != null && r.Ran > r.Attempted;
            var fumble = !close && !overthrow && r != null && r.Fumbled && gloves;
            var pending = RunState.Active && RunState.Manual && RunState.Going > 0;
            // While they're still running, the throw's end is where it's decided: watch it from the throw on
            // (presentation, round 12 — the camera used to stay put unless the engine already knew it was close).
            if (close || overthrow || pending || RunState.Active && r?.Running != null)
                WatchStumps(keeperEnd, from);

            // Into his hands where he'll be when it gets there (he may still be running to the stumps).
            var side = from.x >= 0 ? 1f : -1f;
            var to = stumpsAt + new Vector3(0, gloves ? 0.95f : 1.15f, 0);
            var seconds = Mathf.Clamp(Vector3.Distance(from, to) / Mathf.Max(9f, speed), 0.2f, 3f);
            if (hitStumps) to = stumpsAt + new Vector3(0.02f * side, 0.45f, 0);
            else if (overthrow) to = stumpsAt + new Vector3(0.35f * side, 0.5f, 0);
            else if (receiver != null)
            {
                // Area 3: the keeper / bowler taking it, if nobody sent him to these stumps (the end changed), goes now.
                if (ThrowCraft.On && receiver != _receiverSpotFor && (receiver == Refs.Keeper || receiver == Refs.Bowler))
                    SendReceiver(receiver, keeperEnd, from, seconds);
                for (var i = 0; i < 2; i++)
                {
                    var at = Flat(receiver.transform.position);
                    // (Only the spot he was sent to — _receiver is him by now, whoever's spot it was.)
                    if ((ThrowCraft.On ? receiver == _receiverSpotFor : receiver == _receiver) && _receiverSpot is Vector3 spot) at = Vector3.MoveTowards(at, spot, 6f * seconds);
                    var facing = Flat(from - at).normalized;
                    to = at + facing * (gloves ? 0.55f : 0.45f) + Vector3.up * (gloves ? 0.95f : 1.15f);
                    seconds = Mathf.Clamp(Vector3.Distance(from, to) / Mathf.Max(9f, speed), 0.2f, 3f);
                }
            }
            // Never a flash: even a flick is a visible arc.
            seconds = Mathf.Max(seconds, 0.3f);
            // The engine's wide throw (its break comes a beat late): off line, he has to reach for it.
            if (close && r!.RunOut && !hitStumps && receiver != null && (ThrowCraft.On ? r.Running!.LastThrowWide : r.BrokenAt - r.Running!.Back > RunPlan.Gather + 0.2)) // (area 3: the engine's own wide draw, not a margin on Gather)
                to += Vector3.Cross(Vector3.up, Flat(to - from).normalized) * (0.8f * side) + Vector3.down * 0.45f;
            if (close)
            {
                // The picture must agree with the call: not out — it gets there after he's in;
                // out — before he makes his ground (MatchDirector.RunOutView.PastCrease).
                var homeIn = Mathf.Max(0f, (0.3f - PastCrease(keeperEnd)) / 6f);
                seconds = r!.RunOut ? Mathf.Min(seconds, Mathf.Max(0.3f, homeIn - 0.2f)) : Mathf.Max(seconds, homeIn + 0.15f);
            }
            else if (r?.Running != null && r.Attempted > 0 && !r.RunOut)
            {
                // Not out, and not even close by the engine: the throw must not beat him on screen either.
                // It used to fly at clip speed, regardless of the engine's race, and land in the gloves well
                // before a batter the engine had home — an "easy run out" the umpire never gave (owner v24-v26).
                // It reaches the stumps no sooner than the engine's ball does (Back), gathered after he's in.
                var engineIn = _hitAt + (float)r.Running.Back - Time.time;
                seconds = Mathf.Max(seconds, Mathf.Min(engineIn, 4f));
            }
            // Area 3: from the deep, a flat one-bounce throw into his hands (the time is the same — the engine's).
            var bounce = ThrowCraft.On && !close && !hitStumps && !overthrow && receiver != null && Vector3.Distance(Flat(from), Flat(to)) > ThrowCraft.BounceFrom;
            var trace = Autoplay ? BeginThrowTrace(play, thrower, from, to, seconds, bounce, receiver) : default;
            var g = -Physics.gravity.y;
            var taken = false;
            var catching = receiver != null && !hitStumps && !overthrow && !fumble;
            var hand = receiver == null ? null : gloves && _keeper!.Gloves != null ? _keeper.Gloves : ThrowCraft.On ? CatchHands.For(receiver).CupTransform : receiver.GetBoneTransform(HumanBodyBones.RightHand); // (area 3: the palm cup, dev 2's CatchHands)
            var homing = new TakeHoming(); // (fielding dev: IntoHands' state for this throw)
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var left = seconds - t;
                if (!taken && receiver != null && left <= 0.45f && !hitStumps && !overthrow)
                {
                    taken = true;
                    var face = Flat(from - receiver.transform.position);
                    // Area 3 (owner ITEM 4): a close run-out at his end, the bowler / an infielder over the stumps — the take
                    // and the break in one clip, its catch frame on the arrival (MatchDirector.Gather.ReceiveAndBreak).
                    var receiveClip = ThrowCraft.On && !gloves && close && CloseEnd(r!) == keeperEnd && ReceiveAndBreak(receiver, keeperEnd, from, left);
                    // Area 3: a fielder turns to it over a moment, not in a frame (the keeper's take turns him itself: dev 2's).
                    if (ThrowCraft.On && !gloves) { if (!receiveClip) Extra(Face(receiver, from, Mathf.Clamp(left - 0.1f, 0.12f, 0.28f))); } // (under 0.3 s: never a turn clip mid-take, TurnStep)
                    else if (face.sqrMagnitude > 0.01f) receiver.transform.rotation = Quaternion.LookRotation(face);
                    if (gloves)
                    {
                        if (fumble) _keeper!.Fumble(to, left);
                        else if (close) _keeper!.RunOutTake(to, left);
                        else if (_keeper!.TakeBall(to, left) == null) Extra(After(Mathf.Max(0, left - 0.12f), () => Event(receiver, CricketClips.FieldClipNamed("catch_01_cup_catch"))));
                    }
                    else
                    {
                        var high = to.y > 1.4f;
                        var rcv = receiver;
                        if (ThrowCraft.On)
                        {
                            // Area 3: his hands go to the throw (dev 2's CatchHands reach; logged as a "receive").
                            var takeClip = receiveClip ? (rcv == Refs.Bowler ? "bowler_receive_break_stumps" : "fielder_receive_break_stumps") : high ? "catch_02_reverse_cup_catch" : "catch_01_cup_catch";
                            var rcvName = rcv.name;
                            var arrive = to;
                            // (Fielding dev, final rating 2026-11-14: the take held until the ball is within 0.3 m of the cup — CatchHands.takeWithin.)
                            CatchHands.For(rcv).Reach(() => arrive, Time.time + left, _ball, false, false, c => CatchContact("receive", takeClip, rcv, rcvName, c), () => _ball.position, takeWithin: TakeWithin);
                        }
                        // (Fielding dev, final rating 2026-11-14: the take clip waits for the ball to be at his hands — MatchDirector.CatchHands TakeWhenClose.)
                        if (!receiveClip) Extra(TakeWhenClose(rcv, CricketClips.FieldClipNamed(high ? "catch_02_reverse_cup_catch" : "catch_01_cup_catch"), hand, Time.time + left + 0.5f));
                    }
                }
                var p = bounce ? OneBounce(from, to, t, seconds, g) : Vector3.Lerp(from, to, t / seconds);
                if (!bounce) p.y += 0.5f * g * t * (seconds - t) * (hitStumps || overthrow ? 0.4f : 0.85f);
                // The last moments: into his hands wherever they actually are (no jump when he holds it).
                // (Fielding dev, final rating 2026-11-14: flies into the hands over as long as the miss needs — no 0.2 s snap.)
                _ball.position = catching ? IntoHands(p, to, hand, t, seconds, homing) : p;
                yield return null;
            }
            _ball.position = catching && hand != null ? hand.position : to;
            if (Autoplay) EndThrowTrace(trace, keeperEnd, receiver);
            _throwHome = true;
            _ballPhase = catching && hand != null ? "held" : "loose";
            var dir = Flat(to - from).normalized;
            if (hitStumps)
            {
                // Direct hit: the stumps rattle, the bails fly; it runs on and the man backing up has it.
                BreakWicket(keeperEnd, dir);
                yield return Roll(to, dir, 5f, 1.2f);
                yield return Gather(_ball.position, thrower);
                yield break;
            }
            if (overthrow)
            {
                yield return Roll(to, dir, 16f, 2.2f);
                yield return Gather(_ball.position, thrower);
                yield break;
            }
            if (fumble)
            {
                // Out of the gloves, a few metres behind him; he turns and gathers it.
                yield return Roll(to, dir, 3.8f, 1.1f);
                yield return Gather(_ball.position, thrower, receiver);
                yield break;
            }
            // In his hands: the bails off for a close run out (now, or once MANUAL running settles it).
            if (close || pending && RunState.Active) yield return AtTheStumps(keeperEnd, from, hand, close && gloves && taken);
            // And it stays there (the next ball takes it back).
            if (hand != null) Extra(Hold(hand, 30f));
        }

        /// <summary>
        /// Which end the throw goes to and who takes it: the engine's race (a close run out's end, else the
        /// end it sends it to), never to himself (the keeper throws to the bowler's end and vice versa), and
        /// someone is always there (the nearest fielder if neither the keeper nor the bowler is free).
        /// </summary>
        private (bool keeperEnd, Animator? receiver) ThrowEnd(FieldPlay play, Animator thrower)
        {
            var r = _hitResult;
            var keeperEnd = r?.Running != null ? CloseEnd(r) : play.Target.z < 0;
            // The keeper / bowler throws to the other end — unless the engine's run out is at his own
            // end (it used to go to the far end and break the wrong wicket); then the man nearest his stumps takes it.
            var closeHere = r != null && CloseRunOut(r);
            if (thrower == Refs.Keeper && !(closeHere && keeperEnd)) keeperEnd = false;
            if (thrower == Refs.Bowler && !(closeHere && !keeperEnd)) keeperEnd = true;
            var stumpsAt = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var receiver = keeperEnd ? Refs.Keeper : Refs.Bowler;
            if (receiver == thrower) receiver = null;
            return (keeperEnd, receiver ?? NearestTo(stumpsAt, thrower));
        }

        /// <summary>Where the man holding the ball will throw it, now: the hands of whoever takes it (or the stumps).</summary>
        private Vector3 ThrowAim(FieldPlay play)
        {
            var thrower = play.Thrower != null ? play.Thrower : play.Fielder;
            var (keeperEnd, receiver) = ThrowEnd(play, thrower);
            if (receiver == null) return new Vector3(0, 1f, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var at = (ThrowCraft.On ? receiver == _receiverSpotFor : receiver == _receiver) && _receiverSpot is Vector3 spot ? spot : Flat(receiver.transform.position);
            return at + Vector3.up * 1f;
        }

        /// <summary>The fielder nearest a point, not <paramref name="not"/> (someone's always at the stumps to take it).</summary>
        private Animator? NearestTo(Vector3 point, Animator? not)
        {
            Animator? best = null;
            var bestDist = float.MaxValue;
            foreach (var (a, _) in _fielders)
            {
                if (a == not || !a.gameObject.activeSelf) continue;
                var d = Vector3.Distance(Flat(a.transform.position), Flat(point));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = a;
                }
            }
            return best;
        }

        /// <summary>A loose ball gathered: the man backing up (else the nearest) runs to it, picks it up, and keeps it.</summary>
        private IEnumerator Gather(Vector3 ball, Animator? not, Animator? who = null)
        {
            who ??= _backer != null && _backer != not ? _backer : NearestTo(ball, not);
            if (who == null) yield break;
            Body.Rebase(who);
            // Stop where the pick-up clip's hands reach the ball (it collects well out in front of him).
            var pick = SeqClip.Named("field_13_two_hand_pickup");
            var reach = 0.7f;
            if (pick != null && FieldPlay.Has(who, pick))
            {
                var (l, rt) = FieldPlay.HandsAt(who, pick.Name, pick.Length, pick.EventTime);
                reach = Mathf.Clamp(Flat((l + rt) / 2).magnitude, 0.4f, 3f);
            }
            var stop = Vector3.MoveTowards(Flat(ball), Flat(who.transform.position), reach);
            yield return RunTo(who, stop, Mathf.Max(0.6f, Vector3.Distance(Flat(who.transform.position), stop) / 6f + Reaction));
            yield return Face(who, ball, 0.15f);
            Event(who, CricketClips.FieldClipNamed("field_13_two_hand_pickup"));
            // Off the turf into his hands as they get to it (eased, not snapped).
            // (Area 3: into his palms' cup — HandPoint, kept on CatchHands' cup transform after the animation — not the wrist.)
            var hand = ThrowCraft.On ? CatchHands.For(who).CupTransform : who.GetBoneTransform(HumanBodyBones.RightHand);
            var lying = _ball.position;
            for (var t = 0f; t < 0.12f + HandBlend && hand != null; t += Time.deltaTime)
            {
                _ball.position = Vector3.Lerp(lying, hand.position, Mathf.SmoothStep(0f, 1f, t / (0.12f + HandBlend)));
                yield return null;
            }
            if (hand != null) Extra(Hold(hand, 30f));
        }
    }
}
