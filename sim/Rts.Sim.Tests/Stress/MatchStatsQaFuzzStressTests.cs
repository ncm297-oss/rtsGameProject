using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M6-S1 (session 2026-10-17-0056): the match stats counters against independent oracles, every tick, in 4-seat AI-v-AI
/// matches (seeds 1-6, 6,000 ticks; seeds 1-3 with raiders so they fight from the start, 4-6 without, for 9,000 ticks, so they grow and research) with random cancels of sites and queue items injected for every player (refunds), three
/// sims per row: A and its twin B (same config), and C sampling every tick at the default capacity (1,024, so its buffer is
/// exhausted at tick 1,024). Every tick: A, B and C hash the same (the stats and their cadence decide nothing); A's and B's
/// counters and samples are equal; and A's counters move exactly as the oracles say:
/// <list type="bullet">
/// <item>gathered: the cargo that live workers (same handle, same cargo kind) carried into the tick and not out of it (a
/// worker that died this tick with cargo is the only slack). A refund moves the total but carries no cargo, so a build that
/// counted a refund as gathered fails here.</item>
/// <item>trained: units new this tick that are not summons, plus the non-expired deaths of units born this tick.</item>
/// <item>built: sites that were under construction and stand finished (slack: sites that died this tick).</item>
/// <item>the kill / loss halves: this tick's <see cref="World.Deaths"/>, split by <see cref="DeathEvent.IsBuilding"/>,
/// expiries and collapses skipped.</item>
/// <item>the researched tick: exactly the tick count at which <see cref="World.HasTech"/> first reads true.</item>
/// <item>samples: the recount (slot != worker, cost gold + wood) at each sample tick; C's buffer stops at 1,024 and A's every
/// 200th tick equals C's sample at the same tick.</item>
/// </list>
/// </summary>
public class MatchStatsQaFuzzStressTests
{
    private static readonly Lazy<GameData> s_earlyWaves = new(() => AiScenes.EarlyWaves());

    private readonly ITestOutputHelper _out;

    public MatchStatsQaFuzzStressTests(ITestOutputHelper output) => _out = output;

    private const int Seats = 4;

