using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.Match
{
    /// <summary>
    /// The crowd: a sprite fan on every seat row (Apl/Crowd shader), shirts in
    /// both sides' colours weighted by home and away, flags among them, all
    /// bobbing; <see cref="CrowdCheer"/> gets them up on a four, a six or a
    /// wicket. Each tier is cut into 60° chunks (a few static meshes that
    /// cull separately), every chunk drawn with the one shared material.
    /// </summary>
    public static partial class StadiumBuilder
    {
        /// <summary>Seats 0.5 m apart; each fan's card 0.9 x 1.8 m from 0.2 m above his row's floor.</summary>
        private const float SeatWidth = 0.5f, FanWidth = 0.9f, FanFloor = 0.2f, FanHeight = 1.8f, RowDepth = 0.85f;

        /// <summary>
        /// The sprite sheet (Resources/Gear/crowd_atlas + crowd_mask, tools/gear/crowd_atlas.py):
        /// 16 x 8 cells; body b's frames at b*8 + (sit, stand, cheer a/b, clap a/b, wave a/b).
        /// </summary>
        private const int Cols = 16, Rows = 8, Bodies = 14, FlagCell = 124;

        /// <summary>A group of fans: its shirts and which group code the shader sees (0, 0.5, 1 = neutral).</summary>
        private sealed class Fans
        {
            public string Id = "";
            public Color[] Shirts = Array.Empty<Color>();
            public float Share, Code;
        }

        private sealed class Chunk
        {
            public readonly List<Vector3> Pos = new List<Vector3>();
            public readonly List<Color32> Col = new List<Color32>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector4> K = new List<Vector4>();
            public readonly List<Vector3> Alt = new List<Vector3>();
            public readonly List<int> Tris = new List<int>();
        }

        private static readonly Dictionary<(int, int), Chunk> Chunks = new Dictionary<(int, int), Chunk>();
        private static List<Fans> _fans = new List<Fans>();
        private static string _groupA = "", _groupB = "";
        private static readonly int CheerId = Shader.PropertyToID("_AplCheer");
        private static readonly int CheerTeamsId = Shader.PropertyToID("_AplCheerTeams");
        private static readonly int WaveId = Shader.PropertyToID("_AplWave");

        /// <summary>
        /// The crowd goes up: arms in the air and jumping, strength 0–1 (a
        /// four ~0.6, a six or a wicket ~1). Fans of <paramref name="teamId"/>
        /// go loudest, the other side's barely; 0.8 and up also sends a
        /// Mexican wave round the ground.
        /// </summary>
        public static void CrowdCheer(float intensity, string? teamId = null, string? screen = null)
        {
            var t = Time.timeSinceLevelLoad;
            var s = Mathf.Clamp01(intensity);
            Shader.SetGlobalVector(CheerId, new Vector4(s, t, 1.2f + 2.2f * s, 0));
            float Joins(string id) => teamId == null || teamId == id ? 1f : 0.15f;
            Shader.SetGlobalVector(CheerTeamsId, new Vector4(Joins(_groupA), Joins(_groupB), 0.6f, 0));
            if (s >= 0.8f) Shader.SetGlobalVector(WaveId, new Vector4(1, t + 0.8f, 0.11f, 0));
            if (CheerSquad.Instance != null) CheerSquad.Instance.Dance(s);
            // The big screens: the callers' cheers are a six 1, a wicket 0.9, a four 0.6 (WinFx passes its own text).
            var text = screen ?? (s >= 0.95f ? "SIX!" : s >= 0.85f ? "WICKET!" : s >= 0.5f ? "FOUR!" : null);
            var team = _fans.FirstOrDefault(f => f.Id == teamId);
            if (text != null) StadiumBigScreen.Event(text, team != null && team.Shirts.Length > 0 ? Color.Lerp(team.Shirts[0], Color.white, 0.25f) : Color.white);
        }

        /// <summary>
        /// Who's in the stands: the home side's fans (their look's share) when
        /// they play, the visitors', and neutrals in every colour.
        /// </summary>
        private static void SeatFans(StadiumLook look, Dataset data, Franchise home, Franchise? batting, Franchise? bowling)
        {
            Chunks.Clear();
            Shader.SetGlobalVector(CheerId, Vector4.zero);
            Shader.SetGlobalVector(WaveId, Vector4.zero);
            Shader.SetGlobalVector(BannerId, Vector4.zero);
            Shader.SetGlobalVector(CheerTeamsId, Vector4.one);
            Color[] Kit(Franchise f) => new[] { UiKit.ParseHex(f.PrimaryColor), UiKit.ParseHex(f.PrimaryColor), UiKit.ParseHex(f.PrimaryColor), UiKit.ParseHex(f.SecondaryColor), new Color(0.95f, 0.95f, 0.95f) };
            _fans = new List<Fans>();
            var playing = new[] { batting, bowling }.Where(f => f != null).Select(f => f!).ToList();
            if (playing.Count == 0 || playing.Any(f => f.Id == home.Id))
            {
                var away = playing.FirstOrDefault(f => f.Id != home.Id);
                _fans.Add(new Fans { Id = home.Id, Shirts = Kit(home), Share = look.HomeCrowd, Code = 0 });
                if (away != null) _fans.Add(new Fans { Id = away.Id, Shirts = Kit(away), Share = (1 - look.HomeCrowd) * 0.55f, Code = 0.5f });
            }
            else
            {
                _fans.Add(new Fans { Id = playing[0].Id, Shirts = Kit(playing[0]), Share = 0.32f, Code = 0 });
                if (playing.Count > 1) _fans.Add(new Fans { Id = playing[1].Id, Shirts = Kit(playing[1]), Share = 0.32f, Code = 0.5f });
            }
            _groupA = _fans[0].Id;
            _groupB = _fans.Count > 1 ? _fans[1].Id : "";
            // Neutrals: every franchise's colours and the usual whites, blacks, pinks and oranges.
            var mixed = data.Franchises.Select(f => UiKit.ParseHex(f.PrimaryColor)).Concat(new[]
            {
                new Color(0.95f, 0.95f, 0.95f), new Color(0.1f, 0.1f, 0.12f), new Color(0.95f, 0.35f, 0.65f), new Color(0.98f, 0.55f, 0.1f),
                new Color(0.15f, 0.75f, 0.85f), new Color(0.95f, 0.85f, 0.2f), new Color(0.3f, 0.75f, 0.3f), new Color(0.55f, 0.2f, 0.7f),
            }).ToArray();
            _fans.Add(new Fans { Id = "", Shirts = mixed, Share = 1 - _fans.Sum(f => f.Share), Code = 1 });
        }

        private static Fans PickFans(float r)
        {
            foreach (var f in _fans)
            {
                if (r < f.Share) return f;
                r -= f.Share;
            }
            return _fans[_fans.Count - 1];
        }

        /// <summary>Fans on a tier's seat slope: a row every <see cref="RowDepth"/>, a seat every half metre, a few empty.</summary>
        private static void CrowdTier(int tier, float front, float baseY, float depth, float rise, Func<float, bool> skip, float shade, int seed)
        {
            var rows = Mathf.Max(3, Mathf.RoundToInt(depth / RowDepth));
            var rng = new System.Random(seed * 131 + tier * 17);
            float R() => (float)rng.NextDouble();
            for (var row = 0; row < rows; row++)
            {
                var f = (row + 0.5f) / rows;
                var off = front + depth * f;
                var y = baseY + rise * f - 0.06f;
                var a = R() * 0.5f;
                while (a < 360)
                {
                    var radius = Rope(a) + off;
                    var step = SeatWidth * (0.9f + 0.2f * R()) / radius * Mathf.Rad2Deg;
                    if (!skip(a) && R() > 0.07f)
                    {
                        var fans = PickFans(R());
                        var shirt = fans.Shirts[rng.Next(fans.Shirts.Length)] * (0.85f + 0.2f * R());
                        var p = At(a, off + (R() - 0.5f) * 0.12f, y + (R() - 0.5f) * 0.05f);
                        var light = shade * (0.85f + 0.25f * R());
                        Fan(ChunkOf(tier, a), p, shirt, fans.Code, rng.Next(1 << 20), R(), light);
                        if (fans.Code < 1 && R() < 0.022f) Flag(ChunkOf(tier, a), p + Vector3.up * 0.55f, fans.Shirts[0], fans.Code, R(), light);
                        if (R() < 0.13f) Placard(ChunkOf(tier, a), p, fans.Shirts[0], fans.Code, R(), light);
                    }
                    a += step;
                }
            }
        }

        private static Chunk ChunkOf(int tier, float bearing)
        {
            var key = (tier, Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(bearing, 360) / 60), 0, 5));
            if (!Chunks.TryGetValue(key, out var c)) Chunks[key] = c = new Chunk();
            return c;
        }

        /// <summary>A cell's bottom-left uv (the sheet's row 0 is at the top of the image).</summary>
        private static Vector2 Cell(int index) => new Vector2((index % Cols) / (float)Cols, 1f - (index / Cols + 1) / (float)Rows);

        /// <summary>
        /// A fan: one of the bodies, seated (3 in 5) or standing, and the frame pair
        /// he goes to when excited — arms pumping (most), clapping or waving.
        /// </summary>
        private static void Fan(Chunk c, Vector3 foot, Color shirt, float code, int variant, float phase, float light)
        {
            var body = variant % Bodies;
            var idle = (variant / Bodies) % 5 < 3 ? 0 : 1;
            var roll = (variant / (Bodies * 5)) % 20;
            var pair = roll < 11 ? 2 : roll < 16 ? 4 : 6;
            var cell = Cell(body * 8 + idle);
            var excited = Cell(body * 8 + pair) - cell;
            var du = 1f / Cols;
            var dv = 1f / Rows;
            var col = (Color32)new Color(shirt.r, shirt.g, shirt.b, code);
            var start = c.Pos.Count;
            const float top = FanFloor + FanHeight;
            var corners = new[] { new Vector2(-FanWidth / 2, FanFloor), new Vector2(-FanWidth / 2, top), new Vector2(FanWidth / 2, top), new Vector2(FanWidth / 2, FanFloor) };
            var uvs = new[] { cell, cell + new Vector2(0, dv), cell + new Vector2(du, dv), cell + new Vector2(du, 0) };
            for (var i = 0; i < 4; i++)
            {
                c.Pos.Add(foot);
                c.Col.Add(col);
                c.Uv.Add(uvs[i]);
                c.K.Add(new Vector4(corners[i].x, corners[i].y, phase, 0));
                c.Alt.Add(new Vector3(excited.x, excited.y, light));
            }
            c.Tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        /// <summary>A flag held up above a fan: pole on the left, the cloth in four strips so it can ripple.</summary>
        private static void Flag(Chunk c, Vector3 foot, Color colour, float code, float phase, float light)
        {
            const float w = 0.8f, h = 1.6f;
            const int strips = 4;
            var cell = Cell(FlagCell);
            var du = 1f / Cols;
            var dv = 1f / Rows;
            var col = (Color32)new Color(colour.r, colour.g, colour.b, code);
            var start = c.Pos.Count;
            for (var i = 0; i <= strips; i++)
            {
                var x = w * i / strips;
                foreach (var y in new[] { 0f, h })
                {
                    c.Pos.Add(foot);
                    c.Col.Add(col);
                    c.Uv.Add(cell + new Vector2(du * i / strips, y > 0 ? dv : 0));
                    c.K.Add(new Vector4(x, y, phase, 1));
                    c.Alt.Add(new Vector3(0, 0, light));
                }
            }
            for (var i = 0; i < strips; i++)
            {
                var b = start + i * 2;
                c.Tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }
        }

        /// <summary>The chunks as meshes, one shared material; bounds padded for the billboards and the jumping.</summary>
        private static void CrowdSpawn(Transform root)
        {
            var shader = Resources.Load<Shader>("Shaders/Crowd") ?? Shader.Find("Apl/Crowd");
            if (shader == null)
            {
                Debug.LogWarning("[APL] Apl/Crowd shader missing: no crowd sprites");
                return;
            }
            var atlas = Resources.Load<Texture2D>("Gear/crowd_atlas");
            var mask = Resources.Load<Texture2D>("Gear/crowd_mask");
            if (atlas == null || mask == null)
            {
                Debug.LogWarning("[APL] Resources/Gear/crowd_atlas or crowd_mask missing: no crowd sprites");
                Chunks.Clear();
                return;
            }
            var mat = new Material(shader) { name = "Crowd" };
            mat.SetTexture("_MainTex", atlas);
            mat.SetTexture("_MaskTex", mask);
            mat.SetFloat("_CellU", 1f / Cols);
            mat.SetFloat("_Bright", 0.9f);
            var people = 0;
            foreach (var kv in Chunks)
            {
                var c = kv.Value;
                if (c.Pos.Count == 0) continue;
                var mesh = new Mesh { name = $"Crowd_{kv.Key.Item1}_{kv.Key.Item2}" };
                if (c.Pos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(c.Pos);
                mesh.SetColors(c.Col);
                mesh.SetUVs(0, c.Uv);
                mesh.SetUVs(1, c.K);
                mesh.SetUVs(2, c.Alt);
                mesh.SetTriangles(c.Tris, 0);
                var b = new Bounds(c.Pos[0], Vector3.zero);
                foreach (var p in c.Pos) b.Encapsulate(p);
                b.Expand(new Vector3(2, 5, 2));
                mesh.bounds = b;
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                people += c.Pos.Count / 4;
            }
            Debug.Log($"[APL] Crowd: ~{people} fans in {Chunks.Count} chunks");
            Chunks.Clear();
        }

        /// <summary>Seat rows for under the fans: a riser, the seat and its back, one row per tile.</summary>
        private static Texture2D Seats(Color home) =>
            Tex($"seats_{ColorUtility.ToHtmlStringRGB(home)}", 256, 16, (x, y) =>
            {
                var seat = Color.Lerp(new Color(0.14f, 0.16f, 0.22f), home, 0.3f);
                var ix = x % 8;
                if (y < 4) return new Color(0.2f, 0.2f, 0.23f);
                if (y >= 14 || ix == 0 || ix == 7) return new Color(0.07f, 0.07f, 0.09f);
                return y < 6 ? seat * 1.25f : seat * (0.9f + 0.1f * (y - 6) / 8f);
            });
    }
}
