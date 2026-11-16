using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The APL trophy, made from primitives in code: a gold cup on a stem with two
    /// handles, a dark plinth with a gold band. About 55 cm tall, its origin at the
    /// bottom of the plinth (where a hand holds it).
    /// </summary>
    public static class Trophy
    {
        public static GameObject Make()
        {
            var root = new GameObject("Trophy");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var gold = new Material(shader) { color = new Color(0.95f, 0.74f, 0.25f) };
            gold.SetFloat("_Metallic", 0.9f);
            gold.SetFloat("_Smoothness", 0.75f);
            var dark = new Material(shader) { color = new Color(0.08f, 0.07f, 0.12f) };
            Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(0.14f, 0.05f, 0.14f), dark);
            Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.105f, 0), new Vector3(0.145f, 0.008f, 0.145f), gold);
            Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.2f, 0), new Vector3(0.035f, 0.09f, 0.035f), gold);
            Part(root, PrimitiveType.Sphere, new Vector3(0, 0.3f, 0), new Vector3(0.08f, 0.05f, 0.08f), gold);
            // The cup: a squashed sphere with a wide rim.
            Part(root, PrimitiveType.Sphere, new Vector3(0, 0.41f, 0), new Vector3(0.2f, 0.22f, 0.2f), gold);
            Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.5f, 0), new Vector3(0.21f, 0.012f, 0.21f), gold);
            foreach (var side in new[] { -1f, 1f })
            {
                var handle = Part(root, PrimitiveType.Capsule, new Vector3(side * 0.13f, 0.42f, 0), new Vector3(0.025f, 0.07f, 0.025f), gold);
                handle.transform.localRotation = Quaternion.Euler(0, 0, side * 25f);
            }
            return root;
        }

        private static GameObject Part(GameObject root, PrimitiveType type, Vector3 at, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }
    }
}
