using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Round 13 (owner decision): PRACTICE NETS is an EMPTY practice net, not the stadium —
    /// a netted lane round the batting end (side and back netting on steel posts), the pitch
    /// and stumps, a second empty lane alongside, kit bags, a ball bucket, cones and a bench,
    /// grass out to a ring of trees and a low clubhouse, under an open late-afternoon sky.
    /// No stands, crowd, boards, screens, roof or floodlights. Used only by Nets mode
    /// (MatchDirector.Start builds this instead of <see cref="Build"/>).
    /// </summary>
    public static partial class StadiumBuilder
    {
        /// <summary>Where the netted lane runs: from behind the batting crease to just past the middle.</summary>
        private const float NetBack = -GroundSpace.HalfPitch - 5.5f, NetFront = 1.5f, NetHalfWidth = 2.2f, NetHeight = 3.6f;

        public static GameObject BuildNets(Venue venue, GroundRefs refs)
        {
            _v = venue;
            _look = StadiumLook.For(venue.Id);
            var root = new GameObject("Nets " + venue.Id).transform;
            var m = new Mats(refs);
            _cover = refs.Cover;
            Chunks.Clear();
            Shader.SetGlobalVector(CheerId, Vector4.zero);
            Shader.SetGlobalVector(WaveId, Vector4.zero);
            Shader.SetGlobalVector(BannerId, Vector4.zero);
            DayLight(refs);
            PracticeGround(root, m);
            NetLane(root, m, 0f, true);
            NetLane(root, m, NetHalfWidth * 2 + 3.5f, false);
            NetLane(root, m, -(NetHalfWidth * 2 + 3.5f), false);
            NetsProps(root, m);
            Stumps(root, m);
            return root.gameObject;
        }

        /// <summary>An open late-afternoon sky: the sun low and warm, a bright, hazy horizon.</summary>
        private static void DayLight(GroundRefs refs)
        {
            if (RenderSettings.skybox != null)
            {
                var sky = new Material(RenderSettings.skybox);
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.45f, 0.58f, 0.85f));
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 1.05f);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.25f);
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.035f);
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.35f, 0.38f, 0.32f));
                RenderSettings.skybox = sky;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.54f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.32f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.77f, 0.84f);
            RenderSettings.fogStartDistance = 120;
            RenderSettings.fogEndDistance = 900;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                if (l.name.Contains("Key"))
                {
                    // The sun, low over the bowler's shoulder, casting long shadows down the pitch.
                    l.transform.rotation = Quaternion.Euler(32f, 155f, 0f);
                    l.color = new Color(1f, 0.93f, 0.8f);
                    l.intensity = 1.3f;
                    RenderSettings.sun = l;
                }
                else l.intensity = 0.35f;
            }
            if (refs.Camera != null) refs.Camera.farClipPlane = 2000;
        }

        /// <summary>Grass right out (this ground's own mowing), rough grass beyond, trees round it, a clubhouse.</summary>
        private static void PracticeGround(Transform root, Mats m)
        {
            var plane = GameObject.Find("Outfield");
            var r = plane != null ? plane.GetComponent<Renderer>() : null;
            if (r != null)
            {
                var grass = new Material(r.sharedMaterial) { name = "NetsGrass" };
                grass.SetTexture("_BaseMap", Grass(_look));
                r.sharedMaterial = grass;
                r.enabled = true;
            }
            var land = new MeshKit();
            const float l = 1500;
            land.Quad(0, new Vector3(-l, -0.05f, -l), new Vector3(-l, -0.05f, l), new Vector3(l, -0.05f, l), new Vector3(l, -0.05f, -l), Vector2.zero, new Vector2(l / 8, l / 8));
            land.Spawn("Land", root, m.Lit(new Color(0.24f, 0.36f, 0.16f), Grass(), 0.05f));
            NetsTrees(root, m);
        }
    }
}
