using System;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>
    /// One log line per sound played, so a test run proves what was heard and when
    /// (the rater can't listen): "[APL sfx] 12.34s bat/power bat_power_1 v0.98 @(0.1,0.9,-9.6)".
    /// On in test runs (-aplmatchtest, -aplmodesshots, -aplshots, -aplaudiolog) and
    /// development builds; silent in a release build otherwise.
    /// </summary>
    public static class SoundLog
    {
        private static bool? _on;

        public static bool On => _on ??= Debug.isDebugBuild || Application.isEditor
            || Environment.GetCommandLineArgs().Any(a => a == "-aplaudiolog" || a == "-aplmatchtest" || a == "-aplmodesshots" || a == "-aplshots");

        /// <summary>A sound: its kind ("bat/power", "crowd/cheer", "comm/four"), the clip, its volume, and where (null: not placed).</summary>
        public static void Played(string kind, AudioClip? clip, float volume, Vector3? at = null)
        {
            if (!On) return;
            var where = at.HasValue ? $" @({at.Value.x:0.0},{at.Value.y:0.0},{at.Value.z:0.0})" : "";
            Debug.Log($"[APL sfx] {Time.time:0.00}s {kind} {(clip != null ? clip.name : "-")} v{volume:0.00}{where}");
        }

        /// <summary>A named line (commentary, a call) whose clip is loaded elsewhere.</summary>
        public static void Said(string kind, string key)
        {
            if (!On) return;
            Debug.Log($"[APL sfx] {Time.time:0.00}s {kind} {key}");
        }
    }
}
