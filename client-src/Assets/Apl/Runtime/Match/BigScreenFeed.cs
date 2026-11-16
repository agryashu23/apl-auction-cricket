using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The big screens' live feed (round 12, audio-modes with characters-stadium's
    /// StadiumBigScreen): the broadcast picture itself — a second camera copying the
    /// match camera's shot at a low resolution, ten times a second — so the screen
    /// shows the ball being bowled and hit live, and the replays when the director runs
    /// them (they play on the match camera), tagged LIVE or REPLAY. One camera and one
    /// render texture feed every screen face.
    /// </summary>
    public sealed class BigScreenFeed : MonoBehaviour
    {
        private const int W = 384, H = 216;
        private const float Interval = 0.1f;

        private static BigScreenFeed? _feed;
        private Camera _cam = null!;
        private RenderTexture _rt = null!;
        private float _next;
        private MatchDirector? _director;
        private readonly System.Collections.Generic.List<Text> _tags = new System.Collections.Generic.List<Text>();

        /// <summary>The feed's picture on a screen face: <paramref name="panel"/> gets it in the rect between the anchors given.</summary>
        public static void Attach(RectTransform panel, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (_feed == null) _feed = new GameObject("BigScreenFeed").AddComponent<BigScreenFeed>();
            var go = new GameObject("LiveFeed", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<RawImage>();
            img.texture = _feed._rt;
            img.raycastTarget = false;
            var tagBox = UiKit.Box(rt, "FeedTag", new Vector2(0, 0), new Vector2(90, 34), new Vector2(160, 52), new Color(0.85f, 0.1f, 0.12f, 0.95f));
            _feed._tags.Add(UiKit.Label(tagBox, "LIVE", 40, TextAnchor.MiddleCenter));
        }

        private void Awake()
        {
            _rt = new RenderTexture(W, H, 16, RenderTextureFormat.ARGB32) { name = "BigScreenFeed" };
            _cam = gameObject.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
        }

        private void OnDestroy()
        {
            if (_feed == this) _feed = null;
            if (_rt != null) _rt.Release();
        }

        private void LateUpdate()
        {
            // Rendered for one frame in every Interval: enabled, then off again next frame.
            if (_cam.enabled)
            {
                _cam.enabled = false;
                return;
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            var main = Camera.main;
            if (main == null || main == _cam) return;
            _cam.CopyFrom(main);
            _cam.targetTexture = _rt;
            // The world only: no HUD overlays, no other render textures' cameras.
            _cam.cullingMask = main.cullingMask & ~(1 << LayerMask.NameToLayer("UI"));
            _cam.depth = main.depth - 1;
            _cam.enabled = true;
            if (_director == null) _director = FindFirstObjectByType<MatchDirector>();
            var replay = _director != null && _director.ReplayOnAir;
            foreach (var t in _tags)
            {
                if (t == null) continue;
                t.text = replay ? "REPLAY" : "LIVE";
                t.transform.parent.GetComponent<Image>().color = replay ? new Color(0.95f, 0.65f, 0.1f, 0.95f) : new Color(0.85f, 0.1f, 0.12f, 0.95f);
            }
        }
    }
}
