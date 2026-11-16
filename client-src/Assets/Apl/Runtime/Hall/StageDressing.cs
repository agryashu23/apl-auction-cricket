using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Dresses the hall's stage props at runtime (UI/UX dev, rater r1 §5: "the lectern and podium
    /// are flat brown boxes, the stage is a grey slab — the most-seen objects in the game").
    /// No art files and no paid assets: every texture is drawn here in code, and every material
    /// is a copy of one the baked scene already uses (so the build's URP shader variants exist).
    ///
    ///  - The auctioneer's lectern: lacquered wood grain, an overhanging cap with a gold lip, a
    ///    plinth, gold pinstripes up the front corners, a dark front panel framed in gold with
    ///    the APL crest on it.
    ///  - The player box: dark lacquer with gold edges, a card slot and the crest; its stand in wood.
    ///  - The stage: a patterned carpet on the deck, a slatted riser along the front.
    ///  - The LED wall's title line becomes a scrolling ticker band ("APL 2026 • PLAYER AUCTION
    ///    • LIVE •"): close-ups (auctioneer, card) used to crop the baked "PLAYER AUCTION 2026"
    ///    into "AYER …CTI N 2026"; a moving ticker of short words reads at any crop.
    ///
    /// Attaches itself to the hall's "Stage" (the one with a StageCast) on every scene load,
    /// so no other file has to call it. Scene objects are found by the names HallBuilder gives them.
    /// </summary>
    public sealed class StageDressing : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded += (_, _) => Attach();
            Attach();
        }

        private static void Attach()
        {
            var cast = FindAnyObjectByType<StageCast>();
            if (cast == null || cast.GetComponent<StageDressing>() != null) return;
            cast.gameObject.AddComponent<StageDressing>();
        }

        private Text? _ticker;
        private float _tokenWidth;

        private void Start()
        {
            try
            {
                Dress();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[APL stage] dressing skipped: {ex.Message}");
            }
        }

        private void Dress()
        {
            var stage = transform;
            var podium = stage.Find("Podium");
            var front = stage.Find("PodiumFront");
            if (podium == null || front == null) return;
            var woodBase = podium.GetComponent<Renderer>().sharedMaterial;
            var goldBase = front.GetComponent<Renderer>().sharedMaterial;

            var wood = Copy(woodBase, "LecternWood", Color.white, WoodTex(), 0.55f);
            var darkWood = Copy(woodBase, "LecternWoodDark", new Color(0.55f, 0.5f, 0.48f), WoodTex(), 0.6f);
            var lacquer = Copy(woodBase, "Lacquer", new Color(0.05f, 0.06f, 0.11f), null, 0.75f);
            var gold = new Material(goldBase) { name = "TrimGold" };

            // --- the lectern (podium: centre (-2.2, 1.18, 8.5), 1.1 × 1.16 × 0.7; front face z 8.15).
            podium.GetComponent<Renderer>().sharedMaterial = wood;
            var px = podium.position.x;
            var pz = podium.position.z;
            var top = podium.position.y + podium.lossyScale.y / 2f;
            var bottom = podium.position.y - podium.lossyScale.y / 2f;
            var fz = pz - podium.lossyScale.z / 2f;
            var hw = podium.lossyScale.x / 2f;
            Piece("LecternCap", new Vector3(px, top + 0.0175f, pz), new Vector3(1.22f, 0.035f, 0.82f), darkWood);
            Piece("LecternCapLip", new Vector3(px, top + 0.0175f, pz - 0.41f), new Vector3(1.22f, 0.036f, 0.012f), gold);
            Piece("LecternPlinth", new Vector3(px, bottom + 0.04f, pz), new Vector3(1.2f, 0.08f, 0.8f), darkWood);
            Piece("LecternPlinthLip", new Vector3(px, bottom + 0.075f, pz - 0.4f), new Vector3(1.2f, 0.012f, 0.012f), gold);
            foreach (var side in new[] { -1f, 1f })
                Piece("LecternPin", new Vector3(px + side * (hw - 0.03f), (top + bottom) / 2f, fz - 0.006f), new Vector3(0.018f, top - bottom - 0.12f, 0.012f), gold);
            // The front panel: dark lacquer in a gold frame, the crest on it.
            front.GetComponent<Renderer>().sharedMaterial = lacquer;
            var fp = front.position;
            var fs = front.lossyScale;
            const float bar = 0.022f;
            Piece("PanelFrameTop", new Vector3(fp.x, fp.y + fs.y / 2f, fp.z - 0.008f), new Vector3(fs.x + bar, bar, 0.012f), gold);
            Piece("PanelFrameBottom", new Vector3(fp.x, fp.y - fs.y / 2f, fp.z - 0.008f), new Vector3(fs.x + bar, bar, 0.012f), gold);
            Piece("PanelFrameLeft", new Vector3(fp.x - fs.x / 2f, fp.y, fp.z - 0.008f), new Vector3(bar, fs.y, 0.012f), gold);
            Piece("PanelFrameRight", new Vector3(fp.x + fs.x / 2f, fp.y, fp.z - 0.008f), new Vector3(bar, fs.y, 0.012f), gold);
            Piece("LowerPanel", new Vector3(px, bottom + 0.25f, fz - 0.004f), new Vector3(fs.x - 0.04f, 0.24f, 0.008f), lacquer);
            var crest = CrestMaterial();
            if (crest != null) Decal("LecternCrest", new Vector3(fp.x, fp.y, fp.z - 0.016f), 0.36f, crest);

            // --- the player box and its stand.
            var box = stage.Find("PlayerBox");
            var stand = stage.Find("BoxStand");
            if (stand != null)
            {
                stand.GetComponent<Renderer>().sharedMaterial = wood;
                var s = stand.position;
                var sb = s.y - stand.lossyScale.y / 2f;
                Piece("StandPlinth", new Vector3(s.x, sb + 0.03f, s.z), new Vector3(stand.lossyScale.x + 0.1f, 0.06f, stand.lossyScale.z + 0.1f), darkWood);
            }
            if (box != null)
            {
                box.GetComponent<Renderer>().sharedMaterial = lacquer;
                var b = box.position;
                var e = box.lossyScale / 2f;
                const float t = 0.024f;
                foreach (var y in new[] { -e.y, e.y })
                {
                    Piece("BoxEdgeF", new Vector3(b.x, b.y + y, b.z - e.z), new Vector3(e.x * 2 + t, t, t), gold);
                    Piece("BoxEdgeB", new Vector3(b.x, b.y + y, b.z + e.z), new Vector3(e.x * 2 + t, t, t), gold);
                    Piece("BoxEdgeL", new Vector3(b.x - e.x, b.y + y, b.z), new Vector3(t, t, e.z * 2 + t), gold);
                    Piece("BoxEdgeR", new Vector3(b.x + e.x, b.y + y, b.z), new Vector3(t, t, e.z * 2 + t), gold);
                }
                foreach (var x in new[] { -e.x, e.x })
                foreach (var z in new[] { -e.z, e.z })
                    Piece("BoxEdgeV", new Vector3(b.x + x, b.y, b.z + z), new Vector3(t, e.y * 2, t), gold);
                Piece("BoxSlot", new Vector3(b.x, b.y + e.y + 0.004f, b.z), new Vector3(e.x * 0.9f, 0.01f, 0.05f), Copy(woodBase, "Slot", new Color(0.01f, 0.01f, 0.015f), null, 0.2f));
                if (crest != null) Decal("BoxCrest", new Vector3(b.x, b.y, b.z - e.z - 0.006f), e.y * 1.4f, crest);
            }

            // --- the stage deck: carpet on top, a slatted riser along the front.
            var platform = stage.Find("Platform");
            if (platform != null)
            {
                var deck = platform.GetComponent<Renderer>().sharedMaterial;
                var p = platform.position;
                var ps = platform.lossyScale;
                var carpet = Copy(deck, "StageCarpet", Color.white, CarpetTex(), 0.15f);
                carpet.mainTextureScale = new Vector2(ps.x / 1.0f, ps.z / 1.0f);
                carpet.SetTextureScale("_BaseMap", carpet.mainTextureScale);
                var c = Quad("StageCarpet", new Vector3(p.x, p.y + ps.y / 2f + 0.003f, p.z), new Vector2(ps.x - 0.3f, ps.z - 0.3f), carpet);
                c.rotation = Quaternion.Euler(90, 0, 0);
                var riser = Copy(deck, "StageRiser", Color.white, RiserTex(), 0.4f);
                riser.mainTextureScale = new Vector2(ps.x / 0.6f, 1f);
                riser.SetTextureScale("_BaseMap", riser.mainTextureScale);
                Quad("StageRiser", new Vector3(p.x, p.y, p.z - ps.z / 2f - 0.004f), new Vector2(ps.x, ps.y), riser);
            }

            // --- the LED wall's title line as a ticker band.
            var cover = stage.Find("LedCover") ?? stage.Find("LedWall");
            if (cover != null) BuildTicker(cover);
            Debug.Log("[APL stage] dressed: lectern (grain, cap, plinth, gold frame, crest), player box (lacquer, gold edges, crest), carpet + riser, LED ticker");
        }

        private void Update()
        {
            if (_ticker == null || _tokenWidth <= 0) return;
            // Real time, so the ticker doesn't race under FAST.
            var x = -(Time.unscaledTime * 140f % _tokenWidth);
            _ticker.rectTransform.anchoredPosition = new Vector2(x, 0);
        }

        // ------------------------------------------------------------ the ticker

        private void BuildTicker(Transform cover)
        {
            // The baked title runs ~73-82 % down the cover; the band sits over it, just in front.
            var c = cover.position;
            var s = cover.lossyScale;
            var topY = c.y + s.y / 2f;
            var y = topY - s.y * 0.775f;
            const float ppm = 360f;
            var w = s.x;
            const float h = 0.62f;
            var go = new GameObject("LedTicker", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, true);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            rt.sizeDelta = new Vector2(w * ppm, h * ppm);
            rt.position = new Vector3(c.x, y, c.z - 0.045f);
            rt.rotation = Quaternion.identity;
            rt.localScale = Vector3.one / ppm;
            UiKit.Stretch(rt, "Band", new Color(0.025f, 0.03f, 0.07f, 1f));
            foreach (var edge in new[] { 0f, 1f })
            {
                var line = UiKit.Box(rt, "Rule", new Vector2(0.5f, edge), new Vector2(0, edge < 0.5f ? 5 : -5), new Vector2(w * ppm, 10), UiKit.Gold);
                line.GetComponent<Image>().raycastTarget = false;
            }
            var view = UiKit.Stretch(rt, "View");
            view.gameObject.AddComponent<RectMask2D>();
            const string token = "APL 2026     •     PLAYER AUCTION     •     LIVE     •     ";
            var text = UiKit.Label(view, token, 150, TextAnchor.MiddleLeft, UiKit.Gold);
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _tokenWidth = text.preferredWidth;
            if (_tokenWidth <= 1) _tokenWidth = 3000;
            var reps = Mathf.CeilToInt(w * ppm / _tokenWidth) + 2;
            text.text = string.Concat(System.Linq.Enumerable.Repeat(token, reps));
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(0, 1);
            trt.pivot = new Vector2(0, 0.5f);
            trt.sizeDelta = new Vector2(_tokenWidth * reps, 0);
            trt.anchoredPosition = Vector2.zero;
            _ticker = text;
        }

        // --------------------------------------------------------------- pieces

        private Transform Piece(string name, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.transform.localScale = Divide(size, go.transform.lossyScale);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        private Transform Quad(string name, Vector3 pos, Vector2 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.transform.localScale = Divide(new Vector3(size.x, size.y, 1), go.transform.lossyScale);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private void Decal(string name, Vector3 pos, float size, Material m) => Quad(name, pos, new Vector2(size, size), m);

        private static Vector3 Divide(Vector3 a, Vector3 b) =>
            new Vector3(a.x / Mathf.Max(1e-4f, b.x), a.y / Mathf.Max(1e-4f, b.y), a.z / Mathf.Max(1e-4f, b.z));

        /// <summary>The APL crest: a copy of the scene's (hidden) logo quad material, transparent and lit.</summary>
        private Material? CrestMaterial()
        {
            var logo = transform.Find("Logo");
            var r = logo != null ? logo.GetComponent<Renderer>() : null;
            if (r == null || r.sharedMaterial == null) return null;
            return new Material(r.sharedMaterial) { name = "LecternCrest" };
        }

        private static Material Copy(Material from, string name, Color tint, Texture2D? tex, float smooth)
        {
            var m = new Material(from) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            m.color = tint;
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                m.mainTexture = tex;
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        // ------------------------------------------------------------- textures

        private static Texture2D? _wood, _carpet, _riser;

        /// <summary>Lacquered wood: long grain with wavy growth rings and fine pores.</summary>
        private static Texture2D WoodTex()
        {
            if (_wood != null) return _wood;
            const int n = 256;
            var dark = new Color(0.20f, 0.10f, 0.05f);
            var light = new Color(0.44f, 0.25f, 0.12f);
            return _wood = Make("WoodGrain", n, (u, v) =>
            {
                var t = u * 9f + Mathf.PerlinNoise(u * 3f, v * 0.7f) * 4f;
                var ring = Mathf.Abs(Mathf.Sin(t * Mathf.PI));
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Pow(ring, 0.6f));
                var pores = (Mathf.PerlinNoise(u * 90f, v * 6f) - 0.5f) * 0.08f;
                var col = Color.Lerp(dark, light, k);
                return new Color(col.r + pores, col.g + pores * 0.7f, col.b + pores * 0.5f, 1f);
            }, TextureWrapMode.Repeat);
        }

        /// <summary>Deep-red event carpet: fibre noise and a faint gold diamond lattice.</summary>
        private static Texture2D CarpetTex()
        {
            if (_carpet != null) return _carpet;
            const int n = 128;
            var baseCol = new Color(0.30f, 0.05f, 0.09f);
            var gold = new Color(0.62f, 0.45f, 0.16f);
            return _carpet = Make("Carpet", n, (u, v) =>
            {
                var a = Mathf.Abs(Mathf.Repeat(u + v, 0.5f) - 0.25f);
                var b = Mathf.Abs(Mathf.Repeat(u - v, 0.5f) - 0.25f);
                var line = Mathf.Clamp01(1f - Mathf.Min(a, b) / 0.012f) * 0.35f;
                var fibre = (Mathf.PerlinNoise(u * 64f, v * 64f) - 0.5f) * 0.10f;
                var col = Color.Lerp(baseCol, gold, line);
                return new Color(col.r + fibre, col.g + fibre * 0.4f, col.b + fibre * 0.5f, 1f);
            }, TextureWrapMode.Repeat);
        }

        /// <summary>The riser: dark navy slats with a thin gold line along the top.</summary>
        private static Texture2D RiserTex()
        {
            if (_riser != null) return _riser;
            const int n = 128;
            var navy = new Color(0.05f, 0.06f, 0.11f);
            return _riser = Make("Riser", n, (u, v) =>
            {
                var slat = Mathf.Abs(Mathf.Repeat(u, 1f) - 0.5f) > 0.47f ? 0.55f : 1f;
                var col = navy * slat;
                if (v > 0.9f && v < 0.95f) col = new Color(0.75f, 0.55f, 0.2f);
                var n2 = (Mathf.PerlinNoise(u * 20f, v * 20f) - 0.5f) * 0.03f;
                return new Color(col.r + n2, col.g + n2, col.b + n2, 1f);
            }, TextureWrapMode.Repeat);
        }

        private static Texture2D Make(string name, int n, System.Func<float, float, Color> f, TextureWrapMode wrap)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                px[y * n + x] = f(x / (float)n, y / (float)n);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
