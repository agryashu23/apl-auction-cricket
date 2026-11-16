using System;
using System.Collections.Generic;

namespace Apl.Core.Play
{
    /// <summary>
    /// The field for each over (the user's pick when he bowls, the AI captain's
    /// when he bats), the keeper up or back, and the strategic timeout.
    /// </summary>
    public sealed partial class QuickMatch
    {
        private readonly Dictionary<int, FieldPreset> _aiPreset = new Dictionary<int, FieldPreset>();
        private readonly Dictionary<int, bool> _keeperUp = new Dictionary<int, bool>();

        /// <summary>This over's field: the user's own when he's bowling, the AI captain's pick (fixed for the over) when he's batting.</summary>
        public FieldPreset FieldPresetNow
        {
            get
            {
                if (!UserBatting) return Preset;
                if (_aiPreset.TryGetValue(Over, out var p)) return p;
                p = Field.AiPreset(Situation, Field.KindOf(CurrentBowler), Over * 31 + InningsNumber * 7 + global::Apl.Core.Play.Bowling.Stable(CurrentBowler.Id));
                _aiPreset[Over] = p;
                return p;
            }
        }

        /// <summary>
        /// The keeper stands up this over: to spin and to medium and slow-medium
        /// pacers (stumpings on); back to the quicker men.
        /// </summary>
        public bool KeeperUp
        {
            get
            {
                var bowler = CurrentBowler;
                if (bowler.BowlType == BowlType.Spin) return true;
                var c = global::Apl.Core.Play.Bowling.PaceOf(bowler);
                // Up to the medium pacers too (the keeper rig stands up by the same rule).
                return c == PaceClass.Medium || c == PaceClass.SlowMedium;
            }
        }

        // ---------------------------------------------------------- the timeout

        /// <summary>The overs (0-based, taken at their start) the strategic timeout may fall in: 30%–75% of the innings; none under 5 overs.</summary>
        public (int from, int to) TimeoutWindow => Overs >= 5 ? ((int)Math.Ceiling(Overs * 0.3), (int)Math.Floor(Overs * 0.75)) : (-1, -1);

        /// <summary>This innings' timeout has been taken.</summary>
        public bool TimeoutTaken { get; private set; }

        /// <summary>At an over break inside the window, and not taken yet.</summary>
        public bool CanTakeTimeout
        {
            get
            {
                var (from, to) = TimeoutWindow;
                return from >= 0 && !TimeoutTaken && !Finished && LegalBalls > 0 && BallInOver == 0 && Over >= from && Over <= to;
            }
        }

        /// <summary>The timeout can still be called this innings (it's taken at the next over break inside the window).</summary>
        public bool TimeoutOpen
        {
            get
            {
                var (from, to) = TimeoutWindow;
                return from >= 0 && !TimeoutTaken && !Finished && Over <= to && Over >= from - 1;
            }
        }

        /// <summary>Not taken by the last over of the window: it's taken now (the fixed timeout).</summary>
        public bool TimeoutDue => CanTakeTimeout && Over == TimeoutWindow.to;

        /// <summary>Take this innings' timeout (false outside the window or once taken).</summary>
        public bool TakeTimeout()
        {
            if (!CanTakeTimeout) return false;
            TimeoutTaken = true;
            return true;
        }

        /// <summary>The new innings: its own timeout, fields, keeper.</summary>
        private void ResetBreaks()
        {
            TimeoutTaken = false;
            _aiPreset.Clear();
            _keeperUp.Clear();
        }
    }
}
