using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// One card at a time (round 8 rating: TOO LATE under BEATEN, IMPACT PLAYER through the
    /// innings break, BOWLING CHANGE over the bowler's intro, "2 RUNS" over the next ball's
    /// controls). The transient overlays — the centre banner (Banner: run results, BOWLING
    /// CHANGE, IMPACT PLAYER, captaincy, challenges) and the big event graphic (4 / 6 / BOWLED
    /// / FREE HIT / WIN) — go through this stage: one shows; a higher priority one takes over;
    /// the rest wait their turn (and are dropped if they've waited too long to still mean
    /// anything). While a scheduled panel is up — a full card (break, result), the player
    /// intro card, the over card, the replay, the timing stamp, the pause menu — nothing on
    /// the stage shows, and what's showing is put away. <see cref="NextBall"/> clears the
    /// stage for the next delivery (after a minimum hold so nothing just flashes).
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>Stage priorities: the event graphic over a banner, the result of the match over all.</summary>
        public const int BannerPriority = 1, EventPriority = 2, FinalePriority = 3;

        private sealed class Overlay
        {
            public string Key = "";
            public int Priority;
            /// <summary>Shown at least this long before the next ball may take it away (s).</summary>
            public float MinHold;
            /// <summary>Waiting longer than this (s) it's stale: dropped.</summary>
            public float MaxWait = 4f;
            public Action Show = null!, Hide = null!;
            public Func<bool> Alive = null!;
            public float QueuedAt, ShownAt, HideAt = -1f;
        }

        private Overlay? _stage;
        private readonly List<Overlay> _waiting = new List<Overlay>();
        private readonly List<Func<bool>> _blockers = new List<Func<bool>>();
        /// <summary>The stage's own clock: real time, stopped while paused.</summary>
        private float _stageClock;

        /// <summary>What holds the stage clear: full cards, intros, the over card, replays, the timing stamp, pause.</summary>
        private bool StageBlocked()
        {
            if (_blockers.Count == 0)
            {
                _blockers.Add(() => _card != null);
                _blockers.Add(() => _playerCard != null);
                _blockers.Add(() => _overCard != null || _pitchMap != null);
                _blockers.Add(() => _replayBar != null);
                _blockers.Add(() => _pauseMenu != null);
                _blockers.Add(() => _stamp != null && _stamp.Showing);
            }
            foreach (var b in _blockers)
            {
                if (b()) return true;
            }
            return false;
        }

        private void Stage(string key, int priority, Action show, Action hide, Func<bool> alive, float maxWait = 4f, float minHold = 0f)
        {
            var o = new Overlay { Key = key, Priority = priority, Show = show, Hide = hide, Alive = alive, QueuedAt = _stageClock, MaxWait = maxWait, MinHold = minHold };
            if (_stage != null && !_stage.Alive()) _stage = null;
            if (!StageBlocked() && (_stage == null || priority > _stage.Priority))
            {
                if (_stage != null) _stage.Hide();
                Put(o);
                return;
            }
            // The same notice again replaces its older self in the line.
            _waiting.RemoveAll(w => w.Key == key);
            _waiting.Add(o);
        }

        private void Put(Overlay o)
        {
            _stage = o;
            o.ShownAt = _stageClock;
            o.Show();
        }

        /// <summary>Each frame (MatchHud.Update): age the line, put away what's blocked, bring on the next.</summary>
        private void StageUpdate()
        {
            if (!Paused) _stageClock += Time.unscaledDeltaTime;
            Watchdog();
            // SKIP only while a broadcast moment is on (every SetSkippable(true) is inside one): never in live play (r10 f84/88/204).
            if (_tap != null && _tap.activeSelf && _broadcasting == 0) _tap.SetActive(false);
            if (_stage != null && !_stage.Alive()) _stage = null;
            if (_stage != null && _stage.HideAt >= 0 && _stageClock >= _stage.HideAt)
            {
                _stage.Hide();
                _stage = null;
            }
            _waiting.RemoveAll(w => _stageClock - w.QueuedAt > w.MaxWait);
            if (StageBlocked())
            {
                if (_stage != null)
                {
                    _stage.Hide();
                    // A long notice cut off by an intro or a card comes back after it, read in full.
                    if (Sticky(_stage) && _stageClock - _stage.ShownAt < _stage.MinHold)
                    {
                        _stage.QueuedAt = _stageClock;
                        _stage.MaxWait = 10f;
                        _stage.HideAt = -1f;
                        _waiting.Add(_stage);
                    }
                    _stage = null;
                }
                return;
            }
            if (_stage != null || _waiting.Count == 0) return;
            var next = _waiting[0];
            foreach (var w in _waiting)
            {
                if (w.Priority > next.Priority) next = w;
            }
            _waiting.Remove(next);
            Put(next);
        }

        /// <summary>
        /// The next delivery is starting (its controls are coming up): the waiting line is dropped and
        /// the card on the stage goes once it has had its minimum hold.
        /// </summary>
        public void NextBall()
        {
            // Never into a ball frozen: whatever stopped the clock (a moment cut short) is over now (r12 hang).
            ClockReset("the ball's start");
            _waiting.RemoveAll(w => !Sticky(w));
            if (_stage == null || !_stage.Alive()) return;
            if (_stage.Priority >= FinalePriority || Sticky(_stage)) return;
            var hideAt = _stage.ShownAt + _stage.MinHold;
            if (_stageClock >= hideAt)
            {
                _stage.Hide();
                _stage = null;
            }
            else _stage.HideAt = hideAt;
        }

        /// <summary>Time back to normal unless the pause menu has it; logged when it had been left stopped.</summary>
        public void ClockReset(string where)
        {
            if (Paused || Time.timeScale > 0f) return;
            Debug.LogWarning($"[APL match] clock was stopped at {where}: reset to 1");
            Time.timeScale = 1f;
        }

        private float _stoppedFor;

        /// <summary>
        /// The clock stopped with NOBODY holding it (<see cref="ClockHeld"/>: no frozen moment alive) for
        /// 5 s outside the pause menu is an abandoned freeze: restarted, the owner told to clean up
        /// (<see cref="ClockRestarted"/>), and logged ("CLOCK STOPPED" — an error in test runs). Every build:
        /// a player must never be left on a frozen game. Test runs also log a frame over 5 s ("FRAME STALL").
        /// </summary>
        private void Watchdog()
        {
            var test = AutoContinue != null && AutoContinue();
            if (test && Time.unscaledDeltaTime > 5f)
                Debug.LogError($"[APL match] FRAME STALL: one frame took {Time.unscaledDeltaTime:0.0}s (on air {_broadcasting}, card {(_card != null ? _card.name : "-")}); last steps:{StallProbe.Recent()}");
            if (Time.timeScale > 0f || Paused || ClockIsHeld)
            {
                _stoppedFor = 0f;
                return;
            }
            _stoppedFor += Mathf.Min(Time.unscaledDeltaTime, 1f);
            if (_stoppedFor < 5f) return;
            var line = $"[APL match] CLOCK STOPPED for {_stoppedFor:0}s outside the pause menu with no moment holding it (on air {_broadcasting}, card {(_card != null ? _card.name : "-")}): restarting it; last steps:{StallProbe.Recent()}";
            if (test) Debug.LogError(line);
            else Debug.LogWarning(line);
            Time.timeScale = 1f;
            _stoppedFor = 0f;
            RaiseClockRestarted();
        }

        /// <summary>A notice meant to be read in full (3 s or more): it waits out intros and survives the next ball.</summary>
        private static bool Sticky(Overlay o) => o.MinHold >= 3f;

        /// <summary>Clear the stage and its line at once (a scene change: the finale, the result).</summary>
        public void ClearStage()
        {
            _waiting.Clear();
            if (_stage != null && _stage.Alive()) _stage.Hide();
            _stage = null;
        }
    }
}
