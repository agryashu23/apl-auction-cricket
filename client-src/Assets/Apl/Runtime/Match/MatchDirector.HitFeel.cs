using System.Collections;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// How bat on ball feels. Middled (perfectly timed, clean, good quality): the deep
    /// knock, a flash of light off the bat's face at the sweet spot, a strong buzz, and
    /// a short slow-motion beat (0.3× for ~0.35 s, easing back) — the user's own shots
    /// only, never the AI's. Edged or mistimed: a thin nick and a light tick, no flash.
    /// Otherwise the plain knock.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The slow-motion beat: time scale, how long it holds (real s), how long it eases back.</summary>
        public const float SlowMoScale = 0.3f, SlowMoHold = 0.35f, SlowMoEase = 0.3f;

        /// <summary>A middled shot: perfectly timed, off the middle (quality at least this), no edge.</summary>
        public const double MiddledQuality = 0.72;

        private Coroutine? _slowMo;
        private Transform? _flash;
        /// <summary>The batter's reaction shows an inside edge onto the pad (BatterReaction): a nick, not silence.</summary>
        private bool _insideEdge;

        /// <summary>At the ball's moment: the sound, the buzz, and for a middled hit the flash and slow-mo.</summary>
        private void ContactFeel(BallResult r, bool hit)
        {
            var nick = r.Edge || _insideEdge;
            _insideEdge = false;
            if (!hit && !nick) return;
            var middled = hit && !r.Edge && r.Timing == Timing.Perfect && r.Quality >= MiddledQuality;
            var thin = nick || (r.Timing != Timing.Perfect && r.Timing != Timing.Good) || r.Quality < 0.35;
            // Wooden, by kind (audio-modes, round 13: MatchDirector.AudioFx.BatKind): edge / stroke / loft.
            Sfx.Bat(BatKind(r, nick), nick ? 0.55f : (float)r.Quality);
            if (!_match.UserBatting) return;
            Haptics.Hit(r.Timing, thin);
            if (r.Six) StartCoroutine(After(Mathf.Clamp((float)r.EndTime, 0.5f, 4f), Haptics.Six));
            if (!middled) return;
            SweetSpotFlash(_striker.UsesClips ? _striker.BatMiddle : _ball.position);
            if (_slowMo != null) StopCoroutine(_slowMo);
            _slowMo = StartCoroutine(SlowMo());
        }

        /// <summary>
        /// One line per ball for the PM's counts (final rating 2026-11-14: sixes v fours, bowled / lbw):
        /// <c>[APL outcome] user bats | Loft PERFECT q0.92 carry 81.3m | SIX 6 | out -</c>. The release grade
        /// is the user's (null: the AI bowled it). Result: SIX / FOUR / OUT / DOT / RUNS / WIDE.
        /// </summary>
        private void LogOutcome(BallResult r, string side)
        {
            var shot = r.Shot.IsLeave ? "Leave" : r.Shot.Type.ToString();
            var carry = r.Path.Carry > 0 ? $" carry {r.Path.Carry:0.0}m" : "";
            var result = r.Wicket ? "OUT" : r.Six ? "SIX" : r.Four ? "FOUR" : r.Wide ? "WIDE" : r.Runs == 0 ? "DOT" : "RUNS";
            var release = r.Delivery.Release is Timing g ? $" release {g}" : "";
            Debug.Log($"[APL outcome] {side} | {shot} {r.Timing} q{r.Quality:0.00}{carry}{(r.Edge ? " edge" : "")}{(r.PadHit ? " pad" : "")} | {result} {r.Runs} | out {r.HowOut ?? "-"} | {r.Delivery.Variation} {r.Delivery.Length}{release}");
        }

        /// <summary>
        /// Off the pad (MatchDirector.Outcome, a pad hit): the ball dies against the pad and drops a little way in
        /// front of him — a short hop back up the pitch and a roll, not on through his legs to the keeper.
        /// </summary>
        private IEnumerator PadDrop(BallResult r, double impactTime, bool leftHanded)
        {
            var hit = r.Trajectory.At(impactTime);
            var from = GroundSpace.Ball(hit.x, hit.y + 0.036, hit.z, leftHanded);
            var land = GroundSpace.Ball(hit.x + (hit.x >= 0 ? 0.25 : -0.25), 0.036, hit.z + 0.9, leftHanded);
            const float drop = 0.35f;
            for (var t = 0f; t < drop; t += Time.deltaTime)
            {
                var k = t / drop;
                var p = Vector3.Lerp(from, land, k);
                p.y = Mathf.Lerp(from.y, land.y, k * k) + 0.12f * Mathf.Sin(k * Mathf.PI);
                if (_ball != null) _ball.position = p;
                yield return null;
            }
            if (_ball != null) _ball.position = land;
        }

        private IEnumerator SlowMo()
        {
            for (var t = 0f; t < SlowMoHold + SlowMoEase; t += Time.unscaledDeltaTime)
            {
                // The pause menu owns the clock while it's up.
                if (!_hud.Paused) Time.timeScale = t < SlowMoHold ? SlowMoScale : Mathf.Lerp(SlowMoScale, 1f, Mathf.SmoothStep(0, 1, (t - SlowMoHold) / SlowMoEase));
                yield return null;
            }
            if (!_hud.Paused) Time.timeScale = 1f;
            _slowMo = null;
        }

        private void OnDisable()
        {
            // Never leave the game in slow motion.
            if (_slowMo != null && (_hud == null || !_hud.Paused)) Time.timeScale = 1f;
        }

        /// <summary>A white-gold flare at the sweet spot, facing the camera, gone in a quarter of a second (real time).</summary>
        private void SweetSpotFlash(Vector3 at)
        {
            if (_flash == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "SweetSpotFlash";
                var r = quad.GetComponent<Renderer>();
                r.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit")) { mainTexture = Star() };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _flash = quad.transform;
            }
            StartCoroutine(Flash(at));
        }

        private IEnumerator Flash(Vector3 at)
        {
            var flash = _flash!;
            var mat = flash.GetComponent<Renderer>().material;
            flash.gameObject.SetActive(true);
            for (var t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
            {
                var k = t / 0.25f;
                var cam = UnityEngine.Camera.main;
                if (cam != null) flash.SetPositionAndRotation(at + (cam.transform.position - at).normalized * 0.15f, cam.transform.rotation);
                flash.localScale = Vector3.one * Mathf.Lerp(0.25f, 0.9f, Mathf.Sqrt(k));
                mat.color = new Color(1f, 0.95f, 0.75f, (1f - k) * (1f - k));
                yield return null;
            }
            flash.gameObject.SetActive(false);
        }

        /// <summary>A soft four-pointed glint.</summary>
        private static Texture2D Star()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "sweet_spot_flash", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var dx = Mathf.Abs(x + 0.5f - n / 2f) / (n / 2f);
                var dy = Mathf.Abs(y + 0.5f - n / 2f) / (n / 2f);
                var core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f);
                var rays = Mathf.Clamp01(1f - dx * 8f) * (1f - dy) + Mathf.Clamp01(1f - dy * 8f) * (1f - dx);
                var a = Mathf.Clamp01(core + rays * 0.8f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Chopped on: off the inside of the bat down into the stumps (it was going past them).</summary>
        private IEnumerator ChoppedOn(bool leftHanded)
        {
            var from = _striker.UsesClips ? _striker.BatMiddle : _ball.position;
            var to = new Vector3(0, 0.35f, -GroundSpace.HalfPitch + 0.05f);
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.22f)
            {
                var p = Vector3.Lerp(from, to, t);
                p.y = Mathf.Lerp(from.y, to.y, t) - 0.15f * Mathf.Sin(t * Mathf.PI);
                _ball.position = p;
                yield return null;
            }
            _ball.position = to;
        }
    }
}
