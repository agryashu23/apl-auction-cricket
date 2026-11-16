using System.Collections;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The throw's at the stumps: when (and whether) the wicket's broken. A close
    /// run out the engine already knows (out, or close enough for the TV umpire)
    /// breaks at its moment — the throw back plus the gather; MANUAL running may
    /// settle it only now, so the ball waits in the gloves at the stumps until the
    /// call's in. If it's the other end he's short of, a quick throw down there
    /// does it. The third umpire's replay is centred on the moment it was broken.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>When the wicket was broken this ball (the TV replay's moment).</summary>
        private float _brokenAt = -1f;

        private IEnumerator AtTheStumps(bool keeperEnd, Vector3 from, Transform? hand, bool tookItBreaking)
        {
            var r = _hitResult;
            if (r == null) yield break;
            StallProbe.Mark($"at the stumps ({(keeperEnd ? "keeper's" : "bowler's")} end)");
            // (Deep area 3: in his palm, not at the wrist bone, while he breaks the wicket with it.)
            var holder = hand != null ? hand.GetComponentInParent<Animator>() : null;
            var wrist = holder != null && hand == holder.GetBoneTransform(HumanBodyBones.RightHand);
            void Keep()
            {
                if (hand != null) _ball.position = wrist ? HandPoint.Palm(holder!, false) : hand.position;
            }
            // MANUAL: the race isn't settled yet — hold it at the stumps for the call.
            for (var until = Time.time + 6f; !CloseRunOut(r) && RunState.Active && Time.time < until;)
            {
                Keep();
                yield return null;
            }
            if (!CloseRunOut(r)) yield break;
            if (CloseEnd(r) != keeperEnd)
            {
                // He's short at the other end: a quick throw down there, at the stumps.
                keeperEnd = CloseEnd(r);
                var to = new Vector3(0.04f, 0.45f, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
                WatchStumps(keeperEnd, _ball.position);
                var start = _ball.position;
                var span = Mathf.Clamp(Vector3.Distance(start, to) / 24f, 0.3f, 1.2f);
                for (var t = 0f; t < span; t += Time.deltaTime)
                {
                    var p = Vector3.Lerp(start, to, t / span);
                    p.y += 0.5f * 9.81f * t * (span - t) * 0.4f;
                    _ball.position = p;
                    yield return null;
                }
                BreakWicket(keeperEnd, Flat(to - start));
                yield return Roll(to, Flat(to - start).normalized, 4f, 1f);
                yield return Gather(_ball.position, null);
                yield break;
            }
            // The engine's moment: the throw back and the gather (never before the ball's there).
            // (Round 10: the engine's own break time — the take plus the whip, a wide throw a beat longer.)
            var engineAt = r.RunOut && r.BrokenAt > 0 ? r.BrokenAt : r.Running!.Back + (r.DirectHit ? 0 : RunPlan.Gather);
            // (Run-out owner, 2026-11-13: the take and the break are one movement — the engine's RunPlan.Gather is 0.15 s
            // now — so the bails can come off 0.12 s after the ball's in his hands, not 0.3 s; at 0.3 s a batter 1.5–2 m
            // short when it arrived was all but home before the bails came off.)
            var at = Mathf.Max(Time.time + 0.12f, _hitAt + (float)engineAt);
            if (!tookItBreaking && keeperEnd && _keeper != null && _keeper.UsesClips && hand != null)
            {
                // He's had it a while: the gloves whipped across onto the bails.
                _keeper.RunOutTake(hand.position, Mathf.Max(0.25f, at - Time.time));
            }
            // His receive-and-break clip's hands reach the bails then (dev 1, MatchDirector.Gather): that's the sweep the
            // break waits for — no WhipBails run, step or field_13 over the clip.
            else if (_receiveBreakAt > 0f) _whipAt = _receiveBreakAt;
            else if (!(keeperEnd && _keeper != null && _keeper.UsesClips) && _receiver != null && hand != null)
            {
                // The bowler (or whoever's over the stumps) brings the ball in his hand down onto the bails.
                _whipAt = float.MaxValue;
                Extra(WhipBails(_receiver, keeperEnd, at));
            }
            else _whipAt = 0f;
            // Out: the bails off while he's clearly short (now, if he's closing on the line).
            // Not out: only once his bat or body is over the line, so it never looks out.
            // (Deep area 3, review R6: an out isn't broken until the man with the ball has swept it onto the bails —
            // the bails used to come off on the engine's clock with him still metres away — unless the batter is
            // about to make his ground, or 0.8 s on.)
            while (true)
            {
                Keep();
                if (!CloseRunOut(r)) yield break;
                var past = PastCrease(keeperEnd);
                var swept = Time.time >= _whipAt;
                // (Review b2, F4 ball 1: waiting up to 0.8 s for the sweep let the run-out man close to 0.5 m — it read as
                // in. Now: no waiting once he's within 1.5 m, and never more than 0.4 s past the engine's moment.)
                if (r.RunOut ? Time.time >= at && (swept || past > -1.5f) || past > -0.8f || Time.time > at + 0.4f : Time.time >= at && past > 0.3f || Time.time > at + 2.5f) break;
                yield return null;
            }
            var stumps = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            BreakWicket(keeperEnd, Flat(stumps - from));
            for (var until = Time.time + 0.8f; Time.time < until;)
            {
                Keep();
                yield return null;
            }
        }

        /// <summary>
        /// A fielder or the bowler at the stumps, ball in hand: turned to the stumps and the hands
        /// swept down through the bails (the pick-up's low sweep, timed so they pass bail height at
        /// <paramref name="at"/>) — the wicket broken by hand, not by a throw.
        /// </summary>
        private IEnumerator WhipBails(Animator who, bool keeperEnd, float at)
        {
            var stumps = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var sweep = SeqClip.Named("field_13_two_hand_pickup");
            var lead = sweep != null && FieldPlay.Has(who, sweep) ? Mathf.Clamp(sweep.EventTime * 0.8f, 0.15f, 0.5f) : 0.25f;
            // Deep area 3 (F4 frame 84: the bowler ~2 m wide, the bails coming off by themselves): he gets to the
            // stumps first — run there (the sliding area's RunTo) — then the sweep from a stride off them.
            var to = Flat(stumps) + Flat(who.transform.position - stumps).normalized * 0.55f;
            // (Never a burst: no quicker than a hard run, ~5.5 m/s — the break waits for the sweep, AtTheStumps.)
            var dist = Vector3.Distance(Flat(who.transform.position), to);
            if (dist > 0.6f)
                yield return RunTo(who, to, Mathf.Max(Mathf.Max(0.3f, at - lead - 0.2f - Time.time), dist / 5.5f));
            while (Time.time < at - lead - 0.2f) yield return null;
            if (who == null) yield break;
            yield return Face(who, stumps, 0.15f);
            if (who == null) yield break;
            // The last few centimetres so the sweep passes over the bails.
            who.transform.position = Vector3.MoveTowards(who.transform.position, to, 0.15f);
            Event(who, CricketClips.FieldClipNamed("field_13_two_hand_pickup"));
            // The hands pass bail height about now + lead: the break waits for it.
            _whipAt = Time.time + lead;
        }

        /// <summary>When a hand-held break's sweep reaches the bails (MaxValue: not yet; 0: no sweep this ball — the keeper's gloves).</summary>
        private float _whipAt;

        /// <summary>The replay's moment for a run-out referral: when the wicket was broken (else the caller's default).</summary>
        private float RunOutMoment(BallResult r) => r.Referral == TvCheck.RunOut && _wicketBroken && _brokenAt > 0 ? _brokenAt : -1f;

        /// <summary>
        /// Before a run-out referral or the wicket: if the throw never broke the
        /// wicket on screen (no fielding sequence, a call settled after the ball was
        /// dead), the man at that end takes it and breaks it now, so the TV umpire
        /// has something to look at.
        /// </summary>
        private IEnumerator EnsureBroken(BallResult r)
        {
            if (!CloseRunOut(r) || _wicketBroken) yield break;
            var keeperEnd = CloseEnd(r);
            var stumps = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var who = keeperEnd ? Refs.Keeper : Refs.Bowler;
            if (who == null || Vector3.Distance(Flat(who.transform.position), stumps) > 6f) who = NearestTo(stumps, null);
            var hand = who == null ? null : keeperEnd && who == Refs.Keeper && _keeper != null && _keeper.Gloves != null ? _keeper.Gloves : who.GetBoneTransform(HumanBodyBones.RightHand);
            _ball.gameObject.SetActive(true);
            if (hand != null)
            {
                // Wherever the ball is, it's thrown to him (a visible arc, never a jump into his hand).
                var from = _ball.position;
                var span = Mathf.Clamp(Vector3.Distance(from, hand.position) / 26f, 0.3f, 2f);
                if (Vector3.Distance(from, hand.position) > 0.5f)
                {
                    for (var t = 0f; t < span; t += Time.deltaTime)
                    {
                        var p = Vector3.Lerp(from, hand.position, t / span);
                        p.y += 0.5f * 9.81f * t * (span - t) * 0.8f;
                        _ball.position = OntoHand(p, hand, span - t);
                        yield return null;
                    }
                }
                _ball.position = hand.position;
                Extra(Hold(hand, 30f));
            }
            if (keeperEnd && who == Refs.Keeper && _keeper != null && _keeper.UsesClips && hand != null) _keeper.RunOutTake(hand.position, 0.3f);
            WatchStumps(keeperEnd, stumps + Vector3.forward * (keeperEnd ? 20f : -20f));
            yield return new WaitForSeconds(0.35f);
            // Not out: only with him in his ground.
            for (var until = Time.time + 2.5f; !r.RunOut && PastCrease(keeperEnd) < 0.3f && Time.time < until;) yield return null;
            BreakWicket(keeperEnd, keeperEnd ? Vector3.back : Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            Camera.EndFielding(false);
        }
    }
}
