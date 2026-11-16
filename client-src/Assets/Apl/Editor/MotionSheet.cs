using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// Review aid for new motion clips: plays each clip on a Rocketbox
    /// character and renders six frames across it into build/unity/motions.png.
    ///   Unity -batchmode -executeMethod Apl.Client.EditorTools.MotionSheet.Run -apl-clips a,b,c
    /// </summary>
    public static class MotionSheet
    {
        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, "-apl-clips");
            var names = i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : new[] { "sit_idle" };
            Render(names, "Male_Adult_01");
        }

        public static void Render(string[] names, string model)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            var light = new GameObject("Key", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(35, 160, 0);
            var cam = new GameObject("Cam", typeof(Camera)).GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.24f);
            cam.fieldOfView = 34;
            cam.transform.position = new Vector3(0.9f, 1.25f, 3.4f);
            cam.transform.LookAt(new Vector3(0, 0.95f, 0));

            const int frames = 6, w = 300, h = 360;
            var sheet = new Texture2D(w * frames, h * names.Length, TextureFormat.RGB24, false);
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Rocketbox/{model}/Export/{model}.fbx");
            for (var row = 0; row < names.Length; row++)
            {
                var clip = AnimatorBuilder.Clip(names[row]);
                if (clip == null || prefab == null)
                {
                    Debug.LogWarning($"[APL] motion sheet: no clip {names[row]}");
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0, HallBuilder.ModelYawFix, 0);
                var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                animator.applyRootMotion = false;
                // The editor's preview sampler: poses humanoid clips at any time,
                // which a graph or a bare Animator.Update in edit mode doesn't.
                var span = Mathf.Min(clip.length, 8f);
                AnimationMode.StartAnimationMode();
                for (var f = 0; f < frames; f++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(go, clip, span * f / (frames - 1));
                    AnimationMode.EndSampling();
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, w, h), f * w, (names.Length - 1 - row) * h);
                }
                AnimationMode.StopAnimationMode();
                Debug.Log($"[APL] motion sheet: {names[row]} {clip.length:0.00}s, humanoid={clip.humanMotion}");
                // How much the key muscles actually move over the clip (humanoid curves, -1..1).
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var n = binding.propertyName;
                    if (!(n.Contains("Arm Down-Up") || n.Contains("Head Turn") || n.Contains("Head Nod") || n.Contains("Forearm Stretch") || n.Contains("Neck Turn") || n.Contains("Arm Front-Back"))) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (var t = 0f; t <= clip.length; t += clip.length / 60f)
                    {
                        var v = curve.Evaluate(t);
                        lo = Mathf.Min(lo, v);
                        hi = Mathf.Max(hi, v);
                    }
                    Debug.Log($"[APL]   {names[row]} {n}: {lo:0.00}..{hi:0.00} (range {hi - lo:0.00})");
                }
                Object.DestroyImmediate(go);
            }
            sheet.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            Directory.CreateDirectory("../build/unity");
            File.WriteAllBytes("../build/unity/motions.png", sheet.EncodeToPNG());
            Debug.Log("[APL] motion sheet written");
        }
    }
}
