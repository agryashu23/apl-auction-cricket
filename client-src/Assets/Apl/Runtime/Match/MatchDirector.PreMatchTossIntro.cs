using System.Collections;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Before the coin goes up, the two captains are introduced: each on a
    /// close-up from in front with his card (name, side, role), about two
    /// seconds each — the opponent's first, yours last. SKIP moves on.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private IEnumerator CaptainIntros(Player myCap, Animator? capA, Player oppCap, Animator? capB, Animator? presenter)
        {
            // The presenter stands in front of them: out of these close-ups (he's back for the wide shot).
            if (presenter != null) presenter.gameObject.SetActive(false);
            foreach (var (p, body, team, mine) in new[] { (oppCap, capB, _bowl, false), (myCap, capA, _bat, true) })
            {
                if (body == null) continue;
                var hips = Hips(body);
                Camera.Track(() =>
                {
                    var f = Strides.BodyForward(body);
                    var right = Vector3.Cross(Vector3.up, f);
                    var chest = hips.position + Vector3.up * 0.3f;
                    // In front of him and a little to his right: he stands right of centre, the card on the left.
                    return (chest + f * 2.6f + right * 0.75f + Vector3.up * 0.1f, chest + right * 0.45f);
                }, fov: 32f, smooth: 0.2f, cut: true);
                _pre.ShowCaptain(p, team, mine);
                // He looks up at the camera for his card.
                var cam = Camera.transform;
                PreMatchLook.Set(body, () => cam.position);
                _pre.ResetSkip();
                yield return Hold(Secs(2.1f));
                _pre.HideCaptain();
                PreMatchLook.Set(body, null);
                if (_pre.Skipped) break;
            }
            _pre.HideCaptain();
            _pre.ResetSkip();
            if (presenter != null) presenter.gameObject.SetActive(true);
        }
    }
}
