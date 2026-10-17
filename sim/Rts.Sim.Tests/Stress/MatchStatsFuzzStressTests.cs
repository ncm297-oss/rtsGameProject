using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S1 criterion 4: the stats counters' invariants in AI-v-AI matches (2-4 seats, raiders so they fight from the start),
/// checked every 10 ticks: the kill and loss halves sum to <see cref="World.Kills"/> / <see cref="World.Losses"/>, every
/// counter is monotone (a researched tick, once set, never moves, and is set exactly when <see cref="World.HasTech"/> holds),
/// the samples number <c>min(tick / interval, capacity)</c> and old samples never change; and a twin run (same seed, same
/// commands: the AIs are deterministic) ends with identical counters and samples.
/// </summary>
public class MatchStatsFuzzStressTests
{
    // AIs that wave early (AiScenes.EarlyWaves), loaded once for the three rows.
    private static readonly Lazy<GameData> s_earlyWaves = new(() => AiScenes.EarlyWaves());

    private readonly ITestOutputHelper _out;

    public MatchStatsFuzzStressTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(3UL, 2, 200, 1024)]
    [InlineData(7UL, 3, 200, 1024)]
    [InlineData(11UL, 4, 7, 100)] // a short interval and a small buffer: the buffer fills mid-match
    public void TheCounters_KeepTheirInvariants_AndATwinRunMatches(ulong seed, int seats, int interval, int capacity)
    {
        const int ticks = 9000; // 7.5 min: waves, razing and research happen
        var seatList = new PlayerSeat[seats];
        for (int p = 0; p < seats; p++) seatList[p] = new PlayerSeat(p % 2, SeatController.Ai, p % 2 == 0 ? AiDifficulty.Hard : AiDifficulty.Normal);
        Func<SimConfig, SimConfig> configure = c => c with { StatsSampleTicks = interval, StatsSampleCapacity = capacity };
        GameData data = s_earlyWaves.Value;
        Simulation a = AiScenes.Generated(seatList, seed, record: false, out _, units: 600, data: data, raiders: 4, configure: configure);
        Simulation b = AiScenes.Generated(seatList, seed, record: false, out _, units: 600, data: data, raiders: 4, configure: configure);
        World w = a.World;
        int techs = w.Data.Techs.Length;
        var last = new int[seats, 10];
        var lastTech = new int[seats, techs];
        for (int p = 0; p < seats; p++)
            for (int t = 0; t < techs; t++) lastTech[p, t] = -1;
        var samples = new List<int>[seats];
        for (int p = 0; p < seats; p++) samples[p] = new List<int>();
        for (int t = 0; t < ticks; t++)
        {
            a.Tick();
            b.Tick();
            if (a.TickNumber % 10 != 0) continue;
            string at = $"seed {seed} seats {seats} tick {a.TickNumber}";
            for (int p = 0; p < seats; p++)
            {
                Assert.True(w.UnitKills[p] + w.BuildingKills[p] == w.Kills[p], $"{at} player {p}: kills {w.UnitKills[p]} + {w.BuildingKills[p]} != {w.Kills[p]}");
                Assert.True(w.UnitLosses[p] + w.BuildingLosses[p] == w.Losses[p], $"{at} player {p}: losses {w.UnitLosses[p]} + {w.BuildingLosses[p]} != {w.Losses[p]}");
                Assert.Equal(w.BuildingLosses[p], w.BuildingsLost[p]);
                int[] now = Counters(w, p);
                for (int c = 0; c < now.Length; c++)
                {
                    Assert.True(now[c] >= last[p, c], $"{at} player {p}: counter {c} fell {last[p, c]} -> {now[c]}");
                    last[p, c] = now[c];
                }
                for (int tech = 0; tech < techs; tech++)
                {
                    int r = w.TechResearchedTick(p, tech);
                    Assert.True(w.HasTech(p, tech) == (r >= 0), $"{at} player {p} tech {tech}: HasTech {w.HasTech(p, tech)}, tick {r}");
                    Assert.True(lastTech[p, tech] < 0 || lastTech[p, tech] == r, $"{at} player {p} tech {tech}: tick moved {lastTech[p, tech]} -> {r}");
                    Assert.True(r <= a.TickNumber, $"{at} player {p} tech {tech}: tick {r} in the future");
                    lastTech[p, tech] = r;
                }
                ReadOnlySpan<int> s = w.ArmyValueSamples(p);
                Assert.True(s.Length == Math.Min(a.TickNumber / interval, capacity), $"{at} player {p}: {s.Length} samples");
                for (int k = 0; k < samples[p].Count; k++)
                    Assert.True(s[k] == samples[p][k], $"{at} player {p}: sample {k} changed {samples[p][k]} -> {s[k]}");
                for (int k = samples[p].Count; k < s.Length; k++)
                {
                    Assert.True(s[k] >= 0, $"{at} player {p}: sample {k} = {s[k]}");
                    samples[p].Add(s[k]);
                }
            }
        }
        // The twin: identical hashes (the stats are outside them), counters and samples.
        Assert.Equal(a.StateHash(), b.StateHash());
        int kills = 0, buildingKills = 0, trained = 0, built = 0, gathered = 0, armyPeak = 0, researched = 0;
        for (int p = 0; p < seats; p++)
        {
            Assert.Equal(Counters(w, p), Counters(b.World, p));
            Assert.Equal(w.ArmyValueSamples(p).ToArray(), b.World.ArmyValueSamples(p).ToArray());
            for (int tech = 0; tech < techs; tech++)
            {
                Assert.Equal(w.TechResearchedTick(p, tech), b.World.TechResearchedTick(p, tech));
                if (w.TechResearchedTick(p, tech) >= 0) researched++;
            }
            kills += w.Kills[p];
            buildingKills += w.BuildingKills[p];
            trained += w.UnitsTrained[p];
            built += w.BuildingsBuilt[p];
            gathered += w.GatheredGold[p] + w.GatheredWood[p];
            foreach (int v in w.ArmyValueSamples(p)) armyPeak = Math.Max(armyPeak, v);
        }
        _out.WriteLine($"seed {seed}, {seats} seats: kills {kills} ({buildingKills} buildings), trained {trained}, built {built}, gathered {gathered}, techs {researched}, army peak {armyPeak}, samples {w.ArmyValueSamples(0).Length}");
        Assert.True(kills > 0 && trained > 0 && built > 0 && gathered > 0 && armyPeak > 0, "precondition: the match did not play out");
    }

    /// <summary>Every per-player counter, in a fixed order.</summary>
    private static int[] Counters(World w, int p) => new[]
    {
        w.GatheredGold[p], w.GatheredWood[p], w.UnitsTrained[p], w.UnitKills[p], w.BuildingKills[p],
        w.UnitLosses[p], w.BuildingLosses[p], w.BuildingsBuilt[p], w.Kills[p], w.Losses[p],
    };
}
