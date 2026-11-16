using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// FLD-1 diagnostics, LOG ONLY (no behaviour): who plays or samples states on a body.
    /// Callers note each Play / CrossFade / sample they make (<see cref="Note"/>); FootLock, when a
    /// walking body's planted point jumps over 0.5 m in a frame, prints the animator's state with
    /// the notes from the last 2 frames ("[APL snap] …"); a note on a body that a walk owns
    /// (Strides.Busy) is printed at once ("[APL busy] …").
    /// </summary>
    public static class AnimTrace
    {
        private static readonly Dictionary<Animator, List<(int frame, string what)>> Recent = new Dictionary<Animator, List<(int, string)>>();

        public static void Note(Animator? a, string what, bool own = false, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (a == null || !Debug.isDebugBuild) return;
            var who = $"{what} by {System.IO.Path.GetFileNameWithoutExtension(file)}.{member}:{line}";
            if (!Recent.TryGetValue(a, out var list)) Recent[a] = list = new List<(int, string)>();
            list.Add((Time.frameCount, who));
            if (list.Count > 8) list.RemoveAt(0);
            if (!own && Strides.Busy(a)) Debug.Log($"[APL busy] {a.name} is being walked: {who} (frame {Time.frameCount})");
        }

        /// <summary>The last few notes on this body (deep area 1: the treadmill trace).</summary>
        public static string RecentNotes(Animator a)
        {
            if (!Recent.TryGetValue(a, out var list) || list.Count == 0) return "none noted";
            var parts = new List<string>();
            foreach (var (f, w) in list) parts.Add($"[f-{Time.frameCount - f}] {w}");
            return string.Join(" | ", parts);
        }

        /// <summary>From FootLock: the planted point leapt <paramref name="jump"/> m in one frame.</summary>
        public static void Snap(Animator a, float jump, string clip)
        {
            if (!Debug.isDebugBuild) return;
            var st = a.GetCurrentAnimatorStateInfo(0);
            var trans = a.IsInTransition(0);
            var next = trans ? a.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            var notes = new List<string>();
            if (Recent.TryGetValue(a, out var list))
                foreach (var (f, w) in list)
                    if (Time.frameCount - f <= 2) notes.Add($"[f-{Time.frameCount - f}] {w}");
            Debug.Log($"[APL snap] {a.name} walking {clip}: planted point jumped {jump:0.00} m in one frame (frame {Time.frameCount}); "
                      + $"state {(st.IsName(clip) ? clip : st.shortNameHash.ToString())} t={st.normalizedTime:0.000} speed {a.speed:0.00} "
                      + $"transition {trans}{(trans ? $" -> {next}" : "")}; calls in the last 2 frames: {(notes.Count == 0 ? "none noted" : string.Join(" | ", notes))}");
        }
    }
}
