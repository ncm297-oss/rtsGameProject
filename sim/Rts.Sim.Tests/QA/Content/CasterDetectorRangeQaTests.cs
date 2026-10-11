using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D12 (session 2026-10-10-2334): the shipped casters' 10 m detector, played on the shipped data. A stealthed enemy worker
/// on ground the caster sees (its sight is 16 m) at 9.9 m is detected, at 10.1 m it is not; a non-caster at 9.9 m (and well
/// outside the 3 m proximity rule) never detects. Uses the M4-5 harness (StealthTests.Stealth, CombatScenes.Place).
/// </summary>
public class CasterDetectorRangeQaTests
{
    private static GameData Data => TestSim.Data;

    [Theory]
    [InlineData("malazan_cadre_mage", "whirlwind_camp_follower", 9.9f, true)]
    [InlineData("malazan_cadre_mage", "whirlwind_camp_follower", 10.1f, false)]
    [InlineData("whirlwind_priest", "malazan_laborer", 9.9f, true)]
    [InlineData("whirlwind_priest", "malazan_laborer", 10.1f, false)]
    [InlineData("malazan_crossbowman", "whirlwind_camp_follower", 9.9f, false)]
    [InlineData("whirlwind_desert_archer", "malazan_laborer", 9.9f, false)]
    public void AStealthedEnemyOnSeenGround_IsDetectedOnlyInsideTheCastersTenMetres(string watcherId, string hiderId, float metres, bool detected)
    {
        var sim = new Simulation(new SimConfig(1, 2, 32, 8 * 32 + 32) { Data = Data, Combat = true }, LocalMovementTests.Flat(64));
        World w = sim.World;
        Vector2 p = At(sim, 30, 30);
        EntityHandle watcher = Place(sim, 0, Data.FindUnit(watcherId), p);
        EntityHandle hidden = Place(sim, 1, Data.FindUnit(hiderId), Off(p, metres, 0f));
        sim.Enqueue(Command.HoldPosition(0, watcher));
        sim.Enqueue(Command.HoldPosition(1, hidden));
        StealthTests.Stealth(sim, hidden);
        for (int t = 0; t < 10; t++) sim.Tick();

        float d = Vector2.Distance(w.Units.Position[watcher.Index], w.Units.Position[hidden.Index]);
        Assert.True(Math.Abs(d - metres) < 0.01f, $"{watcherId} / {hiderId}: the pair moved, {d} m apart");
        Assert.True(w.Fog.SeesUnitCell(0, hidden.Index), $"{watcherId}: the hidden unit's cell at {d} m is not on seen ground");
        Assert.True(w.IsStealthed(hidden.Index), $"{hiderId}: no longer stealthed");
        Assert.True(detected == w.Fog.CanSeeUnit(0, hidden.Index),
            $"{watcherId} (detector {Data.Units[Data.FindUnit(watcherId)].Detector} m) at {d} m: detected {!detected}, want {detected}");
    }
}
