using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>Owner v26 #13: the field presets by men out, their legality, and the custom field (FieldMore.cs).</summary>
    public class FieldPresetTests
    {
        private static IEnumerable<FieldPreset> Named => Field.Presets.Where(p => p != FieldPreset.Auto);

        [Fact]
        public void Presets_come_in_tabs_of_at_most_five_with_distinct_names()
        {
            foreach (var (label, fields) in Field.Groups) Assert.InRange(fields.Length, 1, 5);
            Assert.Equal(Named.Count(), Named.Distinct().Count());
            foreach (var p in Named) Assert.False(string.IsNullOrEmpty(Field.PresetNote(p)), p.ToString());
            Assert.Equal(Named.Count(), Named.Select(Field.PresetName).Distinct().Count());
            // Every value of the enum but the custom slots is offered somewhere.
            foreach (FieldPreset p in Enum.GetValues(typeof(FieldPreset)))
                if (!Field.IsCustom(p)) Assert.Contains(p, Field.Presets);
            // The powerplay fields have two out, the middle and death ones up to five.
            foreach (var p in Field.Groups.First(g => g.label == "PACE PP").fields) Assert.Equal(2, Field.OutsideCount(p));
            foreach (var p in Field.Groups.First(g => g.label == "DEATH").fields.Where(p => p != FieldPreset.SaveOne)) Assert.Equal(5, Field.OutsideCount(p));
        }

        /// <summary>A field's nine in the ground frame (x across from the batter's point of view as a right-hander sees it; a left-hander's mirrored).</summary>
        private static (double x, double f)[] World(Field field) => field.Fielders.Select(f => (field.LeftHanded ? -f.Position.x : f.Position.x, f.Position.f)).ToArray();

        [Fact]
        public void Every_preset_keeps_the_T20_rules_for_a_right_and_a_left_hander()
        {
            foreach (var p in Field.Presets)
            foreach (var phase in new[] { 0, 1, 2 })
            foreach (var spin in new[] { false, true })
            foreach (var keeperUp in new[] { false, true })
            foreach (var lh in new[] { false, true })
            {
                var field = Field.For(phase, spin, null, lh, p, keeperUp);
                var w = World(field);
                var tag = $"{Field.PresetName(p)} phase {phase} spin {spin} up {keeperUp} {(lh ? "LHB" : "RHB")}";
                Assert.Equal(9, w.Length);
                Assert.True(field.Fielders.Count(Field.OutsideRing) <= Field.MaxOutside(phase), tag + ": too many out");
                // In the ground frame the off side is +x for a right-hander, so his leg side is x < 0; a left-hander's is x > 0.
                // Behind square: behind the batter's stumps (f < 0).
                var leg = w.Count(q => lh ? q.x > 0 : q.x < 0);
                Assert.True(leg <= Field.MaxLegSide, $"{tag}: {leg} on the leg side");
                var legBehind = w.Count(q => (lh ? q.x > 0 : q.x < 0) && q.f < 0);
                Assert.True(legBehind <= 2, $"{tag}: {legBehind} behind square on the leg side");
                Assert.Empty(Field.Validate(field.Fielders, phase).Where(s => !s.StartsWith("Powerplay")));
                // The mirror: the same men, the other side.
                var other = World(Field.For(phase, spin, null, !lh, p, keeperUp));
                for (var i = 0; i < 9; i++)
                {
                    Assert.Equal(w[i].x, -other[i].x, 6);
                    Assert.Equal(w[i].f, other[i].f, 6);
                }
            }
        }

        [Fact]
        public void Nobody_stands_behind_the_keeper_short_of_the_rope()
        {
            // Owner: "a fielder is just behind the keeper". No long stop: within 25 degrees of the line straight behind
            // the stumps only the slips beside the keeper (at his depth, a long stride to the side) or a man on the rope.
            foreach (var p in Field.Presets.Concat(Field.CustomSlots))
            foreach (var phase in new[] { 0, 1, 2 })
            foreach (var spin in new[] { false, true })
            foreach (var keeperUp in new[] { false, true })
            foreach (var v in new Venue?[] { null }.Concat(Venue.All))
            {
                var field = Field.For(phase, spin, v, false, p, keeperUp);
                var keeper = keeperUp ? Field.KeeperUpDepth : Field.KeeperBackDepth;
                foreach (var f in field.Fielders)
                {
                    var (x, y) = f.Position;
                    if (Field.BesideKeeper(f.Name))
                    {
                        // Level with the keeper's gloves (within a metre and a half), never a way behind him.
                        Assert.True(y >= -keeper - 1.5, $"{p} up {keeperUp}: {f.Name} {-y - keeper:0.0} m behind the keeper");
                        Assert.True(Math.Abs(x) >= 1.4, $"{p}: {f.Name} straight behind the keeper");
                        continue;
                    }
                    if (!Field.BehindKeeper(f)) continue;
                    Assert.True(f.Radius >= field.Rope(f.AngleDeg) - 7, $"{p} {v?.Id}: {f.Name} at {f.AngleDeg:0} deg, {f.Radius:0} m: behind the keeper short of the rope");
                }
            }
        }

        [Fact]
        public void Boundary_riders_stand_three_to_six_metres_inside_the_rope()
        {
            foreach (var p in Field.Presets)
            foreach (var v in new Venue?[] { null }.Concat(Venue.All))
            foreach (var f in Field.For(2, false, v, false, p).Fielders.Where(Field.OutsideRing))
            {
                var gap = (v == null ? Field.Boundary(f.AngleDeg) : v.FromBatter(f.AngleDeg, false)) - f.Radius;
                Assert.InRange(gap, 3, 6);
            }
        }

        [Fact]
        public void The_AI_captain_keeps_the_field_all_over_and_moves_only_for_the_hand_or_a_new_field()
        {
            // Owner (2026-11-16): "fielders move after every ball; only move when RHB/LHB changes". Ball to ball with the
            // same striker's hand and the same over, nobody's spot or man changes.
            var data = Dataset.FromJson(System.IO.File.ReadAllText(System.IO.Path.Combine(FindRoot(), "data", "apl_dataset.json")));
            var teams = data.Franchises.Select(t => t.Id).ToList();
            var checkedBalls = 0;
            for (var g = 0; g < 12; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(data, teams[g % 10]), QuickMatch.LastSeasonXI(data, teams[(g + 3) % 10]), 20, 7700 + g, null, userBatsFirst: g % 2 == 0);
                (int over, int innings, bool lh, FieldPreset plan, string bowler, (double x, double f)[] byMan)? last = null;
                while (!m.Finished)
                {
                    var field = m.Field;
                    var n = field.Fielders.Count;
                    var byMan = new (double x, double f)[n];
                    for (var i = 0; i < n; i++) byMan[m.SlotOf(i)] = (field.LeftHanded ? -field.Fielders[i].Position.x : field.Fielders[i].Position.x, field.Fielders[i].Position.f);
                    var reshares = m.FieldReshares;
                    if (last is var (o, inn, lh, plan, bowler, prev) && o == m.Over && inn == m.InningsNumber && bowler == m.CurrentBowler.Id)
                    {
                        Assert.Equal(plan, field.Plan); // the plan is the over's, never re-picked ball to ball
                        if (lh == field.LeftHanded)
                        {
                            for (var s = 0; s < n; s++)
                                Assert.True(Math.Abs(prev[s].x - byMan[s].x) < 1e-6 && Math.Abs(prev[s].f - byMan[s].f) < 1e-6, $"game {g} over {m.Over}: man {s} moved with the same batter hand");
                            checkedBalls++;
                        }
                    }
                    last = (m.Over, m.InningsNumber, field.LeftHanded, field.Plan, m.CurrentBowler.Id, byMan);
                    // The view tells the engine where the men ended up after a chase: it must not re-share.
                    m.FieldersStand(new[] { ("nobody", 30.0, 30.0) });
                    Assert.Equal(reshares, m.FieldReshares);
                    m.PlayAi();
                }
            }
            Assert.True(checkedBalls > 500, $"{checkedBalls} balls checked");
        }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!System.IO.File.Exists(System.IO.Path.Combine(dir, "data", "apl_dataset.json"))) dir = System.IO.Path.GetDirectoryName(dir)!;
            return dir;
        }

        [Fact]
        public void Every_preset_is_a_legal_field_after_the_powerplay_and_the_two_out_ones_in_it()
        {
            foreach (var spin in new[] { false, true })
            foreach (var p in Named)
            {
                Assert.True(Field.LegalIn(p, 1, spin), $"{p} (spin {spin}): " + string.Join("; ", Field.Validate(Field.For(1, spin, null, false, p).Fielders, 1)));
                var f = Field.For(0, spin, null, false, p).Fielders;
                // In the powerplay any preset is brought within the rules (the field screen offers only the legal ones).
                Assert.True(Field.OutsideCount(f) <= 2, $"{p} in the powerplay");
                if (Field.OutsideCount(p, spin) <= 2) Assert.True(Field.LegalIn(p, 0, spin), p.ToString());
            }
            Assert.False(Field.LegalIn(FieldPreset.Death, 0));
        }

        [Fact]
        public void The_editor_has_twenty_odd_named_spots_apart()
        {
            Assert.InRange(Field.Spots.Count, 20, 25);
            Assert.Equal(Field.Spots.Count, Field.Spots.Select(s => s.Name).Distinct().Count());
            foreach (var a in Field.Spots)
            foreach (var b in Field.Spots)
            {
                if (a == b) continue;
                var (ax, af) = a.Position;
                var (bx, bf) = b.Position;
                Assert.True((ax - bx) * (ax - bx) + (af - bf) * (af - bf) >= 4, $"{a.Name} / {b.Name}");
            }
            Assert.True(Field.Spots.Count(Field.OutsideRing) >= 8);
        }

        [Fact]
        public void A_custom_field_plays_as_set_and_is_checked_against_the_rules()
        {
            var nine = new[] { "slip", "point", "cover", "mid-off", "mid-on", "mid-wicket", "square leg", "third man", "fine leg" }.Select(n => Field.Spot(n)!).ToList();
            Assert.Empty(Field.Validate(nine, 0));
            Field.SetCustom(FieldPreset.Custom2, nine);
            try
            {
                var f = Field.For(0, false, null, false, FieldPreset.Custom2);
                Assert.Equal(nine.Select(x => x.Name), f.Fielders.Select(x => x.Name));
                Assert.True(Field.LegalIn(FieldPreset.Custom2, 0));
                Assert.Equal("CUSTOM 2", Field.PresetName(FieldPreset.Custom2));
            }
            finally { Field.SetCustom(FieldPreset.Custom2, null); }
            // Empty: plays as BALANCED, and isn't offered.
            Assert.False(Field.LegalIn(FieldPreset.Custom3, 1));
            Assert.Equal(Field.For(1, false, null, false, FieldPreset.Balanced).Fielders.Select(x => x.Name), Field.For(1, false, null, false, FieldPreset.Custom3).Fielders.Select(x => x.Name));

            // Three out in the powerplay; three behind square on the leg side; eight men.
            var three = nine.Where(x => x.Name != "mid-on").Append(Field.Spot("long-on")!).ToList();
            Assert.Contains(Field.Validate(three, 0), s => s.StartsWith("Powerplay"));
            Assert.Empty(Field.Validate(three, 1));
            var legHeavy = new[] { "leg slip", "short fine", "fine leg", "slip", "point", "cover", "mid-off", "mid-on", "mid-wicket" }.Select(n => Field.Spot(n)!).ToList();
            Assert.Contains(Field.Validate(legHeavy, 1), s => s.Contains("behind square"));
            Assert.Contains(Field.Validate(nine.Take(8).ToList(), 1), s => s.Contains("nine"));
        }

        /// <summary>The close catchers a field may have by design; everyone else is on the ring or the rope.</summary>
        private static readonly HashSet<string> Catchers = new HashSet<string>
        {
            "slip", "first slip", "second slip", "gully", "leg slip", "leg gully", "short leg", "silly point", "short cover", "short mid-wicket",
        };

        [Fact]
        public void Nobody_but_the_catchers_stands_close_to_the_pitch()
        {
            // Owner: "fielders very close to the pitch". Ring men stand on the 30-yard circle (a few steps in),
            // the deep on the rope; only slips, gully and the named close catchers come in nearer.
            foreach (var p in Field.Presets.Append(FieldPreset.Custom1))
            foreach (var phase in new[] { 0, 1, 2 })
            foreach (var spin in new[] { false, true })
            foreach (var v in new Venue?[] { null }.Concat(Venue.All))
            foreach (var f in Field.For(phase, spin, v, false, p).Fielders)
            {
                if (Catchers.Contains(f.Name)) continue;
                var gap = Field.FromPitch(f.AngleDeg, f.Radius);
                Assert.True(gap >= 19.5, $"{p} phase {phase} spin {spin} {v?.Id}: {f.Name} {gap:0.0} m from the pitch");
                if (!Field.OutsideRing(f)) Assert.True(gap >= 21.9, $"{p}: {f.Name} {gap:0.0} m, inside the ring line");
                else Assert.True(f.Radius >= 45, $"{p}: {f.Name} out but only {f.Radius:0} m from the bat");
            }
            // Brought up for the powerplay, a deep man never lands on a ring man (long-off up onto mid-off).
            foreach (var p in Field.Presets)
            foreach (var spin in new[] { false, true })
            {
                var nine = Field.For(0, spin, null, false, p).Fielders;
                for (var i = 0; i < nine.Count; i++)
                for (var j = i + 1; j < nine.Count; j++)
                {
                    var (ax, af) = nine[i].Position;
                    var (bx, bf) = nine[j].Position;
                    Assert.True(Math.Sqrt((ax - bx) * (ax - bx) + (af - bf) * (af - bf)) >= (Catchers.Contains(nine[i].Name) && Catchers.Contains(nine[j].Name) ? 2 : 4), $"{p} powerplay: {nine[i].Name} on {nine[j].Name}");
                }
                Assert.True(nine.Count(f => Field.LegBehindSquare(f.AngleDeg)) <= 2, $"{p} powerplay: leg side behind square");
            }
            foreach (var s in Field.Spots)
                if (!Catchers.Contains(s.Name)) Assert.True(Field.FromPitch(s.AngleDeg, s.Radius) >= 22.5, s.Name);
            // The AUTO field and the AI's plans: mostly ring, rope and slips.
            foreach (var phase in new[] { 0, 1, 2 })
            foreach (var spin in new[] { false, true })
                Assert.True(Field.For(phase, spin).Fielders.Count(f => Catchers.Contains(f.Name)) <= 1);
        }

        [Fact]
        public void The_AI_captain_and_AUTO_never_set_close_catchers_but_slips_and_gully()
        {
            // Owner (build 6): fields the AI sets are ring, rope and slips. Every situation it can see, every phase,
            // pace and spin, many seeds: nobody but a slip or gully within 22 m of the pitch.
            var slips = new HashSet<string> { "slip", "first slip", "second slip", "gully" };
            var picked = new HashSet<FieldPreset>();
            foreach (var spin in new[] { false, true })
            foreach (var phase in new[] { 0, 1, 2 })
            foreach (int? target in new int?[] { null, 150, 240 })
            foreach (var ballsLeft in new[] { 1, 30, 100 })
            foreach (var batterBalls in new[] { 0, 10 })
            for (var seed = 0; seed < 60; seed++)
            {
                var s = new Situation(140, 3, ballsLeft, 20, target, batterBalls, false, phase);
                var p = Field.AiPreset(s, spin, seed);
                picked.Add(p);
                foreach (var v in new Venue?[] { null, Venue.All[0] })
                foreach (var f in Field.For(phase, spin, v, seed % 2 == 0, p).Fielders)
                {
                    if (slips.Contains(f.Name)) continue;
                    Assert.True(Field.FromPitch(f.AngleDeg, f.Radius) >= 21.9, $"AI {p} phase {phase} spin {spin}: {f.Name} {Field.FromPitch(f.AngleDeg, f.Radius):0.0} m from the pitch");
                }
            }
            foreach (var p in picked) Assert.False(Field.UserOnly(p), p.ToString());
            // Every kind of bowler gets his own researched field.
            foreach (var kind in new[] { FieldBowler.Pace, FieldBowler.OffSpin, FieldBowler.LegSpin, FieldBowler.LeftArmSpin })
            foreach (var phase in new[] { 0, 1 })
                Assert.False(Field.UserOnly(Field.PlanFor(kind, phase)));
            Assert.Equal(4, new[] { FieldBowler.Pace, FieldBowler.OffSpin, FieldBowler.LegSpin, FieldBowler.LeftArmSpin }.Select(k => Field.PlanFor(k, 1)).Distinct().Count());
        }
    }
}
