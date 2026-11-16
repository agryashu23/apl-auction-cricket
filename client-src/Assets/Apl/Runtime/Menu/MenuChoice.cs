using System;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// A settings row for the menus: its name on top, then its choices as a
    /// segmented control in big rounded buttons (the current one gold). Rows
    /// stack down a glass panel from <c>y</c>.
    /// </summary>
    public static class MenuChoice
    {
        public static void Row(RectTransform panel, ref float y, string label, string[] options, Func<int> current, Action<int> pick)
        {
            var width = panel.sizeDelta.x - 60f;
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, y - 34), new Vector2(-30, y + 6), label, 26, TextAnchor.MiddleLeft, UiKit.Dim, condensed: true);
            var buttons = new Image[options.Length];
            void Paint()
            {
                var c = current();
                for (var i = 0; i < buttons.Length; i++)
                {
                    buttons[i].color = i == c ? UiKit.Gold : new Color(0.12f, 0.14f, 0.22f, 0.95f);
                    buttons[i].GetComponentInChildren<Text>().color = i == c ? UiKit.Ink : Color.white;
                }
            }
            var w = (width - 12f * (options.Length - 1)) / options.Length;
            for (var i = 0; i < options.Length; i++)
            {
                var index = i;
                var img = PlayerCardKit.Shape(panel, "Opt_" + label + "_" + i, CardSprites.Round(18), Color.white,
                    new Vector2(30 + w / 2 + i * (w + 12), y - 84), new Vector2(w, 88), new Vector2(0, 1));
                img.raycastTarget = true;
                PlayerCardKit.Text((RectTransform)img.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, options[i], 30, TextAnchor.MiddleCenter, Color.white, condensed: true);
                img.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    pick(index);
                    Paint();
                });
                buttons[i] = img;
            }
            Paint();
            y -= 136f;
        }
    }
}
