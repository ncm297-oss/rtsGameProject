using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA stress (M3-3, session 2026-10-06-2114): seeded Build / Cancel / Repair / destroy / unit-order streams on generated
/// maps with the never-seal oracle checked at every placement and on sampled refusals, invariants after every tick,
/// determinism twins, a hash audit of the new fields, an allocation probe through the seal flood and push-out, scale,
/// and BUG-0080 at a realistic placement rate.
/// </summary>
public class ConstructionFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public ConstructionFuzzStressTests(ITestOutputHelper output) => _out = output;

    private const int Players = 2, WorkersPerPlayer = 10;

    // The oracles compare CanPlace with geometry only, and every type is placed anywhere: D3's building requires would
    // answer Requires first (BUG-0112), so these worlds run on the shipped data with requires cleared (same ids).

    private static Simulation NewWorld(ulong seed) =>
        new(TestSim.ConfigWithoutBuildingRequires(Seed: seed, PlayerCount: Players, UnitCapacity: 48, CommandCapacity: 512) with
        {
            Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 },
            BuildingCapacity = 512,
        });

    private static int TypeOf(int player, int slot) => TestSim.Data.Buildings.First(d => d.Faction == player % 2 && (int)d.Slot == slot).Id;

    /// <summary>A cell next to a blocked cell (half the time) or anywhere, so seal candidates come up often.</summary>
    private static int Anchor(ref SimRng rng, NavGrid g, List<int> nearBlocked)
    {
        if (rng.NextInt(0, 2) == 0) return nearBlocked[rng.NextInt(0, nearBlocked.Count)] + rng.NextInt(-2, 1) + rng.NextInt(-2, 1) * g.Width;
        return rng.NextInt(0, g.Width * g.Height);
    }

    private static List<int> NearBlocked(NavGrid g)
    {
        var list = new List<int>();
        for (int y = 1; y < g.Height - 1; y++)
            for (int x = 1; x < g.Width - 1; x++)
                if (g.IsPassable(x, y) && (!g.IsPassable(x + 1, y) || !g.IsPassable(x - 1, y) || !g.IsPassable(x, y + 1) || !g.IsPassable(x, y - 1)))
                    list.Add(y * g.Width + x);
        return list;
    }

    /// <summary>Invariants after a tick; null when all hold.</summary>
    private static string? Check(World w)
    {
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        NavGrid g = w.NavGrid;
        for (int p = 0; p < Players; p++)
            if (w.Gold[p] < 0 || w.Wood[p] < 0) return $"player {p} totals {w.Gold[p]} / {w.Wood[p]}";
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            BuildingDef d = TestSim.Data.Buildings[b.TypeId[k]];
            if (b.Hp[k] < 1 || b.Hp[k] > d.Hp) return $"building {k} hp {b.Hp[k]} of {d.Hp}";
            if (b.Work[k] < 0 || b.Work[k] > b.WorkNeeded(b.TypeId[k])) return $"building {k} work {b.Work[k]}";
            if (!b.UnderConstruction[k] && b.Work[k] != b.WorkNeeded(b.TypeId[k]) && b.Work[k] != 0) return $"finished building {k} with work {b.Work[k]}";
            // Damage to a site stands until builders work again (documented M4 revisit), so hp is at most the formula.
            if (b.UnderConstruction[k] && b.Hp[k] > Math.Max(1, (int)((long)d.Hp * b.Work[k] / b.WorkNeeded(b.TypeId[k])))) return $"site {k}: hp {b.Hp[k]} above the progress formula";
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return $"unit {i} at {p}";
            if (!g.WorldToCell(p, out int x, out int y)) return $"unit {i} off the map at {p}";
            EntityHandle t = u.BuildTarget[i];
            if (t != default && !b.IsAlive(t)) return $"unit {i} keeps a dead build target {t.Index}/{t.Generation}";
            if (t != default && b.Owner[t.Index] != u.Owner[i]) return $"unit {i} builds another player's building";
            if (t != default && u.GatherNode[i] != default) return $"unit {i} both builds and gathers";
            if (u.State[i] == UnitState.Building && t == default) return $"unit {i} Building with no target";
        }
        return null;
    }

    /// <summary>
    /// The fuzz: <paramref name="steps"/> actions of Build (half new, some joins), queued Build, Cancel, Repair, destroy
    /// (the damage seam), Move / Stop / Gather orders and unit frees; ticks between. With <paramref name="oracle"/> every
    /// accepted placement is checked for lost connections and sampled refusals against the flood oracle; returns the
    /// per-tick hashes.
    /// </summary>
    private List<ulong> Fuzz(ulong seed, int steps, bool oracle, out string summary)
    {
        Simulation sim = NewWorld(seed);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var rng = new SimRng(seed, 404);
        List<int> open = FlowFieldOracle.PassableCells(g);
        for (int i = 0; i < Players * WorkersPerPlayer; i++)
            sim.Enqueue(Command.SpawnUnit(i % Players, i % Players == 0 ? GatherMaps.Laborer : TestSim.Data.FindUnit("whirlwind_camp_follower"), MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
        sim.Tick();
        sim.Tick();
        for (int p = 0; p < Players; p++) BuildMaps.SetTotals(sim, p, 30_000, 30_000);
        List<int> nearBlocked = NearBlocked(g);
        var hashes = new List<ulong>();
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        int placed = 0, sealRefusals = 0, oracleChecks = 0, cancels = 0, destroys = 0, repairs = 0;
        for (int step = 0; step < steps; step++)
        {
            int p = rng.NextInt(0, Players);
            int slot = rng.NextInt(0, u.Capacity);
            EntityHandle unit = new(slot, u.Generation[slot]);
            int roll = rng.NextInt(0, 20);
            if (roll < 9)
            {
                // A Build at a fresh anchor (checked against the oracle) or at an existing site of p's (a join).
                int type = TypeOf(p, rng.NextInt(0, 10)), anchor = Anchor(ref rng, g, nearBlocked);
                if (rng.NextInt(0, 4) == 0)
                {
                    for (int k = 0; k < b.Capacity; k++)
                        if (b.Alive[k] && b.Owner[k] == p && b.UnderConstruction[k]) { type = b.TypeId[k]; anchor = b.Cell[k]; break; }
                }
                if ((uint)anchor >= (uint)(g.Width * g.Height)) continue;
                if (!u.Alive[slot] || u.Owner[slot] != p) unit = FirstWorker(u, p);
                BuildingDef d = TestSim.Data.Buildings[type];
                int x0 = anchor % g.Width, y0 = anchor / g.Width;
                bool queued = rng.NextInt(0, 5) == 0;
                sim.Enqueue(Command.Build(p, unit, type, g.CellCenter(x0, y0), queued));
                hashes.Add(TickAndCheck(sim));
                // The Build applies first thing in the next tick: ask now, on exactly the state it will see.
                bool can = w.CanPlace(p, type, anchor, out PlacementError why);
                if (oracle && why is not (PlacementError.UnknownType or PlacementError.WrongFaction or PlacementError.OffMap or PlacementError.Blocked)
                    && !SealOracle.RingOpen(g, x0, y0, d.FootprintWidth, d.FootprintHeight))
                {
                    bool seals = SealOracle.Seals(g, x0, y0, d.FootprintWidth, d.FootprintHeight);
                    Assert.True(seals == (why == PlacementError.SealsGround), $"seed {seed} step {step}: {d.Id} at ({x0}, {y0}): CanPlace {why}, oracle seals {seals}");
                    oracleChecks++;
                }
                if (why == PlacementError.SealsGround) sealRefusals++;
                int[]? before = oracle && can ? SealOracle.Labels(g) : null;
                int site = b.SlotAt(x0, y0);
                bool join = site >= 0 && b.Cell[site] == anchor && b.TypeId[site] == type && b.Owner[site] == p && b.UnderConstruction[site];
                bool validWorker = unit != default && u.IsAlive(unit) && u.Owner[unit.Index] == p && EconomySystem.IsWorker(w, unit.Index);
                hashes.Add(TickAndCheck(sim));
                int now = b.SlotAt(x0, y0);
                // Mine placed exactly here (a queued Build popping in phase 7 may place another elsewhere in this tick).
                bool mine = site < 0 && now >= 0 && b.Cell[now] == anchor && b.TypeId[now] == type && b.Owner[now] == p && b.UnderConstruction[now];
                if (queued || join) { }
                else if (mine)
                {
                    placed++;
                    Assert.True(can && validWorker, $"seed {seed} step {step}: a Build placed what CanPlace refused ({why})");
                    if (before != null)
                    {
                        int lost = SealOracle.LostConnection(before, SealOracle.Labels(g));
                        Assert.True(lost < 0, $"seed {seed} step {step}: placing {d.Id} at ({x0}, {y0}) cut cell {lost} off");
                    }
                    for (int i = 0; i < u.Capacity; i++)
                    {
                        if (!u.Alive[i] || !g.WorldToCell(u.Position[i], out int ux, out int uy)) continue;
                        Assert.True(ux < x0 || ux >= x0 + d.FootprintWidth || uy < y0 || uy >= y0 + d.FootprintHeight,
                            $"seed {seed} step {step}: unit {i} (player {u.Owner[i]}) left inside the new {d.Id}");
                    }
                }
                else Assert.False(can && validWorker, $"seed {seed} step {step}: CanPlace said None but a worker's Build placed nothing");
            }
            else if (roll < 11)
            {
                int k = RandomBuilding(ref rng, b, p, site: true);
                if (k < 0) continue;
                BuildingDef d = TestSim.Data.Buildings[b.TypeId[k]];
                sim.Enqueue(Command.Cancel(p, g.CellCenter(b.Cell[k] % g.Width + d.FootprintWidth - 1, b.Cell[k] / g.Width)));
                hashes.Add(TickAndCheck(sim));
                bool wasSite = b.Alive[k] && b.UnderConstruction[k];
                EntityHandle handle = b.HandleOf(k);
                hashes.Add(TickAndCheck(sim));
                if (wasSite)
                {
                    // The slot may be taken again in this very tick (a cancelled builder's queued Build pops in phase 7).
                    Assert.False(b.IsAlive(handle), $"seed {seed} step {step}: own site {k} survived its Cancel");
                    cancels++;
                }
            }
            else if (roll < 12)
            {
                int k = RandomBuilding(ref rng, b, -1, site: false);
                if (k < 0) continue;
                b.Damage(b.HandleOf(k), rng.NextInt(0, 3) == 0 ? 100_000 : rng.NextInt(1, 800));
                destroys++;
            }
            else if (roll < 14)
            {
                int k = RandomBuilding(ref rng, b, p, site: false);
                if (k < 0) continue;
                if (!u.Alive[slot] || u.Owner[slot] != p) unit = FirstWorker(u, p);
                BuildingDef d = TestSim.Data.Buildings[b.TypeId[k]];
                sim.Enqueue(Command.Repair(p, unit, g.CellCenter(b.Cell[k] % g.Width, b.Cell[k] / g.Width + d.FootprintHeight - 1), rng.NextInt(0, 4) == 0));
                repairs++;
            }
            else if (roll < 16)
            {
                if (!u.Alive[slot]) continue;
                Vector2 to = MoveScenario.Center(g, open[rng.NextInt(0, open.Count)]);
                sim.Enqueue(rng.NextInt(0, 3) switch
                {
                    0 => Command.Stop(u.Owner[slot], unit),
                    1 => Command.HoldPosition(u.Owner[slot], unit),
                    _ => Command.Move(u.Owner[slot], unit, to, rng.NextInt(0, 3) == 0),
                });
            }
            else if (roll < 17)
            {
                if (rng.NextInt(0, 4) == 0 && u.Alive[slot]) u.Free(unit);
                else sim.Enqueue(Command.SpawnUnit(p, GatherMaps.Laborer, MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
            }
            int ticks = rng.NextInt(0, 3) == 0 ? rng.NextInt(1, 30) : 1;
            for (int t = 0; t < ticks; t++) hashes.Add(TickAndCheck(sim));
        }
        summary = $"seed {seed}: {placed} placed, {sealRefusals} seal refusals, {oracleChecks} oracle checks, {cancels} cancels, {destroys} hits, {repairs} repairs; {b.Count} buildings at the end";
        return hashes;
    }

    private static ulong TickAndCheck(Simulation sim)
    {
        sim.Tick();
        string? err = Check(sim.World);
        Assert.True(err == null, $"tick {sim.World.TickNumber}: {err}");
        return sim.StateHash();
    }

    private static EntityHandle FirstWorker(UnitStore u, int p)
    {
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == p) return new EntityHandle(i, u.Generation[i]);
        return default;
    }

    private static int RandomBuilding(ref SimRng rng, BuildingStore b, int owner, bool site)
    {
        int start = rng.NextInt(0, b.Capacity);
        for (int n = 0; n < b.Capacity; n++)
        {
            int k = (start + n) % b.Capacity;
            if (b.Alive[k] && (owner < 0 || b.Owner[k] == owner) && (!site || b.UnderConstruction[k])) return k;
        }
        return -1;
    }

    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)]
    [InlineData(5UL)] [InlineData(6UL)] [InlineData(7UL)] [InlineData(8UL)]
    public void FiveHundredActions_OracleAgrees_InvariantsHold(ulong seed)
    {
        Fuzz(seed, 500, oracle: true, out string summary);
        _out.WriteLine(summary);
    }

    /// <summary>
    /// 500 accepted placements per seed through <c>Build</c> (all ten types, both players), each checked against the oracle
    /// (no connection lost), plus every refused-or-accepted candidate with a non-trivial ring checked for the same verdict
    /// as the oracle; every 25 placements a random building is destroyed (the damage seam), which can leave pockets.
    /// </summary>
    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)]
    [InlineData(5UL)] [InlineData(6UL)] [InlineData(7UL)] [InlineData(8UL)]
    public void FiveHundredPlacements_EveryVerdictMatchesTheFloodOracle(ulong seed)
    {
        Simulation sim = NewWorld(seed);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingStore b = w.Buildings;
        var rng = new SimRng(seed, 505);
        List<int> open = FlowFieldOracle.PassableCells(g);
        sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, MoveScenario.Center(g, open[0])));
        sim.Enqueue(Command.SpawnUnit(1, TestSim.Data.FindUnit("whirlwind_camp_follower"), MoveScenario.Center(g, open[^1])));
        sim.Tick();
        sim.Tick();
        var worker = new[] { new EntityHandle(0, w.Units.Generation[0]), new EntityHandle(1, w.Units.Generation[1]) };
        Assert.True(w.Units.Owner[0] == 0 && w.Units.Owner[1] == 1);
        int placed = 0, checks = 0, seals = 0, destroyed = 0, tries = 0;
        while (placed < 500 && tries < 200_000)
        {
            int p = rng.NextInt(0, 2);
            BuildMaps.SetTotals(sim, p, 100_000, 100_000);
            int type = TypeOf(p, rng.NextInt(0, 10));
            BuildingDef d = TestSim.Data.Buildings[type];
            List<int> nearBlocked = NearBlocked(g);
            int anchor = -1;
            for (int k = 0; k < 60 && anchor < 0; k++)
            {
                tries++;
                int c = Anchor(ref rng, g, nearBlocked);
                if ((uint)c >= (uint)(g.Width * g.Height)) continue;
                int x0 = c % g.Width, y0 = c / g.Width;
                bool can = w.CanPlace(p, type, c, out PlacementError why);
                if (why is PlacementError.OffMap or PlacementError.Blocked) continue;
                if (!SealOracle.RingOpen(g, x0, y0, d.FootprintWidth, d.FootprintHeight))
                {
                    bool oracle = SealOracle.Seals(g, x0, y0, d.FootprintWidth, d.FootprintHeight);
                    Assert.True(oracle == (why == PlacementError.SealsGround), $"seed {seed}: {d.Id} at ({x0}, {y0}): CanPlace {why}, oracle seals {oracle}");
                    checks++;
                }
                if (why == PlacementError.SealsGround) seals++;
                if (can) anchor = c;
            }
            // Extra probes next to blocked ground (checked, not placed): the other player's types and sizes too.
            for (int k = 0; k < 8; k++)
            {
                int c = nearBlocked[rng.NextInt(0, nearBlocked.Count)] + rng.NextInt(-3, 1) + rng.NextInt(-3, 1) * g.Width;
                int q = rng.NextInt(0, 2), t = TypeOf(q, rng.NextInt(0, 10));
                BuildingDef dq = TestSim.Data.Buildings[t];
                if ((uint)c >= (uint)(g.Width * g.Height)) continue;
                w.CanPlace(q, t, c, out PlacementError why);
                if (why is PlacementError.OffMap or PlacementError.Blocked) continue;
                int x0 = c % g.Width, y0 = c / g.Width;
                if (SealOracle.RingOpen(g, x0, y0, dq.FootprintWidth, dq.FootprintHeight)) continue;
                bool oracle = SealOracle.Seals(g, x0, y0, dq.FootprintWidth, dq.FootprintHeight);
                Assert.True(oracle == (why == PlacementError.SealsGround), $"seed {seed}: probe {dq.Id} at ({x0}, {y0}): CanPlace {why}, oracle seals {oracle}");
                checks++;
                if (why == PlacementError.SealsGround) seals++;
            }
            if (anchor < 0) continue;
            int[] before = SealOracle.Labels(g);
            sim.Enqueue(Command.Build(p, worker[p], type, MoveScenario.Center(g, anchor)));
            sim.Tick();
            sim.Tick();
            int now = b.SlotAt(anchor % g.Width, anchor / g.Width);
            Assert.True(now >= 0 && b.Cell[now] == anchor && b.Owner[now] == p, $"seed {seed}: CanPlace said None, the Build placed nothing");
            int lost = SealOracle.LostConnection(before, SealOracle.Labels(g));
            Assert.True(lost < 0, $"seed {seed}: placing {d.Id} at {anchor} cut cell {lost} off");
            placed++;
            if (placed % 25 == 0)
            {
                int k = RandomBuilding(ref rng, b, -1, site: false);
                if (k >= 0) { b.Damage(b.HandleOf(k), 1_000_000); destroyed++; }
            }
        }
        _out.WriteLine($"seed {seed}: {placed} placements, {checks} verdicts checked against the oracle ({seals} sealing refusals seen), {destroyed} destroyed, {tries} candidates, {b.Count} buildings standing");
        Assert.Equal(500, placed);
        Assert.True(checks > 1000 && seals > 0, $"{checks} oracle checks, {seals} sealing");
    }

    [Theory]
    [InlineData(11UL)] [InlineData(12UL)] [InlineData(13UL)] [InlineData(14UL)]
    public void Twins_SameSeedSameHashEveryTick_OtherSeedDiffers(ulong seed)
    {
        List<ulong> a = Fuzz(seed, 300, oracle: false, out string summary);
        List<ulong> b = Fuzz(seed, 300, oracle: false, out _);
        Assert.Equal(a.Count, b.Count);
        for (int t = 0; t < a.Count; t++) Assert.True(a[t] == b[t], $"seed {seed}: twins part at hash {t}");
        List<ulong> c = Fuzz(seed + 100, 300, oracle: false, out _);
        Assert.NotEqual(a[^1], c[^1]);
        _out.WriteLine($"{summary}; {a.Count} hashes identical");
    }

    [Fact]
    public void HashAudit_EveryNewFieldMovesTheHash()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(30, 20));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        EntityHandle keep = GatherMaps.Building(sim, 3, 3);
        EntityHandle w = GatherMaps.Unit(sim, GatherMaps.At(sim, 15, 15));
        sim.Enqueue(Command.Build(0, w, BuildMaps.House, GatherMaps.At(sim, 20, 10)));
        GatherMaps.Run(sim, 2);
        BuildingStore b = sim.World.Buildings;
        UnitStore u = sim.World.Units;
        int site = BuildMaps.SiteAt(sim, 20, 10);
        var mutations = new (string Name, Action Do, Action Undo)[]
        {
            ("Work", () => b.SetWork(site, b.Work[site] + 1), () => b.SetWork(site, b.Work[site] - 1)),
            ("UnderConstruction", () => b.SetWork(site, b.WorkNeeded(BuildMaps.House)), () => { }),
            ("RepairProgress", () => b.RepairProgress(keep.Index) = 7, () => b.RepairProgress(keep.Index) = 0),
            ("RepairGold", () => b.RepairGold(keep.Index) = 7, () => b.RepairGold(keep.Index) = 0),
            ("RepairWood", () => b.RepairWood(keep.Index) = 7, () => b.RepairWood(keep.Index) = 0),
            ("RepairWood high bits", () => b.RepairWood(keep.Index) = 1L << 40, () => b.RepairWood(keep.Index) = 0),
            ("BuildTarget", () => u.BuildTarget[w.Index] = keep, () => u.BuildTarget[w.Index] = b.HandleOf(site)),
            ("BuildTarget generation", () => u.BuildTarget[w.Index] = new EntityHandle(site, b.Generation[site] + 1), () => u.BuildTarget[w.Index] = b.HandleOf(site)),
            ("QueueTypeId", () => { u.QueueKind[w.Index * Orders.OrderConstants.QueueCapacity] = CommandKind.Build; u.QueueTypeId[w.Index * Orders.OrderConstants.QueueCapacity] = 3; u.QueueCount[w.Index] = 1; }, () => { }),
        };
        ulong last = sim.StateHash();
        foreach ((string name, Action doIt, Action undo) in mutations)
        {
            doIt();
            ulong h = sim.StateHash();
            Assert.True(h != last, $"{name} isn't hashed");
            undo();
            last = sim.StateHash();
        }
        // The queued type id alone (same kind and position) moves the hash.
        int at = w.Index * Orders.OrderConstants.QueueCapacity;
        ulong h3 = sim.StateHash();
        u.QueueTypeId[at] = 4;
        Assert.NotEqual(h3, sim.StateHash());
    }

    /// <summary>This class's allocation test: it runs alone in <see cref="SerialCollection"/> while the fuzz rows above stay in the parallel batch.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Allocation_SealFloodsPushOutAndJoins_AllocateNothing()
    {
        (Simulation sim, int wallX) = ConstructionQaTests.LongWall(128, commands: 256);
        BuildMaps.SetTotals(sim, 0, 1_000_000, 1_000_000);
        UnitStore u = sim.World.Units;
        EntityHandle[] ws = Enumerable.Range(0, 6).Select(i => GatherMaps.Unit(sim, GatherMaps.At(sim, 10 + 2 * i, 60))).ToArray();
        int round = 0, sink = 0;
        NavGrid g = sim.World.NavGrid;
        Action block = () =>
        {
            // A long-detour "yes" and a sealing "no" through CanPlace; a placement on top of two own workers (push-out);
            // a join; a cancel of last round's site.
            if (sim.World.CanPlace(0, BuildMaps.House, wallX + g.Width, out _)) sink++;
            if (sim.World.CanPlace(0, BuildMaps.House, 5 * g.Width + wallX - 1, out _)) sink++;
            int x = 20 + 4 * (round % 20), y = 80 + 6 * (round / 20 % 5);
            sim.Enqueue(Command.Build(0, ws[0], BuildMaps.House, GatherMaps.At(sim, x, y)));
            sim.Enqueue(Command.Build(0, ws[1], BuildMaps.House, GatherMaps.At(sim, x, y)));
            for (int t = 0; t < 30; t++) sim.Tick();
            sim.Enqueue(Command.Cancel(0, GatherMaps.At(sim, x, y)));
            sim.Tick();
            sim.Tick();
            round++;
        };
        block();
        AllocationProbe.AssertZero(block, _out);
        Assert.True(sink >= 0);
    }
}
}
