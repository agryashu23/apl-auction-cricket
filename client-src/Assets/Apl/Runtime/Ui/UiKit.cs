using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// Code-built uGUI pieces shared by the HUD, menus and results, so no
    /// screen needs prefabs. Layout is in a 1920×1080 reference space.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Panel = new Color(0.05f, 0.07f, 0.13f, 0.88f);
        // Solid and Muted are fully opaque (UI/UX dev r1): in linear colour space 0.95-0.96 still let a lit wall read through text.
        public static readonly Color PanelSolid = new Color(0.05f, 0.07f, 0.13f, 1f);
        public static readonly Color Gold = new Color(0.95f, 0.76f, 0.31f);
        public static readonly Color Accent = new Color(0.93f, 0.42f, 0.13f);
        public static readonly Color Muted = new Color(0.16f, 0.18f, 0.24f, 1f);
        public static readonly Color Ink = new Color(0.1f, 0.1f, 0.12f);
        public static readonly Color Good = new Color(0.30f, 0.85f, 0.5f);
        public static readonly Color Bad = new Color(0.92f, 0.32f, 0.32f);
        public static readonly Color Dim = new Color(0.72f, 0.75f, 0.82f);

        private static Font? _font;
        /// <summary>The UI font (the built-in one).</summary>
        public static Font Font => _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            // Never under 1920 x 1080 reference in either direction, on any phone (AspectFit).
            go.AddComponent<AspectFit>();
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
            return canvas;
        }

        public static Color ParseHex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static RectTransform Box(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        /// <summary>Full-screen stretch child (a dimmer or a page).</summary>
        public static RectTransform Stretch(RectTransform parent, string name, Color? color = null)
        {
            var go = color == null ? new GameObject(name, typeof(RectTransform)) : new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (color != null) go.GetComponent<Image>().color = color.Value;
            return rt;
        }

        /// <summary>A horizontal band of the parent, from yMin to yMax (0–1).</summary>
        public static RectTransform Strip(RectTransform parent, float yMin, float yMax)
        {
            var go = new GameObject("Strip", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, yMin);
            rt.anchorMax = new Vector2(1, yMax);
            rt.offsetMin = new Vector2(10, 0);
            rt.offsetMax = new Vector2(-10, 0);
            return rt;
        }

        public static Text Label(RectTransform parent, string text, int size, TextAnchor anchor, Color? color = null, bool bold = true)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = anchor;
            t.color = color ?? Color.white;
            t.raycastTarget = false;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 12;
            t.resizeTextMaxSize = size;
            return t;
        }

        /// <summary>A label at a fixed spot (no box behind it).</summary>
        public static Text LabelAt(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, string text, int fontSize, TextAnchor align, Color? color = null, bool bold = true)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return Label(rt, text, fontSize, align, color, bold);
        }

        public static Image Fill(RectTransform parent, Color color)
        {
            var go = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6, 6);
            rt.offsetMax = new Vector2(-6, -6);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = false;
            return img;
        }

        public static Button Btn(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string label, Color color, Action? onClick, int fontSize = 34)
        {
            var rt = Box(parent, name, anchor, pos, size, color);
            var button = rt.gameObject.AddComponent<Button>();
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var text = Label(rt, label, fontSize, TextAnchor.MiddleCenter);
            // Light buttons get dark text.
            if (color.grayscale > 0.55f) text.color = Ink;
            return button;
        }

        /// <summary>A team crest from Resources/Crests (blank if missing).</summary>
        public static RawImage Crest(RectTransform parent, string teamId, Vector2 anchor, Vector2 pos, float size)
        {
            var go = new GameObject("Crest_" + teamId, typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            var tex = Resources.Load<Texture2D>("Crests/" + teamId);
            if (tex != null) img.texture = tex;
            else img.color = new Color(1, 1, 1, 0);
            return img;
        }

        /// <summary>A panel with an offset accent edge behind it (the broadcast-card look).</summary>
        public static RectTransform EdgedPanel(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, Color? edge = null)
        {
            var back = Box(parent, name, anchor, pos + new Vector2(6, -6), size, edge ?? Accent);
            var front = Box(back, "Face", new Vector2(0.5f, 0.5f), new Vector2(-6, 6), size, fill);
            return front;
        }

        /// <summary>Hexagon portrait with an accent rim; returns the picture to fill.</summary>
        public static RawImage HexPortrait(RectTransform parent, string name, Vector2 anchor, Vector2 pos, float size)
        {
            var rim = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rimRt = (RectTransform)rim.transform;
            rimRt.SetParent(parent, false);
            rimRt.anchorMin = rimRt.anchorMax = anchor;
            rimRt.anchoredPosition = pos;
            rimRt.sizeDelta = new Vector2(size, size);
            var rimImg = rim.GetComponent<Image>();
            rimImg.sprite = Portraits.Hex();
            rimImg.color = Accent;
            rimImg.raycastTarget = false;

            var mask = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Mask));
            var maskRt = (RectTransform)mask.transform;
            maskRt.SetParent(rimRt, false);
            maskRt.anchorMin = Vector2.zero;
            maskRt.anchorMax = Vector2.one;
            maskRt.offsetMin = new Vector2(7, 7);
            maskRt.offsetMax = new Vector2(-7, -7);
            var maskImg = mask.GetComponent<Image>();
            maskImg.sprite = Portraits.Hex();
            maskImg.raycastTarget = false;
            mask.GetComponent<Mask>().showMaskGraphic = false;

            var pic = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            var picRt = (RectTransform)pic.transform;
            picRt.SetParent(maskRt, false);
            picRt.anchorMin = Vector2.zero;
            picRt.anchorMax = Vector2.one;
            picRt.offsetMin = picRt.offsetMax = Vector2.zero;
            var img = pic.GetComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = Portraits.Silhouette;
            return img;
        }

        /// <summary>
        /// The auction's player portrait (UI/UX dev, rater r1 §5: the flat hexagon read as a cartoon
        /// sticker beside the 3D people): a broadcast head-shot card. A soft drop shadow, a gold
        /// bevelled rim (light top edge, deep lower edge), a dark inner keyline, the picture in a
        /// rounded mask with a studio sheen across the top and the shoulders fading into shadow at
        /// the bottom, as a lit 3D render would. <paramref name="size"/> is the card's width and
        /// height (w × h). Returns the picture to fill; the holder is named <paramref name="name"/>.
        /// </summary>
        public static RawImage CardPortrait(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var holder = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            holder.SetParent(parent, false);
            holder.anchorMin = holder.anchorMax = anchor;
            holder.anchoredPosition = pos;
            holder.sizeDelta = size;
            var mid = new Vector2(0.5f, 0.5f);
            PlayerCardKit.Shape(holder, "Shadow", CardSprites.Glow(20, 16), new Color(0, 0, 0, 0.55f), new Vector2(0, -6), size + new Vector2(22, 22), mid);
            PlayerCardKit.Shape(holder, "RimDeep", CardSprites.Round(18), UiTheme.GoldDeep, new Vector2(0, -2), size, mid);
            PlayerCardKit.Shape(holder, "Rim", CardSprites.Round(18), Gold, new Vector2(0, 1), size - new Vector2(0, 2), mid);
            // The bevel's lit top edge.
            var hi = PlayerCardKit.Shape(holder, "RimLight", CardSprites.FadeUp(), new Color(1f, 0.95f, 0.8f, 0.55f), new Vector2(0, -size.y * 0.2f), new Vector2(size.x - 16, size.y * 0.4f), new Vector2(0.5f, 1));
            hi.rectTransform.localRotation = Quaternion.Euler(180, 0, 0);

            var maskImg = PlayerCardKit.Shape(holder, "Mask", CardSprites.Round(13), Color.white, Vector2.zero, size - new Vector2(12, 12), mid);
            maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var maskRt = maskImg.rectTransform;
            var pic = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            var picRt = (RectTransform)pic.transform;
            picRt.SetParent(maskRt, false);
            // Square art in a taller card: fill the height, crop the sides.
            picRt.anchorMin = picRt.anchorMax = new Vector2(0.5f, 0.5f);
            var side = Mathf.Max(size.x, size.y) - 12;
            picRt.sizeDelta = new Vector2(side, side);
            var img = pic.GetComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = Portraits.Silhouette;
            // Shoulders fall into shadow; a studio sheen across the top.
            PlayerCardKit.Shape(maskRt, "Floor", CardSprites.FadeUp(), new Color(0.01f, 0.015f, 0.04f, 0.75f), new Vector2(0, size.y * 0.16f), new Vector2(size.x, size.y * 0.32f), new Vector2(0.5f, 0));
            var sheen = PlayerCardKit.Shape(maskRt, "Sheen", CardSprites.FadeUp(), new Color(1, 1, 1, 0.09f), new Vector2(0, -size.y * 0.18f), new Vector2(size.x, size.y * 0.36f), new Vector2(0.5f, 1));
            sheen.rectTransform.localRotation = Quaternion.Euler(180, 0, 0);
            PlayerCardKit.Shape(holder, "Keyline", CardSprites.RoundOutline(13, 2), new Color(0, 0, 0, 0.7f), Vector2.zero, size - new Vector2(12, 12), mid);
            return img;
        }

        /// <summary>A text field (opens the phone keyboard on tap), centred at <paramref name="pos"/>.</summary>
        public static InputField Input(RectTransform parent, Vector2 pos, Vector2 size, string placeholder, string value, int maxLength)
        {
            var box = Box(parent, "Input", new Vector2(0.5f, 0.5f), pos, size, new Color(0.08f, 0.1f, 0.17f, 0.98f));
            var edge = box.gameObject.AddComponent<Outline>();
            edge.effectColor = WithAlpha(Gold, 0.65f);
            edge.effectDistance = new Vector2(2, -2);
            var hint = Label(box, placeholder, 30, TextAnchor.MiddleCenter, Dim, bold: false);
            hint.resizeTextForBestFit = false;
            var text = Label(box, "", 34, TextAnchor.MiddleCenter);
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.raycastTarget = false;
            var field = box.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = maxLength;
            field.lineType = InputField.LineType.SingleLine;
            field.text = value;
            return field;
        }

        /// <summary>A vertical scrolling list; add rows to the returned content with <see cref="ListRow"/>.</summary>
        public static RectTransform ScrollList(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var view = Box(parent, "Scroll", anchor, pos, size, new Color(0, 0, 0, 0.25f));
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            var go = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var content = (RectTransform)go.transform;
            content.SetParent(view, false);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = new Vector2(8, 0);
            content.offsetMax = new Vector2(-8, 0);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.padding = new RectOffset(0, 0, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            return content;
        }

        public static RectTransform ListRow(RectTransform content, float height, Color color)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(content, false);
            go.GetComponent<Image>().color = color;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = height;
            return rt;
        }

        public static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        public static void Clear(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
