using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The CUSTOM FIELD editor (owner v26 #13), from the field screen's CUSTOM tab: the ground from behind
    /// the bowler (as the radar draws it) with the ~24 named spots of Field.Spots — slip, gully, point,
    /// cover, mid-off ... long-on, cow corner, deep square, fine leg — the keeper and bowler fixed. Tap a
    /// spot to put a man there, tap again to take him off; nine to place. The right column counts the men
    /// out against the phase's limit and the leg side behind square (two at most), and lists what breaks
    /// the rules; SAVE keeps it in the slot (CustomFields) once it's a legal field after the powerplay
    /// (one with more than two out is kept, and marked as not for the powerplay).
    /// </summary>
    public sealed class FieldEditor : MonoBehaviour
    {
        private FieldPreset _slot;
        private int _phase;
        private bool _leftHanded;
        private Action<bool>? _done;
        private readonly List<string> _placed = new List<string>();
        private RectTransform _root = null!;
        private string? _flash;

        /// <summary>The ground's radius on screen (px) and the deepest spot it shows (m).</summary>
        private const float R = 410f, Deep = 72f;

        public static void Open(FieldPreset slot, int phase, bool leftHanded, Action<bool> done)
        {
            CloseAll();
            var canvas = UiKit.Canvas("FieldEditor", 45);
            var ed = canvas.gameObject.AddComponent<FieldEditor>();
            ed._root = (RectTransform)canvas.transform;
            ed._slot = slot;
            ed._phase = phase;
            ed._leftHanded = leftHanded;
            ed._done = done;
            var had = Field.Custom(slot);
            // (A save from before the field rework may name an old spot, e.g. "deep square": shown as today's.)
            if (had != null) ed._placed.AddRange(had.Select(f => Field.Spot(f.Name)?.Name ?? f.Name));
            ed.Build();
            Debug.Log($"[APL field] editor open: {slot} ({ed._placed.Count} placed)");
        }

        public static void CloseAll()
        {
            foreach (var e in FindObjectsByType<FieldEditor>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        }

        private List<Fielder> Nine() => _placed.Select(n => Field.Spot(n)).Where(s => s != null).Select(s => s!).ToList();

        /// <summary>Where a spot is drawn: from behind the bowler (straight down at the bottom; a right-hander's off side on the left), the close catchers spread a touch so every spot can be tapped.</summary>
        private Vector2 Draw(Fielder f)
        {
            var angle = f.Name switch
            {
                "slip" => 162.0,
                "second slip" => 145.0,
                "gully" => 128.0,
                "short third" => 110.0,
                _ => f.AngleDeg,
            };
            var k = Mathf.Sqrt((float)Math.Min(f.Radius, Deep) / Deep) * (R - 40f);
            var a = (float)(angle * Math.PI / 180);
            var sign = _leftHanded ? 1f : -1f;
            return new Vector2(sign * Mathf.Sin(a) * k, -Mathf.Cos(a) * k);
        }

        private static string Short(string name) => name switch
        {
            "second slip" => "2ND SLIP",
            "silly point" => "SILLY PT",
            "extra cover" => "EXTRA COV",
            "mid-wicket" => "MID-WKT",
            "square leg" => "SQ LEG",
            "short third" => "SHORT 3RD",
            "third man" => "3RD MAN",
            "deep cover" => "DEEP COV",
            "deep mid-wicket" => "DEEP MID-WKT",
            "deep square" => "DEEP SQ",
            "deep square leg" => "DEEP SQ",
            "deep backward square" => "DEEP BWD SQ",
            "short fine" => "SHORT FINE",
            "cow corner" => "COW CNR",
            _ => name.ToUpperInvariant(),
        };

        private void Build()
        {
            UiKit.Clear(_root);
            var page = PanelKit.Backdrop(_root, 0.92f);
            var nine = Nine();
            PlayerCardKit.Text(page, new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -100), new Vector2(-60, -20), $"{Field.PresetName(_slot)}  ·  SET YOUR FIELD", 54, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(page, new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -100), new Vector2(-60, -20), "TAP A SPOT TO PLACE A MAN, TAP AGAIN TO TAKE HIM OFF", 24, TextAnchor.MiddleRight, UiKit.Dim, condensed: true);

            // The ground: grass, the ring, the pitch, keeper and bowler fixed.
            var centre = new Vector2(-440, -40);
            var ground = UiKit.Box(page, "Ground", new Vector2(0.5f, 0.5f), centre, new Vector2(R * 2, R * 2), new Color(0.07f, 0.34f, 0.12f, 1f));
            ground.GetComponent<Image>().sprite = CardSprites.Disc();
            ground.GetComponent<Image>().raycastTarget = false;
            // The 30-yard ring as it really is: half circles round each set of stumps joined by straight lines (dotted).
            for (var deg = -180; deg < 180; deg += 4)
            {
                var ringDot = UiKit.Box(ground, "Ring", new Vector2(0.5f, 0.5f), Draw(new Fielder("ring", deg, Field.RingRadius(deg, Field.InnerRing))), new Vector2(6, 6), new Color(1, 1, 1, 0.4f));
                ringDot.GetComponent<Image>().sprite = CardSprites.Disc();
                ringDot.GetComponent<Image>().raycastTarget = false;
            }
            var bowlerAt = Draw(new Fielder("bowler", 0, 20.1));
            UiKit.Box(ground, "Pitch", new Vector2(0.5f, 0.5f), bowlerAt / 2f, new Vector2(16, Mathf.Abs(bowlerAt.y)), new Color(0.86f, 0.77f, 0.52f)).GetComponent<Image>().raycastTarget = false;
            var off = _leftHanded ? "OFF SIDE ►" : "◄ OFF SIDE";
            var leg = _leftHanded ? "◄ LEG SIDE" : "LEG SIDE ►";
            PlayerCardKit.Text(ground, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(10, -40), new Vector2(0, 0), _leftHanded ? leg : off, 22, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f), condensed: true);
            PlayerCardKit.Text(ground, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, -40), new Vector2(-10, 0), _leftHanded ? off : leg, 22, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.6f), condensed: true);
            Fixed(ground, Draw(new Fielder("keeper", 180, 3)), "KEEPER");
            Fixed(ground, bowlerAt, "BOWLER");
            foreach (var spot in Field.Spots) SpotButton(ground, spot, _placed.Contains(spot.Name));

            // The column: counts, the rules, SAVE / CLEAR / BACK.
            var col = PanelKit.GlassPanel(page, "Rules", new Vector2(0.5f, 0.5f), new Vector2(500, -40), new Vector2(760, 820));
            var outside = Field.OutsideCount(nine);
            var legBehind = nine.Count(f => Field.LegBehindSquare(f.AngleDeg));
            var y = -40f;
            void Line(string s, int size, Color c, float h = 52f)
            {
                var t = PlayerCardKit.Text(col, new Vector2(0, 1), new Vector2(1, 1), new Vector2(36, y - h), new Vector2(-36, y), s, size, TextAnchor.MiddleLeft, c, condensed: true);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                y -= h;
            }
            Line($"{nine.Count} / 9 PLACED", 46, nine.Count == 9 ? UiKit.Good : Color.white, 70);
            Line($"OUTSIDE THE RING: {outside}  (POWERPLAY 2 · AFTER IT 5)", 28, outside > Field.MaxOutside(1) ? UiKit.Bad : outside > 2 ? UiKit.Gold : Color.white);
            Line($"LEG SIDE BEHIND SQUARE: {legBehind} / 2", 28, legBehind > 2 ? UiKit.Bad : Color.white);
            y -= 10;
            var problems = Field.Validate(nine, 1);
            var powerplayOk = Field.Validate(nine, 0).Count == 0;
            if (problems.Count == 0) Line(powerplayOk ? "A LEGAL FIELD, POWERPLAY TOO" : "A LEGAL FIELD AFTER THE POWERPLAY (NOT IN IT: TWO OUT)", 26, powerplayOk ? UiKit.Good : UiKit.Gold, 70);
            foreach (var p in problems.Take(4)) Line(p.ToUpperInvariant(), 24, UiKit.Bad, 60);
            if (_flash != null) Line(_flash, 24, UiKit.Gold, 60);
            var canSave = problems.Count == 0;
            var save = PanelKit.Pill(col, "Save", new Vector2(0.5f, 0), new Vector2(-180, 230), new Vector2(330, 120), "SAVE", canSave ? new Color(0.15f, 0.6f, 0.3f) : new Color(0.2f, 0.22f, 0.26f), () =>
            {
                if (Field.Validate(Nine(), 1).Count > 0) return;
                CustomFields.Save(_slot, Nine());
                Finish(true);
            }, 40);
            save.interactable = canSave;
            PanelKit.Pill(col, "Clear", new Vector2(0.5f, 0), new Vector2(180, 230), new Vector2(330, 120), "CLEAR", new Color(0.12f, 0.14f, 0.22f, 0.95f), () =>
            {
                _placed.Clear();
                _flash = null;
                Build();
            }, 40);
            PanelKit.Pill(col, "Back", new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(690, 120), "◄ BACK (NOT SAVED)", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => Finish(false), 36);
        }

        private void Fixed(RectTransform ground, Vector2 pos, string label)
        {
            var dot = PlayerCardKit.Shape(ground, "Fixed_" + label, CardSprites.Disc(), new Color(0.65f, 0.9f, 1f), pos, new Vector2(30, 30));
            dot.raycastTarget = false;
            PlayerCardKit.Text((RectTransform)dot.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-60, -30), new Vector2(60, -2), label, 16, TextAnchor.MiddleCenter, new Color(0.65f, 0.9f, 1f), condensed: true);
        }

        private void SpotButton(RectTransform ground, Fielder spot, bool on)
        {
            var at = Draw(spot);
            var size = new Vector2(48, 48);
            var face = PlayerCardKit.Shape(ground, "Spot_" + spot.Name, on ? CardSprites.Disc() : CardSprites.Ring(0.16f), on ? UiKit.Gold : new Color(1, 1, 1, 0.75f), at, size);
            face.raycastTarget = true;
            // A bigger invisible hit area than the dot (thumbs).
            var hit = PlayerCardKit.Shape((RectTransform)face.transform, "Hit", CardSprites.Disc(), new Color(1, 1, 1, 0.001f), Vector2.zero, new Vector2(70, 70));
            hit.raycastTarget = true;
            void Tap()
            {
                if (_placed.Contains(spot.Name)) _placed.Remove(spot.Name);
                else if (_placed.Count >= 9)
                {
                    _flash = "NINE PLACED: TAP ONE TO TAKE HIM OFF FIRST";
                    Build();
                    return;
                }
                else _placed.Add(spot.Name);
                _flash = null;
                Build();
            }
            face.gameObject.AddComponent<Button>().onClick.AddListener(Tap);
            hit.gameObject.AddComponent<Button>().onClick.AddListener(Tap);
            // The name outward from the middle, so the slips' labels don't sit on each other.
            var outward = at.sqrMagnitude > 1f ? at.normalized : Vector2.up;
            var label = PlayerCardKit.Text(ground, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at + outward * 38f - new Vector2(70, 14), at + outward * 38f + new Vector2(70, 14), Short(spot.Name), 17, TextAnchor.MiddleCenter, on ? UiKit.Gold : new Color(1, 1, 1, 0.85f), condensed: true);
            label.raycastTarget = false;
        }

        private void Finish(bool saved)
        {
            var done = _done;
            _done = null;
            Destroy(gameObject);
            done?.Invoke(saved);
        }
    }
}
