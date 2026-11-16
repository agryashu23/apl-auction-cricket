using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>Round 13: when the strike changes hands mid-over the field mirrors; the nearest man takes each position.</summary>
    public class FieldSlotsTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public FieldSlotsTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static (double x, double f) World(Fielder f, bool lh) => (lh ? -f.Position.x : f.Position.x, f.Position.f);

        [Fact]
        public void A_mirrored_field_is_shared_out_to_the_nearest_men()
        {
            var teams = Data.Franchises.Select(t => t.Id).ToList();
            double worstShared = 0, worstFixed = 0, sumShared = 0, sumFixed = 0;
            var changes = 0;
            for (var g = 0; g < 60; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 4) % 10]), 6, 9100 + g, null, userBatsFirst: false);
                (double x, double f)[]? bySlot = null;
                var lastOver = -1;
                while (!m.Finished)
                {
                    var field = m.Field;
                    var n = field.Fielders.Count;
                    var now = new (double x, double f)[n];
                    for (var i = 0; i < n; i++) now[m.SlotOf(i)] = World(field.Fielders[i], field.LeftHanded);
                    if (bySlot != null && bySlot.Length == n && m.Over == lastOver)
                    {
                        var moved = Enumerable.Range(0, n).Max(s => Math.Sqrt(Math.Pow(now[s].x - bySlot[s].x, 2) + Math.Pow(now[s].f - bySlot[s].f, 2)));
                        // The same men with no share-out: slot i at position i.
                        var fixedMax = Enumerable.Range(0, n).Max(i => { var w = World(field.Fielders[i], field.LeftHanded); var p = bySlot[m.SlotOf(i)]; return 0.0; });
                        if (moved > 1)
                        {
                            changes++;
                            worstShared = Math.Max(worstShared, moved);
                            sumShared += moved;
                            var mirror = Enumerable.Range(0, n).Max(i => { var a = World(field.Fielders[i], field.LeftHanded); var b = World(field.Fielders[i], !field.LeftHanded); return Math.Sqrt(Math.Pow(a.x - b.x, 2) + Math.Pow(a.f - b.f, 2)); });
                            worstFixed = Math.Max(worstFixed, mirror);
                            sumFixed += mirror;
                        }
                    }
                    bySlot = now;
                    lastOver = m.Over;
                    m.PlayAi();
                }
            }
            _out.WriteLine($"mid-over field changes {changes}: the furthest anyone walks, shared out {worstShared:0} m (mean {sumShared / Math.Max(1, changes):0}), same men at the same positions {worstFixed:0} m (mean {sumFixed / Math.Max(1, changes):0})");
            Assert.True(changes == 0 || sumShared < sumFixed);
        }
    }
}
