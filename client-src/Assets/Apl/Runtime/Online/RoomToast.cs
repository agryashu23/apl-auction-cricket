using System.Collections.Generic;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Online
{
    /// <summary>
    /// Room messages over everything but the confirm dialog (canvas 90): short toasts — a
    /// refused action (team taken, room full or locked, kicked, paused …) with a red edge, room
    /// news (host paused, a player dropped out, your seat is back) with a gold one — and the
    /// sticky RECONNECTING… pill while the socket is down. Used by the lobby and the hall.
    /// A toast is a narrow (680 px) one-line strip in a slot the current screen keeps free:
    /// in the hall right under the seat strip, in the waiting room under the host controls,
    /// elsewhere under the page header. One shows at a time; the rest queue (at most three,
    /// repeats dropped) and a waiting queue shortens the one on screen, so nothing lingers.
    /// </summary>
    public sealed class RoomToast : MonoBehaviour
    {
        /// <summary>Where the toast strip sits (each screen picks the slot it keeps clear).</summary>
        public enum Slot
        {
            /// <summary>Under a page header (home, create, retention, results).</summary>
            Top,

            /// <summary>The auction hall: right under the seat strip, above the bid timer.</summary>
            Hall,

            /// <summary>The waiting room: the free band under the host controls, over the team column.</summary>
            Lobby,
        }

        public const float Width = 680, Height = 44;

        /// <summary>Seconds on screen: a refusal, room news, and the least once others are waiting.</summary>
        private const float BadSec = 3f, NewsSec = 2.5f, MinSec = 1.2f, FadeSec = 0.15f;

        private const int MaxQueue = 3;

        private static RoomToast? _instance;
        private static Slot _slot = Slot.Top;
        private RectTransform _toast = null!, _reconnect = null!;
        private CanvasGroup _toastGroup = null!;
        private Text _toastText = null!, _reconnectText = null!;
        private Image _toastFill = null!;
        private float _shownAt, _until;
        private string _current = "";
        private readonly Queue<(string Text, bool Bad)> _queue = new Queue<(string, bool)>();

        /// <summary>The last toast's text (test runs log it).</summary>
        public static string Last { get; private set; } = "";

        /// <summary>Where the toast strip is now, in reference pixels from the top (test runs log it).</summary>
        public static string Where
        {
            get
            {
                var (x, y) = Position(_slot);
                return $"{_slot} slot: x {x:0}, top {-y - Height / 2:0}..{-y + Height / 2:0} px, {Width:0} wide";
            }
        }

        /// <summary>A toast is on screen.</summary>
        public static bool Showing => _instance != null && _instance._toast.gameObject.activeSelf;

        private static (float X, float Y) Position(Slot slot) => slot switch
        {
            // Seat strip ends 122 px down; the room's bid timer starts at 176 (HallDirector.Room.cs).
            Slot.Hall => (0, -148),
            // Host controls / room settings end ~761 px down; START / READY start at 938. Centred on the team column.
            Slot.Lobby => (280, -792),
            // Page headers end ~160 px down.
            _ => (0, -186),
        };

        private static RoomToast Get()
        {
            if (_instance != null) return _instance;
            var canvas = UiKit.Canvas("RoomToast", 90);
            // Never in the way of a tap.
            canvas.GetComponent<GraphicRaycaster>().enabled = false;
            var t = canvas.gameObject.AddComponent<RoomToast>();
            var root = (RectTransform)canvas.transform;
            var (x, y) = Position(_slot);
            t._toast = UiKit.Box(root, "Toast", new Vector2(0.5f, 1), new Vector2(x, y), new Vector2(Width, Height), new Color(0.05f, 0.07f, 0.13f, 1f));
            t._toastGroup = t._toast.gameObject.AddComponent<CanvasGroup>();
            t._toastFill = UiKit.Box(t._toast, "Edge", new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(8, Height), UiKit.Bad).GetComponent<Image>();
            t._toastText = UiKit.LabelAt(t._toast, new Vector2(0.5f, 0.5f), new Vector2(4, 0), new Vector2(Width - 28, Height - 6), "", 24, TextAnchor.MiddleCenter);
            t._toastText.resizeTextForBestFit = true;
            t._toastText.resizeTextMinSize = 15;
            t._toastText.resizeTextMaxSize = 24;
            t._toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
            t._toast.gameObject.SetActive(false);
            t._reconnect = UiKit.Box(root, "Reconnecting", new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(620, 110), new Color(0.05f, 0.07f, 0.13f, 0.98f));
            YouMark.Add(t._reconnect, "", 3, UiKit.Gold);
            t._reconnectText = UiKit.Label(t._reconnect, "RECONNECTING…", 40, TextAnchor.MiddleCenter, UiKit.Gold);
            t._reconnect.gameObject.SetActive(false);
            DontDestroyOnLoad(canvas.gameObject);
            return _instance = t;
        }

        /// <summary>The screen now up keeps this slot free for toasts (the hall, the waiting room, or a page header).</summary>
        public static void Place(Slot slot)
        {
            if (_slot == slot) return;
            _slot = slot;
            if (_instance == null) return;
            var (x, y) = Position(slot);
            _instance._toast.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>A short toast; <paramref name="bad"/>: a refusal (red edge), else news (gold). Queued behind the one on screen.</summary>
        public static void Show(string text, bool bad = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            var t = Get();
            Last = text;
            Debug.Log($"[APL room] toast: {text}");
            if (t._toast.gameObject.activeSelf)
            {
                // The same words again: keep the one on screen up a little longer instead of repeating it.
                if (text == t._current)
                {
                    t._until = Mathf.Max(t._until, Time.unscaledTime + MinSec);
                    return;
                }
                foreach (var q in t._queue) if (q.Text == text) return;
                if (t._queue.Count >= MaxQueue) t._queue.Dequeue();
                t._queue.Enqueue((text, bad));
                return;
            }
            t.Present(text, bad);
        }

        private void Present(string text, bool bad)
        {
            _current = text;
            _toastText.text = text;
            _toastFill.color = bad ? UiKit.Bad : UiKit.Gold;
            _toast.gameObject.SetActive(true);
            _shownAt = Time.unscaledTime;
            _until = _shownAt + (bad ? BadSec : NewsSec);
            _toastGroup.alpha = 0f;
        }

        /// <summary>The RECONNECTING… pill (on while the socket is down in a room).</summary>
        public static void SetReconnecting(bool on, string? detail = null)
        {
            if (!on && _instance == null) return;
            var t = Get();
            if (t._reconnect.gameObject.activeSelf != on) t._reconnect.gameObject.SetActive(on);
            if (on) t._reconnectText.text = detail == null ? "RECONNECTING…" : "RECONNECTING…\n<size=22>" + detail + "</size>";
            t._reconnectText.supportRichText = true;
        }

        private void Update()
        {
            if (_toast.gameObject.activeSelf)
            {
                var now = Time.unscaledTime;
                // Others waiting: this one only needs to be readable, not to sit out its full time.
                var until = _queue.Count > 0 ? Mathf.Min(_until, Mathf.Max(_shownAt + MinSec, now)) : _until;
                if (now >= until)
                {
                    if (_queue.Count > 0)
                    {
                        var (text, bad) = _queue.Dequeue();
                        Present(text, bad);
                    }
                    else
                    {
                        _toast.gameObject.SetActive(false);
                        _current = "";
                    }
                }
                else
                {
                    // Quick fade in and out.
                    _toastGroup.alpha = Mathf.Clamp01(Mathf.Min((now - _shownAt) / FadeSec, (until - now) / FadeSec));
                }
            }
            // Pulse the reconnect pill so it reads as "working on it".
            if (_reconnect.gameObject.activeSelf)
            {
                var a = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
                _reconnectText.color = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, a);
            }
        }
    }
}
