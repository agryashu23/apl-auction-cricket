using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client
{
    /// <summary>
    /// A solo auction in progress, saved after every finished lot so closing
    /// the app doesn't lose it: the setup (seed, format, retentions, your team)
    /// plus the log of what happened, in the same actions friend rooms replay.
    /// Resuming rebuilds the engine from the setup and replays the log; a lot
    /// that was still open when the app closed starts again from its reveal.
    /// </summary>
    public sealed class SoloSave
    {
        public RoomSetup Setup = new RoomSetup();
        public string TeamId = "";
        public DateTime SavedAt;

        /// <summary>Actions of finished lots only.</summary>
        public List<JObject> Log = new List<JObject>();
        public int LotsDone;

        private readonly List<JObject> _pending = new List<JObject>();

        private static string FilePath => Path.Combine(Application.persistentDataPath, "solo_save.json");

        public static SoloSave Begin(RoomSetup setup, string teamId) => new SoloSave { Setup = setup, TeamId = teamId };

        /// <summary>Feed it every event of the live auction.</summary>
        public void Record(LiveEvent e)
        {
            if (e is AuctionFinished)
            {
                Delete();
                return;
            }
            _pending.Add(RoomAction.ForEvent(e).ToJson());
            if (!(e is LotClosed)) return;
            Log.AddRange(_pending);
            _pending.Clear();
            LotsDone++;
            Write();
        }

        /// <summary>The engine as it stood after the last finished lot.</summary>
        public AuctionEngine Rebuild(Dataset data)
        {
            var engine = Setup.BuildEngine(data);
            foreach (var a in Log) RoomReplay.Apply(engine, RoomAction.FromJson(a));
            return engine;
        }

        private void Write()
        {
            SavedAt = DateTime.UtcNow;
            var retentions = new JObject();
            foreach (var kv in Setup.Retentions) retentions[kv.Key] = new JArray(kv.Value);
            var json = new JObject
            {
                ["team"] = TeamId,
                // Ticks, not an ISO string: Newtonsoft re-reads date strings as dates.
                ["savedAt"] = SavedAt.Ticks,
                ["lotsDone"] = LotsDone,
                ["setup"] = new JObject
                {
                    ["seed"] = Setup.Seed,
                    ["seasonSeed"] = Setup.SeasonSeed,
                    ["format"] = Setup.FormatId,
                    ["retention"] = Setup.Retention,
                    ["retentions"] = retentions,
                    ["dataVersion"] = Setup.DataVersion,
                    // IPL sets + solo cap on resume (RoomSetup.Solo); older saves lack it and resume as made.
                    ["solo"] = Setup.Solo,
                },
                ["log"] = new JArray(Log),
            };
            try
            {
                // Write aside, then swap in, so a kill mid-write leaves the old save.
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json.ToString(Newtonsoft.Json.Formatting.None));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] could not save the auction: {ex.Message}");
            }
        }

        /// <summary>The saved auction, or null (none, unreadable, or made with other data).</summary>
        public static SoloSave? Load(Dataset data)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var j = JObject.Parse(File.ReadAllText(FilePath));
                var save = new SoloSave
                {
                    TeamId = (string)j["team"]!,
                    SavedAt = new DateTime((long)j["savedAt"]!, DateTimeKind.Utc),
                    LotsDone = (int)j["lotsDone"]!,
                    Setup = RoomSetup.FromJson((JObject)j["setup"]!),
                    Log = ((JArray)j["log"]!).OfType<JObject>().ToList(),
                };
                // Player data changed since (a new dataset): the log no longer fits.
                if (save.Setup.DataVersion != data.Version || data.Rules.FormatList.All(f => f.Id != save.Setup.FormatId)) return null;
                return save;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] ignoring a broken save: {ex.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] could not delete the save: {ex.Message}");
            }
        }
    }
}
