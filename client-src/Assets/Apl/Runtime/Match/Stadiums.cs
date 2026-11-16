using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    public enum RoofKind { Open, Fabric, Solar, Ring, Canopy }

    [Flags]
    /// <summary>How the outfield is mown (round 10: the biggest thing in the broadcast frame, so each ground its own).</summary>
    public enum Mow { Stripes, WideStripes, Checks, Diagonal, Rings, Cross }

    public enum Scenery { None = 0, Skyline = 1, Water = 2, Mountains = 4, Hills = 8, Fort = 16, Pines = 32, Trees = 64, Palace = 128 }

    /// <summary>
    /// How a ground looks: tiers, roof, lights, what's beyond the stands. The
    /// rope, ends and pitch are the engine's <see cref="Venue"/>. Bearings are
    /// degrees round the ground from above: 0 = End A (behind the bowler, the
    /// broadcast end), 90 = the right-hander's off side, 270 = the pavilion.
    /// </summary>
    public sealed class StadiumLook
    {
        public string Id = "";
        public int Tiers = 2;
        public float TierRise = 7.5f;
        public RoofKind Roof;
        public float Waves;
        public bool Towers = true;
        public Scenery Scenery;

        /// <summary>Bearing of the signature view: the sea, the mountains, the fort, the river.</summary>
        public float SceneryAt = 180;

        /// <summary>Bearing of the hills / hill fort when they're not at the signature view.</summary>
        public float? HillsAt;
        public float SkylineHeight = 60;
        public Color Concrete = new Color(0.62f, 0.63f, 0.67f);
        public Color RoofColor = new Color(0.92f, 0.93f, 0.95f);
        public Color HillColor = new Color(0.16f, 0.24f, 0.17f);
        public Color FortColor = new Color(0.62f, 0.3f, 0.2f);
        public Color Sky = new Color(0.12f, 0.16f, 0.35f);

        /// <summary>Share of the crowd in the home side's colours.</summary>
        public float HomeCrowd = 0.55f;
        public bool HillPavilion, Arches;

        /// <summary>Stands stop short of the corners (grass banks under the towers there), ±<see cref="CornerHalf"/>°.</summary>
        public bool OpenCorners;

        /// <summary>The venue's colour (tools/venue_art.py uses the same): tier walls, board trims, sight-screen frame.</summary>
        public Color Accent = new Color(0.3f, 0.45f, 0.9f);
        public Mow Mow = Mow.Stripes;

        /// <summary>The outfield's green (lusher on the coast, drier on the plains).</summary>
        public Color Grass = new Color(0.23f, 0.52f, 0.2f);
        public float CornerHalf = 11;

        private static Color C(float r, float g, float b) => new Color(r, g, b);

        public static readonly IReadOnlyList<StadiumLook> All = new List<StadiumLook>
        {
            new StadiumLook { Id = "seaface", Accent = C(0f, 0.67f, 0.86f), Mow = Mow.Stripes, Grass = C(0.2f, 0.5f, 0.2f), Roof = RoofKind.Fabric, Waves = 30, Scenery = Scenery.Skyline | Scenery.Water, SceneryAt = 90, SkylineHeight = 95, Sky = C(0.16f, 0.15f, 0.34f) },
            new StadiumLook { Id = "marina", Accent = C(0.98f, 0.77f, 0.12f), Mow = Mow.WideStripes, Grass = C(0.27f, 0.52f, 0.18f), Roof = RoofKind.Fabric, Waves = 18, Scenery = Scenery.Water | Scenery.Skyline, SceneryAt = 180, SkylineHeight = 40, HomeCrowd = 0.8f, Concrete = C(0.8f, 0.78f, 0.72f) },
            new StadiumLook { Id = "gardencity", Accent = C(0.47f, 0.78f, 0.27f), Mow = Mow.Checks, Grass = C(0.18f, 0.5f, 0.2f), Roof = RoofKind.Solar, Scenery = Scenery.Skyline | Scenery.Trees, SceneryAt = 90, SkylineHeight = 70 },
            new StadiumLook { Id = "lakeside", Accent = C(0.31f, 0.82f, 0.78f), Mow = Mow.Rings, Grass = C(0.22f, 0.52f, 0.22f), TierRise = 9, Roof = RoofKind.Canopy, Scenery = Scenery.Water | Scenery.Trees, SceneryAt = 0, Concrete = C(0.82f, 0.82f, 0.84f), HomeCrowd = 0.4f },
            new StadiumLook { Id = "hooghly", Accent = C(0.75f, 0.47f, 0.94f), Mow = Mow.Stripes, Grass = C(0.2f, 0.48f, 0.19f), Tiers = 3, TierRise = 8.5f, Roof = RoofKind.Canopy, Scenery = Scenery.Trees | Scenery.Water | Scenery.Skyline, SceneryAt = 180, SkylineHeight = 35, HomeCrowd = 0.7f },
            new StadiumLook { Id = "oldfort", Accent = C(0.9f, 0.43f, 0.24f), Mow = Mow.Diagonal, Grass = C(0.27f, 0.5f, 0.17f), Roof = RoofKind.Open, Scenery = Scenery.Fort | Scenery.Skyline, SceneryAt = 180, SkylineHeight = 30 },
            new StadiumLook { Id = "deccan", Accent = C(0.98f, 0.55f, 0.12f), Mow = Mow.Cross, Grass = C(0.28f, 0.5f, 0.16f), Roof = RoofKind.Open, OpenCorners = true, CornerHalf = 8, Scenery = Scenery.Hills | Scenery.Skyline, SceneryAt = 180, SkylineHeight = 40, HillColor = C(0.3f, 0.24f, 0.18f), HomeCrowd = 0.7f },
            new StadiumLook { Id = "sabarmati", Accent = C(0.24f, 0.55f, 1f), Mow = Mow.WideStripes, Grass = C(0.22f, 0.52f, 0.2f), Tiers = 3, TierRise = 9, Roof = RoofKind.Ring, Towers = false, Scenery = Scenery.Water | Scenery.Trees, SceneryAt = 0, Concrete = C(0.86f, 0.86f, 0.88f) },
            new StadiumLook { Id = "awadh", Accent = C(0.16f, 0.78f, 0.75f), Mow = Mow.Checks, Grass = C(0.24f, 0.5f, 0.18f), TierRise = 8.5f, Roof = RoofKind.Canopy, Scenery = Scenery.Trees | Scenery.Water, SceneryAt = 180, Concrete = C(0.84f, 0.84f, 0.86f), RoofColor = C(0.75f, 0.78f, 0.82f) },
            new StadiumLook { Id = "shivalik", Accent = C(0.9f, 0.24f, 0.31f), Mow = Mow.Diagonal, Grass = C(0.19f, 0.5f, 0.21f), Roof = RoofKind.Ring, Towers = false, Scenery = Scenery.Hills | Scenery.Trees, SceneryAt = 180, Concrete = C(0.86f, 0.86f, 0.88f), HillColor = C(0.14f, 0.22f, 0.2f) },
            new StadiumLook { Id = "dhauladhar", Accent = C(0.78f, 0.88f, 1f), Mow = Mow.Rings, Grass = C(0.17f, 0.46f, 0.22f), Tiers = 1, TierRise = 8, Roof = RoofKind.Open, OpenCorners = true, CornerHalf = 14, Scenery = Scenery.Mountains | Scenery.Pines, SceneryAt = 180, HillPavilion = true, Concrete = C(0.6f, 0.57f, 0.53f), Sky = C(0.1f, 0.16f, 0.36f), HomeCrowd = 0.35f },
            new StadiumLook { Id = "pinkcity", Accent = C(0.98f, 0.47f, 0.63f), Mow = Mow.Cross, Grass = C(0.29f, 0.5f, 0.16f), Tiers = 1, TierRise = 10, Roof = RoofKind.Open, OpenCorners = true, Scenery = Scenery.Palace | Scenery.Fort | Scenery.Hills, SceneryAt = 0, HillsAt = 180, Arches = true, Concrete = C(0.86f, 0.56f, 0.52f), FortColor = C(0.78f, 0.55f, 0.4f), HillColor = C(0.28f, 0.22f, 0.16f) },
            new StadiumLook { Id = "brahmaputra", Accent = C(0.35f, 0.86f, 0.55f), Mow = Mow.Stripes, Grass = C(0.18f, 0.52f, 0.22f), Tiers = 1, TierRise = 9, Roof = RoofKind.Canopy, OpenCorners = true, CornerHalf = 12, Scenery = Scenery.Hills | Scenery.Water, SceneryAt = 0, HillsAt = 180, HillColor = C(0.12f, 0.24f, 0.14f) },
        };

        public static StadiumLook For(string id) => All.FirstOrDefault(l => l.Id == id) ?? All[0];
    }

    /// <summary>
    /// Puts a stadium round the pitch at runtime from its <see cref="Venue"/>
    /// and <see cref="StadiumLook"/>: the rope on the venue's shape, LED boards
    /// (the APL cover between franchise colours), tiers of crowd with APL
    /// fascia boards, sight screens, the pavilion, roof, lights, two big
    /// screens and the view beyond. A few merged meshes, no shadows.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private const int Segments = 192;
        private const float PavilionAt = 270, PavilionHalf = 15, ScreenHalf = 8;
        private static readonly float[] Screens = { 205, 25 };

        private static Venue _v = null!;

        private static float Rope(float deg)
        {
            var a = deg * Mathf.Deg2Rad;
            return (float)_v.RopeFromCentre(Mathf.Sin(a), Mathf.Cos(a));
        }

        private static Vector3 Dir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0, Mathf.Cos(deg * Mathf.Deg2Rad));

        /// <summary>A point <paramref name="offset"/> metres beyond the rope at bearing <paramref name="deg"/>.</summary>
        private static Vector3 At(float deg, float offset, float y) => Dir(deg) * (Rope(deg) + offset) + Vector3.up * y;

        /// <summary>A point at a fixed distance from the middle.</summary>
        private static Vector3 Far(float deg, float radius, float y) => Dir(deg) * radius + Vector3.up * y;

        private static float Gap(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b));

        /// <summary>
        /// The ground for <paramref name="venue"/>; with the two sides playing,
        /// the crowd wears both their colours (home fans by the look's share).
        /// </summary>
        public static GameObject Build(Venue venue, StadiumLook look, GroundRefs refs, Dataset data, Franchise? batting = null, Franchise? bowling = null)
        {
            _v = venue;
            var root = new GameObject("Stadium " + venue.Id).transform;
            var m = new Mats(refs);
            _cover = refs.Cover;
            var home = data.Franchises.FirstOrDefault(f => f.Id == venue.Home) ?? data.Franchises[0];
            var homeA = UiKit.ParseHex(home.PrimaryColor);
            var homeB = UiKit.ParseHex(home.SecondaryColor);
            SeatFans(look, data, home, batting, bowling);
            _homeSeat = homeA;
            _look = look;

            Sky(look, refs);
            NightSky(root, look);
            Land(root, m);
            Outfield(root);
            RopeAndBoards(root, m, data, refs);
            PavilionStand(root, m, look);
            var (back, top) = Tiers(root, m, look, homeA, homeB, refs);
            SightScreens(root, m, look);
            CornerBanks(root, m, back, top);
            Pavilion(root, m, look, back, top);
            RoofAndLights(root, m, look, back, top);
            BigScreens(root, m, refs, back, top);
            CheerPlatforms(root, m);
            Beyond(root, m, look, back, top);
            Stumps(root, m);
            PitchWear();
            return root.gameObject;
        }

        // ------------------------------------------------------------------ sky

        private static void Sky(StadiumLook look, GroundRefs refs)
        {
            if (RenderSettings.skybox != null)
            {
                var sky = new Material(RenderSettings.skybox);
                sky.SetColor("_SkyTint", look.Sky);
                RenderSettings.skybox = sky;
            }
            RenderSettings.fogStartDistance = 260;
            RenderSettings.fogEndDistance = (look.Scenery & Scenery.Mountains) != 0 ? 7000 : 1800;
            if (refs.Camera != null) refs.Camera.farClipPlane = 3500;
        }

        private static void Land(Transform root, Mats m)
        {
            var k = new MeshKit();
            const float l = 3000;
            k.Quad(0, new Vector3(-l, -0.4f, -l), new Vector3(-l, -0.4f, l), new Vector3(l, -0.4f, l), new Vector3(l, -0.4f, -l), Vector2.zero, new Vector2(1, 1));
            k.Spawn("Land", root, m.Lit(new Color(0.05f, 0.07f, 0.06f), smooth: 0.05f));
        }

        /// <summary>
        /// The scene's square of striped grass, cut to this ground: out to the
        /// stands and no further (same texture mapping as the square).
        /// </summary>
        private static void Outfield(Transform root)
        {
            var square = GameObject.Find("Outfield");
            var r = square != null ? square.GetComponent<Renderer>() : null;
            if (r == null) return;
            var size = square!.transform.lossyScale.x * 10;
            var k = new MeshKit();
            Vector2 Uv(Vector3 p) => new Vector2(0.5f - p.x / size, 0.5f - p.z / size);
            var centre = k.Vert(Vector3.zero, Uv(Vector3.zero));
            for (var i = 0; i < Segments; i++)
            {
                var p0 = At(i * 360f / Segments, 10, 0);
                var p1 = At((i + 1) * 360f / Segments, 10, 0);
                k.Tri(0, centre, k.Vert(p0, Uv(p0)), k.Vert(p1, Uv(p1)));
            }
            // This ground's own green and mowing pattern (StadiumBuilder.VenueArt).
            var mat = new Material(r.sharedMaterial) { name = "Grass_" + _look.Id };
            mat.SetTexture("_BaseMap", Grass(_look));
            mat.SetTextureScale("_BaseMap", Vector2.one);
            var grass = k.Spawn("OutfieldCut", root, mat);
            grass.GetComponent<MeshRenderer>().receiveShadows = true;
            r.enabled = false;
        }

        // ------------------------------------------------------------ rope, boards

        private static void RopeAndBoards(Transform root, Mats m, Dataset data, GroundRefs refs)
        {
            Cushion(root, m);

            // LED boards 3.2 m behind the rope: every other one the APL cover.
            var teams = data.Franchises;
            var venueBoard = VenueTex("board");
            var boards = new MeshKit(2 + teams.Count);
            var crests = new MeshKit(teams.Count);
            const float off = 3.2f, h = 1.05f, w = h * 2.59f;
            var perimeter = 0f;
            for (var d = 0; d < 360; d += 2) perimeter += (Rope(d) + off) * 2 * Mathf.Deg2Rad;
            var n = Mathf.Max(24, Mathf.RoundToInt(perimeter / w));
            if (n % 2 == 1) n++;
            var t = 0;
            for (var i = 0; i < n; i++)
            {
                var a0 = i * 360f / n + 0.05f;
                var a1 = (i + 1) * 360f / n - 0.05f;
                Vector3 P(float a, float y) => At(a, off, y);
                if (i % 2 == 0)
                {
                    // Every other one the APL cover — and every fourth this ground's own board.
                    var sub = venueBoard != null && i % 4 == 0 ? 1 + teams.Count : 0;
                    boards.Quad(sub, P(a0, 0.05f), P(a0, 0.05f + h), P(a1, 0.05f + h), P(a1, 0.05f));
                    continue;
                }
                var team = t++ % teams.Count;
                boards.Quad(1 + team, P(a0, 0.05f), P(a0, 0.05f + h), P(a1, 0.05f + h), P(a1, 0.05f));
                // The crest in the middle of the board, a hair in front.
                var mid = (a0 + a1) / 2;
                var c = At(mid, off - 0.04f, 0.05f + h / 2);
                var right = Vector3.Cross(Vector3.up, Dir(mid)) * (h * 0.45f);
                var up = Vector3.up * (h * 0.45f);
                crests.Quad(team, c - right - up, c - right + up, c + right + up, c + right - up);
            }
            var boardMats = new List<Material> { m.Glow(Color.white, 1.1f, refs.Cover) };
            var crestMats = new List<Material>();
            foreach (var f in teams)
            {
                boardMats.Add(m.Glow(UiKit.ParseHex(f.PrimaryColor), 0.9f));
                var crest = Resources.Load<Texture2D>("Crests/" + f.Id);
                crestMats.Add(crest != null ? m.Cutout(crest, 1.2f) : m.Glow(UiKit.ParseHex(f.SecondaryColor), 1f));
            }
            boardMats.Add(venueBoard != null ? m.Glow(Color.white, 1.1f, venueBoard) : boardMats[0]);
            boards.Spawn("Boards", root, boardMats.ToArray());
            crests.Spawn("BoardCrests", root, crestMats.ToArray());

            // Dark frame behind the boards.
            var frame = new MeshKit();
            Ring(frame, 0, _ => new[] { new Vector2(off + 0.15f, 0), new Vector2(off + 0.15f, h + 0.15f), new Vector2(off - 0.02f, h + 0.15f) }, null, 10);
            frame.Spawn("BoardFrame", root, m.Lit(new Color(0.05f, 0.05f, 0.07f)));
        }

        // ---------------------------------------------------------------- stands

        private static bool InPavilion(float deg) => Gap(deg, PavilionAt) < PavilionHalf;

        /// <summary>Straight down both ends, the sight screens stand in front of the lowest tier.</summary>
        private static bool BehindSightScreen(float deg, float offset)
        {
            var across = Mathf.Abs(Mathf.Sin(deg * Mathf.Deg2Rad)) * (Rope(deg) + offset);
            return across < 13.5f && Mathf.Abs(Mathf.Cos(deg * Mathf.Deg2Rad)) > 0.5f;
        }

        /// <summary>Builds the tiers; returns the top tier's back offset and height.</summary>
        private static (float back, float top) Tiers(Transform root, Mats m, StadiumLook look, Color homeA, Color homeB, GroundRefs refs)
        {
            var seats = new MeshKit();
            var concrete = new MeshKit();
            var fascia = new MeshKit(3);
            var o = 9f;
            var y = 0f;
            var rise = look.TierRise;
            var depth = rise * 1.5f;
            for (var tier = 0; tier < look.Tiers; tier++)
            {
                var front = o;
                var baseY = y;
                Func<float, bool> skip = tier == 0
                    ? a => Gone(a) || BehindSightScreen(a, front)
                    : Gone;
                if (tier == 0)
                {
                    // The front wall in the venue's colour (in the broadcast frame behind the boards).
                    Ring(fascia, 2, _ => new[] { new Vector2(front, 0), new Vector2(front, 1.4f) }, skip, 8);
                    baseY = 1.4f;
                }
                else
                {
                    // The tier's front: an APL board all the way round.
                    // The venue's board and the APL cover in turn (StadiumBuilder.VenueArt.Fascia: one strip, twice as long).
                    Ring(fascia, 0, _ => new[] { new Vector2(front, baseY - 2f), new Vector2(front, baseY) }, skip, (VenueTex("board") != null ? 4f : 2f) * 2.59f);
                    // Soffit under the overhang.
                    Ring(concrete, 0, _ => new[] { new Vector2(front + depth * 0.5f, baseY - 2.6f), new Vector2(front, baseY - 2f) }, skip, 8);
                }
                Ring(seats, 0, _ => new[] { new Vector2(front, baseY), new Vector2(front + depth, baseY + rise) }, skip, 16);
                // Fans on the seat rows; the tiers under an overhang a little darker.
                CrowdTier(tier, front, baseY, depth, rise, skip, tier < look.Tiers - 1 ? 0.82f : 1f, look.Id.Aggregate(7, (h, ch) => h * 31 + ch));
                var back = front + depth;
                var topY = baseY + rise;
                if (tier == look.Tiers - 1)
                {
                    // Parapet, then the outside wall down to the ground.
                    Ring(concrete, 0, _ => new[] { new Vector2(back, topY), new Vector2(back, topY + 2.4f), new Vector2(back + 0.6f, topY + 2.4f) }, Gone, 8);
                    Exterior(root, m, look, homeA, back + 0.6f, topY + 2.4f);
                    if (look.Arches) Ring(fascia, 1, _ => new[] { new Vector2(back - 0.05f, topY + 0.2f), new Vector2(back - 0.05f, topY + 2.4f) }, Gone, 2.2f * 2);
                    var fasciaMats = new[] { m.Glow(Color.white, 1.05f, Fascia(refs.Cover)), m.Lit(look.Concrete, Arches(), 0.2f), m.Lit(Color.Lerp(look.Accent, Color.black, 0.45f), smooth: 0.3f, emission: 0.25f) };
                    // Seat rows under the fans (one row per texture tile across each tier).
                    // Seats and stand concrete take the venue's colour too (r11: "per-venue stand colour missing").
                    var seatMat = m.Lit(Color.white, Seats(Color.Lerp(homeA, look.Accent, 0.5f)), 0.05f, emission: 0.3f);
                    seatMat.SetTextureScale("_BaseMap", new Vector2(1, Mathf.Max(3, Mathf.RoundToInt(depth / RowDepth))));
                    var crowd = seats.Spawn("Seats", root, seatMat);
                    CrowdSpawn(root);
                    CameraFlashes(crowd.GetComponent<MeshRenderer>(), m);
                    concrete.Spawn("Concrete", root, m.Lit(Color.Lerp(look.Concrete, look.Accent, 0.22f), smooth: 0.2f));
                    if (!fascia.Empty) fascia.Spawn("Fascia", root, fasciaMats);
                    return (back, topY);
                }
                // Back of this tier up to the next tier's floor; next tier overhangs half of this one.
                var nextY = topY + 3.4f;
                Ring(concrete, 0, _ => new[] { new Vector2(back, topY), new Vector2(back, nextY) }, Gone, 8);
                o = front + depth * 0.5f;
                y = nextY;
            }
            return (o, y);
        }

        /// <summary>Phones and cameras going off in the stands; <see cref="Flash"/> for a big moment.</summary>
        private static ParticleSystem? _flashes;

        private static void CameraFlashes(MeshRenderer crowd, Mats m)
        {
            var go = new GameObject("CameraFlashes");
            go.transform.SetParent(crowd.transform.parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.maxParticles = 800;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 18;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.MeshRenderer;
            shape.meshRenderer = crowd;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = m.Glow(Color.white, 6f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            _flashes = ps;
        }

        /// <summary>A burst of flashes round the ground (a six, a wicket).</summary>
        public static void Flash(int count)
        {
            if (_flashes != null) _flashes.Emit(count);
        }

        private static void SightScreens(Transform root, Mats m, StadiumLook look)
        {
            var k = new MeshKit();
            var h = 1.4f + look.TierRise + (look.Tiers > 1 ? 1.4f : 3f);
            foreach (var end in new[] { 0f, 180f })
            {
                var d = Dir(end);
                k.Box(0, At(end, 7.5f, 0), new Vector3(27, h, 0.8f), -d);
            }
            k.Spawn("SightScreens", root, m.Lit(new Color(0.03f, 0.03f, 0.04f), smooth: 0.05f));
            SightScreenFaces(root, m, h);
        }

        // ------------------------------------------------------------ roof, lights

        private static bool UnderScreen(float deg) => Screens.Any(s => Gap(deg, s) < ScreenHalf);

        private static void RoofAndLights(Transform root, Mats m, StadiumLook look, float back, float top)
        {
            var roof = new MeshKit();
            var steel = new MeshKit();
            var leds = new MeshKit();
            Func<float, bool> skip = a => Gone(a) || UnderScreen(a);
            var reach = back - 9;
            float lip = 0, lipY = 0; // the roof's front edge (offset, height): Floodlit hangs its lights there
            switch (look.Roof)
            {
                case RoofKind.Fabric:
                {
                    var front = back - reach * 0.55f;
                    var waves = look.Waves;
                    // Gentle scallops between the masts (a 1.6 m swing read as a jagged edge from the middle).
                    Ring(roof, 0, a => new[] { new Vector2(back + 1, top + 5), new Vector2(front, top + 8.5f + 0.6f * Mathf.Sin(a * Mathf.Deg2Rad * waves)) }, skip, 10);
                    lip = front;
                    lipY = top + 8.5f;
                    for (var a = 0f; a < 360; a += 360f / 32)
                    {
                        if (skip(a)) continue;
                        steel.Box(0, At(a, back + 1.6f, 0), new Vector3(0.7f, top + 15, 0.7f), Dir(a));
                    }
                    break;
                }
                case RoofKind.Solar:
                {
                    var front = back - reach * 0.5f;
                    Ring(roof, 0, _ => new[] { new Vector2(back + 1, top + 5), new Vector2(front, top + 7) }, skip, 6);
                    lip = front;
                    lipY = top + 7;
                    break;
                }
                case RoofKind.Ring:
                {
                    // Long cantilever with the floodlights built into its edge.
                    var front = back - reach * 0.8f;
                    Ring(roof, 0, _ => new[] { new Vector2(back + 2, top + 8), new Vector2(front, top + 11) }, Gone, 10);
                    Ring(leds, 0, _ => new[] { new Vector2(front + 0.1f, top + 10.6f), new Vector2(front + 2.4f, top + 10.4f) }, Gone, 4);
                    break;
                }
                case RoofKind.Canopy:
                {
                    var front = back - reach * 0.6f;
                    var mid = (back + front) / 2;
                    Ring(roof, 0, _ => new[] { new Vector2(back + 1, top + 4), new Vector2(mid, top + 8), new Vector2(front, top + 7) }, skip, 10);
                    lip = front;
                    lipY = top + 7;
                    break;
                }
            }
            if (!roof.Empty)
            {
                var mat = look.Roof switch
                {
                    RoofKind.Solar => m.Lit(Color.white, Solar(), 0.6f, emission: 0.15f),
                    RoofKind.Fabric => m.Lit(look.RoofColor, Fabric(), 0.15f, emission: 0.5f),
                    _ => m.Lit(look.RoofColor, smooth: 0.35f, metal: 0.2f, emission: 0.06f),
                };
                roof.Spawn("Roof", root, mat);
            }
            if (!leds.Empty) leds.Spawn("RoofLights", root, m.Glow(new Color(1f, 0.97f, 0.9f), 4f));

            if (look.Towers)
            {
                var lamps = new MeshKit();
                var height = top + 34;
                foreach (var a in new[] { 42f, 138f, 222f, 318f })
                {
                    var foot = At(a, back + 12, 0);
                    steel.Box(0, foot, new Vector3(1.6f, height, 1.6f), Dir(a));
                    // Lamp head facing the middle.
                    var inward = -Dir(a);
                    lamps.Box(0, foot + inward * 1.2f + Vector3.up * (height - 2), new Vector3(13, 7, 0.8f), inward);
                }
                lamps.Spawn("Lamps", root, m.Glow(new Color(1f, 0.97f, 0.88f), 3.5f, Lamps()));
            }
            Floodlit(root, m, look, back, top, skip, lip, lipY);
            if (!steel.Empty) steel.Spawn("Steel", root, m.Lit(new Color(0.42f, 0.43f, 0.47f), smooth: 0.5f, metal: 0.6f));
        }

        private static void BigScreens(Transform root, Mats m, GroundRefs refs, float back, float top)
        {
            var frame = new MeshKit();
            var face = new MeshKit();
            // STD-1: 18 m wide (was 24), so with the lower mount its top stays inside the gameplay frame.
            const float w = 18, h = w / 2.59f;
            var feed = root.gameObject.AddComponent<StadiumBigScreen>();
            foreach (var a in Screens)
            {
                // Round 13 (r12 `P94`: the live screen half off the top of the gameplay frame): the screen sits
                // 4.5 m lower, mounted in the top of the upper tier rather than on the parapet.
                // STD-1 (r13: "WICKET!", "SIX!" still touched the frame top): the screen's top edge at most
                // ~21 m up at every venue (2 tiers: on the upper tier's front; 1 tier: on its back rows).
                var mount = Mathf.Min(Mathf.Max(top * 0.5f, top - 6.5f), 21f - (0.7f + h));
                // Forward by the seat rake (1.5 m per metre of rise) so it stands on the rows, not inside them.
                var foot = At(a, Mathf.Max(back - 1 - (top - mount) * 1.5f, 14f), mount);
                var inward = -Dir(a);
                frame.Box(0, foot - inward * 0.7f, new Vector3(w + 1.2f, h + 1.4f, 1), inward);
                // Legs down to the stand.
                var right = Vector3.Cross(Vector3.up, inward);
                // Short legs down to the rows it stands on.
                foreach (var s in new[] { -1f, 1f }) frame.Box(0, foot - inward * 0.7f + right * s * w * 0.35f - Vector3.up * 1.2f, new Vector3(0.8f, 1.2f, 0.8f), inward);
                var c = foot + Vector3.up * (0.7f + h / 2) + inward * 0.05f;
                // Facing the middle: viewer's right is -right.
                var r = -right * (w / 2);
                var u = Vector3.up * (h / 2);
                face.Quad(0, c - r - u, c - r + u, c + r + u, c + r - u);
                feed.Face(c, inward, w, h);
            }
            frame.Spawn("ScreenFrames", root, m.Lit(new Color(0.06f, 0.06f, 0.08f)));
            face.Spawn("BigScreens", root, m.Glow(Color.white, 1.25f, refs.Cover));
        }

        // ----------------------------------------------------------------- beyond

        private static void Beyond(Transform root, Mats m, StadiumLook look, float back, float top)
        {
            var rng = new System.Random(look.Id.GetHashCode());
            float R() => (float)rng.NextDouble();
            var outer = back + 110;
            var at = look.SceneryAt;

            if ((look.Scenery & Scenery.Water) != 0)
            {
                var water = new MeshKit();
                var lights = new MeshKit();
                var r0 = outer + 40;
                for (var a = at - 55; a < at + 55; a += 2)
                {
                    water.Quad(0, Far(a, r0, -0.3f), Far(a, 2800, -0.3f), Far(a + 2, 2800, -0.3f), Far(a + 2, r0, -0.3f), Vector2.zero, Vector2.one);
                    // Lamps along the front.
                    lights.Box(0, Far(a + 1, r0 - 4, 0), new Vector3(1.6f, 1.6f, 1.6f), Dir(a));
                    if (R() < 0.12f) lights.Box(0, Far(a + R() * 2, r0 + 200 + R() * 900, -0.2f), new Vector3(3, 2, 6), Dir(a));
                }
                water.Spawn("Water", root, m.Lit(new Color(0.03f, 0.06f, 0.12f), smooth: 0.92f, metal: 0.3f));
                lights.Spawn("WaterLights", root, m.Glow(new Color(1f, 0.82f, 0.5f), 3f));
            }

            if ((look.Scenery & Scenery.Skyline) != 0)
            {
                var city = new MeshKit(3);
                var caps = new MeshKit();
                var reds = new MeshKit();
                for (var a = 0f; a < 360; a += 3.2f)
                {
                    if ((look.Scenery & Scenery.Water) != 0 && Gap(a, at) < 60) continue;
                    if ((look.Scenery & (Scenery.Fort | Scenery.Hills | Scenery.Palace)) != 0 && Gap(a, at) < 35) continue;
                    var n = 1 + rng.Next(2);
                    for (var i = 0; i < n; i++)
                    {
                        var r = outer + 20 + R() * 260;
                        var h = look.SkylineHeight * (0.25f + R() * R() * 1.1f);
                        Tower(city, caps, reds, Far(a + R() * 3, r, -0.4f), new Vector3(16 + R() * 20, h, 14 + R() * 18), -Dir(a), rng.Next(3), R());
                    }
                }
                SpawnCity(root, m, city, caps, reds);
                FarSkyline(root, m, look, a => ((look.Scenery & Scenery.Water) != 0 && Gap(a, at) < 60) || ((look.Scenery & (Scenery.Hills | Scenery.Mountains)) != 0 && Gap(a, look.HillsAt ?? at) < 100));
            }

            if ((look.Scenery & (Scenery.Hills | Scenery.Mountains)) != 0)
            {
                var hills = new MeshKit();
                var mountains = (look.Scenery & Scenery.Mountains) != 0;
                var hillsAt = look.HillsAt ?? at;
                var layers = mountains ? new[] { (r: 2600f, h: 900f, half: 110f), (r: 1500f, h: 380f, half: 120f) } : new[] { (r: 900f, h: 150f, half: 100f), (r: 600f, h: 70f, half: 140f) };
                foreach (var (r, maxH, half) in layers)
                {
                    var seed = R() * 100;
                    for (var a = hillsAt - half; a < hillsAt + half; a += 1.5f)
                    {
                        float H(float b)
                        {
                            var taper = Mathf.Clamp01((half - Gap(b, hillsAt)) / (half * 0.35f));
                            var n = Mathf.PerlinNoise(b * 0.06f + seed, seed) * 0.65f + Mathf.PerlinNoise(b * 0.22f + seed, 3 + seed) * 0.35f;
                            if (mountains) n = Mathf.Pow(n, 1.6f) * 1.5f;
                            return maxH * Mathf.Clamp01(n) * Mathf.SmoothStep(0, 1, taper) + 4;
                        }
                        var h0 = H(a);
                        var h1 = H(a + 1.5f);
                        // Facing the middle; v by height so only the high peaks catch snow.
                        var bl = Far(a, r, -2);
                        var tl = Far(a, r, h0);
                        var tr = Far(a + 1.5f, r, h1);
                        var br = Far(a + 1.5f, r, -2);
                        var i0 = hills.Vert(bl, new Vector2(0, 0));
                        var i1 = hills.Vert(tl, new Vector2(0, h0 / maxH));
                        var i2 = hills.Vert(tr, new Vector2(1, h1 / maxH));
                        var i3 = hills.Vert(br, new Vector2(1, 0));
                        hills.Tri(0, i0, i1, i2);
                        hills.Tri(0, i0, i2, i3);
                    }
                }
                hills.Spawn(mountains ? "Mountains" : "Hills", root, m.Glow(Color.white, 0.32f, Slopes(look.HillColor, mountains)));
            }

            if ((look.Scenery & Scenery.Fort) != 0)
            {
                var fort = new MeshKit();
                var fortAt = look.HillsAt ?? at;
                // On a hill when there are hills, else just beyond the stands.
                var onHill = (look.Scenery & Scenery.Hills) != 0 && (look.Scenery & Scenery.Palace) != 0;
                var r = onHill ? 560f : back + 75;
                var baseY = onHill ? 55f : -0.4f;
                // Walls high enough to show over the stands from the middle.
                var wall = onHill ? 14f : 30f;
                var step = 600f / r;
                for (var a = fortAt - 24; a < fortAt + 24; a += step)
                {
                    var p = Far(a, r, baseY);
                    fort.Box(0, p, new Vector3(r * step * Mathf.Deg2Rad + 0.5f, wall, 9), -Dir(a), 1 / 8f);
                    for (var c = -1; c <= 1; c++) fort.Box(0, Far(a + c * step * 0.3f, r - 4.2f, baseY + wall), new Vector3(2, 2.4f, 1.4f), -Dir(a));
                    if (Mathf.Abs(Mathf.Repeat(a - fortAt + 4, 16)) < step) fort.Box(0, Far(a, r - 3, baseY), new Vector3(14, wall * 1.35f, 14), -Dir(a), 1 / 8f);
                }
                if (baseY > 0) fort.Box(0, Far(fortAt, r + 20, -1), new Vector3(r * 50 * Mathf.Deg2Rad, baseY + 1, 60), -Dir(fortAt));
                fort.Spawn("Fort", root, m.Lit(look.FortColor, smooth: 0.1f, emission: 0.35f));
            }

            if ((look.Scenery & Scenery.Palace) != 0)
            {
                var palace = new MeshKit();
                var domes = new MeshKit();
                var r = outer + 40;
                for (var a = at - 26; a < at + 26; a += 6.5f)
                {
                    var h = 18 + R() * 14;
                    var w = r * 6 * Mathf.Deg2Rad;
                    palace.Box(0, Far(a, r, -0.4f), new Vector3(w, h, 22), -Dir(a), 1 / 4.4f);
                    for (var c = -1; c <= 1; c += 2)
                    {
                        var dome = Far(a + c * 1.6f, r - 6, h);
                        palace.Box(0, dome, new Vector3(5, 3, 5), -Dir(a));
                        domes.Blob(0, dome + Vector3.up * 4.5f, new Vector3(6, 5, 6));
                    }
                }
                palace.Spawn("Palace", root, m.Lit(new Color(0.9f, 0.58f, 0.54f), Arches(), 0.1f, emission: 0.4f));
                domes.Spawn("Domes", root, m.Lit(new Color(0.95f, 0.75f, 0.68f), smooth: 0.2f, emission: 0.4f));
            }

            if ((look.Scenery & (Scenery.Trees | Scenery.Pines)) != 0)
            {
                var crowns = new MeshKit();
                var trunks = new MeshKit();
                var pines = (look.Scenery & Scenery.Pines) != 0;
                for (var i = 0; i < 420; i++)
                {
                    // Around the ground, thicker towards the signature view.
                    var a = R() < 0.4f ? at + (R() - 0.5f) * 140 : R() * 360;
                    if ((look.Scenery & Scenery.Water) != 0 && Gap(a, at) < 55 && !pines) a += 180;
                    var r = back + 22 + R() * 120;
                    if ((look.Scenery & Scenery.Skyline) != 0 && Gap(a, at) > 70 && !pines) r = back + 14 + R() * 20;
                    var p = Far(a, Rope(a) + r, -0.3f);
                    var h = pines ? 10 + R() * 14 : 7 + R() * 7;
                    if (pines)
                    {
                        crowns.Cone(0, p + Vector3.up * 2, 2.6f + R() * 1.2f, h);
                    }
                    else
                    {
                        trunks.Box(0, p, new Vector3(0.7f, h * 0.5f, 0.7f), Dir(a));
                        crowns.Blob(0, p + Vector3.up * h * 0.7f, new Vector3(h * 0.8f, h * 0.65f, h * 0.8f));
                    }
                }
                crowns.Spawn(pines ? "Pines" : "Trees", root, m.Lit(pines ? new Color(0.06f, 0.16f, 0.1f) : new Color(0.1f, 0.24f, 0.1f), smooth: 0.05f, emission: 0.12f));
                if (!trunks.Empty) trunks.Spawn("Trunks", root, m.Lit(new Color(0.2f, 0.14f, 0.1f)));
            }
        }

        // ------------------------------------------------------------------ rings

        /// <summary>
        /// A band right round the ground (bearing 0–360), following the rope:
        /// <paramref name="profile"/> gives (offset beyond the rope, height) rows
        /// from the side facing the middle outwards / upwards. Skipped bearings
        /// leave gaps. U tiles every <paramref name="uTile"/> metres round;
        /// V runs 0–1 across each row.
        /// </summary>
        private static void Ring(MeshKit k, int sub, Func<float, Vector2[]> profile, Func<float, bool>? skip, float uTile, float from = 0, float to = 360, bool flat = false)
        {
            var step = (to - from) / Segments;
            for (var i = 0; i < Segments; i++)
            {
                var a0 = from + i * step;
                var a1 = a0 + step;
                if (skip != null && skip((a0 + a1) / 2)) continue;
                var p0 = profile(a0);
                var p1 = profile(a1);
                for (var j = 0; j + 1 < p0.Length; j++)
                {
                    var radius = 70 + p0[j].x;
                    var u0 = a0 * Mathf.Deg2Rad * radius / uTile;
                    var u1 = a1 * Mathf.Deg2Rad * radius / uTile;
                    k.Quad(sub, At(a0, p0[j].x, p0[j].y), At(a0, p0[j + 1].x, p0[j + 1].y), At(a1, p1[j + 1].x, p1[j + 1].y), At(a1, p1[j].x, p1[j].y),
                        new Vector2(u0, 0), new Vector2(u1, flat ? (p0[j + 1].x - p0[j].x) / uTile : 1));
                }
            }
        }

        // --------------------------------------------------------------- textures

        private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();

        private static float Hash(int a, int b) => Mathf.Abs(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f) % 1f;

        private static Texture2D Tex(string key, int w, int h, Func<int, int, Color> f)
        {
            if (TexCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = key, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                px[y * w + x] = f(x, y);
            t.SetPixels(px);
            t.Apply(true, true);
            TexCache[key] = t;
            return t;
        }

        /// <summary>Building fronts: lit windows on dark; the pavilion's warmer and fuller.</summary>
        private static Texture2D Windows(bool pavilion) =>
            Tex(pavilion ? "windows_pavilion" : "windows_city", 64, 64, (x, y) =>
            {
                var wx = x / 4;
                var wy = y / 4;
                var inX = x % 4;
                var inY = y % 4;
                var frame = pavilion ? new Color(0.18f, 0.2f, 0.25f) : new Color(0.05f, 0.05f, 0.07f);
                if (inX == 0 || inY == 0) return frame;
                var on = Hash(wx, wy) < (pavilion ? 0.75f : 0.38f);
                if (!on) return pavilion ? new Color(0.12f, 0.16f, 0.24f) : new Color(0.07f, 0.08f, 0.1f);
                var warm = Hash(wy, wx + 7) < 0.7f;
                return warm ? new Color(1f, 0.85f, 0.55f) : new Color(0.75f, 0.85f, 1f);
            });

        /// <summary>A floodlight head: rows of round lamps on a dark frame.</summary>
        private static Texture2D Lamps() =>
            Tex("lamps", 64, 32, (x, y) =>
            {
                var cx = x % 8 - 3.5f;
                var cy = y % 8 - 3.5f;
                var on = cx * cx + cy * cy < 7.5f;
                return on ? Color.white : new Color(0.08f, 0.08f, 0.1f);
            });

        private static Texture2D Solar() =>
            Tex("solar", 64, 64, (x, y) =>
            {
                var line = x % 16 == 0 || y % 8 == 0;
                return line ? new Color(0.7f, 0.72f, 0.78f) : new Color(0.08f, 0.14f, 0.32f) * (0.9f + Hash(x / 16, y / 8) * 0.2f);
            });

        /// <summary>Rajput arches (jharokhas) on sandstone, tiled 2 across.</summary>
        private static Texture2D Arches() =>
            Tex("arches", 128, 64, (x, y) =>
            {
                var cx = x % 64 - 32;
                var inArch = Mathf.Abs(cx) < 18 && y > 6 && (y < 40 || cx * cx + (y - 40) * (y - 40) < 18 * 18);
                if (inArch) return new Color(0.45f, 0.22f, 0.2f);
                var band = y > 56 || y < 4;
                return band ? new Color(1f, 0.85f, 0.75f) : new Color(1f, 1f, 1f);
            });

        /// <summary>Hill or mountain colour by height (v): green-brown below, snow on the peaks.</summary>
        private static Texture2D Slopes(Color low, bool snow) =>
            Tex($"slopes_{ColorUtility.ToHtmlStringRGB(low)}_{snow}", 4, 128, (x, y) =>
            {
                var v = y / 127f;
                if (snow)
                {
                    var rock = Color.Lerp(low, new Color(0.3f, 0.32f, 0.38f), Mathf.Clamp01(v * 1.6f));
                    return v > 0.62f ? Color.Lerp(rock, new Color(0.92f, 0.95f, 1f), Mathf.Clamp01((v - 0.62f) * 12)) : rock;
                }
                return Color.Lerp(low * 0.6f, low * 1.25f, v);
            });

        // --------------------------------------------------------------- materials

        /// <summary>Materials copied from the scene's templates (so their shader variants are in the build).</summary>
        private sealed class Mats
        {
            private readonly GroundRefs _r;

            public Mats(GroundRefs r) { _r = r; }

            public Material Lit(Color c, Texture? tex = null, float smooth = 0.25f, float metal = 0, float emission = 0)
            {
                if (emission > 0) return Glow(c, emission, tex, smooth, metal);
                var m = new Material(_r.LitTemplate);
                m.SetColor("_BaseColor", c);
                m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", metal);
                return m;
            }

            public Material Glow(Color c, float emission, Texture? tex = null, float smooth = 0.3f, float metal = 0)
            {
                var m = new Material(_r.GlowTemplate);
                var t = tex != null ? tex : Texture2D.whiteTexture;
                m.SetColor("_BaseColor", c);
                m.SetTexture("_BaseMap", t);
                m.SetTexture("_EmissionMap", t);
                m.SetColor("_EmissionColor", c * emission);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", metal);
                return m;
            }

            public Material Cutout(Texture tex, float emission)
            {
                var m = new Material(_r.CutoutTemplate);
                m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BaseMap", tex);
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", Color.white * emission);
                return m;
            }
        }
    }
}
