using System;
using System.Collections;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The full match's cards (rules-bowling): the bowler cards each over, the
    /// innings break with the target, and the match result.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>The HUD's soft disc, for the bowling controls' stick and buttons.</summary>
        public static Sprite DiscSprite => Disc();

        /// <summary>The centre banner off now (a run card mustn't sit over the next ball's bowling controls).</summary>
        public void ClearBanner()
        {
            _bannerUntil = 0;
            if (_bannerRoot != null) _bannerRoot.SetActive(false);
        }

        /// <summary>
        /// The one exit for the runs card ("NO RUN", "1 RUN", "2 RUNS", "DROPPED!"): off before the
        /// next ball's controls, on every path (r9 shot 195). Other banners (BOWLING CHANGE…) stay.
        /// </summary>
        public void ClearRunCard()
        {
            if (_bannerRoot == null || !_bannerRoot.activeSelf) return;
            var t = _banner.text;
            if (t.EndsWith(" RUN") || t.EndsWith(" RUNS") || t == "NO RUN" || t == "DROPPED!") ClearBanner();
        }

        /// <summary>The innings break: the first innings' score and top performers, the target, START.</summary>
        public void ShowInningsBreak(Innings first, Franchise firstBat, Franchise firstField, QuickMatch m, Franchise chasing) =>
            InningsBreakScreen(first, firstBat, m, chasing); // card kit: MatchHud.BreakCard.cs (presentation)

        /// <summary>The result of a full match, from the user's side.</summary>
        public void ShowMatchResult(QuickMatch m, Franchise user, Franchise opp) => ResultFor(m, user, opp); // the card-kit result screen (MatchHud.ResultCard.cs)
    }
}
