using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// The impact player: each side names up to four substitutes and may make one
    /// substitution in the match, at an over break or after a wicket (the innings
    /// break is an over break). The man replaced takes no further part; the one
    /// coming in can bat if the man he replaces hasn't (and the side hasn't had
    /// eleven bat), and bowl a full quota.
    /// </summary>
    public sealed partial class QuickMatch
    {
        public const int MaxSubstitutes = 4;

        private readonly Dictionary<string, List<Player>> _subs = new Dictionary<string, List<Player>>();
        private readonly Dictionary<string, (Player off, Player on)> _impact = new Dictionary<string, (Player, Player)>();

        /// <summary>A side's named substitutes (none until <see cref="NameSubstitutes"/>).</summary>
        public IReadOnlyList<Player> Substitutes(string teamId) => _subs.TryGetValue(teamId, out var l) ? l : (IReadOnlyList<Player>)Array.Empty<Player>();

        /// <summary>The substitution a side has made (null: none yet).</summary>
        public (Player off, Player on)? ImpactMade(string teamId) => _impact.TryGetValue(teamId, out var s) ? s : ((Player, Player)?)null;

        /// <summary>Name up to four substitutes (players not in the XI).</summary>
        public void NameSubstitutes(string teamId, IEnumerable<Player> subs)
        {
            var side = Side(teamId);
            _subs[teamId] = subs.Where(p => side != null && !side.XI.Contains(p)).Distinct().Take(MaxSubstitutes).ToList();
        }

        /// <summary>The best four of a squad not in the XI (when nobody picked them).</summary>
        public static List<Player> DefaultSubstitutes(IEnumerable<Player> squad, TeamSheet xi) =>
            squad.Where(p => !xi.XI.Contains(p)).OrderByDescending(p => p.Impact).ThenBy(p => p.Id, StringComparer.Ordinal).Take(MaxSubstitutes).ToList();

        private TeamSheet? Side(string teamId) => Batting.TeamId == teamId ? Batting : Bowling.TeamId == teamId ? Bowling : null;

        /// <summary>Now's a moment for it: before a ball, at an over break or straight after a wicket.</summary>
        public bool ImpactWindow => _pending == null && !MatchOver
            && (BallInOver == 0 || (Log.Count > 0 && Log[Log.Count - 1].Wicket));

        /// <summary>The side may still make its substitution, now.</summary>
        public bool CanSubstitute(string teamId) => ImpactWindow && !_impact.ContainsKey(teamId) && Substitutes(teamId).Count > 0;

        /// <summary>
        /// <paramref name="on"/> (a named substitute) replaces <paramref name="off"/>
        /// (in the XI, not batting at the crease now). False if it isn't allowed.
        /// </summary>
        public bool Substitute(string teamId, Player off, Player on)
        {
            var side = Side(teamId);
            if (side == null || !CanSubstitute(teamId) || !Substitutes(teamId).Contains(on) || !side.XI.Contains(off)) return false;
            var batting = side == Batting;
            if (batting && (OnStrike.Player == off || OffStrike.Player == off)) return false;
            if (!batting && CurrentBowler == off && BallInOver != 0) return false;
            var i = side.XI.IndexOf(off);
            side.XI[i] = on;
            _impact[teamId] = (off, on);
            if (batting)
            {
                // He takes the replaced man's place in the order if that one hasn't batted; else he waits at the end.
                var k = Order.FindIndex(b => b.Player == off);
                if (k >= 0 && !Order[k].Batted) Order[k] = new ChaseBatter(on);
            }
            else
            {
                // The rest of the innings' overs planned with him in the attack.
                var plan = global::Apl.Core.Play.Bowling.PlanOvers(Bowling.XI, Overs);
                for (var o = Over; o < Overs && o < _plan.Count; o++) _plan[o] = plan[o];
            }
            return true;
        }

        /// <summary>
        /// The AI's substitution at the innings break: batting first it brings on
        /// its best bowling sub for a batter who's done; bowling first, its best
        /// batting sub for a bowler who's bowled out — when the sub is better.
        /// </summary>
        public bool AiImpact(string teamId)
        {
            if (!CanSubstitute(teamId)) return false;
            var side = Side(teamId)!;
            var subs = Substitutes(teamId);
            // Who's about to do what: the side batting now needs batters, the side bowling now bowlers.
            var needBowling = side == Bowling;
            var on = needBowling ? subs.OrderByDescending(p => p.Bowling).First() : subs.OrderByDescending(p => p.Batting).First();
            var candidates = side.XI.Where(p => !p.IsKeeper && p != CurrentBowler);
            if (side == Batting) candidates = candidates.Where(p => !Order.Any(b => b.Player == p && b.Batted));
            var off = needBowling
                ? candidates.OrderBy(p => p.Bowling).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault()
                : candidates.OrderBy(p => p.Batting).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (off == null || (needBowling ? on.Bowling <= off.Bowling + 5 : on.Batting <= off.Batting + 5)) return false;
            return Substitute(teamId, off, on);
        }
    }
}
