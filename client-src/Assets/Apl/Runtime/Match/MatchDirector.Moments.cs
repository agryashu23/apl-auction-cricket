using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The big moments, directed like the reference: a wicket (the shout, the
    /// umpire's finger on a close-up with the BOWLED / CAUGHT / LBW graphic, the
    /// celebration, the replay or Hawk-Eye, the out batter walking off with his
    /// card), a boundary (the umpire signalling 4 or 6 with the big number, a
    /// replay of the big ones), the DRS review and Hawk-Eye for lbw shouts. Each
    /// is skippable with a tap; the replays never run gameplay (see
    /// <see cref="ReplayPlayer"/>).
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Seconds after a catch (run out, stumping) that the finger goes up: the camera's on the umpire by then.</summary>
        private const float UmpireCue = 0.6f;

        private static readonly Color Purple = new Color(0.43f, 0.16f, 0.78f);

        private readonly Dictionary<string, Franchise> _franchises = new Dictionary<string, Franchise>();

        /// <summary>The side batting now, and the side in the field (either can be the user's).</summary>
        private Franchise BattingSide => Side(_match.Batting.TeamId, _bat);
        private Franchise FieldingSide => Side(_match.Bowling.TeamId, _bowl);

        private Franchise Side(string id, Franchise fallback)
        {
            if (_franchises.TryGetValue(id, out var f)) return f;
            f = _data.Franchises.FirstOrDefault(x => x.Id == id) ?? fallback;
            _franchises[id] = f;
            return f;
        }

        /// <summary>Wait <paramref name="seconds"/> of game time from <paramref name="t0"/>, or until a tap.</summary>
        private IEnumerator Beat(float t0, float seconds)
        {
            while (Time.time - t0 < seconds && !_skip) yield return null;
        }

        // ------------------------------------------------------------ shots

        /// <summary>The umpire from the front, chest up (his signal fills the frame).</summary>
        private void UmpireShot(Animator? who = null)
        {
            var u = who != null ? who : Refs.Umpire;
            if (u == null) return;
            var hips = Hips(u);
            // In front of him, a little off to one side (he faces the batter, or the pitch at square leg).
            // His body's own facing (shoulders and hips), not the transform's: the square-leg umpire's clips
            // turn him, and the transform's forward put the camera behind him, his back filling the frame (r12 E143-144).
            var (face, _) = Facing(u);
            var across = Vector3.Cross(Vector3.up, face);
            // Round to the side the bowler isn't on, and wider: straight in front, his follow-through
            // put him between the camera and the signal (v8 f144).
            var sign = u.transform.position.x >= 0 ? 1f : -1f;
            if (who == null && _bowler != null)
            {
                var b = Vector3.Dot(_bowler.transform.position - u.transform.position, across);
                if (Mathf.Abs(b) > 0.2f) sign = -Mathf.Sign(b);
            }
            var side = across * (2.4f * sign);
            Camera.Track(() =>
            {
                var h = hips.position;
                return (new Vector3(h.x, 1.6f, h.z) + face * 3.9f + side, new Vector3(h.x, 1.3f, h.z));
            }, fov: 32f, smooth: 0.05f, cut: true, priority: MatchCamera.Moment);
        }

        /// <summary>Low and side-on at the batter's stumps (bowled: the stumps cartwheel in the foreground).</summary>
        private void StumpShot(bool leftHanded)
        {
            var off = leftHanded ? -1f : 1f;
            var stumps = new Vector3(0, 0.4f, -GroundSpace.HalfPitch);
            Camera.Track(() => (stumps + new Vector3(off * 1.7f, 0.2f, 1.9f), stumps), fov: 34f, smooth: 0.05f, cut: true, priority: MatchCamera.Moment);
        }

        /// <summary>In front of someone celebrating, following him (an aeroplane runs off).</summary>
        private void CelebrationShot(Animator a)
        {
            var hips = Hips(a);
            Camera.Track(() =>
            {
                var (f, right) = Facing(a);
                var c = hips.position + Vector3.up * 0.3f;
                return (c + f * 5.2f + right * 1.4f + Vector3.up * 0.4f, c);
            }, fov: 34f, smooth: 0.35f, cut: true, priority: MatchCamera.Moment);
        }

        // ---------------------------------------------------------- wickets

        /// <summary>
        /// After the wicket's routines are under way (<see cref="Wicket"/>): the
        /// shout on the bowler (lbw, caught behind) or the stumps (bowled), the
        /// umpire's close-up with the graphic, the DRS offer, the celebration, the
        /// replay (Hawk-Eye for lbw) and the walk-off card.
        /// </summary>
        private IEnumerator WicketDirection(BallResult r, Animator? catcher, bool appeal)
        {
            _msfx.Swell(2.1f, 4f);
            // From here the box talks about the next man (no name: he isn't in yet).
            _comm.Batter = null;
            // The ball's dead: nothing more to keep in frame.
            Camera.KeepInFrame = null;
            _eventTime = Time.time;
            // Every wicket gets the whole package (r9 f68: the test runs' close-up ball got a plain "OUT!" box):
            // the close-up camera is only for the ball itself.
            _closeUp = false;
            StallProbe.Mark($"wicket package: {r.HowOut}");
            var t0 = Time.time;
            var outPlayer = r.OutBatter ?? _match.Order.LastOrDefault(b => b.HowOut != null)?.Player;
            var line = outPlayer == null ? null : _match.Order.FirstOrDefault(b => b.Player.Id == outPlayer.Id);
            var leftHanded = _striker != null && _striker.LeftHanded;
            var word = BroadcastStats.Dismissal(r.HowOut);
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;
            if (r.HowOut == "bowled")
            {
                StumpShot(leftHanded);
                _hud.EventGraphic(word, "", UiKit.Bad);
                yield return Beat(t0, 1.5f);
            }
            else if (appeal)
            {
                // The shout first, then the umpire as he makes up his mind (the finger at 1.45 s).
                if (_bowler != null)
                {
                    Camera.Target = Hips(_bowler);
                    Camera.Show(MatchCamera.Mode.Player, false, MatchCamera.Moment);
                }
                yield return Beat(t0, 1.0f);
                UmpireShot();
                yield return Beat(t0, 1.5f);
                _hud.EventGraphic(word, "", UiKit.Bad);
                yield return Beat(t0, 2.7f);
            }
            else
            {
                _hud.EventGraphic(word, FielderCredit(r), UiKit.Bad); // by name and position (match-play: MatchDirector.Credits)
                yield return Beat(t0, 0.35f);
                // Stumped, or run out at the batter's end: the square-leg umpire's decision.
                UmpireShot(UmpireOf(r));
                yield return Beat(t0, 2.0f);
            }

            // The batting side's review of an lbw given out.
            if (r.HowOut == "lbw" && _match.UserBatting && _match.CanReview())
            {
                var stood = true;
                yield return OfferReview(r, overturned => stood = !overturned);
                if (!stood)
                {
                    EndWicket();
                    yield break;
                }
            }

            // The celebration: the catcher if he took it away from the bowler, else the bowler.
            var hero = catcher != null && catcher != Refs.Bowler ? catcher : _bowler != null ? _bowler.GetComponent<Animator>() : null;
            if (!_skip && hero != null)
            {
                StallProbe.Mark("wicket: celebration");
                CelebrationShot(hero);
                yield return Beat(Time.time, 1.9f);
            }
            // A referral has already shown it from every angle.
            if (!_skip && _referred != r)
            {
                if (r.HowOut == "lbw" && r.Lbw != null) yield return HawkeyeShow(r, "HAWK-EYE");
                else
                {
                    StallProbe.Mark("wicket: replay");
                    yield return WicketReplay(r, _contactTime > 0);
                }
            }
            if (!_skip && line != null && outPlayer != null && _batters.TryGetValue(outPlayer.Id, out var rig) && rig.gameObject.activeInHierarchy)
            {
                StallProbe.Mark("wicket: dismissed card");
                yield return CardOn(rig.GetComponent<Animator>(), BroadcastStats.Dismissed(line, BattingSide), 2.6f, walking: true);
            }
            // His innings on the wagon wheel.
            StallProbe.Mark("wicket: wagon wheel");
            if (!_skip && outPlayer != null) yield return WagonMoment(outPlayer, $"{outPlayer.Name.ToUpperInvariant()}  ·  {BroadcastStats.Dismissal(r.HowOut)}");
            EndWicket();
        }

        private void EndWicket()
        {
            StallProbe.Mark("wicket: end");
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
            Camera.Release(MatchCamera.Moment);
        }

        // ------------------------------------------------------- boundaries

        /// <summary>The ball's over the rope: the umpire signals it on a close-up with the big 4 / 6; the big ones get a replay.</summary>
        private IEnumerator BoundaryDirection(BallResult r)
        {
            _msfx.Swell(r.Six ? 2.3f : 1.9f, 4f);
            // The ball's dead: nothing more to keep in frame.
            Camera.KeepInFrame = null;
            _eventTime = Time.time;
            var signal = r.Six ? "umpire_six" : "umpire_four";
            if (_closeUp)
            {
                Show(r.Six ? "SIX!" : "FOUR!", "", UiKit.Gold, 2f);
                Umpire(signal);
                yield return new WaitForSeconds(2f);
                yield break;
            }
            var t0 = Time.time;
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;
            UmpireShot();
            Umpire(signal);
            _hud.EventGraphic(r.Six ? "6" : "4", r.Six ? "SIX" : "FOUR", r.Six ? Purple : UiKit.Accent, 2.2f);
            yield return Beat(t0, 2.4f);
            var big = r.Six || r.Launch > 8 || r.Timing == Timing.Perfect;
            if (big && !_skip) yield return ReplayMoment(r, true);
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
            Camera.Release(MatchCamera.Moment);
        }

        // ---------------------------------------------------------- replays

        /// <summary>The replay of this ball: from the bowler's last strides to the end of the moment, slowed through the shot.</summary>
        private IEnumerator ReplayMoment(BallResult r, bool hit)
        {
            if (_closeUp || _released < 0) yield break;
            var lh = _striker != null && _striker.LeftHanded;
            var atBat = RealAt(r.Trajectory.TimeAt(Batting.ContactZ(r.Delivery.Length)));
            var focus = hit && _contactTime > 0 ? _contactTime : atBat;
            var end = _eventTime > 0 ? _eventTime + 0.6f : Time.time;
            var to = Mathf.Min(end, focus + (hit ? 3.4f : 1.8f));
            var shot = hit && r.ExitSpeed > 0 ? GroundSpace.Direction(r.ExitAngle, lh) : Vector3.zero;
            yield return _replays.Play(_released - 0.7f, to, focus, shot, lh ? -1f : 1f);
            // The tap was for the replay only.
            _skip = false;
        }

        // -------------------------------------------------------- Hawk-Eye

        private readonly List<Renderer> _hidden = new List<Renderer>();

        /// <summary>Hawk-Eye draws on an empty pitch: everyone (and the ball, the bats) out of sight, or back.</summary>
        private void HideCast(bool hide)
        {
            if (!hide)
            {
                foreach (var r in _hidden) if (r != null) r.enabled = true;
                _hidden.Clear();
                return;
            }
            var roots = _castBodies.Where(a => a != null).Select(a => a.transform).ToList();
            roots.Add(_ball);
            roots.AddRange(_batters.Values.Select(b => b.Bat));
            foreach (var t in roots)
            {
                if (t == null) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    r.enabled = false;
                    _hidden.Add(r);
                }
            }
        }

        /// <summary>
        /// The ball-tracking for an lbw shout, frozen-frame: the path grows from the
        /// hand to the pad and on to the stumps while the read-out fills in
        /// (pitching, impact, wickets) and the decision lands. Tap skips.
        /// </summary>
        private IEnumerator HawkeyeShow(BallResult r, string title)
        {
            var lbw = r.Lbw;
            if (lbw == null || _closeUp) yield break;
            // Test runs: the ball-tracking verdict (match-play's evidence moments).
            if (Autoplay) StartCoroutine(AfterReal(2.5f, () => Apl.Client.Dev.AutoShot.Moment("drs"))); // real time: the review runs at timeScale 0
            var tr = lbw.Tracking;
            var lh = _striker != null && _striker.LeftHanded;
            var off = lh ? -1f : 1f;
            var wasPaused = _hud.Paused;
            Time.timeScale = 0f;
            Camera.Unscaled = true;
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;
            HideCast(true);
            _hawkView.Show(tr, lh);
            _hud.ShowHawkeye(title);
            var k = 0f;
            var stumps = new Vector3(0, 0.4f, -GroundSpace.HalfPitch);
            Camera.TrackLens(() => k < 0.6f
                ? (new Vector3(0.2f, 3.4f, 17f), new Vector3(0, 0.3f, -7f), 26f)
                : (stumps + new Vector3(off * 2.4f, 0.7f, 5.2f), stumps + new Vector3(0, 0, 0.6f), 32f), smooth: 0f, cut: true, priority: MatchCamera.Replay);
            var rows = 0;
            var decided = -1f;
            var held = 0f;
            // Restored however this ends (finished, skipped, or the coroutine stopped): never left frozen (r12 hang).
            try
            {
                while (!_skip)
                {
                    _hud.ClockHeld();
                    if (!_hud.Paused)
                    {
                        if (Time.timeScale != 0f) Time.timeScale = 0f;
                        var dt = Time.unscaledDeltaTime;
                        if (k < 1f) k = Mathf.Min(1f, k + dt / 3.4f);
                        else held += dt;
                    }
                    _hawkView.Grow(k);
                    if (rows == 0 && k >= 0.3f)
                    {
                        rows++;
                        _hud.HawkeyeRow(0, "PITCHING", HawkeyeView.Text(tr.Pitching), HawkeyeView.Color(tr.Pitching));
                    }
                    if (rows == 1 && k >= 0.6f)
                    {
                        rows++;
                        _hud.HawkeyeRow(1, "IMPACT", HawkeyeView.Text(tr.Impact), HawkeyeView.Color(tr.Impact));
                    }
                    if (rows == 2 && k >= 1f)
                    {
                        rows++;
                        _hud.HawkeyeRow(2, "WICKETS", HawkeyeView.Text(tr.Wickets), HawkeyeView.UiColor(tr.Wickets));
                    }
                    if (rows == 3 && decided < 0 && held >= 0.5f)
                    {
                        decided = held;
                        var outNow = lbw.Out;
                        var text = lbw.Reviewed && lbw.UmpiresCall ? (outNow ? "OUT · UMPIRE'S CALL" : "NOT OUT · UMPIRE'S CALL") : outNow ? "OUT" : "NOT OUT";
                        _hud.HawkeyeDecision(text, outNow ? HawkeyeView.Out : HawkeyeView.In);
                    }
                    if (decided >= 0 && held - decided >= 1.6f) break;
                    yield return null;
                }
            }
            finally
            {
                _hawkView.Hide();
                HideCast(false);
                _hud.HideHawkeye();
                _hud.SetSkippable(false);
                _hud.Broadcasting(false);
                Camera.Unscaled = false;
                Camera.Release(MatchCamera.Replay);
                if (!_hud.Paused && !wasPaused) Time.timeScale = 1f;
                // The skip was for Hawk-Eye only.
                _skip = false;
            }
        }

        /// <summary>
        /// The REVIEW button for the user's side (a few seconds to decide; the test
        /// runs always review). Reviewed: the third umpire's Hawk-Eye, and the
        /// engine's verdict; <paramref name="done"/> hears whether it was overturned.
        /// </summary>
        private IEnumerator OfferReview(BallResult r, System.Action<bool> done)
        {
            var pressed = false;
            var t0 = Time.time;
            _hud.ShowReview(4f, () => pressed = true);
            while (!pressed && Time.time - t0 < 4f && !(Autoplay && Time.time - t0 > 1f)) yield return null;
            _hud.HideReview();
            if (!pressed && !Autoplay)
            {
                done(false);
                yield break;
            }
            var batting = _match.UserBatting;
            var reviewed = _match.Review();
            if (reviewed?.Lbw == null)
            {
                done(false);
                yield break;
            }
            _hud.EventGraphic("DRS", batting ? "BATTING REVIEW" : "FIELDING REVIEW", Purple, 1.4f);
            yield return Beat(Time.time, 1.2f);
            _skip = false;
            yield return HawkeyeShow(reviewed, batting ? "DRS · BATTING REVIEW" : "DRS · FIELDING REVIEW");
            var overturned = reviewed.Lbw.Overturned;
            if (overturned)
            {
                _hud.EventGraphic(reviewed.Wicket ? "OUT" : "NOT OUT", "DECISION OVERTURNED", reviewed.Wicket ? UiKit.Bad : UiKit.Good, 2f);
                Umpire(reviewed.Wicket ? "umpire_out" : "umpire_revoke_last_signal");
                UmpireShot();
                yield return Beat(Time.time, 1.8f);
            }
            _hud.ShowScore(_match, BattingSide, FieldingSide);
            done(overturned);
        }

        /// <summary>
        /// A shout turned down (or a review the AI side asked for): Hawk-Eye on it,
        /// and — the user bowling — his own review of the not-out. Returns via
        /// <paramref name="givenOut"/> whether a review turned it into a wicket.
        /// </summary>
        private IEnumerator NotOutShout(BallResult r, System.Action<bool> givenOut)
        {
            givenOut(false);
            if (r.Lbw == null || !r.Appeal || r.Wicket || _closeUp) yield break;
            if (!_match.UserBatting && _match.CanReview())
            {
                var overturned = false;
                yield return OfferReview(r, o => overturned = o);
                if (overturned) givenOut(true);
                yield break;
            }
            var title = r.Lbw.Reviewed ? (r.Lbw.ReviewedByBatting ? "DRS · BATTING REVIEW" : "DRS · FIELDING REVIEW") : "HAWK-EYE";
            yield return HawkeyeShow(r, title);
        }
    }
}
