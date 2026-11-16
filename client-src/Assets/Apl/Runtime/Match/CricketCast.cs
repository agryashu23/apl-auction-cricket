using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.Match
{
    /// <summary>
    /// The people on the field, made at the start of the match: each player
    /// gets a body that matches his skin tone (no two alike in a side), painted
    /// into his team's kit — shirt in the primary colour, sleeves in the
    /// secondary, darker trousers, white shoes — via the body's kit mask
    /// (Apl/KitBlit). Batters get helmet, pads and gloves; keepers pads and
    /// gloves; the umpire his own colours.
    /// </summary>
    public sealed partial class CricketCast
    {
        private readonly GroundRefs _refs;
        private readonly Transform _parent;
        private readonly Dictionary<(int, string), RenderTexture> _kits = new Dictionary<(int, string), RenderTexture>();

        /// <summary>Which Rocketbox body each spawned person is (the helmet is fitted per body).</summary>
        private readonly Dictionary<Animator, string> _modelOf = new Dictionary<Animator, string>();

        public CricketCast(GroundRefs refs)
        {
            _refs = refs;
            _parent = new GameObject("Cast").transform;
        }

        public bool Ready => _refs.CastModels.Length > 0 && _refs.KitBlit != null && _refs.Cricket != null;

        /// <summary>Kit colours: shirt, sleeves, trousers, shoes.</summary>
        public static Color[] KitOf(Franchise f)
        {
            var a = UiKit.ParseHex(f.PrimaryColor);
            var b = UiKit.ParseHex(f.SecondaryColor);
            // Trousers in the side's darker colour (navy for a yellow side, not olive).
            var dark = a.grayscale <= b.grayscale ? a : b;
            return new[] { a, b, Color.Lerp(dark, Color.black, 0.2f), new Color(0.93f, 0.93f, 0.93f) };
        }

        private static readonly Color[] UmpireKit =
        {
            // APL umpires: purple shirt, black trousers and shoes (and the black wide-brim hat).
            new Color(0.42f, 0.2f, 0.66f), new Color(0.36f, 0.16f, 0.58f), new Color(0.06f, 0.06f, 0.08f), new Color(0.06f, 0.06f, 0.07f),
        };

        /// <summary>
        /// Bodies for a side, one per player: the closest skin tone not yet
        /// used in the side (ties broken by the player's id so it's stable).
        /// </summary>
        public Dictionary<string, Animator> Side(IEnumerable<Player> players, Franchise team, string label)
        {
            var used = new HashSet<int>();
            var numbers = new HashSet<int>();
            var people = new Dictionary<string, Animator>();
            foreach (var p in StableOrder(players))
            {
                var model = PickModel(p, used);
                used.Add(model);
                var kit = KitOf(team);
                var body = Spawn(model, kit, $"{label}_{p.Id}", Tint(ToneOf(p), _refs.CastTones[model]));
                Looks(body, p);
                Face(body, model, p);
                people[p.Id] = body;
                _teamOf[body] = team;
                KitLettering.Apply(body, _refs.CastModels[model].name, KitLettering.Surname(p.Name), SquadNumber(p, numbers), Lettering(kit));
            }
            // The fielding side: caps and sun hats on some (the keeper's own headgear replaces his).
            if (label == "Field") FieldHeadwear(people, team);
            // A cap fitted now (bind pose) for the toss, interview and trophy, hidden until then.
            CeremonyCaps(people, team);
            return people;
        }

        /// <summary>A shirt number from the player's id (1–99, stable), the next free one if a team-mate has it.</summary>
        private static int SquadNumber(Player p, HashSet<int> taken)
        {
            var n = Mathf.Abs(p.Id.Aggregate(23, (h, c) => h * 31 + c)) % 99 + 1;
            while (!taken.Add(n)) n = n % 99 + 1;
            return n;
        }

        /// <summary>Lettering in the sleeve colour, or white / near-black when that wouldn't show on the shirt.</summary>
        private static Color Lettering(Color[] kit)
        {
            var shirt = kit[0].grayscale;
            // Broadcast numbers must read from the far end: sleeve colour only with strong contrast.
            var c = Mathf.Abs(kit[1].grayscale - shirt) > 0.42f ? kit[1] : shirt < 0.55f ? new Color(0.96f, 0.96f, 0.94f) : new Color(0.06f, 0.07f, 0.16f);
            return c * 0.9f + Color.black * 0.1f;
        }

        public Animator Umpire()
        {
            // The umpire: an older-looking body in his own colours.
            var index = System.Array.FindIndex(_refs.CastModels, m => m != null && m.name == "Male_Adult_04");
            var umpire = Spawn(index >= 0 ? index : Allowed(_refs.CastModels.Length - 1), UmpireKit, "Umpire");
            // A cream wide-brim sun hat with a purple band (matte cloth, as a real umpire's).
            var cream = Mat(new Color(0.93f, 0.9f, 0.8f), 0.1f);
            Wear(umpire, HatGear, HatSet, new[] { cream, cream, Mat(new Color(0.36f, 0.16f, 0.58f), 0.15f) });
            return umpire;
        }

        /// <summary>Any body in a side's kit (the line-up test).</summary>
        public Animator Model(int index, Franchise team) => Spawn(Allowed(index), KitOf(team), "Model_" + index);

        public int Models => _refs.CastModels.Length;

        private static int ToneOf(Player p) => Portraits.Looks.TryGetValue(p.Id, out var look) ? Mathf.Clamp(look.skin, 1, 6) : 3;

        // The players' six skin tones (as the portraits paint them).
        private static readonly Color[] Tones =
        {
            new Color(0.96f, 0.82f, 0.72f), new Color(0.91f, 0.73f, 0.60f), new Color(0.80f, 0.60f, 0.45f),
            new Color(0.68f, 0.48f, 0.33f), new Color(0.50f, 0.33f, 0.22f), new Color(0.36f, 0.23f, 0.15f),
        };

        /// <summary>Multiplier that takes a body's own skin tone to the player's.</summary>
        private static Color Tint(int player, int body)
        {
            var a = Tones[player - 1];
            var b = Tones[Mathf.Clamp(body, 1, 6) - 1];
            // Down to 0.33: the dark-skinned bodies all wear jackets (NotPlayers), so tone 5–6 players are tinted from tone-3 bodies.
            return new Color(Mathf.Clamp(a.r / b.r, 0.33f, 1.4f), Mathf.Clamp(a.g / b.g, 0.33f, 1.4f), Mathf.Clamp(a.b / b.b, 0.33f, 1.4f));
        }

        /// <summary>
        /// Bodies never used for anyone on the field, at the toss or as presenter
        /// (line-up review: build/gear/lineup_heads.png, tools/gear/lineup.py):
        /// Male_Adult_02 — holes in its kit mask; Male_Adult_09 — long hair in a
        /// ponytail and Sports_Male_03 — shoulder-length curls (both read as women
        /// in kit); Wood_Male_01 and Male_Adult_17 — a baseball cap modelled into
        /// the head; Male_Adult_18 — a hoodie hood up. Their hair / caps / hoods are
        /// part of the head mesh, so there's nothing to swap.
        /// </summary>
        public static readonly HashSet<string> Dropped = new HashSet<string>
        {
            "Male_Adult_02", "Male_Adult_09", "Sports_Male_03", "Wood_Male_01", "Male_Adult_17", "Male_Adult_18",
            // A bare torso: the kit painted over it reads as a muscled body in blue paint, not a shirt.
            "Sports_Male_01",
        };

        /// <summary>
        /// Bodies kept for the presenter and the umpire but never dressed as a player:
        /// their clothes are modelled into the mesh and show through any kit
        /// (round 8, build/gear/lineup_cast_round5.png) — blazers with lapels and
        /// pockets (Male_Adult_03, Male_Adult_07, the Business suits), a waistcoat
        /// (Business_Male_04), a body-warmer (Male_Adult_05), a denim jacket
        /// (Male_Adult_12), an argyle cardigan (Male_Adult_13), a hooded jacket
        /// (Male_Adult_04), a tie (Business_Male_07). What's left are polos, T-shirts,
        /// a vest, a football top, a track top and a henley: nine bodies (a side of
        /// eleven shares one, told apart by the head bake, CricketCast.Heads).
        /// </summary>
        public static readonly HashSet<string> NotPlayers = new HashSet<string>
        {
            "Male_Adult_03", "Male_Adult_07", "Male_Adult_05", "Male_Adult_12", "Male_Adult_13", "Male_Adult_04",
            "Business_Male_02", "Business_Male_04", "Business_Male_05", "Business_Male_07",
            // Round 9 (rating r8 `324`): a collared work shirt with a belt still read as office wear.
            "Male_Adult_14",
            // Round 11 (r9/r10 `06`, `08`-`14`): the sleeveless hoodie (pouch pocket, hood, drawstrings) read
            // as streetwear on the toss captains — and on everyone else.
            "Male_Adult_20",
        };

        private bool Playable(int i) => _refs.CastModels[i] != null && !Dropped.Contains(_refs.CastModels[i].name) && !NotPlayers.Contains(_refs.CastModels[i].name);

        /// <summary>The body at <paramref name="index"/>, or the next one round that isn't dropped.</summary>
        private int Allowed(int index)
        {
            var n = _refs.CastModels.Length;
            for (var k = 0; k < n; k++)
            {
                var i = ((index + k) % n + n) % n;
                if (_refs.CastModels[i] != null && !Dropped.Contains(_refs.CastModels[i].name)) return i;
            }
            return index;
        }

        private int PickModel(Player p, HashSet<int> used)
        {
            var tone = ToneOf(p);
            var seed = p.Id.Aggregate(17, (h, c) => h * 31 + c);
            var best = -1;
            var bestScore = float.MaxValue;
            // A side bigger than the playable bodies (a squad of 12+) re-uses one: unused ones first.
            for (var pass = 0; pass < 2 && best < 0; pass++)
            for (var i = 0; i < _refs.CastModels.Length; i++)
            {
                if ((pass == 0 && used.Contains(i)) || !Playable(i)) continue;
                // Near in tone (the tint covers the rest), then spread by the player's id for variety.
                var score = Mathf.Abs(_refs.CastTones[i] - tone) * 2 + Mathf.Abs((seed ^ (i * 7919)) % 97) / 97f * 3 + HairScore(p, i);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best < 0 ? Allowed(Mathf.Abs(seed) % _refs.CastModels.Length) : best;
        }

        private Animator Spawn(int model, Color[] kit, string name, Color? skin = null)
        {
            var animator = SpawnFrom(_refs.CastModels[model], _refs.CastKits[model], model, kit, name, skin);
            _modelOf[animator] = _refs.CastModels[model].name;
            return animator;
        }

        /// <summary>
        /// Any body painted in a kit through its mask; <paramref name="key"/> keys the
        /// shared kit textures (cast index; cheerleaders use negative keys).
        /// </summary>
        private Animator SpawnFrom(GameObject prefab, Texture2D? mask, int key, Color[] kit, string name, Color? skin = null)
        {
            var model = key;
            var tint = skin ?? Color.white;
            var go = Object.Instantiate(prefab, _parent);
            go.name = name;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.runtimeAnimatorController = _refs.Cricket;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.quality = SkinQuality.Bone2;
                var mats = r.materials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var matName = mats[i].name.ToLowerInvariant();
                    if (matName.Contains("head"))
                    {
                        mats[i].SetColor("_BaseColor", tint);
                        continue;
                    }
                    if (!matName.Contains("body")) continue;
                    var body = mats[i].HasProperty("_BaseMap") ? mats[i].GetTexture("_BaseMap") : mats[i].mainTexture;
                    if (body == null || mask == null) continue;
                    mats[i].SetTexture("_BaseMap", Kit(model, body, mask, kit, tint));
                }
                r.materials = mats;
            }
            return animator;
        }

        /// <summary>The body texture in a kit (one per body per side, shared).</summary>
        private RenderTexture Kit(int model, Texture body, Texture2D mask, Color[] kit, Color tint)
        {
            var key = (model, string.Join(",", kit.Select(c => ColorUtility.ToHtmlStringRGB(c))) + ColorUtility.ToHtmlStringRGB(tint));
            if (_kits.TryGetValue(key, out var rt)) return rt;
            rt = new RenderTexture(body.width, body.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Repeat,
                name = "Kit_" + model,
            };
            var m = _refs.KitBlit!;
            m.SetTexture("_Mask", mask);
            m.SetColor("_Shirt", kit[0]);
            m.SetColor("_Sleeve", kit[1]);
            m.SetColor("_Trousers", kit[2]);
            m.SetColor("_Shoes", kit[3]);
            m.SetColor("_SkinTint", tint);
            Graphics.Blit(body, rt, m);
            _kits[key] = rt;
            return rt;
        }

        // ------------------------------------------------------------------ gear

        private Material? _white, _metal, _pads;

        /// <summary>Pad canes: white with vertical ridges and a soft shadow in each groove.</summary>
        private Material PadMaterial()
        {
            if (_pads != null) return _pads;
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "pad_canes" };
            var px = new Color[64 * 64];
            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var ridge = 0.78f + 0.22f * Mathf.Pow(Mathf.Abs(Mathf.Sin(x / 64f * Mathf.PI * 5)), 0.5f);
                px[y * 64 + x] = new Color(0.97f, 0.97f, 0.95f) * ridge;
            }
            tex.SetPixels(px);
            tex.Apply(true);
            _pads = Mat(Color.white, 0.25f);
            _pads.SetTexture("_BaseMap", tex);
            return _pads;
        }
        private readonly Dictionary<string, Material> _helmets = new Dictionary<string, Material>();

        private Material Mat(Color c, float smooth)
        {
            var m = new Material(_refs.LitTemplate);
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        /// <summary>Helmet (team colour, dark grille), pads and gloves.</summary>
        public void BatterGear(Animator a, Franchise team)
        {
            var slots = GearSlots(team);
            // The user's own batters in what the locker has equipped (audio-modes: Modes/Locker.cs); others as before.
            Apl.Client.Modes.Locker.Dress(a, team);
            if (Wear(a, HelmetGear, HelmetSet, Apl.Client.Modes.Locker.Helmet(team, slots)) && Wear(a, PadGear, PadSet, Apl.Client.Modes.Locker.Pads(team, slots))) return;
            if (!_helmets.TryGetValue(team.Id, out var helmet))
            {
                helmet = Mat(Color.Lerp(UiKit.ParseHex(team.PrimaryColor), Color.black, 0.2f), 0.6f);
                _helmets[team.Id] = helmet;
            }
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) Helmet(a, head, helmet);
            Pads(a);
            Gloves(a);
        }

        /// <summary>
        /// A helmet built round the head: a shell of quads with the face left open
        /// (team colour), the peak a fan off the brow, and the grille — curved bars
        /// across the opening with two uprights — in dark metal.
        /// </summary>
        private void Helmet(Animator a, Transform head, Material shell)
        {
            var fwd = Vector3.ProjectOnPlane(a.transform.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            var c = head.position + Vector3.up * 0.085f + fwd * 0.012f;
            var r = new Vector3(0.112f, 0.122f, 0.132f);
            Vector3 P(float lat, float lon, float grow = 0) => c
                + right * ((r.x + grow) * Mathf.Sin(lat) * Mathf.Sin(lon))
                + Vector3.up * ((r.y + grow) * Mathf.Cos(lat))
                + fwd * ((r.z + grow) * Mathf.Sin(lat) * Mathf.Cos(lon));
            var kit = new MeshKit(2);
            const int rows = 11, cols = 24;
            const float latMax = 108f * Mathf.Deg2Rad;
            for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
            {
                var lat0 = latMax * i / rows;
                var lat1 = latMax * (i + 1) / rows;
                var lon0 = Mathf.PI * 2 * j / cols;
                var lon1 = Mathf.PI * 2 * (j + 1) / cols;
                var mid = Mathf.DeltaAngle(0, (lon0 + lon1) / 2 * Mathf.Rad2Deg);
                // The face: open below the brow, across the front.
                if ((lat0 + lat1) / 2 > 56f * Mathf.Deg2Rad && Mathf.Abs(mid) < 68f) continue;
                kit.Quad(0, P(lat1, lon1), P(lat0, lon1), P(lat0, lon0), P(lat1, lon0));
            }
            // The peak: a fan off the brow, out and a little down (both faces).
            const float brow = 58f * Mathf.Deg2Rad;
            Vector3 Out(float lon) => P(brow, lon) + (right * Mathf.Sin(lon) + fwd * Mathf.Cos(lon)) * 0.065f - Vector3.up * 0.018f;
            for (var j = -5; j < 5; j++)
            {
                var lon0 = 14f * j * Mathf.Deg2Rad;
                var lon1 = 14f * (j + 1) * Mathf.Deg2Rad;
                kit.Quad(0, P(brow, lon1), Out(lon1), Out(lon0), P(brow, lon0));
                kit.Quad(0, P(brow, lon0), Out(lon0), Out(lon1), P(brow, lon1));
            }
            // The grille, a little proud of the shell.
            Vector3 Bar(float lat, float lon) => P(lat, lon, 0.022f);
            foreach (var deg in new[] { 70f, 82f, 94f, 106f })
            {
                var lat = deg * Mathf.Deg2Rad;
                for (var j = -6; j < 6; j++) Strip(kit, Bar(lat, 11f * j * Mathf.Deg2Rad), Bar(lat, 11f * (j + 1) * Mathf.Deg2Rad), Vector3.up * 0.004f);
            }
            foreach (var deg in new[] { -22f, 22f })
            {
                var lon = deg * Mathf.Deg2Rad;
                for (var lat = 62f; lat < 108f; lat += 8f) Strip(kit, Bar(lat * Mathf.Deg2Rad, lon), Bar((lat + 8f) * Mathf.Deg2Rad, lon), right * 0.004f);
            }
            var go = kit.Spawn("Helmet", null, shell, _metal ??= Mat(new Color(0.12f, 0.12f, 0.14f), 0.6f));
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            go.transform.SetParent(head, true);
        }

        /// <summary>A thin ribbon between two points, both faces (grille bars).</summary>
        private static void Strip(MeshKit kit, Vector3 a, Vector3 b, Vector3 half)
        {
            kit.Quad(1, a - half, a + half, b + half, b - half);
            kit.Quad(1, b - half, b + half, a + half, a - half);
        }

        /// <summary>Keeping pads and the big webbed gloves (trims in the fielding side's colour).</summary>
        public void KeeperGear(Animator a, Franchise? team = null)
        {
            // His own headgear (KeeperHeadgear picks per over), not the fielder's cap or hat.
            foreach (var r in Groups(a, CapGear, HatGear))
            {
                r.name = "Gear_off";
                Object.Destroy(r.gameObject);
            }
            var slots = GearSlots(team);
            if (Wear(a, PadGear, KeeperSet, slots))
            {
                Wear(a, HelmetGear, HelmetSet, slots);
                if (Capped(a)) Wear(a, CapGear, CapSet, CapSlots(team));
                KeeperHeadgear(a, false);
                return;
            }
            Pads(a);
            Gloves(a, 1.25f);
        }

        /// <summary>
        /// Pads: a curved shell down the front of each shin (canes running
        /// vertically), wider at the knee, with three knee rolls above it.
        /// </summary>
        private void Pads(Animator a)
        {
            var fwd = Vector3.ProjectOnPlane(a.transform.forward, Vector3.up).normalized;
            foreach (var (knee, ankle) in new[] { (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot) })
            {
                var k = a.GetBoneTransform(knee);
                var f = a.GetBoneTransform(ankle);
                if (k == null || f == null) continue;
                var axis = (k.position - f.position).normalized;
                var len = Vector3.Distance(k.position, f.position);
                var front = Vector3.ProjectOnPlane(fwd, axis).normalized;
                var side = Vector3.Cross(axis, front).normalized;
                var foot = f.position + axis * 0.03f + front * 0.01f;
                // Up the shin, then the knee rolls: (height, side radius, depth radius).
                var rows = new (float s, float rs, float rd)[]
                {
                    (0f, 0.072f, 0.06f), (len * 0.35f, 0.08f, 0.066f), (len * 0.7f, 0.088f, 0.072f), (len * 0.97f, 0.092f, 0.076f),
                    (len + 0.005f, 0.1f, 0.084f), (len + 0.035f, 0.088f, 0.072f), (len + 0.06f, 0.1f, 0.084f),
                    (len + 0.09f, 0.088f, 0.072f), (len + 0.115f, 0.098f, 0.082f), (len + 0.145f, 0.07f, 0.058f),
                };
                const int segs = 10;
                const float sweep = 200f * Mathf.Deg2Rad;
                Vector3 P(int ri, int j)
                {
                    var (s, rs, rd) = rows[ri];
                    var phi = -sweep / 2 + sweep * j / segs;
                    return foot + axis * s + side * (rs * Mathf.Sin(phi)) + front * (rd * Mathf.Cos(phi));
                }
                var kit = new MeshKit();
                for (var ri = 0; ri < rows.Length - 1; ri++)
                for (var j = 0; j < segs; j++)
                {
                    kit.Quad(0, P(ri, j + 1), P(ri + 1, j + 1), P(ri + 1, j), P(ri, j), new Vector2(2.2f * j / segs, 0), new Vector2(2.2f * (j + 1) / segs, 1));
                }
                var go = kit.Spawn("Pad", null, PadMaterial());
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
                go.transform.SetParent(k, true);
            }
        }

        /// <summary>Gloves: a padded back and padded fingers over each hand, and the thumb.</summary>
        private void Gloves(Animator a, float scale = 1f)
        {
            var white = _white ??= Mat(new Color(0.95f, 0.95f, 0.93f), 0.3f);
            foreach (var (bone, thumbBone, middleBone, left) in new[]
            {
                (HumanBodyBones.LeftHand, HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftMiddleProximal, true),
                (HumanBodyBones.RightHand, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightMiddleProximal, false),
            })
            {
                var h = a.GetBoneTransform(bone);
                if (h == null) continue;
                var middle = a.GetBoneTransform(middleBone);
                var fingers = (middle != null ? middle.position - h.position : h.childCount > 0 ? h.GetChild(0).position - h.position : a.transform.forward).normalized;
                var t = a.GetBoneTransform(thumbBone);
                var thumb = t != null ? (t.position - h.position).normalized : Vector3.Cross(fingers, Vector3.up).normalized;
                // The back of the hand is perpendicular to the fingers and the thumb (the other way for the right hand).
                var back = Vector3.Cross(fingers, thumb).normalized * (left ? 1 : -1);
                Piece(PrimitiveType.Cube, h, h.position + fingers * 0.045f + back * 0.014f, new Vector3(0.09f, 0.032f, 0.085f) * scale, fingers, back, white);
                Piece(PrimitiveType.Cube, h, h.position + fingers * 0.115f + back * 0.012f, new Vector3(0.088f, 0.03f, 0.065f) * scale, fingers, back, white);
                if (t != null) Piece(PrimitiveType.Capsule, h, t.position + thumb * 0.025f, new Vector3(0.028f, 0.03f, 0.028f) * scale, back, thumb, white);
            }
        }

        private static void Piece(PrimitiveType type, Transform bone, Vector3 at, Vector3 size, Vector3 forward, Vector3 up, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Gear";
            var f = Vector3.ProjectOnPlane(forward, up);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f : Vector3.forward, up));
            go.transform.localScale = size;
            go.transform.SetParent(bone, true);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
        }
    }
}
