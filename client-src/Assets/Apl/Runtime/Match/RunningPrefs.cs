using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Running between the wickets, the user's setting: MANUAL (the default —
    /// RUN / CANCEL / DIVE) or AUTO (the batters take the engine's safe runs by
    /// themselves). Kept in PlayerPrefs (a new key in v19, so everyone starts on MANUAL).
    /// </summary>
    public static class RunningPrefs
    {
        private const string Key = "apl.running.auto.v19";

        public static bool Auto
        {
            get => PlayerPrefs.GetInt(Key, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }

    /// <summary>
    /// What the batters are doing right now, for anyone drawing it (the
    /// presentation's running picture-in-picture): read
    /// <c>MatchDirector.RunState</c> every frame.
    /// </summary>
    public sealed class RunningState
    {
        /// <summary>They're running (from setting off to the last crease, or the run out).</summary>
        public bool Active;

        /// <summary>The ball being run on, and its race (when the throw gets back: <c>Ball.Running.Back</c> s after <see cref="StartedAt"/>).</summary>
        public BallResult? Ball;

        /// <summary>The run they're on (1…), how many they're going for now, the engine's safe runs.</summary>
        public int Run, Going, Safe;

        /// <summary>Time.time they set off, and seconds a run takes.</summary>
        public float StartedAt, RunTime;

        /// <summary>The user is calling them (MANUAL), else AUTO / the AI.</summary>
        public bool Manual;

        /// <summary>MANUAL: they've been sent back (CANCEL early in a run), and the batter dived for the line.</summary>
        public bool TurnedBack, Dived;

        /// <summary>MANUAL: the DIVE button is lit (the final approach), and a dive would save him (the "DIVE!" prompt).</summary>
        public bool DiveLit, DiveMatters;

        /// <summary>Who set off from the striker's end and from the bowler's end.</summary>
        public BatterRig? FromStrikerEnd, FromBowlerEnd;

        /// <summary>The batter run out (null: nobody), once it's decided.</summary>
        public BatterRig? RunOut;
    }
}
