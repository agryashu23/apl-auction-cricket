using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// (batting) The owner's milestone celebrations (2026-11-12) have a top-hand elbow pop the Blender check found:
    /// fifty_signature_pose frames 81–84 (0.53 m in one frame before the bat raise), the leap clips' take-off 23–24
    /// (kisses, chin swipe, roar &amp; bow) and roar &amp; bow 191. A Blender re-export shifted the frames and the rest
    /// pose, so the FBX stay as delivered and the pop is taken out here, at import: across each window the top arm's
    /// curves (shoulder, arm, forearm, hand muscles and the hand's IK goal — or the bones' local rotations, whichever
    /// the clip carries) run straight from the window's first frame to its last. Frames are the clips' own (1-based,
    /// 30 fps; the import starts the clip at frame 1). Logs "[APL import] arm pop smoothed".
    /// </summary>
    public sealed class CelebrationPopFix : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private const string Cricket = "Assets/ThirdParty/Cricket/";

        /// <summary>Clip → (top arm side, first frame, last frame) windows, the ends kept.</summary>
        private static readonly Dictionary<string, (string side, int a, int b)[]> Windows = new Dictionary<string, (string, int, int)[]>
        {
            ["rhb_fifty_signature_pose"] = new[] { ("Left", 80, 85) },
            ["lhb_fifty_signature_pose"] = new[] { ("Right", 80, 85) },
            ["rhb_century_leap_and_kisses"] = new[] { ("Left", 21, 26) },
            ["lhb_century_leap_and_kisses"] = new[] { ("Right", 21, 26) },
            ["rhb_century_leap_and_chin_swipe"] = new[] { ("Left", 21, 26) },
            ["lhb_century_leap_and_chin_swipe"] = new[] { ("Right", 21, 26) },
            ["rhb_century_roar_helmet_off_bow"] = new[] { ("Left", 21, 26), ("Left", 189, 193) },
            ["lhb_century_roar_helmet_off_bow"] = new[] { ("Right", 21, 26), ("Right", 189, 193) },
        };

        private static bool TopArm(EditorCurveBinding b, string side)
        {
            var p = b.propertyName;
            // Humanoid muscles and the hand's IK goal (not the fingers: "LeftHand.Index.1 Stretched").
            if (p.StartsWith(side + " Shoulder ") || p.StartsWith(side + " Arm ") || p.StartsWith(side + " Forearm ")
                || p.StartsWith(side + " Hand ") || p.StartsWith(side + "HandT.") || p.StartsWith(side + "HandQ.")) return true;
            // Or the bones themselves.
            if (!p.StartsWith("m_LocalRotation.")) return false;
            var path = b.path;
            return path.EndsWith("mixamorig:" + side + "Shoulder") || path.EndsWith("mixamorig:" + side + "Arm")
                || path.EndsWith("mixamorig:" + side + "ForeArm") || path.EndsWith("mixamorig:" + side + "Hand");
        }

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!assetPath.StartsWith(Cricket) || !Windows.TryGetValue(clip.name, out var windows)) return;
            foreach (var (side, a, b) in windows)
            {
                var ta = (a - 1) / 30f;
                var tb = (b - 1) / 30f;
                var n = 0;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!TopArm(binding, side)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null) continue;
                    var va = curve.Evaluate(ta);
                    var vb = curve.Evaluate(tb);
                    var slope = (vb - va) / (tb - ta);
                    var keys = curve.keys;
                    var changed = false;
                    for (var i = 0; i < keys.Length; i++)
                    {
                        var t = keys[i].time;
                        if (t <= ta + 1e-4f || t >= tb - 1e-4f) continue;
                        keys[i].value = Mathf.Lerp(va, vb, (t - ta) / (tb - ta));
                        keys[i].inTangent = keys[i].outTangent = slope;
                        changed = true;
                    }
                    if (!changed) continue;
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                    n++;
                }
                Debug.Log($"[APL import] arm pop smoothed: {clip.name} {side} arm, frames {a}-{b}, {n} curves");
            }
        }
    }
}
