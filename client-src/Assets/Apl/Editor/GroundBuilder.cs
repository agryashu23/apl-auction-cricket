using System.Collections.Generic;
using System.IO;
using Apl.Client.Audio;
using Apl.Client.Match;
using Apl.Core;
using Apl.Core.Play;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Ground.unity from code: a night T20 ground — striped
    /// outfield, pitch and creases, stumps at both ends, the 30-yard circle —
    /// and the people on it (batters, bowler, keeper, umpire, nine fielders).
    /// The stadium round it (rope, boards, stands, roof, lights, the view) is
    /// built when the match starts, for the ground picked (StadiumBuilder).
    /// The match director runs the game in it.
    /// </summary>
    public static class GroundBuilder
    {
        public const string ScenePath = "Assets/Scenes/Ground.unity";
        private const float H = GroundSpace.HalfPitch;

        [MenuItem("APL/Build Ground Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Ground").transform;

            Lights();
            Outfield(root);
            Pitch(root);
            var stumps = Stumps(root, -H, out var bails);
            Stumps(root, H, out _);

            var people = new GameObject("People").transform;
            var refs = new GameObject("Match").AddComponent<GroundRefs>();
            refs.Stumps = stumps;
            refs.Bails = bails;
            Templates(refs);
            Cast(refs);
            var cast = new List<string>(HallBuilder.TableCast);
            var n = 0;
            string Next() => cast[n++ % cast.Count];
            // Players run on the Cricket controller (the batting, bowling and fielding clips).
            refs.Striker = HallBuilder.Person(Next(), new Vector3(-0.32f, 0, -H + 0.95f), 90, "Striker", people, "Cricket");
            refs.NonStriker = HallBuilder.Person(Next(), new Vector3(-1.1f, 0, H - 1.3f), 180, "NonStriker", people, "Cricket");
            refs.Bowler = HallBuilder.Person(Next(), new Vector3(0.9f, 0, H + 13f), 180, "Bowler", people, "Cricket");
            refs.Keeper = HallBuilder.Person(Next(), new Vector3(0, 0, -H - 2.6f), 0, "Keeper", people, "Cricket");
            // Behind the bowler's stumps, a touch to the bowler's right so a right-arm-over
            // bowler runs past his left shoulder (under the broadcast camera's line to the batter).
            refs.Umpire = HallBuilder.Person(Next(), new Vector3(-0.35f, 0, H + 2.3f), 180, "Umpire", people, "Cricket");
            var fielders = new List<Animator>();
            foreach (var f in Apl.Core.Play.Field.For(0, false).Fielders)
            {
                var (x, fwd) = f.Position;
                var p = GroundSpace.Field(x, fwd, false);
                var a = HallBuilder.Person(Next(), p, 180, "Fielder", people, "Cricket");
                if (a != null) fielders.Add(a);
            }
            refs.Fielders = fielders.ToArray();
            foreach (var a in people.GetComponentsInChildren<Animator>())
            {
                // Players stay animated wherever the camera points (fielders are far away).
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var r in a.GetComponentsInChildren<SkinnedMeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            }

            var cam = BuildCamera();
            refs.Camera = cam;
            var director = refs.gameObject.AddComponent<MatchDirector>();
            director.Refs = refs;
            director.Camera = cam.gameObject.AddComponent<MatchCamera>();
            director.Sfx = refs.gameObject.AddComponent<Sfx>();
            director.Voice = new GameObject("Commentary").AddComponent<VoiceQueue>();
            director.Voice.transform.SetParent(refs.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(HallBuilder.ScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
            Debug.Log("[APL] Ground scene built");
        }

        /// <summary>The kit-painted cricketers: bodies, masks and tones from KitBuilder's catalog.</summary>
        private static void Cast(GroundRefs refs)
        {
            refs.Cricket = AnimatorBuilder.Load("Cricket");
            var shader = Shader.Find("Apl/KitBlit");
            if (shader != null)
            {
                var path = "Assets/Apl/Generated/KitBlit.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader);
                    AssetDatabase.CreateAsset(m, path);
                }
                refs.KitBlit = m;
            }
            // Cheerleaders: their own bodies and masks, kept apart from the players' cast.
            var cheer = new List<GameObject>();
            var cheerKits = new List<Texture2D>();
            foreach (var name in KitBuilder.Cheer)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Rocketbox/{name}/Export/{name}.fbx");
                var kit = AssetDatabase.LoadAssetAtPath<Texture2D>($"{KitBuilder.Out}/{name}_kit.png");
                if (prefab == null || kit == null) continue;
                cheer.Add(prefab);
                cheerKits.Add(kit);
            }
            refs.CheerModels = cheer.ToArray();
            refs.CheerKits = cheerKits.ToArray();
            if (!File.Exists(KitBuilder.Catalog)) return;
            var models = new List<GameObject>();
            var kits = new List<Texture2D>();
            var tones = new List<int>();
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(KitBuilder.Catalog));
            foreach (var e in json["cast"]!)
            {
                var name = (string)e["model"]!;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Rocketbox/{name}/Export/{name}.fbx");
                var kit = AssetDatabase.LoadAssetAtPath<Texture2D>($"{KitBuilder.Out}/{name}_kit.png");
                if (prefab == null || kit == null) continue;
                models.Add(prefab);
                kits.Add(kit);
                tones.Add((int)e["tone"]!);
            }
            refs.CastModels = models.ToArray();
            refs.CastKits = kits.ToArray();
            refs.CastTones = tones.ToArray();
            Debug.Log($"[APL] Ground cast: {models.Count} bodies");
        }

        private static void Templates(GroundRefs refs)
        {
            var cover = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Apl/Art/led_cover.png");
            var crest = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Crests/BLR.png");
            refs.Cover = cover;
            refs.LitTemplate = HallBuilder.Mat("StadiumLit", Color.white, smooth: 0.3f, texture: cover);
            refs.GlowTemplate = HallBuilder.Mat("StadiumGlow", Color.white, emission: 1f, texture: cover);
            refs.CutoutTemplate = HallBuilder.Mat("StadiumCutout", Color.white, emission: 1f, texture: crest, transparent: true);
            // Player shadows are the only ones cast; keep them sharp and far enough for the broadcast view.
            // Stands and roofs are single sheets: draw both faces.
            refs.LitTemplate.SetFloat("_Cull", 0);
            refs.GlowTemplate.SetFloat("_Cull", 0);
            EditorUtility.SetDirty(refs.LitTemplate);
            EditorUtility.SetDirty(refs.GlowTemplate);
        }

        private static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3500f;
            cam.fieldOfView = 40;
            go.transform.position = new Vector3(0, 26, -62);
            go.transform.LookAt(new Vector3(0, 0, 4));
            cam.allowHDR = true;
            cam.allowMSAA = true;
            HallDecor.PostProcessing(cam, "GroundPost", night: true);
            return cam;
        }

        private static void Lights()
        {
            // A night match: deep blue sky, cool ambient, a bright key from the towers.
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0f);
            sky.SetFloat("_AtmosphereThickness", 0.45f);
            sky.SetColor("_SkyTint", new Color(0.12f, 0.16f, 0.35f));
            sky.SetColor("_GroundColor", new Color(0.05f, 0.05f, 0.08f));
            sky.SetFloat("_Exposure", 0.55f);
            Directory.CreateDirectory("Assets/Apl/Generated");
            AssetDatabase.CreateAsset(sky, "Assets/Apl/Generated/GroundSky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.4f, 0.55f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.32f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.16f, 0.1f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.08f, 0.1f, 0.18f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 160;
            RenderSettings.fogEndDistance = 420;

            var key = new GameObject("Floodlight Key", typeof(Light)).GetComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.35f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.55f;
            key.transform.rotation = Quaternion.Euler(58, 35, 0);
            var fill = new GameObject("Floodlight Fill", typeof(Light)).GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.85f, 0.9f, 1f);
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(50, 215, 0);
        }

        private static void Outfield(Transform root)
        {
            // Mowing stripes across the ground, a little noise so it isn't flat.
            // Mowing stripes, clumps and single blades (the camera sits close behind the bowler).
            var grass = HallDecor.Generate("grass_stripes_hd", 1024, (x, y, size) =>
            {
                var band = (x * 14 / size) % 2 == 0;
                var c = band ? new Color(0.2f, 0.47f, 0.18f) : new Color(0.26f, 0.56f, 0.23f);
                var clumps = Mathf.PerlinNoise(x * 0.025f, y * 0.025f) * 0.12f - 0.06f;
                var tufts = Mathf.PerlinNoise(x * 0.15f + 31, y * 0.15f) * 0.08f;
                var blade = (Mathf.Abs(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f) % 1f) * 0.1f;
                var hue = Color.Lerp(c, new Color(0.3f, 0.5f, 0.16f), Mathf.PerlinNoise(x * 0.01f + 7, y * 0.01f) * 0.35f);
                return hue * (0.9f + clumps + tufts + blade);
            });
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Outfield";
            plane.transform.SetParent(root);
            plane.transform.localScale = new Vector3(20, 1, 20);
            plane.GetComponent<Renderer>().sharedMaterial = HallBuilder.Mat("Grass", Color.white, smooth: 0.15f, texture: grass, tiling: new Vector2(1, 1));
            Object.DestroyImmediate(plane.GetComponent<Collider>());

            // The 30-yard circle: white dots.
            var white = HallBuilder.Mat("Line", Color.white, emission: 0.2f, smooth: 0.1f);
            for (var i = 0; i < 64; i++)
            {
                var a = i * Mathf.PI * 2 / 64;
                HallBuilder.Cylinder("Circle", new Vector3(Mathf.Sin(a) * 30, 0.01f, Mathf.Cos(a) * 30), new Vector3(0.35f, 0.01f, 0.35f), white, root);
            }
        }

        private static void Pitch(Transform root)
        {
            // A T20 strip: rolled clay, hairline cracks, scuffed ends, a green tinge at the edges.
            var surface = HallDecor.Generate("pitch_surface", 1024, (x, y, size) =>
            {
                var u = x / (float)size;
                var v = y / (float)size;
                var clay = Color.Lerp(new Color(0.83f, 0.74f, 0.53f), new Color(0.74f, 0.64f, 0.44f), Mathf.PerlinNoise(x * 0.02f, y * 0.006f));
                var crack = Mathf.Abs(Mathf.PerlinNoise(x * 0.09f + 11, y * 0.03f) - 0.5f) < 0.012f;
                if (crack) clay *= 0.78f;
                // Footmarks where the bowlers land and the batters stand (both ends).
                var end = Mathf.Min(Mathf.Abs(v - 0.11f), Mathf.Abs(v - 0.89f));
                var wear = Mathf.Clamp01(1 - end / 0.08f) * Mathf.Clamp01(1 - Mathf.Abs(u - 0.5f) / 0.38f);
                clay = Color.Lerp(clay, new Color(0.66f, 0.56f, 0.38f), wear * (0.55f + Mathf.PerlinNoise(x * 0.2f, y * 0.2f) * 0.45f));
                var edge = Mathf.Clamp01((Mathf.Abs(u - 0.5f) - 0.4f) / 0.1f);
                clay = Color.Lerp(clay, new Color(0.45f, 0.55f, 0.28f), edge * 0.7f);
                return clay * (0.95f + (Mathf.Abs(Mathf.Sin(x * 3.7f + y * 9.1f) * 9301.7f) % 1f) * 0.08f);
            });
            var strip = HallBuilder.Mat("Pitch", Color.white, smooth: 0.18f, texture: surface);
            HallBuilder.Block("Pitch", new Vector3(0, 0.006f, 0), new Vector3(3.05f, 0.012f, 22f), strip, root);
            var white = HallBuilder.Mat("Line", Color.white, emission: 0.2f, smooth: 0.1f);
            foreach (var end in new[] { -1f, 1f })
            {
                var stumpsZ = end * H;
                var popping = end * (H - 1.22f);
                // Popping crease (the batter's), bowling crease (the stumps'), return creases.
                HallBuilder.Block("Popping", new Vector3(0, 0.014f, popping), new Vector3(3.66f, 0.002f, 0.05f), white, root);
                HallBuilder.Block("Bowling", new Vector3(0, 0.014f, stumpsZ), new Vector3(2.64f, 0.002f, 0.05f), white, root);
                foreach (var side in new[] { -1.32f, 1.32f })
                {
                    HallBuilder.Block("Return", new Vector3(side, 0.014f, (popping + stumpsZ) / 2 + end * 0.6f), new Vector3(0.05f, 0.002f, 2.44f + 1.22f), white, root);
                }
            }
        }

        private static Transform[] Stumps(Transform root, float z, out Transform[] bails)
        {
            var wood = HallBuilder.Mat("Stump", new Color(0.95f, 0.9f, 0.78f), smooth: 0.5f);
            var bail = HallBuilder.Mat("Bail", new Color(1f, 0.85f, 0.3f), emission: 0.6f);
            var stumps = new Transform[3];
            for (var i = 0; i < 3; i++)
            {
                var s = HallBuilder.Cylinder("Stump", new Vector3((i - 1) * 0.115f, 0.355f, z), new Vector3(0.038f, 0.355f, 0.038f), wood, root);
                stumps[i] = s.transform;
            }
            bails = new Transform[2];
            for (var i = 0; i < 2; i++)
            {
                bails[i] = HallBuilder.Block("Bail", new Vector3(i == 0 ? -0.055f : 0.055f, 0.72f, z), new Vector3(0.11f, 0.02f, 0.02f), bail, root).transform;
            }
            return stumps;
        }
    }
}
