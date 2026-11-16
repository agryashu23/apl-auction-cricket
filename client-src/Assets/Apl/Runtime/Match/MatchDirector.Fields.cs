using System;
using System.Collections;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The field screen (the user bowling): before each over, and from FIELD (on
    /// the bowling controls — any ball, while aiming) or the pause menu before the
    /// next ball — the presets with a mini radar each; Auto is the captain's own.
    /// A changed field is run into (each man jogs to his new spot before the
    /// run-up), not teleported. When the user bats, the AI captain picks
    /// (QuickMatch.FieldPresetNow) and PlaceField shows it.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>FIELD was pressed (controls or pause): the screen comes up before the next ball.</summary>
        private bool _fieldRequested;

        private IEnumerator FieldScreen()
        {
            _fieldRequested = false;
            if (Autoplay || _match.UserBatting) yield break;
            _bowlUi?.Hide();
            FieldPreset? chosen = null;
            var spin = _match.CurrentBowler.BowlType == BowlType.Spin;
            yield return _hud.PickField(_match.Preset, _match.Phase, spin, _match.OnStrike.Player.LeftHanded, Secs(15f), p => chosen = p);
            _bowlUi?.Show();
            if (chosen != null && chosen.Value != _match.Preset)
            {
                _match.Preset = chosen.Value;
                if (Debug.isDebugBuild) Debug.Log($"[APL fieldplan] user set the field: {Field.PresetName(chosen.Value)}");
                yield return RunToNewField();
                _hud.ShowScore(_match, BattingSide, FieldingSide);
            }
        }

        private bool _fieldScreenShown;

        /// <summary>
        /// Test runs (round 13 coverage): once a match, at the start of the user's second bowling over,
        /// the field screen comes up, is captured, and a different preset is chosen — the men jog to it.
        /// </summary>
        private IEnumerator AutoFieldScreen()
        {
            if (!Autoplay || _fieldScreenShown || _match.UserBatting || _match.Over != 1) yield break;
            _fieldScreenShown = true;
            yield return FieldMapTour(); // -aplfieldmap: every named field walked to and mapped from above
            _bowlUi?.Hide();
            var spin = _match.CurrentBowler.BowlType == BowlType.Spin;
            StartCoroutine(AfterReal(1.2f, () => Apl.Client.Dev.AutoShot.Moment("field_screen")));
            yield return _hud.PickField(_match.Preset, _match.Phase, spin, _match.OnStrike.Player.LeftHanded, 2.5f, _ => { });
            _bowlUi?.Show();
            // A normal field (owner, build 6: no close-catcher field on film unless the user picks one).
            var next = _match.Phase == 2 ? FieldPreset.Defensive : _match.Phase == 0 ? FieldPreset.PowerplayClassic : FieldPreset.Balanced;
            if (next == _match.Preset) next = _match.Phase == 0 ? FieldPreset.Attacking : FieldPreset.Defensive;
            // -aplfieldeditor (owner v26 #13): the custom field editor on film — a legal nine set in CUSTOM 1, played.
            if (Environment.GetCommandLineArgs().Contains("-aplfieldeditor"))
            {
                var nine = new[] { "slip", "gully", "point", "cover", "mid-off", "mid-on", "mid-wicket", "third man", "fine leg" }.Select(Field.Spot).ToList();
                CustomFields.Save(FieldPreset.Custom1, nine!);
                FieldEditor.Open(FieldPreset.Custom1, _match.Phase, _match.OnStrike.Player.LeftHanded, _ => { });
                yield return new WaitForSecondsRealtime(1.0f);
                Apl.Client.Dev.AutoShot.Moment("field_editor");
                yield return new WaitForSecondsRealtime(0.6f);
                FieldEditor.CloseAll();
                next = FieldPreset.Custom1;
            }
            _match.Preset = next;
            Debug.Log($"[APL match] field screen opened (test run): field set to {Field.PresetName(next)}");
            yield return RunToNewField();
            StartCoroutine(AfterReal(0.2f, () => Apl.Client.Dev.AutoShot.Moment("field_changed")));
            _hud.ShowScore(_match, BattingSide, FieldingSide);
        }

        /// <summary>
        /// The new field: everyone walks or jogs (feet locked, the crossing in MatchDirector.FieldMoves) from where
        /// he stands to his waiting spot in the engine's share-out, and the screen waits (up to 4 s) for them.
        /// </summary>
        private IEnumerator RunToNewField()
        {
            SyncFieldBodies();
            for (var i = 0; i < _fielders.Count; i++)
            {
                var (a, home) = _fielders[i];
                if (!a.gameObject.activeSelf) continue;
                var to = WaitSpot(i);
                var toBat = new Vector3(0, 0, -GroundSpace.HalfPitch) - home;
                if (Vector3.Distance(Flat(a.transform.position), to) < 0.3f) continue;
                StartCross(a, to, Quaternion.LookRotation(new Vector3(toBat.x, 0, toBat.z)));
            }
            for (var until = Time.time + 4f; Time.time < until && _fielders.Any(f => Crossing(f.a));) yield return null;
        }

        private IEnumerator JogInto(int i, Animator a, Vector3 to, float seconds)
        {
            SlideScene.Tag(a, "fielder field change");
            yield return RunTo(a, to, seconds, betweenBalls: true);
            if (a == null) yield break;
            var toBat = new Vector3(0, 0, -GroundSpace.HalfPitch) - _fielders[i].home;
            a.transform.rotation = Quaternion.LookRotation(new Vector3(toBat.x, 0, toBat.z));
            Loco(a, false);
            Crouch(i, a);
        }
    }
}