    /// <summary>The live army value of <paramref name="p"/>, recounted from the slot rule (independent of <see cref="UnitDef.IsWorker"/>).</summary>
    internal static int ArmyValue(World w, int p)
    {
        UnitStore u = w.Units;
        long sum = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != p) continue;
            UnitDef d = w.Data.Units[u.TypeId[i]];
            if (d.Slot == UnitSlot.Worker) continue;
            sum += d.CostGold + d.CostWood;
        }
        return (int)Math.Min(sum, int.MaxValue);
    }

    private static Vector2 Inside(World w, int k)
    {
        CombatSystem.BuildingRect(w, k, out Vector2 min, out Vector2 max);
        return (min + max) * 0.5f;
    }

    /// <summary>This tick's injected cancels (identical for every sim of a row): a site cancel or a queue-item cancel for a random live building, about one every 80 ticks.</summary>
    private static void Cancels(World w, ref SimRng rng, Simulation[] sims, bool techs, ref int injected)
    {
        if (rng.NextInt(0, 80) != 0) return;
        BuildingStore b = w.Buildings;
        int start = rng.NextInt(0, b.Capacity);
        for (int m = 0; m < b.Capacity; m++)
        {
            int k = (start + m) % b.Capacity;
            if (!b.Alive[k]) continue;
            int p = b.Owner[k];
            if (b.UnderConstruction[k])
            {
                if (rng.NextInt(0, 3) != 0) return; // sites less often, so the AIs still grow
                foreach (Simulation s in sims) s.Enqueue(Command.Cancel(p, Inside(w, k)));
                injected++;
                return;
            }
            if (b.QueueCount[k] > 0)
            {
                int item = rng.NextInt(0, b.QueueCount[k]);
                if (!techs && b.QueueIsTechAt(k, item)) return;
                foreach (Simulation s in sims) s.Enqueue(Command.CancelTrain(p, Inside(w, k), item));
                injected++;
                return;
            }
        }
    }

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL, 3)]
    [InlineData(2UL, 3)]
    [InlineData(3UL, 3)]
    [InlineData(4UL, 0)]
    [InlineData(5UL, 0)]
    [InlineData(6UL, 0)]
    public void FourSeatAi_EveryTick_CountersMatchTheOracles_AndTwinsAndCadencesHashTheSame(ulong seed, int raiders)
    {
        int runTicks = raiders > 0 ? 6000 : 9000; // the raid-free rows run long enough for research
        var seats = new PlayerSeat[Seats];
        for (int p = 0; p < Seats; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, p % 3 == 0 ? AiDifficulty.Hard : AiDifficulty.Normal);
        GameData data = s_earlyWaves.Value;
        Simulation a = AiScenes.Generated(seats, seed, record: false, out _, units: 600, data: data, raiders: raiders);
        Simulation b = AiScenes.Generated(seats, seed, record: false, out _, units: 600, data: data, raiders: raiders);
        Simulation c = AiScenes.Generated(seats, seed, record: false, out _, units: 600, data: data, raiders: raiders,
            configure: cfg => cfg with { StatsSampleTicks = 1 });
        var sims = new[] { a, b, c };
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        int techs = w.Data.Techs.Length;
        int carry = w.Data.Rules.WorkerCarry;
        Assert.Equal(1024, c.World.StatsSampleCapacity);

        // The previous tick's view of A.
        var alive = new bool[u.Capacity];
        var gen = new int[u.Capacity];
        var cargo = new int[u.Capacity];
        var kind = new ResourceKind[u.Capacity];
        var site = new bool[bs.Capacity];
        var bAlive = new bool[bs.Capacity];
        var bGen = new int[bs.Capacity];
        var had = new bool[Seats, techs];
        var prev = new int[Seats, 10];
        var rng = new SimRng(seed, 85_001);
        int injected = 0, refunds = 0, delivered = 0, trainedSeen = 0, builtSeen = 0, collapses = 0, deathsSeen = 0, techsSeen = 0;

        // Ticks 1-2 apply the setup commands (halls, workers, raiders: dev spawns, nothing counted).
        foreach (Simulation s in sims) { s.Tick(); s.Tick(); }
        for (int p = 0; p < Seats; p++)
        {
            Assert.Equal(0, w.UnitsTrained[p]);
            Assert.Equal(0, w.BuildingsBuilt[p]);
            Assert.Equal((0, 0), (w.GatheredGold[p], w.GatheredWood[p]));
        }

        for (int t = 2; t < runTicks; t++)
        {
            for (int i = 0; i < u.Capacity; i++)
            {
                alive[i] = u.Alive[i];
                gen[i] = u.Generation[i];
                cargo[i] = u.Cargo[i];
                kind[i] = u.CargoKind[i];
            }
            for (int k = 0; k < bs.Capacity; k++)
            {
                bAlive[k] = bs.Alive[k];
                bGen[k] = bs.Generation[k];
                site[k] = bs.Alive[k] && bs.UnderConstruction[k];
            }
            for (int p = 0; p < Seats; p++)
            {
                int[] now0 = Counters(w, p);
                for (int x = 0; x < now0.Length; x++) prev[p, x] = now0[x];
                for (int tech = 0; tech < techs; tech++) had[p, tech] = w.HasTech(p, tech);
            }
            int[] totalsBefore = new int[Seats];
            for (int p = 0; p < Seats; p++) totalsBefore[p] = w.Gold[p] + w.Wood[p];

            Cancels(w, ref rng, sims, techs: raiders > 0, ref injected); // the raid-free rows keep their research
            foreach (Simulation s in sims) s.Tick();
            int tick = a.TickNumber;
            string at = $"seed {seed} tick {tick}";

            ulong h = a.StateHash();
            Assert.True(h == b.StateHash(), $"{at}: twin hash differs");
            Assert.True(h == c.StateHash(), $"{at}: the every-tick-sampling sim's hash differs");

            // Oracles for this tick.
            var gold = new long[Seats];
            var wood = new long[Seats];
            var slackG = new long[Seats];
            var slackW = new long[Seats];
            var trained = new int[Seats];
            var built = new int[Seats];
            var builtSlack = new int[Seats];
            for (int i = 0; i < u.Capacity; i++)
            {
                bool same = alive[i] && u.Alive[i] && u.Generation[i] == gen[i];
                if (same && cargo[i] > 0 && u.Cargo[i] == 0 && u.CargoKind[i] == kind[i])
                {
                    Assert.True(cargo[i] == carry, $"{at} unit {i}: a partial load {cargo[i]} left the cargo");
                    if (kind[i] == ResourceKind.Gold) gold[u.Owner[i]] += cargo[i];
                    else wood[u.Owner[i]] += cargo[i];
                }
                else if (alive[i] && (!u.Alive[i] || u.Generation[i] != gen[i]) && cargo[i] > 0)
                {
                    int owner = -1;
                    foreach (DeathEvent e in w.Deaths)
                        if (!e.IsBuilding && e.Victim.Index == i && e.Victim.Generation == gen[i]) owner = e.VictimOwner;
                    if (owner >= 0 && owner < Seats)
                    {
                        if (kind[i] == ResourceKind.Gold) slackG[owner] += cargo[i];
                        else slackW[owner] += cargo[i];
                    }
                }
                bool fresh = u.Alive[i] && (!alive[i] || u.Generation[i] != gen[i]);
                if (fresh && !u.IsSummoned(i)) trained[u.Owner[i]]++;
            }
            for (int k = 0; k < bs.Capacity; k++)
            {
                if (!site[k]) continue;
                if (bs.Alive[k] && bs.Generation[k] == bGen[k] && !bs.UnderConstruction[k]) built[bs.Owner[k]]++;
                else if (!bs.Alive[k] || bs.Generation[k] != bGen[k])
                    foreach (DeathEvent e in w.Deaths)
                        if (e.IsBuilding && e.Victim.Index == k && e.Victim.Generation == bGen[k] && e.VictimOwner < Seats && e.VictimOwner >= 0) builtSlack[e.VictimOwner]++;
            }
            var uk = new int[Seats];
            var bk = new int[Seats];
            var ul = new int[Seats];
            var bl = new int[Seats];
            foreach (DeathEvent e in w.Deaths)
            {
                deathsSeen++;
                if (e.Collapsed) collapses++;
                if (e.Expired || e.Collapsed) continue;
                if (!e.IsBuilding)
                {
                    // Born and killed in the same tick: trained but never seen alive.
                    bool wasAlive = alive[e.Victim.Index] && gen[e.Victim.Index] == e.Victim.Generation;
                    if (!wasAlive && (uint)e.VictimOwner < Seats) trained[e.VictimOwner]++;
                }
                if ((uint)e.KillerOwner < Seats) { if (e.IsBuilding) bk[e.KillerOwner]++; else uk[e.KillerOwner]++; }
                if ((uint)e.VictimOwner < Seats) { if (e.IsBuilding) bl[e.VictimOwner]++; else ul[e.VictimOwner]++; }
            }

            for (int p = 0; p < Seats; p++)
            {
                int[] now = Counters(w, p);
                for (int x = 0; x < now.Length; x++)
                    Assert.True(now[x] >= prev[p, x], $"{at} player {p}: counter {x} fell {prev[p, x]} -> {now[x]}");
                long dG = now[0] - prev[p, 0], dW = now[1] - prev[p, 1];
                Assert.True(dG >= gold[p] && dG <= gold[p] + slackG[p], $"{at} player {p}: gathered gold +{dG}, drop-offs {gold[p]} (slack {slackG[p]})");
                Assert.True(dW >= wood[p] && dW <= wood[p] + slackW[p], $"{at} player {p}: gathered wood +{dW}, drop-offs {wood[p]} (slack {slackW[p]})");
                delivered += (int)(dG + dW);
                int totalRise = w.Gold[p] + w.Wood[p] - totalsBefore[p];
                if (totalRise > dG + dW) refunds++; // money came back that no drop-off brought: a refund this tick
                Assert.True(now[2] - prev[p, 2] == trained[p], $"{at} player {p}: trained +{now[2] - prev[p, 2]}, new non-summoned units {trained[p]}");
                trainedSeen += trained[p];
                Assert.True(now[3] - prev[p, 3] == uk[p] && now[4] - prev[p, 4] == bk[p], $"{at} player {p}: kills +{now[3] - prev[p, 3]} / +{now[4] - prev[p, 4]}, deaths {uk[p]} / {bk[p]}");
                Assert.True(now[5] - prev[p, 5] == ul[p] && now[6] - prev[p, 6] == bl[p], $"{at} player {p}: losses +{now[5] - prev[p, 5]} / +{now[6] - prev[p, 6]}, deaths {ul[p]} / {bl[p]}");
                int dBuilt = now[7] - prev[p, 7];
                Assert.True(dBuilt >= built[p] && dBuilt <= built[p] + builtSlack[p], $"{at} player {p}: built +{dBuilt}, completed sites {built[p]} (slack {builtSlack[p]})");
                builtSeen += dBuilt;
                Assert.Equal(now[3] + now[4], w.Kills[p]);
                Assert.Equal(now[5] + now[6], w.Losses[p]);
                Assert.Equal(w.BuildingLosses[p], w.BuildingsLost[p]);
                for (int tech = 0; tech < techs; tech++)
                {
                    bool has = w.HasTech(p, tech);
                    int r = w.TechResearchedTick(p, tech);
                    if (!has) Assert.True(r == -1, $"{at} player {p} tech {tech}: tick {r} without the tech");
                    else if (!had[p, tech]) { Assert.True(r == tick, $"{at} player {p} tech {tech}: HasTech first true at {tick}, recorded {r}"); techsSeen++; }
                }
                // Twins: every counter and sample equal.
                Assert.True(now.AsSpan().SequenceEqual(Counters(b.World, p)), $"{at} player {p}: twin counters differ");
                Assert.True(w.ArmyValueSamples(p).SequenceEqual(b.World.ArmyValueSamples(p)), $"{at} player {p}: twin samples differ");
                Assert.True(now.AsSpan().SequenceEqual(Counters(c.World, p)), $"{at} player {p}: the every-tick sim's counters differ");
                for (int tech = 0; tech < techs; tech++)
                    Assert.Equal(w.TechResearchedTick(p, tech), b.World.TechResearchedTick(p, tech));
                // Samples.
                int army = ArmyValue(w, p);
                ReadOnlySpan<int> sa = w.ArmyValueSamples(p), sc = c.World.ArmyValueSamples(p);
                Assert.True(sa.Length == tick / 200, $"{at} player {p}: {sa.Length} samples at interval 200");
                Assert.True(sc.Length == Math.Min(tick, 1024), $"{at} player {p}: {sc.Length} samples at interval 1");
                if (tick % 200 == 0) Assert.True(sa[^1] == army, $"{at} player {p}: sample {sa[^1]}, recount {army}");
                if (tick <= 1024)
                {
                    Assert.True(sc[^1] == army, $"{at} player {p}: every-tick sample {sc[^1]}, recount {army}");
                    if (tick % 200 == 0) Assert.Equal(sa[^1], sc[tick - 1]);
                }
            }
        }
        int trainedTotal = 0, builtTotal = 0, kills = 0, bkills = 0, gatheredTotal = 0;
        for (int p = 0; p < Seats; p++)
        {
            trainedTotal += w.UnitsTrained[p];
            builtTotal += w.BuildingsBuilt[p];
            kills += w.Kills[p];
            bkills += w.BuildingKills[p];
            gatheredTotal += w.GatheredGold[p] + w.GatheredWood[p];
        }
        _out.WriteLine($"seed {seed}: cancels injected {injected}, ticks with a refund {refunds}, gathered {gatheredTotal}, trained {trainedTotal}, built {builtTotal}, kills {kills} ({bkills} buildings), deaths {deathsSeen}, collapses {collapses}, techs {techsSeen}");
        Assert.True(refunds > 0 && trainedTotal > 0 && builtTotal > 0 && gatheredTotal > 0 && (raiders == 0 || kills > 0), "precondition: the match did not play out (no refund, training, building, gathering or kill)");
    }

    /// <summary>Every per-player counter, in a fixed order.</summary>
    private static int[] Counters(World w, int p) => new[]
    {
        w.GatheredGold[p], w.GatheredWood[p], w.UnitsTrained[p], w.UnitKills[p], w.BuildingKills[p],
        w.UnitLosses[p], w.BuildingLosses[p], w.BuildingsBuilt[p], w.Kills[p], w.Losses[p],
    };
}
