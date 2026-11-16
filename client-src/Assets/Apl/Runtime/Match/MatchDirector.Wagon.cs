using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The wagon wheels' book: every scoring shot each batter plays (where it went,
    /// how far, for how many), shown after he's out, when he reaches fifty, and
    /// for the pair at the crease from the pause menu.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Shots by "innings:player id"; who each key is and for which side (the explorer's filters).</summary>
        private readonly Dictionary<string, List<WagonShot>> _wagon = new Dictionary<string, List<WagonShot>>();
        private readonly Dictionary<string, (Player who, string team, int innings)> _wagonWho = new Dictionary<string, (Player, string, int)>();

        private string WagonKey(string id, int innings) => innings + ":" + id;

        /// <summary>The batter facing this ball (set as it starts: the strike may change before it's booked).</summary>
        private Player? _facing;

        /// <summary>Book the ball for the man who faced it; true if it took him past fifty.</summary>
        private bool BookShot(BallResult r)
        {
            if (_facing == null || r.ExitSpeed <= 0 || r.BatRuns <= 0) return false;
            var inns = _match.InningsNumber;
            var key = WagonKey(_facing.Id, inns);
            if (!_wagon.TryGetValue(key, out var list))
            {
                _wagon[key] = list = new List<WagonShot>();
                _wagonWho[key] = (_facing, BattingSide.ShortName, inns);
            }
            var (x, f) = r.End;
            var reach = r.Four || r.Six ? 1f : Mathf.Clamp01((float)(System.Math.Sqrt(x * x + f * f) / 68.0));
            list.Add(new WagonShot((float)r.ExitAngle, reach, r.Six ? 6 : r.Four ? 4 : r.BatRuns, _facing.LeftHanded, inns));
            var line = _match.Order.FirstOrDefault(b => b.Player.Id == _facing.Id);
            return line != null && line.Runs >= 50 && line.Runs - r.BatRuns < 50;
        }

        private (IReadOnlyList<WagonShot> shots, bool leftHanded, string title, string line) Wheel(Player p)
        {
            var b = _match.Order.FirstOrDefault(x => x.Player.Id == p.Id);
            var shots = _wagon.TryGetValue(WagonKey(p.Id, _match.InningsNumber), out var list) ? list : new List<WagonShot>();
            var text = b == null ? "" : $"{b.Runs} ({b.Balls})  ·  {b.Fours}×4  {b.Sixes}×6";
            return (shots, p.LeftHanded, p.Name.ToUpperInvariant(), text);
        }

        /// <summary>Every batter's book, both innings, for the explorer (MatchHud.WagonExplorer.cs); in batting order.</summary>
        private IReadOnlyList<WagonBook> WagonBooks()
        {
            var books = new List<WagonBook>();
            foreach (var kv in _wagonWho)
            {
                var (who, team, inns) = kv.Value;
                var order = inns == _match.InningsNumber ? _match.Order : _match.FirstOrder;
                var b = order?.FirstOrDefault(o => o.Player.Id == who.Id);
                var line = b == null ? "" : $"{b.Runs}{(b.HowOut == null ? "*" : "")} ({b.Balls})  ·  {b.Fours}×4  {b.Sixes}×6";
                books.Add(new WagonBook { Id = who.Id, Name = who.Name, Team = team, Innings = inns, LeftHanded = who.LeftHanded, Shots = _wagon[kv.Key], Line = line });
            }
            return books;
        }

        /// <summary>The pause menu's wheels: the two at the crease.</summary>
        private IReadOnlyList<(IReadOnlyList<WagonShot>, bool, string, string)> CurrentWagons() =>
            new[] { Wheel(_match.OnStrike.Player), Wheel(_match.OffStrike.Player) };

        /// <summary>His wheel on screen for a few seconds (after his innings, or a fifty); tap skips.</summary>
        private IEnumerator WagonMoment(Player p, string title)
        {
            if (_closeUp || !_wagon.ContainsKey(WagonKey(p.Id, _match.InningsNumber))) yield break;
            var w = Wheel(p);
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;
            Camera.Show(MatchCamera.Mode.Wide, false, MatchCamera.Moment);
            var wagonT = StallProbe.Start();
            _hud.ShowWagons(new[] { (w.shots, w.leftHanded, title, w.line) }, 3.2f);
            StallProbe.End("wagon wheel", wagonT);
            // Real time: a card never waits on a stopped clock (r12 hang ended on this card).
            for (var until = Time.unscaledTime + 3.4f; Time.unscaledTime < until && !_skip;) yield return null;
            StallProbe.Mark("wagon wheel: off");
            _hud.HideWagons();
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
            Camera.Release(MatchCamera.Moment);
            _skip = false;
        }
    }
}
