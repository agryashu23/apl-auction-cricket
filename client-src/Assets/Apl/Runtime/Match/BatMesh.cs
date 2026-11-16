using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.Match
{
    /// <summary>
    /// The bat: the Blender-modelled blade (bowed face, shoulders, spine,
    /// rounded toe and edges) and grip from gear.bytes, painted with
    /// Resources/Gear/bat (willow, APL stickers, spiral grip). BatterRig's
    /// frame: grip at the origin, blade down local -Y, face toward local +Z.
    /// Falls back to a plain block bat when the gear assets aren't there.
    /// </summary>
    public static class BatMesh
    {
        private static Mesh? _mesh;
        private static Material? _mat;

        public static GameObject Create()
        {
            var root = new GameObject("Bat");
            if (_mesh == null) _mesh = GearMeshes.Rigid("bat");  // (Unity null: unloaded assets come back)
            if (_mesh == null)
            {
                Block(root.transform, new Vector3(0, -0.14f, 0), new Vector3(0.034f, 0.28f, 0.034f), new Color(0.15f, 0.15f, 0.18f));
                Block(root.transform, new Vector3(0, -0.58f, 0), new Vector3(0.108f, 0.56f, 0.045f), new Color(0.86f, 0.74f, 0.52f));
                return root;
            }
            if (_mat == null)
            {

                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _mat = new Material(shader) { name = "Bat" };
                var tex = Resources.Load<Texture2D>("Gear/bat");
                if (tex != null)
                {

                    _mat.SetTexture("_BaseMap", tex);
                    _mat.mainTexture = tex;
                }
                _mat.SetColor("_BaseColor", Color.white);
                _mat.SetFloat("_Smoothness", 0.42f);
            }
            var blade = new GameObject("Blade");
            blade.transform.SetParent(root.transform, false);
            blade.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = blade.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            return root;
        }

        private static void Block(Transform parent, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = color };
            m.SetColor("_BaseColor", color);
            go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
