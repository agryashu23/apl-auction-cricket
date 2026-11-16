using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// After the winner's words the two captains shake hands (captain_handshake,
    /// ceremony.json): placed as the clip is made — 1.0 m apart, facing each other,
    /// both playing it — so the right hands come together halfway, 1 m up; through
    /// the grip frames (31–55) IK pins both hands to one point between them
    /// (PreMatchHandshake), blended in and out, so they really meet and pump together.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private const string Handshake = "captain_handshake";
        private const float GripIn = 31 / 30f, GripOut = 55 / 30f, ShakeLength = 100 / 30f, GripBlend = 0.18f;

        private IEnumerator Shake(Animator? winner, Animator? loser, Animator? presenter)
        {
            if (winner == null || loser == null || !Body.Has(winner, Handshake) || !Body.Has(loser, Handshake)) yield break;
            // The loser back in, the presenter out of the shot; a cut to the two of them side on.
            loser.gameObject.SetActive(true);
            if (presenter != null) presenter.gameObject.SetActive(false);
            var across = TossRight;
            var centre = TossMid;
            var a = centre - across * 0.5f;
            var b = centre + across * 0.5f;
            Stand(winner, a, across, Handshake);
            Stand(loser, b, -across, Handshake);
            Camera.Track(() => (centre - TossFwd * 3.3f + Vector3.up * 1.6f, centre + Vector3.up * 1.2f), fov: 36f, smooth: 0.3f, cut: true);
            _pre.Caption(null);

            var ha = winner.gameObject.AddComponent<PreMatchHandshake>();
            var hb = loser.gameObject.AddComponent<PreMatchHandshake>();
            ha.Other = hb;
            hb.Other = ha;
            Restart(winner, Handshake);
            Restart(loser, Handshake);
            // Eye to eye.
            PreMatchLook.Set(winner, PreMatchLook.Eyes(loser));
            PreMatchLook.Set(loser, PreMatchLook.Eyes(winner));
            Sfx.Applause(0.3f);
            for (var t = 0f; t < ShakeLength && !_pre.Skipped; t += Time.deltaTime)
            {
                // In over the strides before the grip, held through the pumps, out as they let go.
                var w = Mathf.SmoothStep(0, 1, (t - (GripIn - GripBlend)) / GripBlend) * (1 - Mathf.SmoothStep(0, 1, (t - GripOut) / GripBlend));
                ha.Weight = hb.Weight = w;
                yield return null;
            }
            Destroy(ha);
            Destroy(hb);
            if (presenter != null) presenter.gameObject.SetActive(true);
        }
    }
}
