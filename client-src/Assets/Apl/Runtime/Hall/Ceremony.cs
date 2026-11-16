using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Audio;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Pre-auction team introductions: for each franchise the LED wall shows
    /// its crest and colours, its three table members walk on from the wings,
    /// pose while the auctioneer introduces them, and walk off. The human's
    /// team goes last. Skippable at any moment.
    /// </summary>
    public sealed class Ceremony : MonoBehaviour
    {
        public HallCamera Camera = null!;
        public VoiceQueue Auctioneer = null!;
        public Sfx Sfx = null!;

        /// <summary>The LED wall (colour) and the logo quad in front of it (crest).</summary>
        public Renderer LedWall = null!;
        public Renderer LedLogo = null!;

        /// <summary>The APL cover art on the LED wall; swapped out while a team is on.</summary>
        public Renderer? LedCover;

        /// <summary>Press photographers in front of the stage, shown for the ceremony.</summary>
        public Photographer[] Media = System.Array.Empty<Photographer>();

        /// <summary>Stage show effects (confetti for the finale).</summary>
        public StageFx? Fx;

        /// <summary>Where people enter/leave, and the three spots they pose on.</summary>
        public Transform LeftWing = null!, RightWing = null!;
        public Transform[] Marks = Array.Empty<Transform>();

        public RuntimeAnimatorController? StandingController;

        public float WalkSpeed = 1.7f;
        public float PoseSeconds = 2.8f;

        public bool Running { get; private set; }

        private bool _skip;
        private Texture? _defaultLogo;
        private Color _defaultLed;

        private static readonly int Walking = Animator.StringToHash("Walking");
        private static readonly int[] Poses =
        {
            Animator.StringToHash("ArmsCrossed"),
            Animator.StringToHash("FistPump"),
            Animator.StringToHash("Wave"),
            Animator.StringToHash("Victory"),
            Animator.StringToHash("ThumbsUp2"),
            Animator.StringToHash("HandFlash"),
        };

        private sealed class Seat
        {
            public SeatedMember Member = null!;
            public Transform Parent = null!;
            public Vector3 Position;
            public Quaternion Rotation;
            public RuntimeAnimatorController Controller = null!;
        }

        public void Skip() => _skip = true;

        public IEnumerator Run(IReadOnlyList<Franchise> teams, IReadOnlyDictionary<string, TeamTable> tables, string humanTeamId, Action? onDone)
        {
            Running = true;
            _skip = false;
            SetMedia(true);
            _defaultLogo = LedLogo.sharedMaterial.GetTexture(BaseMap);
            _defaultLed = LedWall.sharedMaterial.GetColor(BaseColor);

            // Owners' welcome to the whole room, then the walk-ons.
            Camera.Show("wide", reason: "ceremony-open");
            Sfx.SetCrowd(1.6f);
            Sfx.Applause(1f);
            Auctioneer.Say("arjun", "ceremony_owners", "ceremony_intro");
            yield return WaitForVoice(16f);
            Sfx.Cheer(0.6f);

            // Human's team last, for the build-up.
            var order = teams.Where(t => t.Id != humanTeamId).Concat(teams.Where(t => t.Id == humanTeamId)).ToList();
            for (var i = 0; i < order.Count && !_skip; i++)
            {
                var f = order[i];
                if (!tables.TryGetValue(f.Id, out var table)) continue;
                yield return Introduce(f, table, last: i == order.Count - 1);
            }

            RestoreLed();
            if (!_skip)
            {
                Camera.Show("wide", cut: false, reason: "ceremony-end");
                Auctioneer.Say("arjun", "ceremony_end");
                Sfx.Applause(1f);
                Sfx.Cheer(0.8f);
                yield return WaitForVoice(7f);
                yield return Wait(1.5f);
            }
            else
            {
                Auctioneer.Interrupt();
            }
            Sfx.SetCrowd(1f);
            SetMedia(false);
            Running = false;
            onDone?.Invoke();
        }

        private IEnumerator Introduce(Franchise f, TeamTable table, bool last)
        {
            ShowTeamOnLed(f);
            // The whole table walks on, as many as sit at it.
            var seats = table.Members.Select(Detach).ToList();
            var marks = MarksFor(seats.Count);
            // The column files in front of the podium and stays on the stage.
            var lane = seats.Count > 1 ? Mathf.Min(0.45f, 1.1f / (seats.Count - 1)) : 0f;
            var fromLeft = UnityEngine.Random.value < 0.5f;
            var wing = fromLeft ? LeftWing : RightWing;

            // Walk on.
            for (var k = 0; k < seats.Count; k++)
            {
                var m = seats[k].Member.transform;
                // File in front of the podium, never behind it (it stands in the way).
                m.position = wing.position + Vector3.back * (k * lane);
            }
            Camera.Show("ceremony_wide", reason: "walk-on " + f.Id);
            if (last) Auctioneer.Say("arjun", "ceremony_finally");
            Auctioneer.Say("arjun", "welcome_team_" + f.Id);
            Sfx.Applause(0.4f);
            yield return MoveAll(seats, k => marks[k], walking: true);
            if (_skip) { Return(seats); yield break; }

            // Face the hall and pose.
            foreach (var s in seats)
            {
                s.Member.transform.rotation = Quaternion.Euler(0, 180, 0);
                var anim = s.Member.GetComponent<Animator>();
                anim.SetBool(Walking, false);
                anim.SetTrigger(Poses[UnityEngine.Random.Range(0, Poses.Length)]);
            }
            Sfx.Applause(last ? 1f : 0.7f);
            Sfx.Cheer(last ? 1f : 0.75f);
            Camera.Show("ceremony_hero", reason: "team-photo " + f.Id);
            LogFacing(seats, f.Id);
            StartCoroutine(PhotoBurst(last ? 14 : 8));
            if (last) Fx?.Burst(260);
            yield return Wait(PoseSeconds);
            if (_skip) { Return(seats); yield break; }

            // Walk off the other side.
            var exit = fromLeft ? RightWing : LeftWing;
            yield return MoveAll(seats, k => exit.position + Vector3.back * (k * lane), walking: true);
            Return(seats);
        }

        /// <summary>
        /// Where <paramref name="count"/> people pose: spread across the front of
        /// the stage around the middle mark, bowed slightly towards the hall.
        /// </summary>
        private List<Vector3> MarksFor(int count)
        {
            var centre = Marks.Length > 0 ? Marks[Marks.Length / 2].position : new Vector3(0, 0.6f, 7.5f);
            // Shoulder to shoulder like a team photo (five fit the hero shot head to toe).
            const float spacing = 1.0f;
            var spots = new List<Vector3>();
            for (var k = 0; k < count; k++)
            {
                var offset = k - (count - 1) / 2f;
                spots.Add(centre + new Vector3(offset * spacing, 0, -0.08f * Mathf.Abs(offset)));
            }
            return spots;
        }

        /// <summary>Log only: how far each member's facing is from the camera at the photo (target under 25 degrees).</summary>
        private void LogFacing(List<Seat> seats, string teamId)
        {
            var cam = Camera.transform.position;
            var errs = new List<string>();
            foreach (var s in seats)
            {
                var t = s.Member.transform;
                var to = cam - t.position;
                to.y = 0;
                var fwd = t.forward;
                fwd.y = 0;
                errs.Add(Mathf.RoundToInt(Vector3.Angle(fwd, to)).ToString());
            }
            Debug.Log($"[APL hallcam] ceremony {teamId} facing err deg {string.Join(",", errs)}");
        }

        private Seat Detach(SeatedMember m)
        {
            var anim = m.GetComponent<Animator>();
            var seat = new Seat
            {
                Member = m,
                Parent = m.transform.parent,
                Position = m.transform.position,
                Rotation = m.transform.rotation,
                Controller = anim.runtimeAnimatorController,
            };
            m.transform.SetParent(null, true);
            if (StandingController != null) anim.runtimeAnimatorController = StandingController;
            return seat;
        }

        private static void Return(IEnumerable<Seat> seats)
        {
            foreach (var s in seats)
            {
                var t = s.Member.transform;
                t.SetParent(s.Parent, true);
                t.position = s.Position;
                t.rotation = s.Rotation;
                s.Member.GetComponent<Animator>().runtimeAnimatorController = s.Controller;
            }
        }

        private IEnumerator MoveAll(List<Seat> seats, Func<int, Vector3> target, bool walking)
        {
            foreach (var s in seats) s.Member.GetComponent<Animator>().SetBool(Walking, walking);
            var arrived = false;
            while (!arrived && !_skip)
            {
                arrived = true;
                for (var k = 0; k < seats.Count; k++)
                {
                    var t = seats[k].Member.transform;
                    var goal = target(k);
                    var to = goal - t.position;
                    to.y = 0;
                    if (to.magnitude > 0.05f)
                    {
                        arrived = false;
                        t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
                        t.position = Vector3.MoveTowards(t.position, new Vector3(goal.x, t.position.y, goal.z), WalkSpeed * Time.deltaTime);
                    }
                }
                yield return null;
            }
        }

        /// <summary>Until the auctioneer finishes (or skip / the cap).</summary>
        private IEnumerator WaitForVoice(float maxSeconds)
        {
            var end = Time.time + maxSeconds;
            yield return null;
            while (Auctioneer.Busy && !_skip && Time.time < end) yield return null;
        }

        private IEnumerator Wait(float seconds)
        {
            for (var t = 0f; t < seconds && !_skip; t += Time.deltaTime) yield return null;
        }

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        private void SetMedia(bool on)
        {
            foreach (var m in Media) m.gameObject.SetActive(on);
        }

        /// <summary>Flashes going off across the press pit, with shutters.</summary>
        private IEnumerator PhotoBurst(int shots)
        {
            for (var i = 0; i < shots && !_skip && Media.Length > 0; i++)
            {
                var who = Media[UnityEngine.Random.Range(0, Media.Length)];
                who.Flash();
                Sfx.CameraShutter(UnityEngine.Random.Range(0.25f, 0.4f));
                yield return new WaitForSeconds(UnityEngine.Random.Range(0.12f, 0.38f));
            }
        }

        private void ShowTeamOnLed(Franchise f)
        {
            if (LedCover != null) LedCover.enabled = false;
            LedLogo.enabled = true;
            var crest = Resources.Load<Texture2D>("Crests/" + f.Id);
            if (crest != null)
            {
                var m = LedLogo.material;
                m.SetTexture(BaseMap, crest);
                m.SetTexture(MainTex, crest);
                m.SetTexture(EmissionMap, crest);
            }
            var color = HallHud.ParseHex(f.PrimaryColor);
            var led = LedWall.material;
            led.SetColor(BaseColor, color * 0.6f);
            led.SetColor(ColorId, color * 0.6f);
            led.SetColor(Emission, color * 1.3f);
        }

        private void RestoreLed()
        {
            if (LedCover != null)
            {
                LedCover.enabled = true;
                LedLogo.enabled = false;
            }
            var m = LedLogo.material;
            if (_defaultLogo != null)
            {
                m.SetTexture(BaseMap, _defaultLogo);
                m.SetTexture(MainTex, _defaultLogo);
                m.SetTexture(EmissionMap, _defaultLogo);
            }
            var led = LedWall.material;
            led.SetColor(BaseColor, _defaultLed);
            led.SetColor(ColorId, _defaultLed);
            led.SetColor(Emission, _defaultLed * 1.4f);
        }
    }
}
