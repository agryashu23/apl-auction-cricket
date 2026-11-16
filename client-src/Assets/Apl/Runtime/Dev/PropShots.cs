using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Hall;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Auction props evidence (<c>-aplshots &lt;dir&gt; -aplpropshots</c>): once the hall is bidding
    /// (after the ceremony and the rules), up to five AI tables' bidders — different avatars,
    /// smallest to tallest, at least one woman if the hall has one — each raise the paddle once, then
    /// one more AI table uses its RTM card (a staged <see cref="TeamTable.UseRtm"/>: the same raise the
    /// engine's RTM triggers). Each raise is filmed by three off-screen cameras at four moments; the
    /// result is one sheet per raise, <c>NN_closeup_prop_&lt;team&gt;_&lt;paddle|rtm&gt;.png</c>:
    /// rows = pickup (just off the table), raise (half way up), hold, lower (half way down);
    /// columns = front (stage side, close on the hand), side (the hand's side, close), context
    /// (the member's upper body). The grip numbers for the same raise are the "[APL prop]" log line.
    /// </summary>
    public sealed class PropShots : MonoBehaviour
    {
        private const int Tile = 512;
        private static readonly string[] Rows = { "pickup", "raise", "hold", "lower" };
        private readonly Camera[] _cams = new Camera[3];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-aplpropshots") || !args.Contains("-aplshots") || FindAnyObjectByType<PropShots>() != null) return;
            var go = new GameObject("PropShots");
            DontDestroyOnLoad(go);
            go.AddComponent<PropShots>();
        }

        private IEnumerator Start()
        {
            for (var n = 0; n < _cams.Length; n++)
            {
                var go = new GameObject("PropShotCam" + n);
                go.transform.SetParent(transform, false);
                _cams[n] = go.AddComponent<Camera>();
                _cams[n].enabled = false;
            }
            var deadline = Time.realtimeSinceStartup + 300f;
            while (Time.realtimeSinceStartup < deadline && !Ready()) yield return new WaitForSecondsRealtime(0.5f);
            if (!Ready())
            {
                Debug.Log("[APL prop] propshots: the hall never reached bidding; nothing filmed");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);

            var tables = FindObjectsByType<TeamTable>(FindObjectsSortMode.None)
                .Where(t => t.Bidder != null && t.Bidder.Paddle != null && !(t.Highlight != null && t.Highlight.enabled))
                .OrderBy(t => t.TeamId)
                .ToList();
            var picks = Pick(tables, 5);
            var rtm = tables.Where(t => t.Bidder!.RtmCard != null && !picks.Contains(t)).OrderBy(t => Size(t.Bidder!)).FirstOrDefault()
                      ?? tables.FirstOrDefault(t => t.Bidder!.RtmCard != null);
            Debug.Log($"[APL prop] propshots: paddle {string.Join(", ", picks.Select(Who))}; rtm {(rtm != null ? Who(rtm) : "none")}");
            var k = 0;
            var total = picks.Count + (rtm != null ? 1 : 0);
            foreach (var t in picks)
            {
                if (FindAnyObjectByType<HallHud>() == null) break;
                yield return Film(t, rtmCard: false);
                Debug.Log($"[APL prop] propshots: {++k}/{total} filmed");
            }
            if (rtm != null && FindAnyObjectByType<HallHud>() != null)
            {
                yield return Film(rtm, rtmCard: true);
                Debug.Log($"[APL prop] propshots: {++k}/{total} filmed");
            }
            Debug.Log("[APL prop] propshots: done");
        }

        /// <summary>
        /// The ceremony is over (the rules briefing or the lot panel is up) and every bidder is in their
        /// chair. Starting at the rules (not the first lot) leaves room before a -aplshort run quits (~75 s).
        /// </summary>
        private static bool Ready()
        {
            if (FindAnyObjectByType<HallHud>() == null) return false;
            if (GameObject.Find("CurrentBid") == null && GameObject.Find("SkipRules") == null) return false;
            return FindObjectsByType<TeamTable>(FindObjectsSortMode.None).All(t => t.Bidder == null || t.Bidder.IsHome);
        }

        private static string Who(TeamTable t) => $"{t.TeamId}/{t.Bidder!.name.Replace("Member_", "")} ({Size(t.Bidder!):0.00} m)";

        /// <summary>Seated size: hips to head.</summary>
        private static float Size(SeatedMember m)
        {
            var a = m.GetComponent<Animator>();
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            return head != null && hips != null ? Vector3.Distance(head.position, hips.position) : 0f;
        }

        /// <summary>Up to <paramref name="count"/> tables with different bidder models, spread smallest to tallest, a woman among them if any.</summary>
        private static List<TeamTable> Pick(List<TeamTable> tables, int count)
        {
            var distinct = tables.GroupBy(t => t.Bidder!.name).Select(g => g.First()).OrderBy(t => Size(t.Bidder!)).ToList();
            var picks = new List<TeamTable>();
            if (distinct.Count == 0) return picks;
            for (var k = 0; k < count && k < distinct.Count; k++)
            {
                var idx = count == 1 ? 0 : Mathf.RoundToInt(k * (distinct.Count - 1) / (float)(count - 1));
                if (!picks.Contains(distinct[idx])) picks.Add(distinct[idx]);
            }
            foreach (var t in distinct)
            {
                if (picks.Count >= count) break;
                if (!picks.Contains(t)) picks.Add(t);
            }
            if (!picks.Any(t => t.Bidder!.name.Contains("Female")))
            {
                var woman = distinct.FirstOrDefault(t => t.Bidder!.name.Contains("Female"));
                if (woman != null) picks[picks.Count / 2] = woman;
            }
            return picks;
        }

        private IEnumerator Film(TeamTable table, bool rtmCard)
        {
            // The hall may have closed (results, menu) since the picks were made.
            if (table == null || table.Bidder == null) yield break;
            var m = table.Bidder;
            var kind = rtmCard ? "rtm" : "paddle";
            var wait = Time.realtimeSinceStartup + 10f;
            while (m.HoldingProp && Time.realtimeSinceStartup < wait) yield return null;
            if (m.HoldingProp)
            {
                Debug.Log($"[APL prop] propshots: {Who(table)} busy, {kind} skipped");
                yield break;
            }
            if (rtmCard) table.UseRtm();
            else m.RaisePaddle(1.4f);
            if (!m.HoldingProp)
            {
                Debug.Log($"[APL prop] propshots: {Who(table)} did not raise the {kind}");
                yield break;
            }
            var sheet = new Texture2D(Tile * 3, Tile * Rows.Length, TextureFormat.RGB24, false);
            var row = 0;
            var holdSeen = -1f;
            var end = Time.realtimeSinceStartup + 15f;
            while (m != null && m.HoldingProp && row < Rows.Length && Time.realtimeSinceStartup < end)
            {
                var phase = m.Phase;
                if (phase == SeatedMember.PropPhase.Hold && holdSeen < 0f) holdSeen = Time.time;
                var due = row switch
                {
                    0 => phase == SeatedMember.PropPhase.Lift && m.LiftT >= 0.06f || phase > SeatedMember.PropPhase.Lift,
                    1 => phase == SeatedMember.PropPhase.Lift && m.LiftT >= 0.5f || phase > SeatedMember.PropPhase.Lift,
                    2 => phase == SeatedMember.PropPhase.Hold && Time.time - holdSeen >= 0.4f || phase > SeatedMember.PropPhase.Hold,
                    _ => phase == SeatedMember.PropPhase.Lower && m.LiftT <= 0.5f || phase > SeatedMember.PropPhase.Lower,
                };
                if (!due)
                {
                    yield return null;
                    continue;
                }
                yield return Grab(m, sheet, row);
                row++;
            }
            if (table == null || m == null)
            {
                Destroy(sheet);
                yield break;
            }
            var file = row > 0 ? AutoShot.CloseUpFile($"prop_{table.TeamId}_{kind}") : null;
            if (file != null)
            {
                CaptureWriter.Write(file, sheet.EncodeToPNG());
                Debug.Log($"[APL prop] propshots: {Who(table)} {kind} sheet {System.IO.Path.GetFileName(file)}: rows {string.Join("/", Rows.Take(row))} x cols front/side/context");
            }
            Destroy(sheet);
            // Let the raise finish (its log line comes at the end) before the next one.
            wait = Time.realtimeSinceStartup + 10f;
            while (m != null && m.HoldingProp && Time.realtimeSinceStartup < wait) yield return null;
        }

        private IEnumerator Grab(SeatedMember m, Texture2D sheet, int row)
        {
            if (m == null) yield break;
            var prop = m.HeldProp;
            var a = m.GetComponent<Animator>();
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            if (prop == null || head == null) yield break;
            var centre = prop.position + prop.up * (m.HoldingRtm ? 0.2f : 0.3f);
            var grip = m.GripPoint;
            var focus = Vector3.Lerp(grip, centre, 0.35f);
            var l = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var r = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var right = l != null && r != null ? Vector3.ProjectOnPlane(r.position - l.position, Vector3.up).normalized : m.transform.right;
            var fw = Vector3.Cross(right, Vector3.up);
            var side = m.PropHandRight ? right : -right;
            var chest = head.position - Vector3.up * 0.25f;
            var views = new (Vector3 at, Vector3 look, float fov)[]
            {
                (focus + fw * 0.8f + Vector3.up * 0.2f + side * 0.1f, focus, 34f),
                // Far and high enough not to sit inside the reaching arm at the pickup (r2 sheets, row 1).
                (focus + side * 1.0f + fw * 0.45f + Vector3.up * 0.35f, focus, 34f),
                (chest + fw * 2.3f + Vector3.up * 0.3f, Vector3.Lerp(chest, centre, 0.5f), 40f),
            };
            var main = Camera.main;
            var rts = new RenderTexture[views.Length];
            for (var n = 0; n < views.Length; n++)
            {
                var cam = _cams[n];
                if (main != null) cam.CopyFrom(main);
                rts[n] = RenderTexture.GetTemporary(Tile, Tile, 24);
                cam.targetTexture = rts[n];
                cam.nearClipPlane = 0.03f;
                cam.fieldOfView = views[n].fov;
                cam.transform.SetPositionAndRotation(views[n].at, Quaternion.LookRotation(views[n].look - views[n].at, Vector3.up));
                cam.enabled = true;
            }
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active;
            for (var n = 0; n < views.Length; n++)
            {
                _cams[n].enabled = false;
                RenderTexture.active = rts[n];
                sheet.ReadPixels(new Rect(0, 0, Tile, Tile), n * Tile, (Rows.Length - 1 - row) * Tile);
                _cams[n].targetTexture = null;
                RenderTexture.ReleaseTemporary(rts[n]);
            }
            RenderTexture.active = prev;
        }
    }
}
