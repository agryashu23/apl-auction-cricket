using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>The practice nets' lanes, props and surroundings (StadiumBuilder.Nets.cs).</summary>
    public static partial class StadiumBuilder
    {
        /// <summary>
        /// A netted lane centred on <paramref name="x"/>: side nets and a back net on steel posts,
        /// a top rail; the spare lanes get their own mat and a set of stumps.
        /// </summary>
        private static void NetLane(Transform root, Mats m, float x, bool main)
        {
            var net = new MeshKit();
            var steel = new MeshKit();
            var l = x - NetHalfWidth;
            var r = x + NetHalfWidth;
            void Panel(Vector3 a, Vector3 b)
            {
                var len = Vector3.Distance(a, b);
                // Both faces; one texture tile every 0.5 m (5 cm mesh).
                var up = Vector3.up * NetHeight;
                net.Quad(0, a, a + up, b + up, b, Vector2.zero, new Vector2(len / 0.5f, NetHeight / 0.5f));
                net.Quad(0, b, b + up, a + up, a, Vector2.zero, new Vector2(len / 0.5f, NetHeight / 0.5f));
            }
            Panel(new Vector3(l, 0, NetBack), new Vector3(l, 0, NetFront));
            Panel(new Vector3(r, 0, NetFront), new Vector3(r, 0, NetBack));
            Panel(new Vector3(r, 0, NetBack), new Vector3(l, 0, NetBack));
            for (var z = NetBack; z <= NetFront + 0.01f; z += 3f)
                foreach (var px in new[] { l, r })
                    steel.Box(0, new Vector3(px, 0, z), new Vector3(0.07f, NetHeight + 0.1f, 0.07f), Vector3.forward);
            steel.Box(0, new Vector3(x, 0, NetBack), new Vector3(0.07f, NetHeight + 0.1f, 0.07f), Vector3.forward);
            foreach (var px in new[] { l, r })
                steel.Box(0, new Vector3(px, NetHeight, (NetBack + NetFront) / 2), new Vector3(0.05f, 0.05f, NetFront - NetBack), Vector3.forward);
            steel.Box(0, new Vector3(x, NetHeight, NetBack), new Vector3(NetHalfWidth * 2, 0.05f, 0.05f), Vector3.forward);
            net.Spawn(main ? "NetMain" : "NetSpare", root, m.Cutout(NetTexture(), 0.12f));
            steel.Spawn("NetPosts", root, m.Lit(new Color(0.18f, 0.22f, 0.2f), smooth: 0.5f, metal: 0.6f));
            if (main) return;
            // A spare lane: its own synthetic mat and a set of stumps at the batting end.
            var mat = new MeshKit();
            mat.Box(0, new Vector3(x, 0, -2f), new Vector3(2.4f, 0.02f, GroundSpace.HalfPitch * 2), Vector3.forward);
            mat.Spawn("SpareMat", root, m.Lit(new Color(0.16f, 0.42f, 0.2f), smooth: 0.15f));
            var stumps = new MeshKit();
            for (var i = -1; i <= 1; i++)
                stumps.Box(0, new Vector3(x + i * 0.11f, 0, -GroundSpace.HalfPitch), new Vector3(0.035f, 0.71f, 0.035f), Vector3.forward);
            stumps.Spawn("SpareStumps", root, m.Lit(new Color(0.92f, 0.9f, 0.84f), smooth: 0.4f));
        }

        /// <summary>Practice clutter by the back of the main net: kit bags, a bucket of balls, cones, a bench.</summary>
        private static void NetsProps(Transform root, Mats m)
        {
            var bags = new MeshKit();
            var cones = new MeshKit();
            var bench = new MeshKit();
            var balls = new MeshKit();
            var behind = NetBack - 1.8f;
            bags.Box(0, new Vector3(-1.4f, 0, behind), new Vector3(0.9f, 0.32f, 0.35f), new Vector3(0.3f, 0, 1));
            bags.Box(0, new Vector3(0.6f, 0, behind - 0.4f), new Vector3(0.85f, 0.3f, 0.34f), new Vector3(-0.2f, 0, 1));
            bags.Box(0, new Vector3(NetHalfWidth + 1.6f, 0, NetBack + 2f), new Vector3(0.9f, 0.32f, 0.35f), Vector3.right);
            // The ball bucket and a few balls spilt beside it.
            bench.Box(0, new Vector3(2.1f, 0, behind), new Vector3(0.32f, 0.36f, 0.32f), Vector3.forward);
            balls.Blob(0, new Vector3(2.1f, 0.4f, behind), new Vector3(0.26f, 0.1f, 0.26f));
            foreach (var p in new[] { new Vector3(2.5f, 0.036f, behind + 0.2f), new Vector3(1.8f, 0.036f, behind + 0.35f), new Vector3(2.35f, 0.036f, behind - 0.3f) })
                balls.Blob(0, p, Vector3.one * 0.072f);
            // A bench along the side, cones marking the bowlers' run-ups.
            bench.Box(0, new Vector3(-NetHalfWidth - 2.2f, 0.42f, NetBack + 4f), new Vector3(0.4f, 0.06f, 2.2f), Vector3.forward);
            foreach (var dz in new[] { -0.9f, 0.9f })
                bench.Box(0, new Vector3(-NetHalfWidth - 2.2f, 0, NetBack + 4f + dz), new Vector3(0.36f, 0.42f, 0.06f), Vector3.forward);
            foreach (var x in new[] { 0f, NetHalfWidth * 2 + 3.5f, -(NetHalfWidth * 2 + 3.5f) })
            foreach (var z in new[] { GroundSpace.HalfPitch + 7f, GroundSpace.HalfPitch + 14f })
                cones.Cone(0, new Vector3(x + 1.2f, 0, z), 0.12f, 0.3f, 8);
            bags.Spawn("KitBags", root, m.Lit(new Color(0.08f, 0.12f, 0.3f), smooth: 0.25f));
            bench.Spawn("Bench", root, m.Lit(new Color(0.42f, 0.3f, 0.18f), smooth: 0.2f));
            balls.Spawn("Balls", root, m.Lit(new Color(0.65f, 0.08f, 0.07f), smooth: 0.6f));
            cones.Spawn("Cones", root, m.Lit(new Color(1f, 0.45f, 0.08f), smooth: 0.3f, emission: 0.15f));
        }

        /// <summary>A ring of trees round the practice ground and a low clubhouse on the leg side.</summary>
        private static void NetsTrees(Transform root, Mats m)
        {
            var rng = new System.Random(_v.Id.Length * 31 + 7);
            float R() => (float)rng.NextDouble();
            var crowns = new MeshKit();
            var trunks = new MeshKit();
            for (var i = 0; i < 260; i++)
            {
                var a = R() * 360f;
                if (Gap(a, 270) < 18) continue; // the clubhouse
                var p = Far(a, 95 + R() * 70, -0.05f);
                var h = 8 + R() * 9;
                trunks.Box(0, p, new Vector3(0.6f, h * 0.5f, 0.6f), Dir(a));
                crowns.Blob(0, p + Vector3.up * h * 0.7f, new Vector3(h * 0.8f, h * 0.65f, h * 0.8f));
            }
            crowns.Spawn("Trees", root, m.Lit(new Color(0.16f, 0.32f, 0.14f), smooth: 0.05f));
            trunks.Spawn("Trunks", root, m.Lit(new Color(0.25f, 0.18f, 0.12f)));
            // The clubhouse: white walls, a red pitched roof, a veranda.
            var walls = new MeshKit();
            var roof = new MeshKit(1);
            var at = Far(270, 78, 0);
            walls.Box(0, at, new Vector3(22, 5.5f, 9), Vector3.right, 1 / 3f);
            walls.Box(0, at + Vector3.right * 6f, new Vector3(16, 0.25f, 3), Vector3.right);
            var ridge = at + Vector3.up * 8.5f;
            var eaveIn = at + Vector3.up * 5.5f + Vector3.right * 5.2f;
            var eaveOut = at + Vector3.up * 5.5f - Vector3.right * 5.2f;
            var half = Vector3.forward * 11.5f;
            roof.Quad(0, eaveIn - half, ridge - half, ridge + half, eaveIn + half);
            roof.Quad(0, eaveOut + half, ridge + half, ridge - half, eaveOut - half);
            walls.Spawn("Clubhouse", root, m.Lit(new Color(0.93f, 0.92f, 0.88f), Windows(true), 0.2f));
            roof.Spawn("ClubhouseRoof", root, m.Lit(new Color(0.6f, 0.18f, 0.14f), smooth: 0.2f));
        }

        /// <summary>Netting: knotted cord in a 5 cm diamond-ish square mesh, clear between (alpha cut).</summary>
        private static Texture2D NetTexture() =>
            Tex("net", 64, 64, (x, y) =>
            {
                var cord = x % 6 < 1 || y % 6 < 1;
                return cord ? new Color(0.1f, 0.12f, 0.11f, 1f) : new Color(0, 0, 0, 0);
            });
    }
}
