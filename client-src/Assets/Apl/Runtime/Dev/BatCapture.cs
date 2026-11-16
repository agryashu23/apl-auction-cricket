using System;
using System.Collections;
using System.IO;
using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Films the batter ball by ball for review (<c>-aplbatcap</c>, with
    /// <c>-aplmatchtest</c>): at the guard, the release, just before contact,
    /// contact and three follow-through moments it saves the player's own view
    /// plus four fixed cameras round the batter (off side, bowler's end, keeper,
    /// overhead) into <c>&lt;shots&gt;/batcap/b&lt;ball&gt;_&lt;moment&gt;_&lt;cam&gt;.png</c>,
    /// and logs what was asked for and what happened. It also cycles the aim and
    /// shot type so every direction gets filmed.
    /// </summary>
    public sealed class BatCapture : MonoBehaviour
    {
        public static readonly bool On = Environment.GetCommandLineArgs().Contains("-aplbatcap");

        private static BatCapture? _i;
        private static readonly Zone[] Aims = { Zone.StraightOff, Zone.Cover, Zone.MidWicket, Zone.Point, Zone.Square, Zone.StraightOn, Zone.ThirdMan, Zone.FineLeg };
        private static readonly ShotType[] Types = { ShotType.Stroke, ShotType.Push, ShotType.Loft };

        private string _dir = "";
        private int _ball = -1;
        private StreamWriter _log = null!;
        private Camera[] _cams = Array.Empty<Camera>();

        /// <summary>The aim this ball is filmed with.</summary>
        public static Zone Aim => Aims[Math.Max(0, _i?._ball ?? 0) % Aims.Length];

        /// <summary>The shot type this ball is filmed with.</summary>
        public static ShotType Type => Types[Math.Max(0, _i?._ball ?? 0) / Aims.Length % Types.Length];

        /// <summary>The foot this ball is filmed with (focus batch 1, footwork proof): AUTO, then BACK, then FRONT, ball by ball.</summary>
        public static bool? Foot => (Math.Max(0, _i?._ball ?? 0) % 3) switch { 1 => true, 2 => false, _ => (bool?)null };

        private static BatCapture? Get()
        {
            if (!On) return null;
            if (_i != null) return _i;
            var args = Environment.GetCommandLineArgs();
            var k = Array.IndexOf(args, "-aplshots");
            var root = k >= 0 && k + 1 < args.Length ? args[k + 1] : Application.persistentDataPath;
            var go = new GameObject("BatCapture");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<BatCapture>();
            _i._dir = Path.Combine(root, "batcap");
            Directory.CreateDirectory(_i._dir);
            _i._log = new StreamWriter(Path.Combine(_i._dir, "batcap.txt")) { AutoFlush = true };
            // Filming five views costs a long frame: game time never jumps more than a 30 fps frame,
            // so the swing and the ball still meet in the films (and the PNGs are written later).
            Time.maximumDeltaTime = 1f / 30f;
            _i.StartCoroutine(_i.Writer());
            // One camera per view so all five render in the same frame.
            _i._cams = new Camera[5];
            for (var n = 0; n < _i._cams.Length; n++)
            {
                var cg = new GameObject($"BatCaptureCam{n}");
                cg.transform.SetParent(go.transform, false);
                var cam = cg.AddComponent<Camera>();
                cam.enabled = false;
                _i._cams[n] = cam;
            }
            return _i;
        }

        private readonly System.Collections.Generic.Queue<(string file, Texture2D tex)> _pending = new System.Collections.Generic.Queue<(string, Texture2D)>();

        /// <summary>One PNG a frame, off the moment being filmed.</summary>
        private IEnumerator Writer()
        {
            while (true)
            {
                if (_pending.Count > 0)
                {
                    // Encode here (one a frame), write off the main thread (CaptureWriter: the r13 stalls).
                    var (file, tex) = _pending.Dequeue();
                    CaptureWriter.Write(file, tex.EncodeToPNG());
                    Destroy(tex);
                }
                yield return null;
            }
        }

        /// <summary>A new ball: <paramref name="what"/> is logged as its header.</summary>
        public static void Begin(string what)
        {
            var c = Get();
            if (c == null) return;
            c._ball++;
            c._log.WriteLine($"--- ball {c._ball:00}: {what} (filmed aim {Aim}, type {Type})");
        }

        public static void Note(string line) => Get()?._log.WriteLine($"  b{(_i?._ball ?? 0):00} {line}");

        /// <summary>Film the batter <paramref name="delay"/> real seconds from now.</summary>
        public static void Moment(string tag, Transform striker, float delay = 0f, Func<string>? info = null)
        {
            var c = Get();
            if (c == null || striker == null) return;
            c.StartCoroutine(c.Film(tag, striker, c._ball, delay, info));
        }

        private IEnumerator Film(string tag, Transform striker, int ball, float delay, Func<string>? info)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);
            if (striker == null) yield break;
            if (info != null) _log.WriteLine($"  b{ball:00} {tag}: {info()}");
            var stem = Path.Combine(_dir, $"b{ball:00}_{tag}");
            // Where he stands (his hips), on the ground; off side = +x for a right-hander.
            var a = striker.GetComponent<Animator>();
            var hips = a != null ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
            var p = hips != null ? hips.position : striker.position;
            p.y = 0;
            var off = new Vector3(1, 0, 0);
            var shots = new (string name, Vector3 at, Vector3 look, float fov)[]
            {
                ("off", p + off * 4.2f + new Vector3(0, 1.25f, 0.4f), p + new Vector3(0, 1.0f, 0.3f), 30f),
                ("leg", p - off * 4.2f + new Vector3(0, 1.25f, 0.4f), p + new Vector3(0, 1.0f, 0.3f), 30f),
                ("bowler", p + new Vector3(0, 1.45f, 6f), p + new Vector3(0, 1.0f, 0f), 26f),
                ("keeper", p + new Vector3(0, 2.0f, -4.5f), p + new Vector3(0, 0.9f, 3f), 55f),
                ("close", p + off * 1.7f + new Vector3(0, 1.35f, 0.9f), p + new Vector3(0, 1.15f, 0.1f), 34f),
            };
            var main = Camera.main;
            var rts = new RenderTexture[shots.Length];
            for (var n = 0; n < shots.Length; n++)
            {
                var cam = _cams[n];
                // Same rendering setup as the match camera, then our own view.
                if (main != null) cam.CopyFrom(main);
                rts[n] = RenderTexture.GetTemporary(960, 540, 24);
                cam.targetTexture = rts[n];
                cam.transform.SetPositionAndRotation(shots[n].at, Quaternion.LookRotation(shots[n].look - shots[n].at));
                cam.fieldOfView = shots[n].fov;
                cam.enabled = true;
            }
            // The player's own view of the same frame.
            CaptureWriter.Screen(stem + "_main.png");
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active;
            for (var n = 0; n < shots.Length; n++)
            {
                _cams[n].enabled = false;
                RenderTexture.active = rts[n];
                var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                _pending.Enqueue(($"{stem}_{shots[n].name}.png", tex));
                _cams[n].targetTexture = null;
                RenderTexture.ReleaseTemporary(rts[n]);
            }
            RenderTexture.active = prev;
        }
    }
}
