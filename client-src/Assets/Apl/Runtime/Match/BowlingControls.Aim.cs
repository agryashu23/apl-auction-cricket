using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// Aiming that glides: the stick has a dead zone and a response curve (fine
    /// near the centre, quick at the edge), its speed eases up and down, and the
    /// marker on the pitch eases after the aim point instead of jumping. A drag on
    /// the pitch nudges the aim by a fraction of the finger's travel (a trackpad,
    /// not a jump: owner v26 #15 "direct setting is too much, can't be controlled").
    /// The aim stays on a sensible line and length.
    /// </summary>
    public sealed partial class BowlingControls
    {
        /// <summary>
        /// Stick dead zone and curve; top speeds across and along the pitch (m/s); how fast the
        /// speed builds and dies away (1/s); how fast the drawn marker catches the aim (1/s).
        /// Round 10 (owner: "not smooth"): slower tops, a gentler build-up and a softer follow,
        /// and the stick's own reading filtered so a shaky thumb doesn't jitter the marker.
        /// Owner v26 #15 ("stiff and over-sensitive, can't be controlled"): lower top speeds, a steeper
        /// curve so most of the stick's travel is fine control, a softer build-up, and a damped glide.
        /// </summary>
        private const float DeadZone = 0.15f, Curve = 2.3f, MaxAcross = 0.42f, MaxAlong = 3.3f, Accel = 3.5f, Brake = 7f, Follow = 6f, StickFilter = 9f;

        /// <summary>A drag on the pitch moves the aim this share of the finger's travel over the ground.</summary>
        private const float DragGain = 0.45f;

        /// <summary>Where the dragging finger was last on the pitch (null: not dragging).</summary>
        private Vector2? _dragFrom;

        /// <summary>The stick's reading, filtered.</summary>
        private Vector2 _raw;

        /// <summary>Where the aim may go: from well down the leg side to wide of off, from the toes to a bouncer (spin: a long hop).</summary>
        private const float MinX = -0.7f, MaxX = 1.1f, MinZ = 0.4f, MaxZPace = 11.8f, MaxZSpin = 9.5f;

        private Vector2 _vel, _shown;
        private bool _shownSet;
        private GameObject _pad = null!;

        /// <summary>Where the marker is drawn (eased after <see cref="Mark"/>, the aim the ball is bowled at).</summary>
        public Vector2 Shown => _shown;

        private void BuildAim()
        {
            // Under everything: a tap or drag on the pitch while aiming puts the aim there.
            var pad = UiKit.Stretch(_root, "PitchPad", new Color(1, 1, 1, 0.001f));
            pad.SetAsFirstSibling();
            var trigger = pad.gameObject.AddComponent<EventTrigger>();
            void On(EventTriggerType type, System.Action<Vector2> act)
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(e => { if (e is PointerEventData p) act(p.position); });
                trigger.triggers.Add(entry);
            }
            // Down: where the finger is (nothing moves); drag: the aim follows a share of its travel.
            On(EventTriggerType.PointerDown, s => _dragFrom = PitchAt(s));
            On(EventTriggerType.Drag, DragAim);
            On(EventTriggerType.PointerUp, _ => _dragFrom = null);
            _pad = pad.gameObject;
            // (No length name under the marker any more: the owner found the floating labels drew over things.)
        }

        private bool Spin => _bowler != null && _bowler.BowlType == BowlType.Spin;

        /// <summary>The aim kept on the pitch: a sensible line, a length from the toes to a bouncer.</summary>
        private Vector2 Clamp(Vector2 m) => new Vector2(Mathf.Clamp(m.x, MinX, MaxX), Mathf.Clamp(m.y, MinZ, Spin ? MaxZSpin : MaxZPace));

        /// <summary>The stick (or arrows) as a velocity: nothing inside the dead zone, then a curve up to full speed, eased in and out.</summary>
        private void AimUpdate(Vector2 input)
        {
            // Filter the thumb first (a let-go drops straight to zero: no drift after release).
            _raw = input == Vector2.zero ? Vector2.zero : Vector2.Lerp(_raw, input, 1 - Mathf.Exp(-StickFilter * Time.deltaTime));
            var raw = _raw;
            var m = Mathf.Clamp01(raw.magnitude);
            var want = Vector2.zero;
            if (m > DeadZone)
            {
                var k = Mathf.Pow((m - DeadZone) / (1 - DeadZone), Curve);
                var dir = raw / m;
                // From behind the bowler: stick up = fuller (towards the batter); stick left = screen-left = a right-hander's off side.
                want = new Vector2(-dir.x * (_leftHanded ? -1 : 1) * MaxAcross, -dir.y * MaxAlong) * k;
            }
            // Builds up gently, dies away a little quicker (the marker stops where he lets go).
            var rate = want.sqrMagnitude > _vel.sqrMagnitude ? Accel : Brake;
            _vel = Vector2.Lerp(_vel, want, 1 - Mathf.Exp(-rate * Time.deltaTime));
            if (_vel.sqrMagnitude < 1e-5f && want == Vector2.zero) _vel = Vector2.zero;
            var next = Clamp(Mark + _vel * Time.deltaTime);
            // At an edge the speed into it goes (no wind-up to fight when he turns back).
            if (!Mathf.Approximately(next.x, Mark.x + _vel.x * Time.deltaTime)) _vel.x = 0;
            if (!Mathf.Approximately(next.y, Mark.y + _vel.y * Time.deltaTime)) _vel.y = 0;
            Mark = next;
        }

        /// <summary>The marker eases after the aim; the length's name rides under it.</summary>
        private void ShowUpdate()
        {
            if (!_shownSet)
            {
                _shown = Mark;
                _shownSet = true;
            }
            var before = _shown;
            _shown = Vector2.Lerp(_shown, Mark, 1 - Mathf.Exp(-Follow * Time.deltaTime));
            if ((_shown - before).sqrMagnitude > 1e-8f) MarkChanged?.Invoke(_shown);
            _pad.SetActive(Now == Phase.Aim);
        }

        /// <summary>YORKER / FULL / GOOD / SHORT / BOUNCER (a spinner's very short one is a LONG HOP).</summary>
        private string BandName(float z)
        {
            var l = Bowling.LengthOf(z, Spin);
            return l switch
            {
                Length.Yorker => "YORKER",
                Length.Full => "FULL",
                Length.Good => "GOOD LENGTH",
                Length.Short => "SHORT",
                _ => Spin ? "LONG HOP" : "BOUNCER",
            };
        }

        /// <summary>The point on the ground under <paramref name="screen"/>, in the aim's frame (null: not on or near the pitch).</summary>
        private Vector2? PitchAt(Vector2 screen)
        {
            if (Now != Phase.Aim) return null;
            var cam = _director.ViewCamera;
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return null;
            var p = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            var mark = new Vector2(_leftHanded ? -p.x : p.x, p.z + GroundSpace.HalfPitch);
            // Only on (or near) the pitch: a drag out in the field isn't aiming.
            if (Mathf.Abs(p.x) > 4f || mark.y < -3f || mark.y > 18f) return null;
            return mark;
        }

        /// <summary>A drag on the pitch: the aim moves a share of the finger's travel (the marker glides after it).</summary>
        private void DragAim(Vector2 screen)
        {
            var at = PitchAt(screen);
            if (at == null || _dragFrom == null)
            {
                _dragFrom = at;
                return;
            }
            var delta = (at.Value - _dragFrom.Value) * DragGain;
            _dragFrom = at;
            _vel = Vector2.zero;
            Mark = Clamp(Mark + delta);
        }
    }
}
