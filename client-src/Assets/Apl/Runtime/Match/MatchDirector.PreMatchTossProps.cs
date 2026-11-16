using System;
using System.Collections;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>The toss's props: the presenter and his microphone, the mat, the coin and its flight.</summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The presenter's suit (a dark kit on one of the cast's bodies).</summary>
        private static readonly Franchise Suit = new Franchise { Id = "PRESENTER", Name = "Presenter", ShortName = "", PrimaryColor = "#1E2230", SecondaryColor = "#262B3A" };

        /// <summary>captain_toss_flip (ceremony.json): the coin leaves his hand at frame 37 and lands at 77 (30 fps), 2.9 m up at the top.</summary>
        private const float CoinRelease = 36 / 30f, CoinFlight = 40 / 30f, CoinApex = 2.9f;

        /// <summary>Where it lands: in the flipper's frame 0.15 m to his left and 0.7 m in front.</summary>
        private static Vector3 CoinLand => TossAt - TossRight * 0.15f + TossFwd * 0.7f + Vector3.up * 0.02f;

        /// <summary>What the presenter's microphone points at (his own mouth, or the captain speaking); null: no mic.</summary>
        private Transform? _micTarget;

        /// <summary>The presenter: one of the cast's bodies in a dark suit, or null (no kit bodies).</summary>
        /// <summary>
        /// Bodies whose shirts have full-length sleeves in the kit mask (measured from
        /// Generated/Kits: no bare forearm in the sleeve area). Male_Adult_02, used before,
        /// has rolled sleeves the mask leaves as skin — the patches on the suit's arms.
        /// Male_Adult_04 is the umpire's.
        /// </summary>
        private static readonly string[] SuitBodies = { "Male_Adult_07", "Male_Adult_03", "Male_Adult_13", "Male_Adult_10", "Male_Adult_17", "Male_Adult_12", "Male_Adult_14", "Male_Adult_05" };

        /// <summary>The presenter, on a long-sleeved body neither captain is (<paramref name="avoid"/>).</summary>
        private Animator? Presenter(params Animator?[] avoid)
        {
            if (!_cast.Ready || _cast.Models == 0) return null;
            var models = Refs.CastModels;
            var taken = new System.Collections.Generic.HashSet<Mesh>();
            foreach (var c in avoid)
            {
                if (c == null) continue;
                foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.sharedMesh != null) taken.Add(r.sharedMesh);
            }
            bool Free(GameObject m) => m.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.sharedMesh == null || !taken.Contains(r.sharedMesh));
            var index = -1;
            foreach (var name in SuitBodies)
            {
                index = Array.FindIndex(models, m => m != null && m.name == name && Free(m));
                if (index >= 0) break;
            }
            if (index < 0) index = Array.FindIndex(models, m => m != null && m.name == SuitBodies[0]);
            if (index < 0) index = 0;
            var a = _cast.Model(index, Suit);
            a.name = "Presenter";
            return a;
        }

        /// <summary>A handheld mic in his right hand (the presenter clips hold one there), pointed at <see cref="_micTarget"/>.</summary>
        private void Mic(Animator presenter)
        {
            var hand = presenter.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) return;
            var mic = new GameObject("Mic").transform;
            mic.SetParent(presenter.transform, false);
            // A broadcast mic that reads on camera: black handle, silver grille, the league's flag.
            var dark = new Material(Refs.LitTemplate);
            dark.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f));
            dark.SetFloat("_Smoothness", 0.5f);
            var grille = new Material(Refs.LitTemplate);
            grille.SetColor("_BaseColor", new Color(0.78f, 0.79f, 0.82f));
            grille.SetFloat("_Metallic", 0.8f);
            grille.SetFloat("_Smoothness", 0.6f);
            var flag = new Material(Refs.LitTemplate);
            flag.SetColor("_BaseColor", UiKit.Accent);
            Part(mic, PrimitiveType.Cylinder, new Vector3(0, 0, -0.03f), new Vector3(0.038f, 0.11f, 0.038f), Quaternion.Euler(90, 0, 0), dark);
            Part(mic, PrimitiveType.Sphere, new Vector3(0, 0, 0.12f), Vector3.one * 0.075f, Quaternion.identity, grille);
            Part(mic, PrimitiveType.Cube, new Vector3(0, 0, 0.06f), new Vector3(0.07f, 0.07f, 0.05f), Quaternion.identity, flag);
            StartCoroutine(HoldMic(mic, hand, presenter));
        }

        private static void Part(Transform parent, PrimitiveType type, Vector3 at, Vector3 size, Quaternion rot, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>The mic sits in the fist, its head toward whoever is speaking.</summary>
        private IEnumerator HoldMic(Transform mic, Transform hand, Animator presenter)
        {
            while (mic != null && presenter != null)
            {
                var target = _micTarget != null ? _micTarget.position : hand.position + presenter.transform.forward + Vector3.up * 0.4f;
                var dir = (target + Vector3.down * 0.05f - hand.position).normalized;
                mic.SetPositionAndRotation(hand.position + dir * 0.06f, Quaternion.LookRotation(dir));
                yield return null;
            }
        }

        /// <summary>The mat between them: the APL cover on a disc with a bright rim.</summary>
        private GameObject TossMat(Vector3 at)
        {
            var root = new GameObject("TossMat");
            root.transform.position = at + Vector3.up * 0.006f;
            Disc(root.transform, "Rim", 2.7f, 0.004f, new Color(0.55f, 0.3f, 1f), null, 0f);
            Disc(root.transform, "Face", 2.45f, 0.006f, Refs.Cover != null ? Color.white : new Color(0.06f, 0.07f, 0.16f), Refs.Cover, 0.001f);
            return root;
        }

        private void Disc(Transform parent, string name, float diameter, float thick, Color colour, Texture? tex, float lift)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, lift, 0);
            go.transform.localScale = new Vector3(diameter, thick / 2f, diameter);
            var m = new Material(Refs.LitTemplate);
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
            m.SetFloat("_Smoothness", 0.2f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The coin, big enough to read from the top-down shot (as the reference does): gold, heads marked with a boss.</summary>
        private Transform Coin()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = "Coin";
            go.transform.localScale = new Vector3(0.16f, 0.006f, 0.16f);
            var gold = new Material(Refs.LitTemplate);
            gold.SetColor("_BaseColor", new Color(0.86f, 0.72f, 0.38f));
            gold.SetTexture("_BaseMap", Texture2D.whiteTexture);
            // Less metal, a soft glow: a pure metal coin read as a dark speck on the wide shot.
            gold.SetFloat("_Metallic", 0.35f);
            gold.SetFloat("_Smoothness", 0.75f);
            gold.EnableKeyword("_EMISSION");
            gold.SetColor("_EmissionColor", new Color(0.55f, 0.42f, 0.15f));
            go.GetComponent<MeshRenderer>().sharedMaterial = gold;
            var dark = new Material(gold);
            dark.SetColor("_BaseColor", new Color(0.55f, 0.4f, 0.16f));
            Part(go.transform, PrimitiveType.Cylinder, new Vector3(0, 1f, 0), new Vector3(0.62f, 0.25f, 0.62f), Quaternion.identity, dark);
            return go.transform;
        }

        /// <summary>The coin on the flipper's thumb until he lets it go.</summary>
        private static IEnumerator HoldCoin(Transform coin, Transform? hand, Func<bool> holding)
        {
            while (holding() && coin != null)
            {
                coin.position = hand != null ? hand.position + Vector3.up * 0.03f : TossAt + Vector3.up * 1.32f;
                coin.rotation = Quaternion.identity;
                yield return null;
            }
        }

        /// <summary>Up end over end, down onto the mat with the drawn face up: the spin is chosen for it; a hop and a wobble.</summary>
        private IEnumerator FlipCoin(Transform coin, bool heads)
        {
            var from = coin.position;
            var to = CoinLand;
            var turns = 360f * 11 + (heads ? 0f : 180f);
            var yaw = UnityEngine.Random.Range(0f, 360f);
            // Up to the apex, falling the rest: a parabola through both ends with its top CoinApex above the ground.
            var peak = Mathf.Max(CoinApex, from.y + 0.3f);
            for (var t = 0f; t < CoinFlight; t += Time.deltaTime)
            {
                var k = t / CoinFlight;
                var p = Vector3.Lerp(from, to, k);
                var rise = peak - Mathf.Lerp(from.y, to.y, 0.5f);
                p.y = Mathf.Lerp(from.y, to.y, k) + rise * 4f * k * (1 - k);
                coin.position = p;
                coin.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(turns * k, 0, 0);
                yield return null;
            }
            Sfx.Tick();
            var rest = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(heads ? 0f : 180f, 0, 0);
            for (var t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                var k = t / 0.5f;
                coin.position = to + Vector3.up * (0.04f * Mathf.Sin(k * Mathf.PI) * (1 - k));
                coin.rotation = rest * Quaternion.Euler(12f * (1 - k) * Mathf.Sin(k * 30f), 0, 12f * (1 - k) * Mathf.Cos(k * 30f));
                yield return null;
            }
            coin.SetPositionAndRotation(to, rest);
        }
    }
}
