using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// Who stands where (round 13): each ball's field positions are shared out among the nine fielders
    /// (the XI less the bowler and keeper; a "slot" is an index into them in XI order) so the total
    /// walking from where each man stood is least — the Hungarian method on squared distances. Only the
    /// first ball of an innings is the plain slot i to position i. Field rework (2026-11-16): each man's MARK is
    /// kept by player and nobody is re-shared between balls — only when the field's spots change (the striker's
    /// hand, a new plan) does the share-out run, from the marks; a new bowler with the same field keeps everyone
    /// on his mark and the old bowler takes the one the new bowler left. The match view places its bodies by
    /// this map and the scorecard names fielders by it (<see cref="PlayerAt"/>), so the two always agree.
    /// </summary>
    public sealed partial class QuickMatch
    {
        private int[]? _slots;
        private readonly Dictionary<string, (double x, double f)> _standsAt = new Dictionary<string, (double x, double f)>();
        private (bool lh, string sig, string bowler, string keeper, int innings, int count) _slotKey;

        /// <summary>The slots as they stood for the last ball played (the scorecard names its fielders by these).</summary>
        private int[]? _ballSlots;

        /// <summary>The bowler's end, where a bowler coming off is when his over ends (batter frame, m).</summary>
        private static readonly (double x, double f) BowlersEnd = (1.5, 21);

        /// <summary>The slot (index into the XI less bowler and keeper) standing at position <paramref name="position"/> of the field for the next ball.</summary>
        public int SlotOf(int position)
        {
            EnsureSlots(Field);
            return position >= 0 && position < _slots!.Length ? _slots[position] : position;
        }

        /// <summary>
        /// Where the fielders really are now (the view tells the engine at a dead ball: the man who chased to
        /// the rope is there, not at his spot), ground frame (x across, f up the pitch from the striker's
        /// stumps). The next share-out works from these, so a long chase isn't followed by a long walk.
        /// </summary>
        /// <remarks>
        /// Field rework (owner, 2026-11-16: "the fielders move after every ball; only move when RHB/LHB changes"): this no
        /// longer re-shares the field. It used to (the share-out ran from where the chaser, the backer and the rest stood
        /// at the dead ball), so after nearly every ball men swapped marks and walked 10-40 m. Now everyone keeps his own
        /// mark until the field itself changes; this only notes where a man is who has no mark yet.
        /// </remarks>
        public void FieldersStand(IEnumerable<(string playerId, double x, double f)> where)
        {
            foreach (var (id, x, f) in where) if (Finite(x) && Finite(f) && !_standsAt.ContainsKey(id)) _standsAt[id] = (x, f);
        }

        /// <summary>
        /// Why the field share-out last changed (for the client's "[APL fieldplan]" line): "" while it's kept, else
        /// "first ball", "batter hand", "field changed", "new bowler" or "new innings".
        /// </summary>
        public string FieldChangeReason { get; private set; } = "";

        /// <summary>Counts every re-share (tests: same hand, same field, same men — it must not move).</summary>
        public int FieldReshares { get; private set; }

        /// <summary>At the delivery: the share-out for this ball, kept for naming its fielders afterwards.</summary>
        private void FixBallSlots(Field field)
        {
            EnsureSlots(field);
            _ballSlots = (int[])_slots!.Clone();
        }

        private int BallSlotOf(int position) => _ballSlots != null && position < _ballSlots.Length ? _ballSlots[position] : position;

        /// <summary>The nine, in XI order (the slots).</summary>
        private List<Player> SlotPlayers()
        {
            var bowler = CurrentBowler;
            var keeper = FieldingKeeper;
            return Bowling.XI.Where(p => p.Id != bowler.Id && p.Id != keeper.Id).ToList();
        }

        /// <summary>Re-shares the positions if the field (or the men in it) has changed since the last share-out.</summary>
        private void EnsureSlots(Field field)
        {
            var n = field.Fielders.Count;
            // The field as it stands (its spots, not its name or the over: a new over with the same field and the same
            // men keeps everyone where he is), which way the batter stands, and who's in it.
            var sig = string.Join(";", field.Fielders.Select(f => $"{f.Name}@{f.AngleDeg:0.0}/{f.Radius:0.0}"));
            var key = (field.LeftHanded, sig, CurrentBowler.Id, FieldingKeeper.Id, InningsNumber, n);
            if (_slots != null && key.Equals(_slotKey)) return;
            var men = SlotPlayers();
            var first = _slots == null && _slotKey.innings == 0;
            var newInnings = _slotKey.innings != 0 && key.InningsNumber != _slotKey.innings;
            var sameSpots = !first && !newInnings && key.LeftHanded == _slotKey.lh && key.sig == _slotKey.sig && n == _slotKey.count;
            FieldChangeReason = first ? "first ball" : newInnings ? "new innings" : key.LeftHanded != _slotKey.lh ? "batter hand"
                : key.sig != _slotKey.sig ? "field changed" : "new bowler";
            FieldReshares++;
            if (newInnings) { _standsAt.Clear(); _posIds = null; }
            var lastBowler = newInnings ? null : _slotKey.bowler;
            _slotKey = (key.LeftHanded, key.sig, key.Item3, key.Item4, key.InningsNumber, n);
            // The same spots, only the men changed (a new bowler: the old one back in the field): everyone keeps his
            // mark, and whoever came in takes the mark the new bowler left.
            if (sameSpots && _posIds != null && _posIds.Length == n && men.Count >= n && KeepMarks(men, n) is int[] kept)
            {
                _slots = kept;
                _posIds = Enumerable.Range(0, n).Select(i => men[kept[i]].Id).ToArray();
                for (var i = 0; i < n; i++) _standsAt[_posIds[i]] = World(field.Fielders[i], field.LeftHanded);
                return;
            }
            // The last over's bowler is at the bowler's end, not on his old mark.
            if (_standsAt.Count > 0 && !string.IsNullOrEmpty(lastBowler) && lastBowler != CurrentBowler.Id) _standsAt[lastBowler!] = BowlersEnd;
            var spots = new (double x, double f)[n];
            for (var i = 0; i < n; i++)
            {
                spots[i] = World(field.Fielders[i], field.LeftHanded);
                if (!Finite(spots[i].x) || !Finite(spots[i].f)) spots[i] = (0, 0);
            }
            int[] slots;
            if (_standsAt.Count == 0 || men.Count < n)
            {
                slots = new int[n];
                for (var i = 0; i < n; i++) slots[i] = i;
            }
            else
            {
                var cost = new double[n, n];
                for (var s = 0; s < n; s++)
                {
                    var (sx, sf) = _standsAt.TryGetValue(men[s].Id, out var at) && Finite(at.x) && Finite(at.f) ? at : BowlersEnd;
                    for (var pos = 0; pos < n; pos++)
                    {
                        var c = (sx - spots[pos].x) * (sx - spots[pos].x) + (sf - spots[pos].f) * (sf - spots[pos].f);
                        // Never NaN or infinite into the assignment (round 13: a bad position stalled it for a minute).
                        cost[pos, s] = Finite(c) ? System.Math.Min(c, 1e8) : 1e8;
                    }
                }
                slots = Assign(cost, n) ?? Identity(n);
            }
            _slots = slots;
            // Each man's mark (the next re-share works from the marks, never from where a chase left him).
            for (var i = 0; i < n && slots[i] < men.Count; i++) _standsAt[men[slots[i]].Id] = spots[i];
            _posIds = Enumerable.Range(0, n).Select(i => slots[i] < men.Count ? men[slots[i]].Id : "").ToArray();
        }

        /// <summary>Who held each position of the field (player ids), for keeping the marks when only the men change.</summary>
        private string[]? _posIds;

        /// <summary>Everyone still in the field keeps his position; the newcomers fill the ones left (null: it can't be done).</summary>
        private int[]? KeepMarks(List<Player> men, int n)
        {
            var slotOf = new Dictionary<string, int>();
            for (var s = 0; s < men.Count; s++) slotOf[men[s].Id] = s;
            var slots = new int[n];
            var used = new bool[men.Count];
            var open = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (_posIds![i] != null && slotOf.TryGetValue(_posIds[i], out var s) && !used[s]) { slots[i] = s; used[s] = true; }
                else open.Add(i);
            }
            var free = Enumerable.Range(0, men.Count).Where(s => !used[s]).ToList();
            if (free.Count < open.Count) return null;
            for (var k = 0; k < open.Count; k++) slots[open[k]] = free[k];
            return slots;
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static int[] Identity(int n)
        {
            var r = new int[n];
            for (var i = 0; i < n; i++) r[i] = i;
            return r;
        }

        /// <summary>
        /// Hungarian method (n small, O(n³)): row i (position) to column result[i] (slot), least total cost.
        /// Costs must be finite (EnsureSlots clamps them); every loop is capped, and null comes back if a
        /// cap is ever hit (the caller then keeps slot i at position i) — it can never stall the game.
        /// </summary>
        public static int[]? Assign(double[,] cost, int n)
        {
            const double Inf = 1e18;
            var u = new double[n + 1];
            var v = new double[n + 1];
            var p = new int[n + 1];
            var way = new int[n + 1];
            for (var i = 1; i <= n; i++)
            {
                p[0] = i;
                var j0 = 0;
                var minv = new double[n + 1];
                var used = new bool[n + 1];
                for (var j = 0; j <= n; j++) minv[j] = Inf;
                var guard = 0;
                do
                {
                    if (++guard > n + 2) return null;
                    used[j0] = true;
                    int i0 = p[j0], j1 = 0;
                    var delta = Inf;
                    for (var j = 1; j <= n; j++)
                    {
                        if (used[j]) continue;
                        var cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                        if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                        if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                    }
                    for (var j = 0; j <= n; j++)
                    {
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                        else minv[j] -= delta;
                    }
                    if (j1 == 0) return null;
                    j0 = j1;
                }
                while (p[j0] != 0);
                var back = 0;
                do
                {
                    if (++back > n + 2) return null;
                    var j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                }
                while (j0 != 0);
            }
            var result = new int[n];
            var seen = new bool[n];
            for (var j = 1; j <= n; j++) if (p[j] > 0) { result[p[j] - 1] = j - 1; seen[j - 1] = true; }
            // A full one-to-one share-out, or none.
            for (var j = 0; j < n; j++) if (!seen[j]) return null;
            return result;
        }

        /// <summary>A fielder's spot in the ground's frame (the batter-frame position mirrored for a left-hander).</summary>
        private static (double x, double f) World(Fielder f, bool leftHanded)
        {
            var (x, y) = f.Position;
            return (leftHanded ? -x : x, y);
        }
    }
}
