using Rts.Sim.Ai;
using Rts.Sim.Determinism;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-1 criterion 1 (M5 criterion 1): <see cref="PlayerView"/> exposes nothing the fog hides. Three seeds x 2,000 ticks of
/// <see cref="PlayerViewScene"/> (four players, stealth cast mid-run, detectors, towers, Sandstorms, buildings dying in and out
/// of sight): after every tick, every answer of every player's view (a kept one, and on every 50th tick a fresh one) equals
/// <see cref="PlayerViewOracle"/>'s: 0 leaks, 0 misses, every field equal.
/// </summary>
public class PlayerViewOracleFuzzTests
{
    private const int Ticks = 2000;
    private readonly ITestOutputHelper _out;

    public PlayerViewOracleFuzzTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    [InlineData(7UL)]
    public void EveryAnswer_EveryTick_EqualsTheBruteForceOracle(ulong seed)
    {
        Simulation sim = PlayerViewScene.New(seed);
        World w = sim.World;
        var kept = new PlayerView[PlayerViewScene.Players];
        for (int p = 0; p < kept.Length; p++) kept[p] = new PlayerView(w, p);
        var rng = new SimRng(seed, 512);
        var tally = new PlayerViewOracle.Tally();
        for (int t = 0; t < Ticks; t++)
        {
            PlayerViewScene.Orders(sim, t, ref rng);
            sim.Tick();
            for (int p = 0; p < kept.Length; p++)
            {
                // A view kept across ticks answers for the current state, the same as one built now.
                PlayerView view = t % 50 == 0 ? new PlayerView(w, p) : kept[p];
                PlayerViewOracle.Check(w, view, p, tally, $"seed {seed} tick {t} player {p}");
            }
            Assert.True(tally.First.Length == 0, tally.First + "\n" + tally);
        }
        _out.WriteLine($"seed {seed}: {tally}; units left {w.Units.Count}, buildings left {w.Buildings.Count}");
        Assert.Equal(0, tally.Leaks);
        Assert.Equal(0, tally.Misses);
        Assert.Equal(0, tally.FieldMismatches);
        Assert.Equal(0, tally.OracleDisagreements);
        // The run reached the cases the brief names.
        Assert.True(tally.StealthHidden > 0, "no enemy stood where its cell was seen but stealth hid it");
        Assert.True(tally.StealthDetected > 0, "no stealthed unit was detected");
        Assert.True(tally.InStorm > 0, "no unit was seen while a Sandstorm lived");
        Assert.True(tally.EnemyUnitsSeen > 0 && tally.EnemyUnitsHidden > 0, "no enemy was both seen and hidden");
        Assert.True(tally.EnemyBuildingsSeen > 0 && tally.Ghosts > 0, "no enemy building seen, or no ghost");
        Assert.True(tally.GhostsOfDeadBuildings > 0, "no ghost outlived its building");
        Assert.True(tally.ResourcesUnknownAmount > 0 && tally.Resources > tally.ResourcesUnknownAmount, "no resource node both in and out of sight");
        Assert.True(tally.QueueItems > 0, "no queue was read");
    }
}
