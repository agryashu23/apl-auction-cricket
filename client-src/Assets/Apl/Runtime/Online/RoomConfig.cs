using System;
using UnityEngine;

namespace Apl.Client.Online
{
    /// <summary>
    /// Where the room server lives. In order: the <c>-aplroomserver &lt;url&gt;</c> flag (or
    /// <c>-aplroomtest &lt;url&gt; …</c>), the SERVER field on the online page (saved on the
    /// device), then the default server. An http(s) address is turned into the matching
    /// ws(s) socket address (…/ws). No live address is built into the source: the default is
    /// read from <c>Resources/room_server.txt</c> (git-ignored, written per build machine with
    /// the host that infra/rooms-cs/provision.ps1 prints), falling back to the local dev server.
    /// </summary>
    public static class RoomConfig
    {
        /// <summary>The local dev server (<c>dotnet run --project server-cs/AplRooms</c>).</summary>
        public const string FallbackServer = "http://localhost:8787";

        private static string? _default;

        /// <summary>The default room server: the first non-empty line of
        /// <c>Resources/room_server.txt</c>, or <see cref="FallbackServer"/> when that file is missing.</summary>
        public static string LocalDefault
        {
            get
            {
                if (_default != null) return _default;
                string? fromFile = null;
                var asset = Resources.Load<TextAsset>("room_server");
                if (asset != null)
                {
                    foreach (var line in asset.text.Split('\n'))
                    {
                        var t = line.Trim();
                        if (t.Length > 0 && !t.StartsWith("#")) { fromFile = t; break; }
                    }
                }
                _default = fromFile ?? FallbackServer;
                return _default;
            }
        }

        private const string PrefKey = "apl.roomServer";

        /// <summary>The address as typed or passed (http://host:port or ws://host:port/ws).</summary>
        public static string ServerAddress
        {
            get
            {
                var flag = Flag("-aplroomserver") ?? Flag("-aplroomtest");
                if (!string.IsNullOrWhiteSpace(flag)) return flag!.Trim();
                var saved = PlayerPrefs.GetString(PrefKey, "");
                return saved.Trim().Length > 0 ? saved.Trim() : LocalDefault;
            }
        }

        /// <summary>The address came from a command-line flag (the field shows it but can't change it).</summary>
        public static bool FromFlag => Flag("-aplroomserver") != null || Flag("-aplroomtest") != null;

        /// <summary>Saves the SERVER field (empty = back to the local default).</summary>
        public static void Save(string address)
        {
            PlayerPrefs.SetString(PrefKey, (address ?? "").Trim());
            PlayerPrefs.Save();
        }

        /// <summary>The socket URL for an address: http→ws, https→wss, "/ws" added when no path is given.</summary>
        public static string SocketUrl(string address)
        {
            var a = (address ?? "").Trim();
            if (a.Length == 0) a = LocalDefault;
            if (!a.Contains("://")) a = "http://" + a;
            if (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) a = "ws://" + a.Substring(7);
            else if (a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) a = "wss://" + a.Substring(8);
            if (Uri.TryCreate(a, UriKind.Absolute, out var uri) && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0))
            {
                a = a.TrimEnd('/') + "/ws";
            }
            return a;
        }

        private static string? Flag(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
        }
    }
}
