using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// SIMULATE (solo): finish the rest of the auction at once. Asks first; the hall stops while
    /// the question is up. Every AI team bids as usual; your team gets an auto-pilot that only
    /// buys to reach the minimum squad and fill role gaps (keeper, bowlers …) within your purse.
    /// All the rules hold (purse, 18–25 squad, overseas cap, price cap, RTM, accelerated round,
    /// the base-price auto-fill), then the usual end: the closing line and the results screen.
    /// </summary>
    public sealed partial class HallDirector
    {
        private bool _simulating;

        private void AskSimulate()
        {
            if (Room != null)
            {
                AskRoomSimulate();
                return;
            }
            if (!(_driver is LiveAuction) || _simulating || _driver.Phase == LivePhase.Finished || ConfirmDialog.Open) return;
            // The clock stops while you decide (like the hub).
            _hubPaused = true;
            _driver.HoldingFastForward = false;
            _hud.SetFastForward(false, _driver.CanHoldFastForward);
            var me = _driver.Engine.Teams[_me];
            var f = _driver.Engine.Format;
            ConfirmDialog.Show(
                "SIMULATE TO THE END?",
                $"The AI finishes the auction for every team now. Your team auto-bids only to reach {f.MinSquad} players and fill its role gaps, within your {Money.Format(me.Purse)} purse.",
                "SIMULATE",
                "CANCEL",
                () => RunSimulate(autoBid: true),
                () => _hubPaused = false);
        }

        private void RunSimulate(bool autoBid)
        {
            if (!(_driver is LiveAuction live) || _driver.Phase == LivePhase.Finished) return;
            _simulating = true;
            var engine = _driver.Engine;
            var before = engine.Teams.Values.ToDictionary(t => t.Id, t => (t.Size, t.Purse));
            var lotsBefore = engine.Results.Count;
            Debug.Log($"[APL simulate] start at lot {lotsBefore + (engine.CurrentLot != null ? 1 : 0)}, {engine.LotsRemaining} lots queued, autoBid={autoBid}, you={_me}");

            // Quiet the hall: no card, no prompt, no pending calls; the finish line comes from AuctionFinished.
            Auctioneer.Interrupt();
            _returnToStageAt = -1;
            _hud.ShowRtmPrompt("", false);
            Cast.ClearCard();
            AuctionLog.Quiet = true;
            _racing = true;
            try
            {
                live.SimulateToEnd(autoBid, engine.Results.Count * 7919L + 17);
            }
            finally
            {
                _racing = false;
                AuctionLog.Quiet = false;
                _hubPaused = false;
            }

            Debug.Log($"[APL simulate] done: {engine.Results.Count - lotsBefore} lots settled ({engine.Results.Skip(lotsBefore).Count(r => r.Sold)} sold), finished={engine.IsFinished}");
            foreach (var t in engine.Teams.Values)
            {
                var (size, purse) = before[t.Id];
                Debug.Log($"[APL simulate] {t.Id}{(t.Id == _me ? " (you)" : "")}: squad {size}->{t.Size}/{engine.Format.MaxSquad}, purse {Money.Format(purse)}->{Money.Format(t.Purse)}, " +
                          $"WK/BAT/AR/BOWL {t.CountRole(Role.WicketKeeper)}/{t.CountRole(Role.Batter)}/{t.CountRole(Role.AllRounder)}/{t.CountRole(Role.Bowler)}, " +
                          $"overseas {t.OverseasCount}/{engine.Format.MaxOverseas}, auto-fill {t.Squad.Count(p => p.Kind == PurchaseKind.AutoFill)}, RTM left {t.RtmCards}");
            }
        }
    }
}
