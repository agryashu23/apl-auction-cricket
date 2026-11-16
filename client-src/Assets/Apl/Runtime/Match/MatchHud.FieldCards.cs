using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// "SET THE FIELD": tabs by how many men are out (owner v26 #13: AUTO · 5 OUT · 4 OUT · 3 OUT · 2 OUT ·
    /// ALL IN · CUSTOM), a card per field in the tab — its name, what it's for, and a mini radar of where
    /// the nine stand (as the field radar draws them: from behind the bowler, straight down at the bottom).
    /// Only fields legal for the phase can be picked (the powerplay's two-out limit: the 5/4/3-out tabs say
    /// so and stay shut). CUSTOM: three slots of the user's own, each with EDIT into the custom field editor
    /// (MatchHud.FieldEditor.cs); the countdown waits while he edits. The current one is lit; the countdown keeps it.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>
        /// The tabs (field rework 2026-11-16): the engine's groups of researched fields by bowler and phase (Field.Groups:
        /// AUTO, PACE PP, PACE MID, DEATH, SPIN, LEFT-ARM, CATCHERS), then CUSTOM.
        /// </summary>
        private static readonly (string label, FieldPreset[] fields)[] FieldTabs =
            Field.Groups.Append(("CUSTOM", Field.CustomSlots)).ToArray();

        /// <summary>Which tab a field is listed under.</summary>
        private static int TabOf(FieldPreset p, bool spin)
        {
            for (var i = 0; i < FieldTabs.Length; i++) if (FieldTabs[i].fields.Contains(p)) return i;
            return 0;
        }

        public IEnumerator PickField(FieldPreset current, int phase, bool spin, bool leftHanded, float seconds, Action<FieldPreset?> picked)
        {
            CustomFields.Load();
            var page = PickerPage.Open("SET THE FIELD", Field.PresetName(current).ToUpperInvariant(), null, null);
            FieldPreset? chosen = null;
            var tab = TabOf(current, spin);
            var editing = false;
            RectTransform? body = null;

            void Build()
            {
                if (body != null) Destroy(body.gameObject);
                body = (RectTransform)new GameObject("FieldTab", typeof(RectTransform)).transform;
                body.SetParent(page.Panel, false);
                body.anchorMin = Vector2.zero;
                body.anchorMax = Vector2.one;
                body.offsetMin = body.offsetMax = Vector2.zero;
                // The tabs.
                for (var i = 0; i < FieldTabs.Length; i++)
                {
                    var k = i;
                    var fill = i == tab ? UiKit.Gold : new Color(0.12f, 0.14f, 0.22f, 0.95f);
                    PanelKit.Pill(body, "FieldTab_" + i, new Vector2(0.5f, 1), new Vector2((i - (FieldTabs.Length - 1) / 2f) * 222f, -80), new Vector2(212, 120), FieldTabs[i].label, fill, () =>
                    {
                        tab = k;
                        Build();
                    }, 26);
                }
                var presets = FieldTabs[tab].fields;
                var custom = presets.Length > 0 && Field.IsCustom(presets[0]);
                for (var i = 0; i < presets.Length; i++)
                {
                    var p = presets[i];
                    var pos = new Vector2((i - (presets.Length - 1) / 2f) * 340f, custom ? 20f : -40f);
                    var legal = Field.LegalIn(p, phase, spin);
                    var (face, motion, button) = PlayerCardKit.Tile(body, "Field_" + p, pos, new Vector2(310, 340), UiKit.Accent, 0.04f * i);
                    FieldCard(face, Field.For(phase, spin, null, leftHanded, p), p, Field.IsCustom(p) && Field.Custom(p) == null, legal || (Field.IsCustom(p) && Field.Custom(p) == null) ? null : phase == 0 ? "POWERPLAY: ONLY TWO OUT" : "NOT A LEGAL FIELD");
                    motion.Selected = p == (chosen ?? current);
                    button.interactable = legal;
                    button.onClick.AddListener(() =>
                    {
                        motion.Press();
                        chosen = p;
                        page.Header.Subtitle.text = Field.PresetName(p).ToUpperInvariant();
                        Build();
                    });
                    if (!custom) continue;
                    var slot = p;
                    PanelKit.Pill(body, "Edit_" + p, new Vector2(0.5f, 0.5f), new Vector2(pos.x, -250), new Vector2(240, 120), Field.Custom(p) == null ? "SET ►" : "EDIT ►", new Color(0.85f, 0.45f, 0.1f), () =>
                    {
                        editing = true;
                        FieldEditor.Open(slot, phase, leftHanded, saved =>
                        {
                            editing = false;
                            if (saved && Field.LegalIn(slot, phase, spin))
                            {
                                chosen = slot;
                                page.Header.Subtitle.text = Field.PresetName(slot).ToUpperInvariant();
                            }
                            Build();
                        });
                    }, 30);
                }
            }

            Build();
            var until = Time.unscaledTime + seconds;
            var pickedAt = -1f;
            var wasChosen = (FieldPreset?)null;
            while (Time.unscaledTime < until || editing)
            {
                // The countdown waits while he sets a custom field.
                if (editing) until += Time.unscaledDeltaTime;
                page.Ring.Show(until - Time.unscaledTime, seconds);
                if (chosen != wasChosen)
                {
                    wasChosen = chosen;
                    pickedAt = Time.unscaledTime;
                }
                // A pick closes it a moment later (a custom slot's tile stays up while its tab is open: EDIT is beside it).
                if (!editing && pickedAt >= 0 && chosen != null && Time.unscaledTime - pickedAt > 0.45f) break;
                yield return null;
            }
            FieldEditor.CloseAll();
            page.Close();
            picked(chosen);
        }

        /// <summary>A preset's tile: its name, the mini radar, what it's for (an empty custom slot says so).</summary>
        private static void FieldCard(RectTransform face, Field field, FieldPreset p, bool empty = false, string? warning = null)
        {
            PlayerCardKit.Text(face, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -56), new Vector2(-10, -12), Field.PresetName(p).ToUpperInvariant(), 28, TextAnchor.MiddleCenter, Color.white, condensed: true);
            if (empty) PlayerCardKit.Text(face, new Vector2(0, 0.3f), new Vector2(1, 0.75f), new Vector2(14, 0), new Vector2(-14, 0), "EMPTY", 40, TextAnchor.MiddleCenter, UiKit.Dim, condensed: true);
            else MiniRadar(face, field, new Vector2(0, 6), 92f);
            PlayerCardKit.Text(face, new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 10), new Vector2(-14, 64), warning ?? Field.PresetNote(p), 18, TextAnchor.MiddleCenter, warning != null ? UiKit.Bad : UiKit.Dim, bold: false);
        }

        /// <summary>A small field radar: the ground, the pitch, the nine, keeper and bowler.</summary>
        private static void MiniRadar(RectTransform parent, Field field, Vector2 at, float r)
        {
            var disc = UiKit.Box(parent, "Radar", new Vector2(0.5f, 0.5f), at, new Vector2(r * 2, r * 2), new Color(0.05f, 0.3f, 0.1f, 0.85f));
            disc.GetComponent<Image>().sprite = Disc();
            disc.GetComponent<Image>().raycastTarget = false;
            UiKit.Box(disc, "Pitch", new Vector2(0.5f, 0.5f), new Vector2(0, -r * 0.07f), new Vector2(3, r * 0.14f), new Color(0.85f, 0.75f, 0.5f)).GetComponent<Image>().raycastTarget = false;
            var sign = field.LeftHanded ? 1f : -1f;
            void Dot(double angle, double radius, Color c, float size = 9)
            {
                var k = (float)Math.Min(0.94, radius / Math.Max(1, field.Rope(angle))) * r;
                var a = (float)(angle * Math.PI / 180);
                var dot = UiKit.Box(disc, "Dot", new Vector2(0.5f, 0.5f), new Vector2(sign * Mathf.Sin(a) * k, -Mathf.Cos(a) * k), new Vector2(size, size), c);
                dot.GetComponent<Image>().sprite = Disc();
                dot.GetComponent<Image>().raycastTarget = false;
            }
            // The 30-yard ring as it really is (half circles round each set of stumps, joined by straight lines), dotted.
            for (var deg = -180; deg < 180; deg += 8) Dot(deg, Field.RingRadius(deg, Field.InnerRing), new Color(1, 1, 1, 0.3f), 3);
            foreach (var f in field.Fielders) Dot(f.AngleDeg, f.Radius, Color.white);
            var pale = new Color(0.65f, 0.9f, 1f);
            // The keeper where he really stands (back to pace, up to spin) — the slips beside him, not in front.
            Dot(180, field.KeeperUp ? Field.KeeperUpDepth : Field.KeeperBackDepth, pale);
            Dot(0, 20, pale);
        }
    }
}
