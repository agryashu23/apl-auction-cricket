using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 1 (reviewer S-1: the regex read 0 of the owner's sidecar entries — each has a nested "travel_m" object):
    /// every clip entry of every Resources/Data/cricket/*.json that has a "clips" array, parsed as JSON, by clip name (the
    /// "file" without ".fbx"). TurnStep reads the turns', Strides.StartStop the starts' and stops'. Read once.
    /// </summary>
    public static class ClipSidecars
    {
        private static Dictionary<string, JObject>? _all;

        public static Dictionary<string, JObject> All()
        {
            if (_all != null) return _all;
            _all = new Dictionary<string, JObject>();
            foreach (var t in Resources.LoadAll<TextAsset>("Data/cricket"))
            {
                if (!t.text.Contains("\"clips\"")) continue;
                try
                {
                    if (!(JToken.Parse(t.text) is JObject root) || !(root["clips"] is JArray clips)) continue;
                    foreach (var c in clips)
                    {
                        if (!(c is JObject o) || !(o["file"] is JValue f) || f.Type != JTokenType.String) continue;
                        var file = (string)f!;
                        _all[file.EndsWith(".fbx") ? file.Substring(0, file.Length - 4) : file] = o;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[APL strides] sidecar {t.name} not read: {e.Message}");
                }
            }
            return _all;
        }

        public static JObject? Of(string clip) => All().TryGetValue(clip, out var o) ? o : null;

        /// <summary>A number field (null if absent or not a number).</summary>
        public static float? Num(JObject? o, string key)
        {
            var v = o?[key];
            return v != null && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : (float?)null;
        }

        /// <summary>"travel_m": the ground distance, whether it's a number, [forward, left] or {"forward": …, "left": …}.</summary>
        public static float? Travel(JObject? o)
        {
            var v = o?["travel_m"];
            if (v == null) return null;
            if (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) return Mathf.Abs((float)v);
            float F(JToken? x) => x != null && (x.Type == JTokenType.Float || x.Type == JTokenType.Integer) ? (float)x : 0f;
            if (v is JArray arr && arr.Count > 0) return new Vector2(F(arr[0]), arr.Count > 1 ? F(arr[1]) : 0f).magnitude;
            if (v is JObject ob) return new Vector2(F(ob["forward"]), F(ob["left"])).magnitude;
            return null;
        }
    }
}
