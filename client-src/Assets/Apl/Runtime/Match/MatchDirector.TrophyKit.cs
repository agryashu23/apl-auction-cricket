using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The trophy lifter, made for the shot (characters, round 12): the winning captain on a
    /// new body from the cast in his own side's kit (CricketCast.Side, so his face, hair and
    /// beard match his card), with the side's cap, no batting or keeping gear. Whatever body
    /// the match had for him is hidden, so there's never a second him on the field.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private Animator? TrophyCaptain(Player captain, Franchise side)
        {
            var old = BodyOf(captain);
            if (old != null) old.gameObject.SetActive(false);
            if (!_cast.Ready) return old;
            var made = _cast.Side(new[] { captain }, side, "Trophy");
            if (!made.TryGetValue(captain.Id, out var body) || body == null) return old;
            body.gameObject.SetActive(true);
            _cast.CaptainCap(body, true);
            Debug.Log($"[APL] trophy: {captain.Name} lifts it in {side.ShortName}'s kit");
            return body;
        }
    }
}
