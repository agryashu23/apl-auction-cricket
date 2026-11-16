using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The ball-cam: as the ball comes within <see cref="BallCamLead"/> s of the bat,
    /// the broadcast camera eases in on the batter — a short dolly toward him and a
    /// slight zoom, as the reference game does — so the player sees bat meet ball;
    /// the hit then hands over to the follow / fielding camera (a higher priority
    /// takes it straight away). Played at <see cref="MatchCamera.Play"/>: anyone
    /// holding the camera (a moment, a replay) keeps it, and it never holds it.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>
        /// Real seconds before contact it starts; how long the push-in takes (it carries
        /// on a moment past contact); the share of the way to the batter it dollies; the
        /// zoom (field of view multiplier) at the end.
        /// </summary>
        public const float BallCamLead = 0.35f, BallCamSeconds = 0.5f, BallCamDolly = 0.22f, BallCamZoom = 0.72f;

        private void BallCam(float contactAt)
        {
            if (Camera.Busy) return;
            var lens = Camera.GetComponent<UnityEngine.Camera>();
            var fov0 = lens != null ? lens.fieldOfView : MatchCamera.BroadcastFov;
            var from = Camera.transform.position;
            // Where bat meets ball: just in front of the striker's crease, at about knee-to-waist height.
            var target = new Vector3(_striker.transform.position.x * 0.5f, 0.8f, -GroundSpace.HalfPitch + 1.5f);
            // Start looking where it already looks (at the batter's distance), so nothing jumps.
            var lookFrom = from + Camera.transform.forward * Vector3.Distance(from, target);
            var to = Vector3.Lerp(from, target, BallCamDolly);
            var t0 = Time.time;
            var seconds = Mathf.Max(BallCamSeconds, contactAt - t0 + 0.12f);
            Camera.TrackLens(() =>
            {
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - t0) / seconds));
                return (Vector3.Lerp(from, to, k), Vector3.Lerp(lookFrom, target, k), Mathf.Lerp(fov0, fov0 * BallCamZoom, k));
            }, smooth: 0.05f, cut: false, priority: MatchCamera.Play);
        }
    }
}
