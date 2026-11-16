using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core.Play;
using Newtonsoft.Json;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The user's CUSTOM fields (owner v26 #13): three slots, each nine of the editor's named spots
    /// (Field.Spots), kept on the phone (PlayerPrefs, JSON: slot → spot names) and handed to the engine
    /// (Field.SetCustom) at start-up and whenever the field screen opens. A saved spot the engine no
    /// longer knows drops the slot (it shows EMPTY), never a broken field.
    /// </summary>
    public static class CustomFields
    {
        private const string Key = "apl.fields.custom";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Load()
        {
            Dictionary<string, string[]>? saved = null;
            try { saved = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(PlayerPrefs.GetString(Key, "")); }
            catch (Exception ex) { Debug.LogWarning($"[APL field] custom fields unreadable: {ex.Message}"); }
            foreach (var slot in Field.CustomSlots)
            {
                var names = saved != null && saved.TryGetValue(slot.ToString(), out var n) ? n : null;
                var spots = names?.Select(Field.Spot).ToList();
                Field.SetCustom(slot, spots != null && spots.Count == 9 && spots.All(s => s != null) ? spots! : null);
            }
        }

        /// <summary>Keep <paramref name="slot"/> as these nine (null empties it).</summary>
        public static void Save(FieldPreset slot, IReadOnlyList<Fielder>? nine)
        {
            Field.SetCustom(slot, nine);
            var all = new Dictionary<string, string[]>();
            foreach (var s in Field.CustomSlots)
            {
                var f = Field.Custom(s);
                if (f != null) all[s.ToString()] = f.Select(x => x.Name).ToArray();
            }
            PlayerPrefs.SetString(Key, JsonConvert.SerializeObject(all));
            PlayerPrefs.Save();
            Debug.Log($"[APL field] {slot} saved: {(nine == null ? "empty" : string.Join(", ", nine.Select(x => x.Name)))}");
        }
    }
}
