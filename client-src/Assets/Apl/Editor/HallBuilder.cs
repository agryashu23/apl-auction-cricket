using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Client.Audio;
using Apl.Client.Hall;
using Apl.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Hall.unity from code: stage (LED wall, podium,
    /// player box, auctioneer, assistant), ten team tables with seated
    /// Rocketbox people, paddles and flags, lights, and the camera shots.
    /// Re-run after changing layout constants; the scene is regenerated.
    /// </summary>
    public static class HallBuilder
    {
        public const string ScenePath = "Assets/Scenes/Hall.unity";
        private const string Rocketbox = "Assets/ThirdParty/Rocketbox";
        private const string Materials = "Assets/Apl/Generated/Materials";

        /// <summary>Rocketbox models face this yaw when un-rotated; corrected so people face where we say.</summary>
        public static float ModelYawFix = 0f;

        internal static readonly string[] TableCast =
        {
            "Business_Male_02", "Male_Adult_01", "Business_Female_02", "Male_Adult_02", "Business_Male_03",
            "Male_Adult_03", "Business_Female_03", "Male_Adult_04", "Business_Male_04", "Female_Adult_01",
            "Male_Adult_05", "Business_Male_05", "Business_Female_04", "Male_Adult_06", "Business_Male_06",
            "Male_Adult_07", "Female_Adult_02", "Business_Male_07", "Male_Adult_08",
        };

        // Two rows of five tables facing the stage (stage front edge at z ≈ 6.8).
        private static readonly Vector3[] TableSpots =
        {
            new Vector3(-8.4f, 0, 2.6f), new Vector3(-4.2f, 0, 2.6f), new Vector3(0, 0, 2.6f), new Vector3(4.2f, 0, 2.6f), new Vector3(8.4f, 0, 2.6f),
            new Vector3(-8.4f, 0, -2.6f), new Vector3(-4.2f, 0, -2.6f), new Vector3(0, 0, -2.6f), new Vector3(4.2f, 0, -2.6f), new Vector3(8.4f, 0, -2.6f),
        };

        [MenuItem("APL/Build Hall Scene")]
        public static void Build()
        {
            var data = Dataset.FromJson(File.ReadAllText("Assets/Resources/Data/apl_dataset.json"));
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildRoom(data);
            var cast = BuildStage();
            var camera = BuildCamera(out var hallCamera);

            // Human's team front and centre; the rest fill the remaining spots.
            const string human = "MBH";
            var order = data.Franchises.OrderBy(f => f.Id == human ? 0 : 1).ToList();
            var spotOrder = new[] { 2, 1, 3, 0, 4, 7, 6, 8, 5, 9 };
            var castIndex = 0;
            for (var i = 0; i < order.Count && i < TableSpots.Length; i++)
            {
                var f = order[i];
                var spot = TableSpots[spotOrder[i]];
                BuildTable(f, spot, ref castIndex);
                hallCamera.Shots.Add(new Shot
                {
                    Name = "table_" + f.Id,
                    // From the stage side, high: the paddle bidder (far seat, facing the stage)
                    // sits mid-frame with room above for the raised paddle (face ~1.7-2.1 m);
                    // the backs of the near-side owners and the name plate drop under the HUD
                    // band, and a neighbour's floor ring stays out of frame (16:9 and 2.2:1).
                    Position = spot + new Vector3(0, 2.4f, 2.8f),
                    LookAt = spot + new Vector3(0, 1.3f, -0.6f),
                    FieldOfView = 40,
                });
            }

            BuildLights();
            HallDecor.PostProcessing(camera);
            var director = BuildDirector(cast, hallCamera);
            director.Fx = BuildStageFx(data);
            BuildCeremony(director, hallCamera);
            camera.transform.position = new Vector3(0, 7.5f, -10);
            camera.transform.LookAt(new Vector3(0, 1, 5));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[APL] Hall scene built");
        }

        // ------------------------------------------------------------------ room

        private static void BuildRoom(Dataset data)
        {
            var carpet = HallDecor.Carpet();
            var panel = HallDecor.WallPanel();
            var floor = Block("Floor", new Vector3(0, -0.05f, 2), new Vector3(40, 0.1f, 34), Mat("Carpet", Color.white, smooth: 0.12f, texture: carpet, tiling: new Vector2(18, 15)));
            floor.isStatic = true;
            Block("Ceiling", new Vector3(0, 10, 2), new Vector3(40, 0.2f, 34), Mat("Ceiling", new Color(0.07f, 0.07f, 0.12f), smooth: 0f));

            // Panelled walls (one panel ≈ 2.4 m wide, 3.2 m tall).
            var sideWall = Mat("WallSide", Color.white, smooth: 0.15f, texture: panel, tiling: new Vector2(14, 3));
            var endWall = Mat("WallEnd", Color.white, smooth: 0.15f, texture: panel, tiling: new Vector2(16, 3));
            Block("BackWall", new Vector3(0, 5, -14), new Vector3(40, 10, 0.3f), endWall);
            Block("LeftWall", new Vector3(-17, 5, 2), new Vector3(0.3f, 10, 34), sideWall);
            Block("RightWall", new Vector3(17, 5, 2), new Vector3(0.3f, 10, 34), sideWall);
            Block("StageWall", new Vector3(0, 5, 12), new Vector3(40, 10, 0.3f), endWall);

            // Dark wood wainscot with a gold cap rail; gold crown strip at the top.
            var wood = Mat("Wainscot", new Color(0.2f, 0.11f, 0.06f), smooth: 0.45f);
            var gold = Mat("WallTrim", new Color(0.95f, 0.72f, 0.3f), emission: 1.2f);
            foreach (var side in new[] { -1f, 1f })
            {
                Block("Wainscot", new Vector3(side * 16.82f, 0.6f, 2), new Vector3(0.06f, 1.2f, 34), wood);
                Block("CapRail", new Vector3(side * 16.78f, 1.22f, 2), new Vector3(0.06f, 0.05f, 34), gold);
                Block("Crown", new Vector3(side * 16.8f, 9.55f, 2), new Vector3(0.05f, 0.08f, 34), gold);
            }
            Block("BackWainscot", new Vector3(0, 0.6f, -13.82f), new Vector3(40, 1.2f, 0.06f), wood);
            Block("BackCapRail", new Vector3(0, 1.22f, -13.78f), new Vector3(40, 0.05f, 0.06f), gold);

            // Blue LED strips between the team banners.
            var led = Mat("LedStrip", new Color(0.25f, 0.55f, 1f), emission: 2.6f);
            foreach (var side in new[] { -1f, 1f })
            {
                foreach (var z in new[] { -11.3f, -6.7f, -2.1f, 2.5f, 7.1f, 10.6f })
                {
                    Block("LedStrip", new Vector3(side * 16.78f, 5.4f, z), new Vector3(0.04f, 7.6f, 0.07f), led);
                }
            }

            // Ceiling downlights (warm, glowing) over the tables.
            var downlight = Mat("Downlight", new Color(1f, 0.86f, 0.62f), emission: 3.2f);
            for (var x = -12f; x <= 12f; x += 4.8f)
            {
                for (var z = -10f; z <= 6f; z += 4f)
                {
                    Cylinder("Downlight", new Vector3(x, 9.88f, z), new Vector3(0.42f, 0.02f, 0.42f), downlight);
                }
            }

            // Two lighting trusses over the stage front, each with a row of lamp cans.
            var metal = Mat("Truss", new Color(0.12f, 0.12f, 0.14f), metal: 0.8f, smooth: 0.6f);
            var lamp = Mat("LampFace", new Color(1f, 0.9f, 0.7f), emission: 4f);
            foreach (var z in new[] { 5.2f, 8.2f })
            {
                Block("Truss", new Vector3(0, 8.6f, z), new Vector3(16, 0.22f, 0.22f), metal);
                for (var x = -7f; x <= 7f; x += 2f)
                {
                    var can = Cylinder("LampCan", new Vector3(x, 8.32f, z), new Vector3(0.24f, 0.16f, 0.24f), metal);
                    Cylinder("LampFace", new Vector3(x, 8.155f, z), new Vector3(0.2f, 0.01f, 0.2f), lamp);
                }
            }
            BuildWallBanners(data, gold);
        }

        /// <summary>
        /// Side walls: a tall banner per franchise (colour, crest, short name)
        /// in a gold frame, with warm uplight strips along the floor.
        /// </summary>
        private static void BuildWallBanners(Dataset data, Material trim)
        {
            var root = new GameObject("WallBanners").transform;
            var up = Mat("Uplight", new Color(1f, 0.62f, 0.25f), emission: 1.8f);
            for (var i = 0; i < data.Franchises.Count; i++)
            {
                var f = data.Franchises[i];
                var side = i < 5 ? -1f : 1f;
                var z = -9f + (i % 5) * 4.6f;
                var x = side * 16.8f;
                var color = HallHud.ParseHex(f.PrimaryColor);
                Block("BannerFrame_" + f.Id, new Vector3(x - side * 0.02f, 5.2f, z), new Vector3(0.03f, 4.5f, 2.3f), trim, root);
                Block("Banner_" + f.Id, new Vector3(x - side * 0.05f, 5.2f, z), new Vector3(0.03f, 4.3f, 2.1f), Mat("Banner_" + f.Id, color * 0.85f, emission: 0.35f, smooth: 0.2f), root);
                var crest = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Resources/Crests/{f.Id}.png");
                if (crest != null)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "BannerCrest_" + f.Id;
                    q.transform.SetParent(root);
                    q.transform.position = new Vector3(x - side * 0.08f, 5.8f, z);
                    q.transform.rotation = Quaternion.Euler(0, side * 90f, 0);
                    q.transform.localScale = new Vector3(1.7f, 1.7f, 1);
                    q.GetComponent<Renderer>().sharedMaterial = Mat("BannerCrest_" + f.Id, Color.white, emission: 0.6f, texture: crest, transparent: true);
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                }
                SignText(f.ShortName, new Vector3(x - side * 0.08f, 4.2f, z), side * 90f, root, 0.07f);
            }
            foreach (var side in new[] { -1f, 1f })
            {
                Block("Uplight", new Vector3(side * 16.75f, 0.05f, 2), new Vector3(0.12f, 0.06f, 33), up, root);
            }
        }

        private static StageCast BuildStage()
        {
            var stage = new GameObject("Stage");
            Block("Platform", new Vector3(0, 0.3f, 9.2f), new Vector3(14, 0.6f, 5), Mat("StageFloor", new Color(0.12f, 0.12f, 0.16f), smooth: 0.6f), stage.transform);
            Block("PlatformEdge", new Vector3(0, 0.56f, 6.72f), new Vector3(14, 0.06f, 0.06f), Mat("StageEdge", new Color(1f, 0.75f, 0.3f), emission: 2.2f), stage.transform);

            // LED wall with the league logo.
            var led = Mat("LedWall", new Color(0.55f, 0.03f, 0.06f), emission: 1.4f);
            Block("LedWall", new Vector3(0, 3.4f, 11.6f), new Vector3(15, 5.8f, 0.1f), led, stage.transform);
            var logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Apl/Art/apl_logo.png");
            if (logoTex != null)
            {
                var logo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                logo.name = "Logo";
                logo.transform.SetParent(stage.transform);
                logo.transform.position = new Vector3(0, 3.9f, 11.5f);
                logo.transform.rotation = Quaternion.identity;
                logo.transform.localScale = new Vector3(3.6f, 3.6f, 1);
                var m = Mat("Logo", Color.white, emission: 1.2f, texture: logoTex, transparent: true);
                logo.GetComponent<Renderer>().sharedMaterial = m;
                Object.DestroyImmediate(logo.GetComponent<Collider>());
            }
            var coverTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Apl/Art/led_cover.png");
            if (coverTex != null)
            {
                var cover = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cover.name = "LedCover";
                cover.transform.SetParent(stage.transform);
                cover.transform.position = new Vector3(0, 3.4f, 11.54f);
                cover.transform.localScale = new Vector3(14.8f, 5.72f, 1);
                cover.GetComponent<Renderer>().sharedMaterial = Mat("LedCover", Color.white, emission: 1.15f, texture: coverTex);
                Object.DestroyImmediate(cover.GetComponent<Collider>());
                var logoGo = GameObject.Find("Logo");
                if (logoGo != null) logoGo.GetComponent<Renderer>().enabled = false;
            }

            // Podium with a gold front; the gavel block sits on top.
            var gold = Mat("Gold", new Color(0.95f, 0.72f, 0.28f), metal: 0.8f, smooth: 0.7f);
            var wood = Mat("Wood", new Color(0.33f, 0.19f, 0.1f), smooth: 0.35f);
            Block("Podium", new Vector3(-2.2f, 1.18f, 8.5f), new Vector3(1.1f, 1.16f, 0.7f), wood, stage.transform);
            Block("PodiumFront", new Vector3(-2.2f, 1.22f, 8.14f), new Vector3(1.0f, 0.42f, 0.02f), gold, stage.transform);
            var strike = Block("GavelBlock", new Vector3(-2.0f, 1.79f, 8.5f), new Vector3(0.18f, 0.05f, 0.18f), wood, stage.transform);

            // The player box on a stand.
            // The player box sits right beside the podium; the assistant stands behind it.
            Block("BoxStand", new Vector3(-1.15f, 0.95f, 8.7f), new Vector3(0.45f, 0.7f, 0.45f), wood, stage.transform);
            var box = Block("PlayerBox", new Vector3(-1.15f, 1.55f, 8.7f), new Vector3(0.62f, 0.5f, 0.62f), gold, stage.transform);

            var cast = stage.AddComponent<StageCast>();
            cast.Auctioneer = Person("Business_Male_01", new Vector3(-2.2f, 0.6f, 9.15f), 180, "Auctioneer", stage.transform);
            cast.Assistant = Person("Business_Female_01", new Vector3(-1.15f, 0.6f, 9.35f), 180, "Assistant", stage.transform);
            cast.GavelStrikeTarget = strike.transform;
            cast.ChitInBox = box.transform;

            // Gavel: handle along +z, head across it at the far end.
            var gavel = new GameObject("Gavel").transform;
            gavel.SetParent(stage.transform);
            var handle = Cylinder("GavelHandle", Vector3.zero, new Vector3(0.028f, 0.12f, 0.028f), wood, gavel);
            handle.transform.localPosition = new Vector3(0, 0, 0.1f);
            handle.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var head = Cylinder("GavelHead", Vector3.zero, new Vector3(0.07f, 0.075f, 0.07f), wood, gavel);
            head.transform.localPosition = new Vector3(0, 0, 0.22f);
            head.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var band = Cylinder("GavelBand", Vector3.zero, new Vector3(0.074f, 0.012f, 0.074f), gold, gavel);
            band.transform.localPosition = new Vector3(0, 0, 0.22f);
            band.transform.localRotation = Quaternion.Euler(0, 0, 90);
            cast.Gavel = gavel;

            // The player card the assistant carries over (APL crest on its face).
            var card = Block("PlayerCard", Vector3.zero, new Vector3(0.17f, 0.24f, 0.006f), Mat("Card", new Color(0.96f, 0.95f, 0.9f), smooth: 0.3f), stage.transform);
            if (logoTex != null)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    face.name = "CardFace";
                    face.transform.SetParent(card.transform, false);
                    face.transform.localPosition = new Vector3(0, 0.05f, side * 0.6f);
                    face.transform.localRotation = Quaternion.Euler(0, side > 0 ? 180 : 0, 0);
                    face.transform.localScale = new Vector3(0.8f, 0.55f, 1);
                    face.GetComponent<Renderer>().sharedMaterial = Mat("Logo", Color.white, emission: 1.2f, texture: logoTex, transparent: true);
                    Object.DestroyImmediate(face.GetComponent<Collider>());
                }
            }
            cast.Card = card.transform;
            var spot = new GameObject("HandoverSpot").transform;
            spot.SetParent(stage.transform);
            spot.position = new Vector3(-1.45f, 0.6f, 9.25f);
            cast.HandoverSpot = spot;
            return cast;
        }

        // ----------------------------------------------------------------- tables

        private static void BuildTable(Franchise f, Vector3 c, ref int castIndex)
        {
            var root = new GameObject("Table_" + f.Id);
            root.transform.position = c;
            var team = root.AddComponent<TeamTable>();
            team.TeamId = f.Id;
            var primary = HallHud.ParseHex(f.PrimaryColor);
            var secondary = HallHud.ParseHex(f.SecondaryColor);
            if (secondary.grayscale < 0.08f) secondary = new Color(0.95f, 0.78f, 0.3f);
            var cloth = Mat("Cloth_" + f.Id, primary, smooth: 0.25f);

            Cylinder("Top", c + new Vector3(0, 0.76f, 0), new Vector3(1.9f, 0.02f, 1.9f), cloth, root.transform);
            Cylinder("Skirt", c + new Vector3(0, 0.38f, 0), new Vector3(1.86f, 0.38f, 1.86f), Mat("Skirt_" + f.Id, primary * 0.7f, smooth: 0.2f), root.transform);
            var ring = Cylinder("Highlight", c + new Vector3(0, 0.01f, 0), new Vector3(3.4f, 0.005f, 3.4f), Mat("Highlight", new Color(1f, 0.78f, 0.3f), emission: 1.6f), root.transform);
            team.Highlight = ring.GetComponent<Renderer>();

            // Props: flag, bottles, laptop, name plate.
            var white = Mat("White", new Color(0.92f, 0.94f, 0.96f), smooth: 0.6f);
            Cylinder("FlagPole", c + new Vector3(-0.55f, 1.0f, 0.35f), new Vector3(0.02f, 0.24f, 0.02f), white, root.transform);
            Block("Flag", c + new Vector3(-0.42f, 1.16f, 0.35f), new Vector3(0.26f, 0.16f, 0.01f), Mat("Flag_" + f.Id, secondary, emission: 0.3f), root.transform);
            for (var b = 0; b < 3; b++)
            {
                Cylinder("Bottle", c + new Vector3(-0.5f + b * 0.5f, 0.88f, -0.55f), new Vector3(0.07f, 0.11f, 0.07f), Mat("Bottle", new Color(0.7f, 0.85f, 1f), smooth: 0.9f), root.transform);
            }
            // Team sign: an opaque board with the short name on both faces, so it
            // reads from the stage-side table shots and from the wide shot behind.
            var board = Block("Sign", c + new Vector3(0, 0.93f, 0.62f), new Vector3(0.62f, 0.3f, 0.03f), Mat("Sign_" + f.Id, primary * 0.55f, smooth: 0.2f), root.transform);
            SignText(f.ShortName, board.transform.position + new Vector3(0, 0, 0.02f), 180, root.transform);
            SignText(f.ShortName, board.transform.position - new Vector3(0, 0, 0.02f), 0, root.transform);

            // Chairs + people evenly round the whole table (360° / count), each
            // facing its centre: three to five of them, fixed per team (an
            // FNV-1a hash of its id) so the hall is the same every run.
            var chairMat = Mat("Chair", new Color(0.55f, 0.06f, 0.08f), smooth: 0.3f);
            var seed = 2166136261u;
            foreach (var ch in f.Id) seed = (seed ^ ch) * 16777619u;
            var count = 3 + (int)(seed % 3);
            const float radius = 1.25f;
            // Seat 0 is on the far side, facing the stage: it holds the paddle.
            // The laptop goes to one of the others.
            const int bidderSeat = 0;
            var laptopSeat = 1 + (int)(seed / 3 % (uint)(count - 1));
            for (var s = 0; s < count; s++)
            {
                var a = s * 360f / count;
                var rad = a * Mathf.Deg2Rad;
                // Offsets laid out for the far seat, turned round to this one.
                var around = Quaternion.Euler(0, -a, 0);
                var seat = c + new Vector3(Mathf.Sin(rad) * radius, 0, -Mathf.Cos(rad) * radius);
                var yaw = -a;
                var chair = new GameObject("Chair");
                chair.transform.SetParent(root.transform);
                chair.transform.position = seat;
                chair.transform.rotation = Quaternion.Euler(0, yaw, 0);
                Block("Seat", seat + chair.transform.rotation * new Vector3(0, 0.46f, -0.05f), new Vector3(0.52f, 0.1f, 0.5f), chairMat, chair.transform, chair.transform.rotation);
                Block("Back", seat + chair.transform.rotation * new Vector3(0, 0.85f, -0.3f), new Vector3(0.52f, 0.75f, 0.08f), chairMat, chair.transform, chair.transform.rotation);

                var model = TableCast[castIndex++ % TableCast.Length];
                var person = Person(model, seat, yaw, "Member", root.transform, "Seated");
                if (person == null) continue;
                var member = person.gameObject.AddComponent<SeatedMember>();
                team.Members.Add(member);
                if (s == bidderSeat)
                {
                    team.Bidder = member;
                    var rest = new GameObject("PaddleRest").transform;
                    rest.SetParent(root.transform);
                    rest.position = c + around * new Vector3(0.2f, 0.79f, -0.5f);
                    rest.rotation = Quaternion.Euler(90, yaw, 0);
                    member.PaddleRest = rest;
                    member.Paddle = Paddle(f, secondary, rest);
                    var rtmRest = new GameObject("RtmRest").transform;
                    rtmRest.SetParent(root.transform);
                    rtmRest.position = c + around * new Vector3(-0.3f, 0.785f, -0.45f);
                    rtmRest.rotation = Quaternion.Euler(90, yaw, 0);
                    member.RtmRest = rtmRest;
                    member.RtmCard = RtmCard(rtmRest);
                    var target = new GameObject("RaisedHand").transform;
                    target.SetParent(root.transform);
                    target.position = seat + around * new Vector3(0.28f, 1.62f, 0.2f);
                    target.rotation = Quaternion.Euler(-80, yaw, 0);
                    member.RaisedHandTarget = target;
                }
                if (s == laptopSeat)
                {
                    member.Keyboard = Laptop(root.transform, c, around);
                    team.Typist = member;
                }
            }
        }

        /// <summary>
        /// A laptop on the table in front of one seat: base towards the person,
        /// lid on the far side with a lit screen facing them. Returns the
        /// keyboard point (where the hands go when typing).
        /// </summary>
        private static Transform Laptop(Transform parent, Vector3 c, Quaternion around)
        {
            var body = Mat("Laptop", new Color(0.15f, 0.15f, 0.17f), metal: 0.6f, smooth: 0.6f);
            // Facing the seat (seats are on the -z side of a table).
            var facing = around * Quaternion.Euler(0, 180, 0);
            var basePos = c + around * new Vector3(0, 0.79f, -0.62f);
            Block("Laptop", basePos, new Vector3(0.36f, 0.02f, 0.25f), body, parent, facing);
            var hinge = c + around * new Vector3(0, 0.79f, -0.49f);
            var lidTilt = facing * Quaternion.Euler(-12, 0, 0);
            var lid = Block("LaptopLid", hinge + lidTilt * new Vector3(0, 0.125f, 0), new Vector3(0.36f, 0.25f, 0.015f), body, parent, lidTilt);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "LaptopScreen";
            screen.transform.SetParent(lid.transform, true);
            screen.transform.position = lid.transform.position + lidTilt * new Vector3(0, 0, 0.009f);
            screen.transform.rotation = lidTilt * Quaternion.Euler(0, 180, 0);
            screen.transform.localScale = new Vector3(0.9f, 0.88f, 1f);
            screen.GetComponent<Renderer>().sharedMaterial = Mat("LaptopScreen", new Color(0.55f, 0.72f, 0.95f), emission: 1.1f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            var keys = new GameObject("Keyboard").transform;
            keys.SetParent(parent);
            keys.position = c + around * new Vector3(0, 0.81f, -0.66f);
            keys.rotation = facing;
            return keys;
        }

        private static void SignText(string text, Vector3 position, float yaw, Transform parent, float characterSize = 0.018f)
        {
            var go = new GameObject("SignText", typeof(TextMesh));
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var tm = go.GetComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = characterSize;
            tm.fontSize = 96;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = SignTextMaterial(tm.font);
        }

        /// <summary>The font material with depth testing on, so the sign board hides its back face's text.</summary>
        private static Material SignTextMaterial(Font font)
        {
            var path = $"{Materials}/SignText.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(font.material);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Apl/Art/SignText.shader") ?? font.material.shader;
            m.mainTexture = font.material.mainTexture;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A gold "RTM" card; its root is the grip point, the card stands above it.</summary>
        /// <summary>
        /// The Right to Match card: a gold-edged navy card, the APL crest on the
        /// left and "RTM / RIGHT TO MATCH" on the right, the same on both faces.
        /// </summary>
        private static Transform RtmCard(Transform rest)
        {
            var card = new GameObject("RtmCard").transform;
            card.SetParent(rest, false);
            var body = Block("RtmBody", Vector3.zero, new Vector3(0.24f, 0.16f, 0.01f), Mat("RtmEdge", new Color(0.95f, 0.74f, 0.28f), metal: 0.6f, smooth: 0.7f, emission: 0.2f), card);
            body.transform.localPosition = new Vector3(0, 0.2f, 0);
            body.transform.localRotation = Quaternion.identity;
            var logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Apl/Art/apl_logo.png");
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var gold = new Color(0.98f, 0.8f, 0.36f);
            foreach (var side in new[] { -1f, 1f })
            {
                // Each face is laid out in its own frame, so both read left to right.
                var face = new GameObject("RtmFace").transform;
                face.SetParent(card, false);
                face.localPosition = new Vector3(0, 0.2f, side * 0.0056f);
                face.localRotation = Quaternion.Euler(0, side > 0 ? 180 : 0, 0);
                Quad("RtmPanel", face, new Vector3(0, 0, 0), new Vector2(0.226f, 0.146f), Mat("RtmFace", new Color(0.05f, 0.08f, 0.2f), smooth: 0.5f, emission: 0.15f));
                if (logoTex != null)
                {
                    Quad("RtmLogo", face, new Vector3(-0.058f, 0, -0.0004f), new Vector2(0.095f, 0.095f), Mat("Logo", Color.white, emission: 1.2f, texture: logoTex, transparent: true));
                }
                CardText(face, "RTM", new Vector3(0.042f, 0.014f, -0.0006f), 0.0062f, gold, font);
                CardText(face, "RIGHT TO MATCH", new Vector3(0.042f, -0.036f, -0.0006f), 0.0016f, Color.white, font);
            }
            return card;
        }

        private static void Quad(string name, Transform parent, Vector3 localPosition, Vector2 size, Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            q.transform.SetParent(parent, false);
            q.transform.localPosition = localPosition;
            q.transform.localRotation = Quaternion.identity;
            q.transform.localScale = new Vector3(size.x, size.y, 1);
            q.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(q.GetComponent<Collider>());
        }

        private static void CardText(Transform parent, string text, Vector3 localPosition, float characterSize, Color color, Font font)
        {
            var go = new GameObject("RtmText", typeof(TextMesh));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            var tm = go.GetComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = characterSize;
            tm.fontSize = 96;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = SignTextMaterial(font);
        }

        private static Transform Paddle(Franchise f, Color face, Transform rest)
        {
            var paddle = new GameObject("Paddle").transform;
            paddle.SetParent(rest, false);
            var handle = Cylinder("Handle", Vector3.zero, new Vector3(0.035f, 0.13f, 0.035f), Mat("PaddleHandle", new Color(0.4f, 0.25f, 0.13f)), paddle);
            handle.transform.localPosition = new Vector3(0, 0.13f, 0);
            handle.transform.localRotation = Quaternion.identity;
            var disc = Cylinder("Face", Vector3.zero, new Vector3(0.34f, 0.008f, 0.34f), Mat("Paddle_" + f.Id, face, emission: 0.15f), paddle);
            disc.transform.localPosition = new Vector3(0, 0.42f, 0);
            disc.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var crest = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Resources/Crests/{f.Id}.png");
            if (crest != null)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "PaddleCrest";
                    q.transform.SetParent(paddle, false);
                    q.transform.localPosition = new Vector3(0, 0.42f, side * 0.011f);
                    q.transform.localRotation = Quaternion.Euler(0, side > 0 ? 180 : 0, 0);
                    q.transform.localScale = new Vector3(0.27f, 0.27f, 1);
                    q.GetComponent<Renderer>().sharedMaterial = Mat("PaddleCrest_" + f.Id, Color.white, emission: 0.25f, texture: crest, transparent: true);
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                }
            }
            return paddle;
        }

        // ----------------------------------------------------------------- people

        internal static Animator? Person(string model, Vector3 position, float yaw, string name, Transform parent, string controller = "")
        {
            var path = $"{Rocketbox}/{model}/Export/{model}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[APL] Missing model {path}");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name + "_" + model;
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0, yaw + ModelYawFix, 0);
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            var ctrlName = controller.Length > 0 ? controller : name;
            animator.runtimeAnimatorController = AnimatorBuilder.Load(ctrlName);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.quality = SkinQuality.Bone2;
            }
            return animator;
        }

        // ---------------------------------------------------------- camera/lights

        private static Camera BuildCamera(out HallCamera hall)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.02f, 0.03f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 80f;
            hall = go.AddComponent<HallCamera>();
            // Shot grammar (framed with a pinhole check of the hall geometry at 16:9 and 2.2:1;
            // the HUD's bottom band is ~28% of the height, so subjects sit above it):
            //  wide       establishing: the whole LED wall and all ten tables, none cut at the edges;
            //  stage      the full LED wall (both purse boards whole) with auctioneer and assistant;
            //  auctioneer three-quarter medium from stage left, head in the top third, gavel in frame
            //             (the purse boards are hidden while it's on, so nothing behind him is sliced);
            //  table_X    the bidding team (above), cut to on every bid.
            hall.Shots.Add(new Shot { Name = "wide", Position = new Vector3(0, 7.5f, -13.4f), LookAt = new Vector3(0, -0.5f, 6f), FieldOfView = 58, FitWidth = true });
            // Stage: from between the table rows, aimed a little high so both boards' "PURSE LEFT" sit
            // under the top-corner chips and the auctioneer stays above the HUD card up to 2.2:1.
            // (Raised to 3.2 m so the front table's seated heads drop below the frame.)
            hall.Shots.Add(new Shot { Name = "stage", Position = new Vector3(0, 3.2f, 1.0f), LookAt = new Vector3(0, 2.6f, 11.5f), FieldOfView = 56, FitWidth = true });
            hall.Shots.Add(new Shot { Name = "card", Position = new Vector3(-0.7f, 2.0f, 5.9f), LookAt = new Vector3(-1.65f, 1.75f, 9.1f), FieldOfView = 36 });
            hall.Shots.Add(new Shot { Name = "auctioneer", Position = new Vector3(-3.5f, 2.4f, 6.8f), LookAt = new Vector3(-2.0f, 1.8f, 9.0f), FieldOfView = 32 });
            return cam;
        }

        private static void BuildLights()
        {
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.15f;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.56f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.36f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.13f, 0.12f);

            var sun = new GameObject("Key Light", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.05f;
            sun.color = new Color(1f, 0.92f, 0.85f);
            sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(48, 200, 0);

            void Spot(string name, Vector3 pos, Vector3 target, float intensity, float angle, Color color)
            {
                var l = new GameObject(name, typeof(Light)).GetComponent<Light>();
                l.type = LightType.Spot;
                l.transform.position = pos;
                l.transform.LookAt(target);
                l.intensity = intensity;
                l.range = 22;
                l.spotAngle = angle;
                l.color = color;
                l.shadows = LightShadows.None;
            }

            Spot("Stage Left", new Vector3(-5, 7.5f, 4), new Vector3(-1.5f, 1.2f, 9), 9f, 46, new Color(1f, 0.93f, 0.85f));
            Spot("Stage Right", new Vector3(5, 7.5f, 4), new Vector3(1.5f, 1.2f, 9), 9f, 46, new Color(1f, 0.93f, 0.85f));
            Spot("Tables Front", new Vector3(0, 9, 1), new Vector3(0, 0, 2.6f), 6f, 115, new Color(1f, 0.9f, 0.8f));
            Spot("Tables Back", new Vector3(0, 9, -4), new Vector3(0, 0, -2.6f), 6f, 115, new Color(1f, 0.9f, 0.8f));
            Spot("Wall Wash Left", new Vector3(-9, 9.2f, 0), new Vector3(-16.8f, 4, 0), 5f, 95, new Color(1f, 0.78f, 0.55f));
            Spot("Wall Wash Right", new Vector3(9, 9.2f, 0), new Vector3(16.8f, 4, 0), 5f, 95, new Color(1f, 0.78f, 0.55f));
            Spot("Wall Wash", new Vector3(0, 9, 4), new Vector3(0, 4, 11.6f), 3f, 70, new Color(1f, 0.85f, 0.85f));
        }

        private static HallDirector BuildDirector(StageCast cast, HallCamera hallCamera)
        {
            var go = new GameObject("Director");
            var director = go.AddComponent<HallDirector>();
            director.Camera = hallCamera;
            director.Cast = cast;
            director.Sfx = go.AddComponent<Sfx>();
            director.Auctioneer = new GameObject("AuctioneerVoice").AddComponent<VoiceQueue>();
            director.Auctioneer.transform.SetParent(go.transform);
            director.Assistant = new GameObject("AssistantVoice").AddComponent<VoiceQueue>();
            director.Assistant.transform.SetParent(go.transform);
            go.AddComponent<AplBoot>();
            return director;
        }

        /// <summary>Walk-on marks across the stage front, wings at both ends, two shots.</summary>
        private static void BuildCeremony(HallDirector director, HallCamera hallCamera)
        {
            var ceremony = director.gameObject.AddComponent<Ceremony>();
            ceremony.Camera = hallCamera;
            ceremony.Auctioneer = director.Auctioneer;
            ceremony.Sfx = director.Sfx;
            ceremony.LedWall = GameObject.Find("LedWall").GetComponent<Renderer>();
            ceremony.LedLogo = GameObject.Find("Logo").GetComponent<Renderer>();
            ceremony.StandingController = AnimatorBuilder.Load("Ceremony");
            ceremony.LedCover = GameObject.Find("LedCover")?.GetComponent<Renderer>();
            ceremony.Media = BuildMedia();
            ceremony.Fx = director.Fx;

            var root = new GameObject("CeremonyMarks").transform;
            Transform Point(string name, Vector3 p)
            {
                var t = new GameObject(name).transform;
                t.SetParent(root);
                t.position = p;
                return t;
            }
            ceremony.LeftWing = Point("LeftWing", new Vector3(-7.2f, 0.6f, 7.9f));
            ceremony.RightWing = Point("RightWing", new Vector3(7.2f, 0.6f, 7.9f));
            ceremony.Marks = new[]
            {
                Point("Mark1", new Vector3(-1.3f, 0.6f, 7.7f)),
                Point("Mark2", new Vector3(0f, 0.6f, 7.5f)),
                Point("Mark3", new Vector3(1.3f, 0.6f, 7.7f)),
            };
            director.Ceremony = ceremony;

            // Walk-on over the front tables (no seated head in the lens); the pose is the team
            // photo: everyone head to toe, feet on the stage, the crest whole above them.
            hallCamera.Shots.Add(new Shot { Name = "ceremony_wide", Position = new Vector3(0, 3.4f, 0.8f), LookAt = new Vector3(0, 2.2f, 9f), FieldOfView = 46, FitWidth = true });
            hallCamera.Shots.Add(new Shot { Name = "ceremony_hero", Position = new Vector3(0, 2.6f, 1.8f), LookAt = new Vector3(0, 2.4f, 8.0f), FieldOfView = 52, FitWidth = true });
        }

        /// <summary>Sweeping beams, edge chase lights, crest pillars, LED shine and confetti.</summary>
        private static StageFx BuildStageFx(Dataset data)
        {
            var root = new GameObject("StageFx");
            var fx = root.AddComponent<StageFx>();

            // Beams hang from the truss lamps and sweep over stage and hall.
            var cone = BeamCone();
            var beamShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Apl/Art/Beam.shader");
            float[] xs = { -6f, -3.6f, -1.2f, 1.2f, 3.6f, 6f };
            for (var i = 0; i < xs.Length; i++)
            {
                var pivot = new GameObject("Beam").transform;
                pivot.SetParent(root.transform);
                pivot.position = new Vector3(xs[i], 8.2f, i % 2 == 0 ? 5.2f : 8.2f);
                pivot.rotation = Quaternion.Euler(i % 2 == 0 ? 28f : -18f, 0, (i - 2.5f) * -6f);
                var mesh = new GameObject("Cone", typeof(MeshFilter), typeof(MeshRenderer));
                mesh.transform.SetParent(pivot, false);
                mesh.GetComponent<MeshFilter>().sharedMesh = cone;
                var r = mesh.GetComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (beamShader != null)
                {
                    var path = $"{Materials}/Beam.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null)
                    {
                        m = new Material(beamShader);
                        AssetDatabase.CreateAsset(m, path);
                    }
                    m.shader = beamShader;
                    m.SetColor("_Color", new Color(1f, 0.85f, 0.55f, 0.2f));
                    r.sharedMaterial = m;
                }
                fx.Beams.Add(pivot);
                fx.BeamRenderers.Add(r);
            }

            // Chase light segments along the stage front edge.
            var chase = Mat("Chase", new Color(1f, 0.78f, 0.35f), emission: 1f);
            for (var i = 0; i < 24; i++)
            {
                var x = -6.9f + i * (13.8f / 23f);
                fx.Chase.Add(Block("Chase", new Vector3(x, 0.565f, 6.68f), new Vector3(0.42f, 0.05f, 0.04f), chase, root.transform).GetComponent<Renderer>());
            }

            // LED pillars either side of the stage, cycling the crests.
            var frame = Mat("PillarFrame", new Color(0.05f, 0.05f, 0.08f), metal: 0.6f, smooth: 0.6f);
            foreach (var side in new[] { -1f, 1f })
            {
                Block("PillarBack", new Vector3(side * 8.7f, 3.4f, 10.9f), new Vector3(2.4f, 5.8f, 0.2f), frame, root.transform);
                var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                face.name = "PillarScreen";
                face.transform.SetParent(root.transform);
                face.transform.position = new Vector3(side * 8.7f, 3.6f, 10.78f);
                face.transform.localScale = new Vector3(2.1f, 2.1f, 1);
                var first = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Resources/Crests/{data.Franchises[0].Id}.png");
                face.GetComponent<Renderer>().sharedMaterial = Mat("PillarScreen", Color.white, emission: 1.1f, texture: first, transparent: true);
                Object.DestroyImmediate(face.GetComponent<Collider>());
                fx.Pillars.Add(face.GetComponent<Renderer>());
            }
            foreach (var f in data.Franchises)
            {
                var crest = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Resources/Crests/{f.Id}.png");
                if (crest != null) fx.Crests.Add(crest);
            }

            // A soft band of light sliding across the LED wall.
            if (beamShader != null)
            {
                var shine = GameObject.CreatePrimitive(PrimitiveType.Quad);
                shine.name = "LedShine";
                shine.transform.SetParent(root.transform);
                shine.transform.position = new Vector3(-8f, 3.4f, 11.5f);
                shine.transform.rotation = Quaternion.Euler(0, 0, 22f);
                shine.transform.localScale = new Vector3(0.9f, 7f, 1);
                var m = new Material(beamShader);
                m.SetColor("_Color", new Color(1f, 0.95f, 0.85f, 0.5f));
                AssetDatabase.CreateAsset(m, $"{Materials}/LedShine.mat");
                shine.GetComponent<Renderer>().sharedMaterial = m;
                Object.DestroyImmediate(shine.GetComponent<Collider>());
                fx.Shine = shine.transform;
            }

            fx.Confetti = BuildConfetti(root.transform);
            return fx;
        }

        /// <summary>An open cone along -Y from the origin: uv.y 0 at the lamp, 1 at the far end.</summary>
        private static Mesh BeamCone()
        {
            var path = "Assets/Apl/Art/Generated/beam_cone.asset";
            Directory.CreateDirectory("Assets/Apl/Art/Generated");
            AssetDatabase.DeleteAsset(path);
            const int seg = 24;
            const float length = 9.5f, r0 = 0.1f, r1 = 1.15f;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (var i = 0; i <= seg; i++)
            {
                var a = i / (float)seg * Mathf.PI * 2;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(dir * r0);
                verts.Add(dir * r1 + Vector3.down * length);
                normals.Add(dir);
                normals.Add(dir);
                uvs.Add(new Vector2(i / (float)seg, 0));
                uvs.Add(new Vector2(i / (float)seg, 1));
                if (i == seg) break;
                var k = i * 2;
                tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            var mesh = new Mesh { name = "BeamCone" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Confetti that tumbles down over the stage when <see cref="StageFx.Burst"/> fires.</summary>
        private static ParticleSystem BuildConfetti(Transform parent)
        {
            var go = new GameObject("Confetti", typeof(ParticleSystem));
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(0, 9.4f, 6.5f);
            go.transform.rotation = Quaternion.identity;
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = 0.12f;
            main.maxParticles = 800;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var colours = new Gradient();
            colours.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f), new GradientColorKey(Color.white, 0.33f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 0.66f), new GradientColorKey(new Color(0.95f, 0.25f, 0.3f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(colours) { mode = ParticleSystemGradientMode.RandomColor };
            var emission = ps.emission;
            emission.rateOverTime = 0;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(15f, 0.1f, 7f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.4f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var path = $"{Materials}/Confetti.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            renderer.sharedMaterial = m;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        /// <summary>Four photographers below the stage edge, hidden until the ceremony.</summary>
        private static Photographer[] BuildMedia()
        {
            var root = new GameObject("Media").transform;
            var models = new[] { "Male_Adult_02", "Female_Adult_02", "Male_Adult_05", "Business_Male_06" };
            float[] xs = { -5.2f, -2.7f, 2.7f, 5.2f };
            var body = Mat("CameraBody", new Color(0.06f, 0.06f, 0.07f), metal: 0.4f, smooth: 0.5f);
            var glow = Mat("Flash", Color.white, emission: 6f);
            var list = new List<Photographer>();
            for (var i = 0; i < xs.Length; i++)
            {
                var pos = new Vector3(xs[i], 0, 5.75f + (i % 2) * 0.25f);
                var person = Person(models[i], pos, Mathf.Atan2(-xs[i] * 0.25f, 2f) * Mathf.Rad2Deg, "Media", root, "Media");
                if (person == null) continue;
                var cam = Block("PressCamera", pos, new Vector3(0.14f, 0.09f, 0.08f), body, person.transform);
                var lens = Cylinder("Lens", Vector3.zero, new Vector3(0.06f, 0.05f, 0.06f), body, cam.transform);
                lens.transform.localPosition = new Vector3(0, 0, 0.9f);
                lens.transform.localRotation = Quaternion.Euler(90, 0, 0);
                lens.transform.localScale = new Vector3(0.45f, 0.6f, 0.65f);
                var flash = Block("FlashHead", Vector3.zero, new Vector3(0.07f, 0.04f, 0.04f), glow, cam.transform);
                flash.transform.localPosition = new Vector3(0, 0.85f, 0.2f);
                flash.transform.localScale = new Vector3(0.5f, 0.45f, 0.5f);
                var light = new GameObject("FlashLight", typeof(Light)).GetComponent<Light>();
                light.transform.SetParent(cam.transform, false);
                light.transform.localPosition = new Vector3(0, 1.2f, 2f);
                light.type = LightType.Point;
                light.range = 7f;
                light.intensity = 8f;
                light.color = new Color(0.92f, 0.95f, 1f);
                light.shadows = LightShadows.None;
                var p = person.gameObject.AddComponent<Photographer>();
                p.CameraProp = cam.transform;
                p.FlashGlow = flash.GetComponent<Renderer>();
                p.FlashLight = light;
                p.UseIK = AnimatorBuilder.Clip("photo") == null;
                person.gameObject.SetActive(false);
                list.Add(p);
            }
            return list.ToArray();
        }

        // ---------------------------------------------------------------- helpers

        private static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        public static Material Mat(string name, Color color, float emission = 0, float metal = 0, float smooth = 0.4f, Texture2D? texture = null, bool transparent = false, Vector2? tiling = null)
        {
            var path = $"{Materials}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Glossiness", smooth);
            if (texture != null)
            {
                m.SetTexture("_BaseMap", texture);
                m.SetTexture("_MainTex", texture);
                m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
                m.SetTextureScale("_MainTex", tiling ?? Vector2.one);
            }
            if (emission > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * emission);
                if (texture != null) m.SetTexture("_EmissionMap", texture);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (transparent)
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_AlphaClip", 1);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static GameObject Block(string name, Vector3 position, Vector3 size, Material mat, Transform? parent = null, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.rotation = rotation ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        internal static GameObject Cylinder(string name, Vector3 position, Vector3 scale, Material mat, Transform? parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
    }
}
