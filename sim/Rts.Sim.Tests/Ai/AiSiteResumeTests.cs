using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Economy;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-H1 (BUG-0640): an AI's own construction site nobody builds is resumed, and a builder whose walk to its site just ended
/// is not read as an idle worker. Root cause (Malazan, flat scene, 36,000 ticks): a builder pacing short of its site stood
/// Idle for one tick between movement's stuck stop and the construction system's stand; a think landing on that tick sent it
/// to gather, which cancelled the Build, and the AI had no path back to the site.
/// </summary>
public class AiSiteResumeTests
{
    private static readonly Lazy<GameData> s_quietOrder = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));

    /// <summary>
    /// An own Billet site with no builder (spawned straight into the store, as if its builder had been taken away): within
    /// <see cref="EconomyManager.StalledThinks"/> + 1 thinks the AI sends one worker a Build of its type at its anchor (a
    /// join, no new site), only one while that worker is on it, and the site finishes.
    /// </summary>
    [Fact]
    public void AnOwnSiteNobodyBuilds_GetsOneWorkerSentToJoinIt_AndIsFinished()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value);
        World w = sim.World;
        int house = TestSim.Data.FindBuilding("malazan_billet");
        int anchor = 30 * AiScenes.FlatSize + 18;
        Assert.True(w.Buildings.Spawn(0, house, anchor, out EntityHandle site, site: true));
        List<Command> rec = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        int finished = -1;
        for (int t = 0; t < 4000 && finished < 0; t++)
        {
            sim.Tick();
            if (!w.Buildings.UnderConstruction[site.Index]) finished = sim.TickNumber;
        }
        List<Command> joins = rec.Where(c => c.Kind == CommandKind.Build && c.TypeId == house).ToList();
        Command join = Assert.Single(joins);
        Assert.Equal(ai.CellCentre(anchor), join.Position);
        Assert.True(join.Tick <= (EconomyManager.StalledThinks + 1) * think + 1, $"joined at {join.Tick}");
        Assert.True(finished > 0, "the site was never finished");
        Assert.Equal(site, w.Buildings.HandleOf(site.Index)); // the same site, finished: no second one placed
    }

    /// <summary>
    /// The root cause: a worker with a building order standing Idle (its walk ended short of the site) is answered as
    /// Building, what the construction system makes it next tick; its build target is answered to its owner only.
    /// </summary>
    [Fact]
    public void AnIdleWorkerWithABuildOrder_IsSeenBuilding_AndItsTargetOnlyByItsOwner()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value);
        World w = sim.World;
        int house = TestSim.Data.FindBuilding("malazan_billet");
        Assert.True(w.Buildings.Spawn(0, house, 30 * AiScenes.FlatSize + 18, out EntityHandle site, site: true));
        EntityHandle worker = CombatScenes.Place(sim, 0, TestSim.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(30, 40));
        var own = new PlayerView(w, 0);
        var other = new PlayerView(w, 1);
        Assert.False(own.TryGetBuildTarget(worker.Index, out _));
        Assert.True(own.TryGetUnit(worker.Index, out SeenUnit before));
        Assert.Equal(UnitState.Idle, before.State);
        w.Units.BuildTarget[worker.Index] = site; // the stuck stop's tick: Idle, order still on
        Assert.Equal(UnitState.Idle, w.Units.State[worker.Index]);
        Assert.True(own.TryGetUnit(worker.Index, out SeenUnit during));
        Assert.Equal(UnitState.Building, during.State);
        Assert.True(own.TryGetBuildTarget(worker.Index, out EntityHandle target));
        Assert.Equal(site, target);
        Assert.False(other.TryGetBuildTarget(worker.Index, out _));
        ConstructionSystem.End(w.Units, worker.Index);
        Assert.True(own.TryGetUnit(worker.Index, out SeenUnit after));
        Assert.Equal(UnitState.Idle, after.State);
    }
}
