using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Field rework evidence, TEST RUNS ONLY (<c>-aplshots &lt;dir&gt; -aplfieldmap</c>): a straight-down picture of the
    /// ground (1000×1000, the bowler's end at the top) written next to the run's shots as
    /// <c>NN_closeup_fieldmap_&lt;tag&gt;.png</c> and logged in autoshot.txt. Drawn on it: each fielder's mark from the
    /// engine (red square), the 30-yard ring as it really is (white dots, from <see cref="Apl.Core.Play.Field.RingRadius"/>)
    /// and the pitch's two ends (yellow). The bodies themselves are where the render shows them, so a man not on his
    /// mark shows up. Rendered by its own camera into a texture: the game's camera and HUD never change.
    /// </summary>
    public static class FieldMap
    {
        public static readonly bool On = Environment.GetCommandLineArgs().Contains("-aplfieldmap");

        private const int Px = 1000;
        private const float Half = 82f, Height = 140f, HalfPitch = 10.06f;

        /// <summary>Renders the ground from straight above and writes it with the marks drawn on (no-op outside a -aplshots run).</summary>
        public static void Capture(string tag, IEnumerable<Vector3> marks, bool leftHanded)
        {
            var file = AutoShot.CloseUpFile("fieldmap_" + tag);
            if (file == null) return;
            var go = new GameObject("FieldMapCam");
            var rt = new RenderTexture(Px, Px, 24);
            var prev = RenderTexture.active;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = Half;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = Height + 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                cam.transform.position = new Vector3(0, Height, 0);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(Px, Px, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Px, Px), 0, 0);
                void Mark(Vector3 world, int r, Color c, bool hollow)
                {
                    var s = cam.WorldToScreenPoint(world);
                    int cx = Mathf.RoundToInt(s.x), cy = Mathf.RoundToInt(s.y);
                    for (var dx = -r; dx <= r; dx++)
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (hollow && Math.Abs(dx) < r - 1 && Math.Abs(dy) < r - 1) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < Px && y < Px) tex.SetPixel(x, y, c);
                    }
                }
                // The ring: the engine's batter frame is the striker's stumps, f up the pitch (mirrored for a left-hander).
                for (var deg = -180; deg < 180; deg += 3)
                {
                    var r = Apl.Core.Play.Field.RingRadius(deg, Apl.Core.Play.Field.InnerRing);
                    var a = deg * Math.PI / 180;
                    var x = (float)(r * Math.Sin(a)) * (leftHanded ? -1 : 1);
                    Mark(new Vector3(x, 0, -HalfPitch + (float)(r * Math.Cos(a))), 1, Color.white, false);
                }
                Mark(new Vector3(0, 0, -HalfPitch), 3, Color.yellow, false);
                Mark(new Vector3(0, 0, HalfPitch), 3, Color.yellow, false);
                foreach (var m in marks) Mark(m, 6, Color.red, true);
                tex.Apply();
                CaptureWriter.Write(file, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                Debug.Log($"[APL fieldmap] {tag} -> {file}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[APL fieldmap] {tag} failed: {e.Message}");
            }
            finally
            {
                RenderTexture.active = prev;
                UnityEngine.Object.Destroy(go);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }
    }
}
