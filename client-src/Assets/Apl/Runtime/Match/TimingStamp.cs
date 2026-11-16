using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The timing grade, big, by the batter: EARLY / PERFECT / LATE (and GOOD, TOO EARLY,
    /// TOO LATE, MISSED) stamped over his head the moment the ball is played, coloured by
    /// the grade, with the lean and the region underneath (no ms: owner, final rating); it punches in,
    /// holds and fades. The small top-right meter stays as the detail; this is the read-at-a-glance answer.
    /// Final rating fix #4 (P283): it never sits on the batter — if his body is under it (the close
    /// follow-through camera cuts in after it's placed) it moves up to the upper third, or to his side.
    /// </summary>
    public sealed class TimingStamp : MonoBehaviour
    {
        /// <summary>Where the batter is in the world when the stamp shows; null: it sits above screen centre.</summary>
        public Vector3? Anchor;

        private RectTransform _rt = null!, _parent = null!;
        private Text _grade = null!, _detail = null!;
        private CanvasGroup _group = null!;
        private float _shownAt = -99f;

        private const float Punch = 0.16f, Hold = 1.5f, Fade = 0.45f;

        public static TimingStamp Create(RectTransform parent)
        {
            var go = new GameObject("TimingStamp", typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(560, 150);
            var s = go.AddComponent<TimingStamp>();
            s._rt = rt;
            s._parent = parent;
            s._group = go.GetComponent<CanvasGroup>();
            s._group.blocksRaycasts = false;
            s._group.interactable = false;
            var back = UiKit.Box(rt, "Back", new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(460, 92), new Color(0, 0, 0, 0.55f));
            back.GetComponent<Image>().raycastTarget = false;
            s._grade = UiKit.LabelAt(rt, new Vector2(0.5f, 0.5f), new Vector2(0, 16), new Vector2(560, 90), "", 72, TextAnchor.MiddleCenter, Color.white);
            var o = s._grade.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.9f);
            o.effectDistance = new Vector2(3, -3);
            s._detail = UiKit.LabelAt(rt, new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(560, 34), "", 28, TextAnchor.MiddleCenter, Color.white);
            s._detail.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
            s._group.alpha = 0f;
            return s;
        }

        /// <summary>Stamp <paramref name="grade"/> (and the <paramref name="detail"/> line) now.</summary>
        public void Show(string grade, string detail, Color color)
        {
            _grade.text = grade;
            _grade.color = color;
            _detail.text = detail;
            _shownAt = Time.unscaledTime;
            Place();
            Update();
        }

        /// <summary>On screen now (punch, hold or fade): the HUD's overlay stage waits for it.</summary>
        public bool Showing => Time.unscaledTime - _shownAt < Punch + Hold + Fade;

        public void Clear()
        {
            _shownAt = -99f;
            _group.alpha = 0f;
        }

        /// <summary>The colour of a grade: green perfect, light green good, amber early/late, red too early/late or missed.</summary>
        public static Color ColorOf(string grade) => grade switch
        {
            "PERFECT" => new Color(0.45f, 1f, 0.45f),
            "GOOD" => new Color(0.75f, 1f, 0.55f),
            "EARLY" or "LATE" => new Color(1f, 0.78f, 0.2f),
            "LEFT" => new Color(0.8f, 0.85f, 0.95f),
            _ => new Color(1f, 0.35f, 0.3f),
        };

        /// <summary>Over his head on screen as the ball is played (clamped inside it), else above centre; then kept off him.</summary>
        private void Place()
        {
            var cam = Camera.main;
            var at = Anchor;
            Vector2 local = new Vector2(0, 160);
            if (cam != null && at is Vector3 w)
            {
                var sp = cam.WorldToScreenPoint(w + Vector3.up * 2.3f);
                if (sp.z > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, sp, null, out var p)) local = p;
            }
            var half = _parent.rect.size * 0.5f - _rt.sizeDelta * 0.5f;
            local.x = Mathf.Clamp(local.x, -half.x, half.x);
            // Kept out of the top band (the score graphics and the big screen live there: r9 f415).
            local.y = Mathf.Clamp(local.y, -half.y * 0.3f, half.y * 0.35f);
            _rt.anchoredPosition = local;
            _goal = local;
            _movedOff = false;
            KeepOffBatter(true);
        }

        /// <summary>Where the stamp is heading (it eases there when the batter comes under it).</summary>
        private Vector2 _goal;
        private bool _movedOff;

        /// <summary>The stamp's drawn box (the dark band and the detail line under it) about its centre.</summary>
        private static readonly Vector2 BoxCentre = new Vector2(0, -4), BoxSize = new Vector2(470, 140);

        /// <summary>
        /// Final rating fix #4: the batter's box on screen now (feet to over the helmet, his width); if the stamp
        /// overlaps it, the stamp goes to the upper third over him, else beside him (the side with more room).
        /// </summary>
        private void KeepOffBatter(bool snap)
        {
            var cam = Camera.main;
            if (cam == null || !(Anchor is Vector3 w)) return;
            if (!Box(cam, w, out var bMin, out var bMax)) return;
            var half = _parent.rect.size * 0.5f;
            if (!Overlaps(_goal, bMin, bMax)) return;
            // Upper third: the band's bottom above the screen's top third line, under the top band (pause, ball chip).
            var upY = Mathf.Min(half.y * 0.46f, half.y - 140f);
            var limX = half.x - BoxSize.x * 0.5f - 10f;
            var bx = (bMin.x + bMax.x) * 0.5f;
            var up = new Vector2(Mathf.Clamp(bx, -limX, limX), upY);
            Vector2 goal;
            string where;
            if (!Overlaps(up, bMin, bMax))
            {
                goal = up;
                where = "upper third";
            }
            else
            {
                var roomLeft = bMin.x + half.x;
                var roomRight = half.x - bMax.x;
                var sideX = roomRight >= roomLeft ? bMax.x + BoxSize.x * 0.5f + 30f : bMin.x - BoxSize.x * 0.5f - 30f;
                goal = new Vector2(Mathf.Clamp(sideX, -limX, limX), upY);
                where = roomRight >= roomLeft ? "upper third, right of him" : "upper third, left of him";
            }
            _goal = goal;
            if (snap) _rt.anchoredPosition = goal;
            if (!_movedOff)
            {
                _movedOff = true;
                Debug.Log($"[APL hudrole] timing stamp moved off the batter -> {where} ({goal.x:0},{goal.y:0}); batter box ({bMin.x:0},{bMin.y:0})-({bMax.x:0},{bMax.y:0})");
            }
        }

        private static bool Overlaps(Vector2 centre, Vector2 bMin, Vector2 bMax)
        {
            var c = centre + BoxCentre;
            var h = BoxSize * 0.5f;
            return c.x - h.x < bMax.x && c.x + h.x > bMin.x && c.y - h.y < bMax.y && c.y + h.y > bMin.y;
        }

        /// <summary>The batter standing at <paramref name="w"/>, as a box in the parent's local space (false: not in view).</summary>
        private bool Box(Camera cam, Vector3 w, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            var side = cam.transform.right * 0.55f;
            var any = false;
            foreach (var pt in new[] { w - side, w + side, w + Vector3.up * 2.05f - side, w + Vector3.up * 2.05f + side, w + Vector3.up * 1.0f })
            {
                var sp = cam.WorldToScreenPoint(pt);
                if (sp.z <= 0.05f) continue;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, sp, null, out var p)) continue;
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
                any = true;
            }
            return any;
        }

        private void Update()
        {
            var t = Time.unscaledTime - _shownAt;
            if (t > Punch + Hold + Fade)
            {
                _group.alpha = 0f;
                return;
            }
            // The camera can cut after it's placed (the close follow-through, P283): keep it off him, easing over.
            KeepOffBatter(false);
            _rt.anchoredPosition = Vector2.Lerp(_rt.anchoredPosition, _goal, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            // Punch in from 1.6x, hold, then fade.
            var k = Mathf.Clamp01(t / Punch);
            _rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, 1f - (1f - k) * (1f - k));
            _group.alpha = t < Punch + Hold ? Mathf.Clamp01(t / 0.06f) : 1f - (t - Punch - Hold) / Fade;
        }
    }
}
