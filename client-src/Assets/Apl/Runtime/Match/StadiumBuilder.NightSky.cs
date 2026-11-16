using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.Match
{
    /// <summary>
    /// Round 9: a real night sky (rating r8: "a flat brown-grey sky"). A sphere
    /// round the ground drawn by Apl/NightSky — navy overhead, the venue's sky
    /// tint, a warm floodlit glow on the horizon, thin clouds, stars — and the fog
    /// set to the horizon's colour so the far scenery melts into it.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private const float SkyRadius = 3200;

        private static void NightSky(Transform root, StadiumLook look)
        {
            var shader = Resources.Load<Shader>("Shaders/NightSky") ?? Shader.Find("Apl/NightSky");
            if (shader == null)
            {
                Debug.LogWarning("[APL] Apl/NightSky shader missing: the procedural skybox stays");
                return;
            }
            var mid = look.Sky;
            var zenith = Color.Lerp(mid, Color.black, 0.72f);
            var horizon = Color.Lerp(new Color(0.42f, 0.32f, 0.33f), mid, 0.35f);
            var mat = new Material(shader) { name = "NightSky" };
            mat.SetColor("_Zenith", zenith);
            mat.SetColor("_Mid", Color.Lerp(mid, Color.black, 0.25f));
            mat.SetColor("_Horizon", horizon);
            mat.SetColor("_Cloud", Color.Lerp(mid, new Color(0.25f, 0.25f, 0.3f), 0.5f));
            mat.SetFloat("_Stars", (look.Scenery & Scenery.Mountains) != 0 ? 1.4f : 0.8f);
            RenderSettings.fogColor = Color.Lerp(horizon, mid, 0.3f) * 0.75f;

            const int lon = 32, lat = 16;
            var verts = new Vector3[(lon + 1) * (lat + 1)];
            for (var i = 0; i <= lat; i++)
            for (var j = 0; j <= lon; j++)
            {
                var th = Mathf.PI * i / lat;
                var ph = 2 * Mathf.PI * j / lon;
                verts[i * (lon + 1) + j] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * SkyRadius;
            }
            var tris = new int[lon * lat * 6];
            var k = 0;
            for (var i = 0; i < lat; i++)
            for (var j = 0; j < lon; j++)
            {
                var a = i * (lon + 1) + j;
                var b = a + lon + 1;
                tris[k++] = a; tris[k++] = b; tris[k++] = a + 1;
                tris[k++] = a + 1; tris[k++] = b; tris[k++] = b + 1;
            }
            var mesh = new Mesh { name = "NightSky", vertices = verts, triangles = tris };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * SkyRadius * 2);
            var go = new GameObject("NightSky");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
