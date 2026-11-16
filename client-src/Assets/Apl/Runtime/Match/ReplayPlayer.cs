using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Plays a stretch of the <see cref="ReplayRecorder"/> tape back: the game
    /// frozen (time scale 0, so nothing in the match moves on), the tape written
    /// onto the bodies, ball and bats, the camera on one of the reference's
    /// replay cameras (AUTO cuts bowler's end → side-on through the shot, in slow
    /// motion → following the ball; or SIDE, BOWLER, STUMP CAM, AERIAL,
    /// BOUNDARY), a tracer behind the ball. It plays once; picking a camera plays
    /// it again from that camera; SKIP or a tap ends it. Then the live frame
    /// comes back exactly as it was.
    /// </summary>
    public sealed partial class ReplayPlayer
    {
        private readonly ReplayRecorder _tape;
        private readonly MatchHud _hud;
        private readonly MatchCamera _cam;
        private readonly Transform _ball;
        private readonly TrailRenderer _trail;
        private readonly LineRenderer _tracer;
        private bool _skip;

        public bool Running { get; private set; }

        public ReplayPlayer(ReplayRecorder tape, MatchHud hud, MatchCamera cam, Transform ball, TrailRenderer trail)
        {
            _tape = tape;
            _hud = hud;
            _cam = cam;
            _ball = ball;
            _trail = trail;
            _hud.Skipped += () => _skip = true;
            var go = new GameObject("ReplayTracer");
            _tracer = go.AddComponent<LineRenderer>();
            _tracer.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
            // The ball's line of action (ReplayPlayer.Line.cs): faint where it came from, strong at the ball.
            var warm = new Color(1f, 0.85f, 0.35f);
            _tracer.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(warm, 0f), new GradientColorKey(warm, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.3f, 0f), new GradientAlphaKey(0.85f, 1f) },
            };
            _tracer.widthMultiplier = 1f;
            _tracer.numCapVertices = 4;
            _tracer.numCornerVertices = 2;
            _tracer.alignment = LineAlignment.View;
            _tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tracer.positionCount = 0;
            go.SetActive(false);
        }

        /// <summary>
        /// Replay game time <paramref name="from"/>–<paramref name="to"/>;
        /// <paramref name="focus"/> is the moment that matters (bat on ball, ball on
        /// pad) — slowed down; <paramref name="shot"/> the way it went off the bat
        /// (zero: not hit), <paramref name="off"/> the batter's off side (world x sign).
        /// </summary>
        public IEnumerator Play(float from, float to, float focus, Vector3 shot, float off)
        {
            from = Mathf.Max(from, _tape.Oldest);
            to = Mathf.Min(to, _tape.Newest);
            if (Running || to - from < 1f) yield break;
            StallProbe.Mark($"replay play {from:0.00}-{to:0.00}");
            Running = true;
            _skip = false;
            var cam = 0;
            var restart = true;
            var t = from;
            // This pass's end: brought in once the ball is out of everyone's reach (Gone).
            var end = to;
            var trailWas = _trail.enabled;
            _trail.emitting = false;
            _trail.Clear();
            _trail.enabled = false;
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            Time.timeScale = 0f;
            BeginLine();
            _tape.Begin();
            _cam.Unscaled = true;
            _tracer.gameObject.SetActive(true);
            Rewire(() => cam, k => { cam = k; restart = true; });
            while (!_skip)
            {
                if (restart)
                {
                    restart = false;
                    t = from;
                    end = to;
                    var which = cam;
                    ResetGuard();
                    _cam.TrackLens(() => Guard(Lens(which, _tape.PlayTime, focus, shot, off)), smooth: 0.06f, cut: true, priority: MatchCamera.Replay);
                }
                _hud.ClockHeld(); // alive and keeping time frozen (the HUD's watchdog leaves it be)
                if (!_hud.Paused)
                {
                    // The pause menu's resume puts time back on; the replay keeps it frozen.
                    if (Time.timeScale != 0f) Time.timeScale = 0f;
                    var near = Mathf.Abs(t - focus) < 0.4f;
                    var speed = near ? (cam == 0 ? 0.3f : 0.5f) : 1f;
                    t += Time.unscaledDeltaTime * speed;
                }
                _tape.PlayTime = Mathf.Min(t, end);
                Tracer(t, focus);
                // No angle that is only grass or crowd and a speck (v23 rating f60/61/89/90): once the ball is
                // far from the batter the replay ends shortly after — except the BOUNDARY camera, which it flies at.
                if (end >= to && cam != 5 && Gone(t, focus, shot)) end = Mathf.Min(to, t + 0.35f);
                if (t >= end) break;
                yield return null;
            }
            StallProbe.Mark("replay play: end");
            _tracer.gameObject.SetActive(false);
            EndLine();
            _tape.End();
            _cam.Unscaled = false;
            _cam.Release(MatchCamera.Replay);
            if (!_hud.Paused) Time.timeScale = 1f;
            _trail.enabled = trailWas;
            _hud.HideReplayBar();
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
            Running = false;
        }

        /// <summary>The bar, its camera buttons calling back into this replay.</summary>
        private void Rewire(System.Func<int> current, System.Action<int> pick)
        {
            void Pick(int k)
            {
                pick(k);
                _hud.ShowReplayBar(k, Pick, () => _skip = true);
            }
            _hud.ShowReplayBar(current(), Pick, () => _skip = true);
        }

        /// <summary>Hit and well on its way: more than <see cref="FarBall"/> m from the batter's stumps.</summary>
        private bool Gone(float t, float focus, Vector3 shot)
        {
            if (shot.sqrMagnitude < 0.01f || t < focus + 0.7f || !_tape.PropAt(_ball, t, out var b)) return false;
            var d = b - new Vector3(0, 0, -GroundSpace.HalfPitch);
            d.y = 0;
            return d.magnitude > FarBall;
        }

        /// <summary>Past this the ball is a speck on any of our angles but the boundary's.</summary>
        private const float FarBall = 30f;

        /// <summary>
        /// The ball and the batter both in frame, from the side of the shot's line and above, as far back as
        /// their spread needs: the AUTO replay's follow-through (was: behind and above the ball only).
        /// </summary>
        private static (Vector3 pos, Vector3 look, float fov) Follow(Vector3 ball, Vector3 batter, Vector3 shot)
        {
            const float fov = 40f;
            var line = new Vector3(shot.x, 0, shot.z);
            line = line.sqrMagnitude > 1e-4f ? line.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, line);
            var mid = (ball + batter) / 2f;
            var flat = ball - batter;
            flat.y = 0;
            var tanH = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1.6f;
            var dist = Mathf.Max(9f, (flat.magnitude / 2f + 4f) / tanH);
            return (mid + side * dist + Vector3.up * (2f + dist * 0.35f), mid + Vector3.up * 0.5f, fov);
        }

        /// <summary>The replay cameras (world: the batter's stumps at z −10.06, the bowler's end +z).</summary>
        private (Vector3 pos, Vector3 look, float fov) Lens(int cam, float t, float focus, Vector3 shot, float off)
        {
            var stumps = new Vector3(0, 0, -GroundSpace.HalfPitch);
            var ball = _tape.PropAt(_ball, t, out var b) ? b : stumps + Vector3.up;
            var hit = shot.sqrMagnitude > 0.01f;
            var after = t > focus + 0.15f;
            switch (cam)
            {
                case 1: // SIDE: square of the wicket, level with the ball's progress.
                    var z = Mathf.Clamp(ball.z, -GroundSpace.HalfPitch, 6f);
                    return (new Vector3(off * 16f, 2.2f, z), after && hit ? ball : new Vector3(0, 1f, z), 32f);
                case 2: // BOWLER: low behind the bowler's arm, a long lens down the pitch.
                    return (new Vector3(0.6f, 2.2f, 19f), after && hit ? ball : stumps + Vector3.up * 0.9f, after && hit ? 34f : 16f);
                case 3: // STUMP CAM: from the batter's stumps up the pitch at the bowler.
                    return (stumps + new Vector3(0.05f, 0.42f, -0.25f), after && hit ? ball : new Vector3(0, 1.4f, GroundSpace.HalfPitch), 46f);
                case 4: // AERIAL: the spidercam, high behind the bowler down onto the striker (bowler and batter in it), then over the ball.
                    if (!(after && hit)) return (new Vector3(0, 15f, 6f), stumps + new Vector3(0, 0.6f, 3.5f), 44f);
                    return (new Vector3(ball.x * 0.6f, 16f, ball.z * 0.6f - 6f), (ball + stumps) / 2f, 50f);
                case 5: // BOUNDARY: from the rope the shot went to, looking back at it.
                    var dir = hit ? new Vector3(shot.x, 0, shot.z).normalized : Vector3.forward;
                    return (stumps + dir * 66f + Vector3.up * 3f, after && hit ? ball : stumps + Vector3.up, after && hit ? 30f : 12f);
                default: // AUTO: bowler's end for the delivery, side-on through the shot, then after the ball.
                    if (t < focus - 0.35f) return (new Vector3(0.7f, 2.6f, 17f), new Vector3(0, 1f, -GroundSpace.HalfPitch), 20f);
                    if (t < focus + 0.7f || !hit)
                        return (stumps + new Vector3(off * 6.5f, 1.4f, 1.6f), stumps + new Vector3(0, 0.9f, 0.4f), 30f);
                    return Follow(ball, stumps + Vector3.up, shot);
            }
        }
    }
}
