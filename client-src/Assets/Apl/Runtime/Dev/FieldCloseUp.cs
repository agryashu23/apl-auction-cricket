using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Area 3 fielding evidence, TEST RUNS ONLY. With <c>-aplshots &lt;dir&gt; -aplfieldcloseup</c>, a fielding
    /// moment gets two 800×800 close-ups written next to the run's shots as <c>NN_closeup_&lt;tag&gt;_side.png</c> and
    /// <c>NN_closeup_&lt;tag&gt;_front.png</c>, numbered and logged in autoshot.txt like the moments. They're taken from
    /// two extra cameras that render into a texture, so the game's own camera and HUD never change.
    /// <list type="bullet">
    /// <item>side: square to the ball's line, 3.2 m off, at hand height — is the ball in the hands or against the body?</item>
    /// <item>front: from the far end of the line (the throw's target for a release, where the ball came from for a take):
    /// for a release, the chest is seen only if he's facing the man he's throwing to.</item>
    /// </list>
    /// What fires them: <see cref="FieldEvidence.Moment"/> (the developers' log points: gather, set, release, taken, stop);
    /// the existing <c>catch_hands</c> and <c>bails_off</c> moments (bails: a crease view and a wide one taking in a
    /// runner up to about 8 m short); and, as a fallback, the log lines <c>[APL throw]</c> (release) and
    /// <c>[APL catch] … stop / receive</c> when no explicit moment of that kind fired in the last 1.5 s.
    /// The host also runs the real-time-delayed screen moments (<see cref="FieldEvidence.Screen"/>) for the replays,
    /// which needs only <c>-aplshots</c>.
    /// </summary>
    public sealed class FieldCloseUp : MonoBehaviour
    {
        /// <summary>Close-ups on (-aplshots and -aplfieldcloseup).</summary>
        public static readonly bool On = Environment.GetCommandLineArgs().Contains("-aplfieldcloseup")
            && Environment.GetCommandLineArgs().Contains("-aplshots");

        private const int Px = 800;
        private static FieldCloseUp? _i;

        private Camera[] _cams = Array.Empty<Camera>();
        private readonly Queue<(string file, Texture2D tex)> _pending = new Queue<(string, Texture2D)>();
        private Transform? _ball;
        private float _ballLook;
        private readonly List<(float t, Vector3 p)> _trail = new List<(float, Vector3)>();
        private readonly Dictionary<string, float> _explicitAt = new Dictionary<string, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!On) return;
            Application.logMessageReceived += OnLog;
            // Two extra renders and PNG encodes make a long frame: game time never jumps more than a 30 fps frame
            // after one (as BatCapture), so the bodies, the ball and the motion meters don't leap across it
            // (Unity review r1: a 1-frame ball lag became ~1 m across a capture frame).
            Time.maximumDeltaTime = 1f / 30f;
            // Up from the start, so the ball's recent motion is known at the first moment.
            Host();
        }

        /// <summary>The host (created on first use in a -aplshots run; null otherwise).</summary>
        internal static FieldCloseUp? Host()
        {
            if (_i != null) return _i;
            if (!AutoShot.Active && !On) return null;
            var go = new GameObject("FieldCloseUp");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<FieldCloseUp>();
            if (On)
            {
                _i._cams = new Camera[2];
                for (var n = 0; n < _i._cams.Length; n++)
                {
                    var cg = new GameObject($"FieldCloseUpCam{n}");
                    cg.transform.SetParent(go.transform, false);
                    var cam = cg.AddComponent<Camera>();
                    cam.enabled = false;
                    _i._cams[n] = cam;
                }
            }
            _i.StartCoroutine(_i.Writer());
            return _i;
        }

        // ------------------------------------------------------------------ triggers

        /// <summary>From AutoShot.Moment: the moments that already exist get their close-ups.</summary>
        internal static void OnMoment(string name)
        {
            if (!On) return;
            if (name == "catch_hands") Capture(FieldEvidence.Tag("catch"), null, null, 0f);
            else if (name == "bails_off") Stumps(FieldEvidence.Tag("bails_off"));
        }

        /// <summary>An explicit FieldEvidence.Moment of this kind just fired (the log fallback then stands down).</summary>
        internal static void Explicit(string kind)
        {
            var h = Host();
            if (h != null) h._explicitAt[kind] = Time.realtimeSinceStartup;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("[APL throw]")) Fallback("release");
            else if (message.StartsWith("[APL catch]"))
            {
                if (message.Contains(" stop ")) Fallback("stop");
                // (A keeper's take of a delivery, " keeper ", is every ball: not filmed. A throw taken is " receive ".)
                else if (message.Contains(" receive ")) Fallback("taken");
            }
        }

        private static void Fallback(string kind)
        {
            var h = Host();
            if (h == null) return;
            // (1.5 s: the [APL catch] line is printed 0.5 s after the contact, the [APL throw] line at the arrival.)
            if (h._explicitAt.TryGetValue(kind, out var at) && Time.realtimeSinceStartup - at < 1.5f) return;
            var tag = FieldEvidence.Tag(kind) + "_fromlog";
            AutoShot.Moment(tag);
            Capture(tag, null, null, 0f);
        }

        // ------------------------------------------------------------------ the shots

        /// <summary>
        /// Two close-ups of <paramref name="focus"/> (null: the ball) <paramref name="delay"/> real seconds from now,
        /// square to the line from <paramref name="lineFrom"/> (null: the ball's own recent motion, else out from the pitch).
        /// </summary>
        internal static void Capture(string tag, Func<Vector3>? focus, Vector3? lineFrom, float delay)
        {
            if (!On) return;
            var h = Host();
            if (h == null) return;
            h.StartCoroutine(h.Film(tag, focus, lineFrom, delay));
        }

        /// <summary>The wicket nearest the ball, side-on along its popping crease: a tight view and a wide one (the runner short).</summary>
        private static void Stumps(string tag)
        {
            var h = Host();
            if (h == null) return;
            var b = h.BallNow();
            var z0 = b.z < 0 ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch;
            var toward = z0 < 0 ? 1f : -1f;
            var crease = z0 + 1.22f * toward;
            var side = SideTowardMain(new Vector3(0, 0, crease), Vector3.right);
            var views = new[]
            {
                ("crease", new Vector3(side.x * 7f, 0.9f, crease), new Vector3(0, 0.5f, crease + 0.6f * toward), 44f),
                ("wide", new Vector3(side.x * 12f, 2.4f, crease + 3f * toward), new Vector3(0, 0.6f, crease + 2.5f * toward), 52f),
            };
            h.StartCoroutine(h.Render(tag, views));
        }

        private IEnumerator Film(string tag, Func<Vector3>? focus, Vector3? lineFrom, float delay)
        {
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            var f = focus != null ? focus() : BallNow();
            Vector3 dir;
            if (lineFrom is Vector3 lf && Flat(f - lf).sqrMagnitude > 0.25f) dir = Flat(f - lf).normalized;
            else if (BallMotion() is Vector3 v) dir = Flat(v).normalized;
            else dir = Flat(f).sqrMagnitude > 1f ? Flat(f).normalized : Vector3.forward;
            var side = SideTowardMain(f, Vector3.Cross(Vector3.up, dir));
            var look = f + Vector3.up * 0.05f;
            var views = new[]
            {
                ("side", f + side * 3.2f + Vector3.up * 0.25f, look, 34f),
                ("front", f - dir * 3.4f + side * 0.6f + Vector3.up * 0.35f, look, 34f),
            };
            yield return Render(tag, views);
        }

        /// <summary>Render the views this frame into textures (the same set-up as the game camera), queued for the writer.</summary>
        private IEnumerator Render(string tag, (string name, Vector3 at, Vector3 look, float fov)[] views)
        {
            if (_cams.Length < views.Length) yield break;
            var main = Camera.main;
            var rts = new RenderTexture[views.Length];
            for (var n = 0; n < views.Length; n++)
            {
                var cam = _cams[n];
                if (main != null) cam.CopyFrom(main);
                rts[n] = RenderTexture.GetTemporary(Px, Px, 24);
                cam.targetTexture = rts[n];
                cam.transform.SetPositionAndRotation(views[n].at, Quaternion.LookRotation(views[n].look - views[n].at));
                cam.fieldOfView = views[n].fov;
                cam.enabled = true;
            }
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active;
            for (var n = 0; n < views.Length; n++)
            {
                _cams[n].enabled = false;
                RenderTexture.active = rts[n];
                var tex = new Texture2D(Px, Px, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Px, Px), 0, 0);
                var file = AutoShot.CloseUpFile($"{tag}_{views[n].name}");
                if (file != null) _pending.Enqueue((file, tex));
                else Destroy(tex);
                _cams[n].targetTexture = null;
                RenderTexture.ReleaseTemporary(rts[n]);
            }
            RenderTexture.active = prev;
        }

        /// <summary>One PNG a frame: encoded here, written off the main thread (CaptureWriter).</summary>
        private IEnumerator Writer()
        {
            while (true)
            {
                if (_pending.Count > 0)
                {
                    var (file, tex) = _pending.Dequeue();
                    CaptureWriter.Write(file, tex.EncodeToPNG());
                    Destroy(tex);
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------ the ball

        private void LateUpdate()
        {
            if (!On) return;
            if (_ball == null && Time.unscaledTime > _ballLook)
            {
                _ballLook = Time.unscaledTime + 1f;
                var view = FindAnyObjectByType<BallView>();
                if (view != null) _ball = view.transform;
            }
            if (_ball == null) return;
            _trail.Add((Time.unscaledTime, _ball.position));
            _trail.RemoveAll(x => Time.unscaledTime - x.t > 0.35f);
        }

        private Vector3 BallNow() => _ball != null ? _ball.position : Vector3.zero;

        /// <summary>The ball's motion over the last third of a second (null: hardly moving).</summary>
        private Vector3? BallMotion()
        {
            if (_trail.Count < 2) return null;
            var (t0, p0) = _trail[0];
            var (t1, p1) = _trail[_trail.Count - 1];
            if (t1 - t0 < 0.05f) return null;
            var v = (p1 - p0) / (t1 - t0);
            return Flat(v).magnitude > 1f ? v : (Vector3?)null;
        }

        /// <summary>Of the two sides of a line, the one the game camera is on (the close-up then reads like the game).</summary>
        private static Vector3 SideTowardMain(Vector3 at, Vector3 side)
        {
            var main = Camera.main;
            return main != null && Vector3.Dot(side, main.transform.position - at) < 0 ? -side : side;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }
}
