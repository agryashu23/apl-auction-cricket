using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Test-only ground truth for foot slide (round 10c; command line <c>-aplwalkburst</c>,
    /// with <c>-aplshots &lt;dir&gt;</c>): the first opener's walk, the first opener's jog and the
    /// first fielder's walk out are each filmed from a fixed side camera at foot height over a
    /// black-and-white ground strip (0.25 m tiles, a white line every metre) — 45 PNGs at 15 fps
    /// (3 s) in &lt;dir&gt;/walkburst/ — and every frame logs FootLock's planted point and step
    /// next to both ankles and toes in world space ("[APL walkburst] …"), so a planted foot
    /// moving over the tiles can be seen and measured frame by frame.
    /// </summary>
    public sealed class WalkBurst : MonoBehaviour
    {
        private static readonly bool On = Environment.GetCommandLineArgs().Contains("-aplwalkburst");
        private static bool _walk, _jog, _fielder;
        private static WalkBurst? _host;
        private static Material? _black, _white;

        /// <summary>From Strides.Walk: film this walk if it's the first of its kind.</summary>
        public static void Maybe(Animator a, string clip, Vector3 from, Vector3 to)
        {
            if (!On || a == null) return;
            string? kind = null;
            if (clip.Contains("walk_out_bat") && !_walk) { _walk = true; kind = "walk"; }
            else if (clip.Contains("run_loop_with_bat") && !_jog) { _jog = true; kind = "jog"; }
            // A fielder with a long walk ahead (r12: the first one filmed arrived after 5 frames).
            else if (clip.StartsWith("fielder_walk_out") && !_fielder && Vector3.Distance(from, to) > 10f) { _fielder = true; kind = "fielder"; }
            if (kind == null) return;
            if (_host == null) _host = new GameObject("WalkBurst").AddComponent<WalkBurst>();
            _host.StartCoroutine(_host.Film(a, clip, kind, from, to));
        }

        private static string Dir()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplshots");
            var root = i >= 0 && i + 1 < args.Length ? args[i + 1] : Application.persistentDataPath;
            var dir = Path.Combine(root, "walkburst");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private IEnumerator Film(Animator a, string clip, string kind, Vector3 from, Vector3 to)
        {
            var dir = (to - from);
            dir.y = 0;
            if (dir.sqrMagnitude < 1e-4f) yield break;
            dir.Normalize();
            var side = Vector3.Cross(Vector3.up, dir);
            var strip = Strip(from, dir, side, Mathf.Min(14f, Vector3.Distance(from, to) + 1f));
            // Round 12 (r11: the subjects were tiny at the frame's edge, then gone): a side camera that tracks
            // him along the path, 3 m off (4.5 m for the jog), at shin height, his feet in the lower third.
            // It moves with him, so a planted foot must stay on the same tile while the tiles slide past.
            var cam = new GameObject("WalkBurstCam").AddComponent<Camera>();
            cam.enabled = false;
            var off = kind == "jog" ? 4.5f : 3f;
            var hipsT = a.GetBoneTransform(HumanBodyBones.Hips);
            void Aim()
            {
                var h = hipsT != null ? hipsT.position : a.transform.position;
                // Along the path only: his sway doesn't move the camera.
                var along = Vector3.Dot(h - from, dir);
                var p = from + dir * along;
                cam.transform.position = p + side * off + Vector3.up * 0.45f;
                cam.transform.LookAt(p + Vector3.up * 0.62f);
            }
            Aim();
            cam.fieldOfView = kind == "jog" ? 38f : 36f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var outDir = Dir();
            var points = new[]
            {
                a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.LeftToes),
                a.GetBoneTransform(HumanBodyBones.RightFoot), a.GetBoneTransform(HumanBodyBones.RightToes),
            };
            var feet = a.GetComponent<FootLock>();
            Debug.Log($"[APL walkburst] {kind} {a.name} {clip}: filming 45 frames at 15 fps to {outDir}");
            var owner = feet != null ? feet.Owner : -1;
            for (var f = 0; f < 45 && a != null; f++)
            {
                yield return new WaitForEndOfFrame();
                // His walk is over (arrived, halted or cut to the next shot): stop filming rather than film the empty strip.
                if (feet != null && (!feet.Active || feet.Owner != owner))
                {
                    Debug.Log($"[APL walkburst] {kind}: walk ended at frame {f}");
                    break;
                }
                Aim();
                // Nobody else between the camera and him (r12 `fielder_01`-`04`: a team-mate's leg filled the frame).
                var hidden = new System.Collections.Generic.List<Renderer>();
                var eye = cam.transform.position;
                var him = a.transform.position + Vector3.up * 0.6f;
                foreach (var other in FindObjectsByType<Animator>(FindObjectsSortMode.None))
                {
                    if (other == a) continue;
                    var p = other.transform.position + Vector3.up * 0.6f;
                    var t = Mathf.Clamp01(Vector3.Dot(p - eye, him - eye) / Mathf.Max(1e-4f, (him - eye).sqrMagnitude));
                    if (t <= 0f || t >= 0.98f || Vector3.Distance(p, eye + (him - eye) * t) > 0.9f) continue;
                    foreach (var r in other.GetComponentsInChildren<Renderer>())
                        if (r.enabled) { r.enabled = false; hidden.Add(r); }
                }
                cam.Render();
                foreach (var r in hidden) if (r != null) r.enabled = true;
                var was = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                tex.Apply(false);
                RenderTexture.active = was;
                File.WriteAllBytes(Path.Combine(outDir, $"{kind}_{f:00}.png"), tex.EncodeToPNG());
                string P(Transform? t) => t == null ? "-" : $"({t.position.x:0.000},{t.position.y:0.000},{t.position.z:0.000})";
                var lockInfo = feet != null ? $"lock pt={feet.LastPoint} back=({feet.LastBack.x:0.000},{feet.LastBack.z:0.000}) step=({feet.LastStep.x:0.000},{feet.LastStep.z:0.000}) v={feet.Velocity.magnitude:0.00}" : "no lock";
                var st = a.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[APL walkburst] {kind} f={f:00} t={Time.time:0.000} dt={Time.deltaTime:0.0000} state={(st.IsName(clip) ? clip : st.shortNameHash.ToString())} speed={a.speed:0.00} root=({a.transform.position.x:0.000},{a.transform.position.z:0.000}) yaw={a.transform.eulerAngles.y:0.0} {lockInfo} | ankleL={P(points[0])} toeL={P(points[1])} ankleR={P(points[2])} toeR={P(points[3])}");
                for (var until = Time.realtimeSinceStartup + 1f / 15f; Time.realtimeSinceStartup < until;) yield return null;
            }
            Destroy(cam.gameObject);
            rt.Release();
            Destroy(tex);
            Destroy(strip, 20f);
        }

        /// <summary>The ground strip: 0.25 m tiles in black and white along the path, a white line across every metre.</summary>
        private static GameObject Strip(Vector3 from, Vector3 dir, Vector3 side, float length)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            _black ??= new Material(shader) { color = new Color(0.05f, 0.05f, 0.05f) };
            _white ??= new Material(shader) { color = Color.white };
            var root = new GameObject("WalkBurstStrip");
            for (var i = 0; i * 0.25f < length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(root.transform);
                q.transform.position = from + dir * (i * 0.25f + 0.125f) + Vector3.up * 0.012f;
                q.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
                var metre = i % 4 == 0;
                q.transform.localScale = new Vector3(metre ? 2.4f : 1.6f, 0.25f, 1);
                q.GetComponent<MeshRenderer>().sharedMaterial = metre || i % 2 == 0 ? _white : _black;
            }
            return root;
        }
    }
}
