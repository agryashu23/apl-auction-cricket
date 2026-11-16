using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// The home art's posing and photography: bodies on their own layer, poses held from
    /// the clips, held props (bat, trophy) that are made per shot and destroyed after it,
    /// framing from the bones (not stale renderer bounds), and a true-alpha matte — each
    /// shot rendered once on black and once on white, alpha from the difference (hair cards
    /// and soft edges keep their own colour: no key colour to bleed in).
    /// </summary>
    public static partial class HomeArt
    {
        private static void Prepare(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            var a = go.GetComponent<Animator>();
            if (a != null) a.applyRootMotion = false;
        }

        private static void Solo(IEnumerable<Animator> all, Animator? only)
        {
            foreach (var b in all) b.gameObject.SetActive(b == only);
        }

        private static List<Transform> Props(params Transform?[] made) => made.Where(t => t != null).Select(t => t!).ToList();

        /// <summary>Hold the first of <paramref name="states"/> the body has at <paramref name="t"/> (0–1).</summary>
        private static void Pose(Animator a, float t, params string[] states)
        {
            var s = states.FirstOrDefault(x => Body.Has(a, x)) ?? (Body.Has(a, "Ready") ? "Ready" : null);
            if (s == null) return;
            a.Play(s, 0, t);
            a.speed = 0f;
            a.Update(0f);
        }

        /// <summary>Which way his chest faces, on the ground (from the shoulders and hips).</summary>
        private static Vector3 Chest(Animator a)
        {
            Vector3 Across(HumanBodyBones l, HumanBodyBones r)
            {
                var lt = a.GetBoneTransform(l);
                var rt = a.GetBoneTransform(r);
                return lt != null && rt != null ? rt.position - lt.position : Vector3.zero;
            }
            var right = Across(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm) + Across(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
            right.y = 0;
            if (right.sqrMagnitude < 1e-4f) return a.transform.forward;
            return Vector3.Cross(right.normalized, Vector3.up).normalized;
        }

        private static Vector3 Turn(Vector3 v, float degrees) => Quaternion.Euler(0, degrees, 0) * v;

        /// <summary>Turn the body so his chest faces <paramref name="dir"/>.</summary>
        private static void FaceTo(Animator a, Vector3 dir)
        {
            dir.y = 0;
            a.transform.rotation = Quaternion.Euler(0, Vector3.SignedAngle(Chest(a), dir.normalized, Vector3.up), 0) * a.transform.rotation;
            a.Update(0f);
        }

        /// <summary>
        /// A bat in his right hand: raised (up his forearm), or grounded at his side (toe by his right
        /// foot). The capture's own placement; the batter's card uses the match rig's real bat.
        /// </summary>
        private static Transform? Bat(Animator a, bool raised, bool grounded = false)
        {
            var hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            var arm = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var foot = a.GetBoneTransform(HumanBodyBones.RightFoot);
            if (hand == null || arm == null || foot == null) return null;
            var bat = BatMesh.Create().transform;
            var toe = new Vector3(foot.position.x, 0.02f, foot.position.z) + Chest(a) * 0.12f;
            var dir = raised ? (hand.position - arm.position).normalized : (toe - hand.position).normalized;
            var face = Vector3.ProjectOnPlane(Chest(a), dir);
            bat.SetPositionAndRotation(hand.position, Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward, -dir));
            Prepare(bat.gameObject);
            return bat;
        }

        private static Transform? Trophy(Animator a)
        {
            var r = a.GetBoneTransform(HumanBodyBones.RightHand);
            var l = a.GetBoneTransform(HumanBodyBones.LeftHand);
            if (r == null || l == null) return null;
            var t = Apl.Client.Match.Trophy.Make().transform;
            t.SetPositionAndRotation((r.position + l.position) / 2f + Vector3.up * 0.05f, Quaternion.identity);
            Prepare(t.gameObject);
            return t;
        }

        /// <summary>The bodies' extent from their bones (head top, hands, feet), plus what they hold.</summary>
        private static Bounds Extent(IEnumerable<Animator> who, IEnumerable<Transform> props)
        {
            var bones = new[] { HumanBodyBones.Head, HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
            Bounds? b = null;
            void Add(Vector3 p)
            {
                if (b == null) b = new Bounds(p, Vector3.zero);
                else { var x = b.Value; x.Encapsulate(p); b = x; }
            }
            foreach (var a in who)
            foreach (var bone in bones)
            {
                var t = a.GetBoneTransform(bone);
                if (t == null) continue;
                Add(t.position);
                if (bone == HumanBodyBones.Head) Add(t.position + Vector3.up * 0.24f);
                if (bone == HumanBodyBones.LeftFoot || bone == HumanBodyBones.RightFoot) Add(new Vector3(t.position.x, 0, t.position.z));
            }
            foreach (var p in props)
            foreach (var r in p.GetComponentsInChildren<Renderer>())
            {
                Add(r.bounds.min);
                Add(r.bounds.max);
            }
            var e = b ?? new Bounds(Vector3.up, Vector3.one * 2f);
            e.Expand(new Vector3(0.45f, 0.08f, 0.45f));
            return e;
        }

        /// <summary>
        /// Fit them to <paramref name="fill"/> of the frame's height (or width), seen from
        /// <paramref name="view"/> (subject → camera), the lens a little below their middle looking up.
        /// </summary>
        private static IEnumerator Snap(Camera cam, IEnumerable<Animator> who, IEnumerable<Transform> props, Vector3 view, int w, int h, string path, List<string> made, float fill = 0.88f)
        {
            yield return null;
            var e = Extent(who, props);
            view.y = 0;
            view = view.sqrMagnitude > 1e-4f ? view.normalized : Vector3.back;
            var side = Vector3.Cross(Vector3.up, view);
            var corners = new List<Vector3>();
            for (var i = 0; i < 8; i++) corners.Add(e.center + Vector3.Scale(e.extents, new Vector3(i % 2 == 0 ? -1 : 1, (i / 2) % 2 == 0 ? -1 : 1, i / 4 == 0 ? -1 : 1)));
            var halfW = corners.Max(c => Mathf.Abs(Vector3.Dot(c - e.center, side)));
            var depth = corners.Max(c => Vector3.Dot(c - e.center, view));
            const float fov = 30f;
            var tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var aspect = w / (float)h;
            var dist = Mathf.Max(e.extents.y / fill / tanV, halfW / fill / (tanV * aspect)) + depth;
            var bottom = e.min.y;
            var height = e.size.y;
            var ground = new Vector3(e.center.x, 0, e.center.z);
            cam.fieldOfView = fov;
            cam.transform.position = ground + view * dist + Vector3.up * (bottom + height * 0.4f);
            cam.transform.LookAt(ground + Vector3.up * (bottom + height * 0.5f));
            var black = Render(cam, w * 2, h * 2, Color.black);
            var white = Render(cam, w * 2, h * 2, Color.white);
            var outPx = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                Color premul = default;
                var alpha = 0f;
                for (var k = 0; k < 4; k++)
                {
                    var i = (y * 2 + k / 2) * w * 2 + x * 2 + k % 2;
                    var a = 1f - ((white[i].r - black[i].r) + (white[i].g - black[i].g) + (white[i].b - black[i].b)) / 3f;
                    a = Mathf.Clamp01(a);
                    // On black the colour is already premultiplied by coverage.
                    premul += new Color(black[i].r, black[i].g, black[i].b, 0);
                    alpha += a;
                }
                alpha /= 4f;
                outPx[y * w + x] = alpha > 0.004f ? new Color(Mathf.Clamp01(premul.r / 4f / alpha), Mathf.Clamp01(premul.g / 4f / alpha), Mathf.Clamp01(premul.b / 4f / alpha), alpha) : new Color(0, 0, 0, 0);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(outPx);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            made.Add(path);
        }

        private static Color[] Render(Camera cam, int w, int h, Color background)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = was;
            cam.targetTexture = null;
            rt.Release();
            var px = tex.GetPixels();
            Object.Destroy(tex);
            return px;
        }

        /// <summary>Every render on navy in a grid, six to a row (a team's six), for a quick look.</summary>
        private static void Sheet(List<string> files, string path)
        {
            const int cell = 240, cols = 6;
            var rows = Mathf.Max(1, (files.Count + cols - 1) / cols);
            var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGBA32, false);
            var navy = new Color(0.12f, 0.1f, 0.3f, 1f);
            var fillPx = Enumerable.Repeat(navy, cell * cols * cell * rows).ToArray();
            sheet.SetPixels(fillPx);
            for (var n = 0; n < files.Count; n++)
            {
                var img = new Texture2D(2, 2);
                if (!img.LoadImage(File.ReadAllBytes(files[n]))) continue;
                var k = Mathf.Min((cell - 8f) / img.width, (cell - 8f) / img.height);
                var iw = (int)(img.width * k);
                var ih = (int)(img.height * k);
                var ox = (n % cols) * cell + (cell - iw) / 2;
                var oy = (rows - 1 - n / cols) * cell + (cell - ih) / 2;
                for (var y = 0; y < ih; y++)
                for (var x = 0; x < iw; x++)
                {
                    var c = img.GetPixelBilinear((x + 0.5f) / iw, (y + 0.5f) / ih);
                    sheet.SetPixel(ox + x, oy + y, Color.Lerp(navy, new Color(c.r, c.g, c.b, 1), c.a));
                }
                Object.Destroy(img);
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.Destroy(sheet);
        }
    }
}
