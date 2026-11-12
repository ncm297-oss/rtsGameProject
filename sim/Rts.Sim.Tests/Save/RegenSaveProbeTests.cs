using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M7-H1 (BUG-1165's determinism probe): a Heavy Infantry hit on the regen copy (Malazan 1 HP/s after 6 s) and carrying a
/// regenerating status, saved mid-delay at tick 60, loaded through the stricter <c>REGN</c> reader, and both sims run to tick
/// 600: every tick's hash equal, the unit healed, and the loaded sim re-saves byte for byte at the save tick. It prints
/// "PROBE &lt;digest&gt;" (a fold of every tick's hash) so three processes can be compared.
/// </summary>
[Collection(SerialCollection.Name)]
public class RegenSaveProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void ARegeneratingUnit_SavedAndLoaded_PlaysOnHashIdentical_AndPrintsItsDigest()
    {
        GameData d = RegenTests.s_regen.Value;
        Simulation sim = RegenTests.Scene(d);
        EntityHandle h = CombatScenes.Place(sim, 0, d.FindUnit("malazan_heavy_infantry"), CombatScenes.At(sim, 20, 20));
        sim.Tick();
        RegenTests.Hit(sim, h, 30);
        StatusSystem.Apply(sim.World, h.Index, d.FindStatus("regenerating"), 1f, 400, 0);
        while (sim.TickNumber < 60) sim.Tick();
        int hpAtSave = sim.World.Units.Hp[h.Index];
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(d, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(loaded));
        var fold = new Determinism.StateHasher();
        while (sim.TickNumber < 600)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
            fold.Add(sim.StateHash());
        }
        Assert.True(sim.World.Units.Hp[h.Index] > hpAtSave, $"no heal: {hpAtSave} -> {sim.World.Units.Hp[h.Index]}");
        output.WriteLine($"PROBE {fold.Value:X16} hp {hpAtSave} -> {sim.World.Units.Hp[h.Index]}");
    }
}
