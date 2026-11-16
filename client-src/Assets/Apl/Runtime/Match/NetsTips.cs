using System;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The nets' tip panel, top centre: one tip at a time ("TIP 2 OF 6" with ◄ ►),
    /// moving on by itself after a couple of balls on it; and EXIT NETS.
    /// </summary>
    public sealed class NetsTips : MonoBehaviour
    {
        private string[] _tips = Array.Empty<string>();
        private string _title = "";
        private int _at, _balls;
        private Text _head = null!, _body = null!;

        /// <summary>Balls on a tip before the next one comes up.</summary>
        private const int BallsPerTip = 2;

        public static NetsTips Create(string title, string[] tips, Action exit)
        {
            var canvas = UiKit.Canvas("NetsTips", 16);
            var t = canvas.gameObject.AddComponent<NetsTips>();
            t._title = title;
            t._tips = tips;
            var root = (RectTransform)canvas.transform;
            var panel = UiKit.EdgedPanel(root, "Tip", new Vector2(0.5f, 1), new Vector2(-170, -96), new Vector2(980, 150), new Color(0.03f, 0.04f, 0.1f, 0.9f), UiKit.Accent);
            t._head = UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(760, 34), "", 24, TextAnchor.MiddleCenter, UiKit.Gold);
            t._body = UiKit.LabelAt(panel, new Vector2(0.5f, 0.5f), new Vector2(0, -16), new Vector2(800, 92), "", 28, TextAnchor.MiddleCenter, Color.white, bold: false);
            UiKit.Btn(panel, "Prev", new Vector2(0, 0.5f), new Vector2(44, 0), new Vector2(72, 120), "◄", UiKit.Muted, () => t.Go(t._at - 1), 34);
            UiKit.Btn(panel, "Next", new Vector2(1, 0.5f), new Vector2(-44, 0), new Vector2(72, 120), "►", UiKit.Muted, () => t.Go(t._at + 1), 34);
            UiKit.Btn(root, "ExitNets", new Vector2(0, 1), new Vector2(150, -70), new Vector2(240, 84), "◄ EXIT NETS", new Color(0.3f, 0.1f, 0.1f, 0.95f), exit, 26);
            t.Go(0);
            return t;
        }

        private void Go(int i)
        {
            _at = Mathf.Clamp(i, 0, _tips.Length - 1);
            _balls = 0;
            _head.text = $"{_title}  ·  TIP {_at + 1} OF {_tips.Length}";
            _body.text = _tips.Length > 0 ? _tips[_at] : "";
        }

        /// <summary>A ball's been played: after a couple on this tip, the next one.</summary>
        public void Ball()
        {
            if (++_balls >= BallsPerTip && _at < _tips.Length - 1) Go(_at + 1);
        }

        /// <summary>Out of the way while a card is up (the pause menu, a bowler pick).</summary>
        public void Show(bool on) => GetComponent<Canvas>().enabled = on;
    }
}
