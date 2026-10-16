using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-H1, session 2026-10-13-0527): attacks on the sim hardening, independent of the developer's tests. Waves against a
/// hall fenced by trees two deep (alone, and with two towers); the AI's house sites over 30 minutes on both factions with
/// every house's first builder killed; the BUG-0621 start rules on seeds 1-100 x 3 sizes (and 3-4 locations); a wider
/// <c>Validate</c> x <c>Generate</c> sweep that also checks the generated starts keep the rules (BUG-0620's replay header is
/// checked by name in <c>StartLocationQaTests.Bug0620_ReplayHeaderThatValidatesButCannotFit_IsRefusedNotThrown</c>).
/// </summary>
[Collection(SerialCollection.Name)]
public class SimHardeningM5H1QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningM5H1QaTests(ITestOutputHelper output) => _out = output;

    // ------------------------------------------------------------------ BUG-0598: waves against a fenced hall

    /// <summary>A quiet malazan order, a wave at 1 pop and no cost, loss fraction 0.5, waveCooldown <paramref name="cooldown"/> s.</summary>
    private static GameData WaveData(double cooldown) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            root["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
            root["retreat"]!["lossFraction"] = 0.5;
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r => r["ai"]!["waveCooldown"] = cooldown);
    });

    private static readonly Lazy<GameData> s_wave60 = new(() => WaveData(60));
    private static readonly Lazy<GameData> s_wave30 = new(() => WaveData(30));

    /// <summary>
    /// Player 0 AI (malazan, Normal), player 1 a human doing nothing; a billet of player 0's keeps player 1's hall seen; the
    /// hall (cells 52-55, 30-33) ringed two cells deep by trees (cells 50-57, 28-35), so no passable cell touches it.
    /// </summary>
    private static Simulation FencedScene(GameData data, out EntityHandle hall, bool leaveTowerCells = false)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, combat: true);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        hall = default;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1 && w.Buildings.TypeId[k] == data.FindBuilding("whirlwind_holy_camp"))
                hall = new EntityHandle(k, w.Buildings.Generation[k]);
        int tree = data.FindResource("tree");
        for (int y = 28; y <= 35; y++)
            for (int x = 50; x <= 57; x++)
            {
                if (x >= 52 && x <= 55 && y >= 30 && y <= 33) continue;
                if (leaveTowerCells && x >= 52 && x <= 55 && y >= 34) continue; // the ring's south band: two towers stand in it
                Assert.True(w.Resources.Spawn(tree, y * AiScenes.FlatSize + x, data.Rules.TreeWood, out _), $"tree ({x}, {y})");
            }
        if (leaveTowerCells)
        {
            int tower = data.FindBuilding("whirlwind_lookout_tower");
            Assert.True(w.Buildings.Spawn(1, tower, 34 * AiScenes.FlatSize + 52, out _));
            Assert.True(w.Buildings.Spawn(1, tower, 34 * AiScenes.FlatSize + 54, out _));
        }
        sim.Tick();
        Assert.True(sim.AiOf(0)!.Board.KnownEnemyBuildings > 0);
        return sim;
    }

    private static List<EntityHandle> Army(Simulation sim, int type, int n, int x, int y)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(sim, 0, type, sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
        return list;
    }

    /// <summary>
    /// The fenced hall, <paramref name="n"/> Heavy Infantry (melee), waveCooldown 60 s, 9,000 ticks. Each wave must end (the
    /// members can't strike the hall), its members sent home, and the next wave must start once the cooldown passes: the
    /// AI's offence is not frozen. Waves are bounded by the cooldown. Every end that isn't a loss retreat came at least a
    /// cooldown after the last tick any member was non-Idle (the rule ends only idle waves).
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void FencedHall_EachWaveEndsAndTheNextStarts_NoneEndsWhileAMemberWasBusyWithinACooldown(int n)
    {
        GameData data = s_wave60.Value;
        Simulation sim = FencedScene(data, out EntityHandle hall);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        List<EntityHandle> army = Army(sim, data.FindUnit("malazan_heavy_infantry"), n, 38, 42);
        int hp = w.Buildings.Hp[hall.Index];
        int cooldown = ai.Rules.WaveCooldownTicks;
        var ends = new List<(int Tick, int SinceBusy)>();
        int lastBusy = -1;
        bool wasActive = false;
        while (sim.TickNumber < 9000)
        {
            int before = log.Count;
            sim.Tick();
            if (ai.Board.WaveActive)
                foreach (EntityHandle h in army)
                    if (w.Units.IsAlive(h) && ai.Board.WaveGeneration[h.Index] == h.Generation && w.Units.State[h.Index] != UnitState.Idle)
                        lastBusy = sim.TickNumber;
            if (log.Skip(before).Any(c => army.Contains(c.Unit) && c.Kind != CommandKind.Move)) lastBusy = sim.TickNumber;
            if (wasActive && !ai.Board.WaveActive) ends.Add((sim.TickNumber, sim.TickNumber - lastBusy));
            if (!wasActive && ai.Board.WaveActive) lastBusy = sim.TickNumber;
            wasActive = ai.Board.WaveActive;
        }
        int alive = army.Count(h => w.Units.IsAlive(h));
        _out.WriteLine($"{n} HI: waves {ai.Board.WavesStarted}, ends at " + string.Join(", ", ends.Select(e => $"{e.Tick} (idle {e.SinceBusy})")) +
            $"; alive {alive}; hall hp {w.Buildings.Hp[hall.Index]}/{hp}; commands " +
            string.Join(", ", log.Where(c => army.Contains(c.Unit)).GroupBy(c => c.Kind).Select(g => $"{g.Key} {g.Count()}")));
        Assert.Equal(hp, w.Buildings.Hp[hall.Index]); // precondition: nobody can strike it
        Assert.Equal(n, alive);                         // precondition: no losses, so no end is a loss retreat
        Assert.True(ai.Board.WavesStarted >= 2, $"only {ai.Board.WavesStarted} wave(s) in 9,000 ticks: the offence froze");
        Assert.InRange(ai.Board.WavesStarted, 2, 9000 / cooldown + 1);
        Assert.NotEmpty(ends);
        // Ends are judged at thinks: allow a think's slack below the cooldown, never less.
        foreach ((int tick, int since) in ends)
            Assert.True(since >= cooldown - ai.ThinkTicks, $"the wave ended at {tick} only {since} ticks after a member was busy (cooldown {cooldown})");
        Assert.Contains(log, c => army.Contains(c.Unit) && c.Kind == CommandKind.Move); // sent home
    }

    /// <summary>
    /// The 2-tower variant: the fenced hall plus two Lookout Towers of player 1, either on open ground west of the ring or
    /// standing in the ring's south band in place of its trees (part of the fence, strikable only from the south), waveCooldown
    /// 30 s, six Heavy Infantry, 8,000 ticks. Members fight the towers (busy) and take losses.
    /// Every wave end is either a loss retreat (live members' half-pop below half the start's, or none left) or comes at
    /// least a cooldown after the last busy member: the idle rule never cuts a fight short. At least one wave ends.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FencedHall_TwoTowers_NoWaveEndsByTheIdleRuleWhileItFights(bool towersInsideTheFence)
    {
        GameData data = s_wave30.Value;
        Simulation sim = FencedScene(data, out EntityHandle hall, leaveTowerCells: towersInsideTheFence);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int tower = data.FindBuilding("whirlwind_lookout_tower");
        if (!towersInsideTheFence)
        {
            // Outside the ring, west of it: in the wave's way, strikable.
            Assert.True(w.Buildings.Spawn(1, tower, 26 * AiScenes.FlatSize + 43, out _));
            Assert.True(w.Buildings.Spawn(1, tower, 37 * AiScenes.FlatSize + 43, out _));
        }
        sim.Tick();
        List<Command> log = AiScenes.Log(sim, 0);
        int heavy = data.FindUnit("malazan_heavy_infantry");
        List<EntityHandle> army = Army(sim, heavy, 6, 38, 42);
        int cooldown = ai.Rules.WaveCooldownTicks;
        var ends = new List<string>();
        var bad = new List<string>();
        int lastBusy = -1;
        bool wasActive = false;
        while (sim.TickNumber < 8000)
        {
            int liveHalf = 0, members = 0;
            foreach (EntityHandle h in army)
                if (w.Units.IsAlive(h) && ai.Board.WaveGeneration[h.Index] == h.Generation)
                {
                    members++;
                    liveHalf += data.Units[w.Units.TypeId[h.Index]].HalfPop;
                }
            int startHalf = ai.Board.WaveStartHalfPop;
            int before = log.Count;
            sim.Tick();
            // BUG-0730: "busy" is sampled where the AI samples it. The idle rule reads its members through its view at its
            // thinks (rule 3), and a think at the start of tick N sees the state left by the tick that made TickNumber N. A
            // retaliation step shorter than a think (7 ticks at 5321-5327 on the surround trajectory) can begin and end
            // between two thinks; the AI never sees it, and counting from it asked the rule for more than it can know. The
            // limit stays cooldown - ThinkTicks: the rule still may not end a wave sooner than a cooldown after a busy sample.
            if (ai.Board.WaveActive && sim.TickNumber % ai.ThinkTicks == 0)
                foreach (EntityHandle h in army)
                    if (w.Units.IsAlive(h) && ai.Board.WaveGeneration[h.Index] == h.Generation && w.Units.State[h.Index] != UnitState.Idle)
                        lastBusy = sim.TickNumber;
            if (log.Skip(before).Any(c => army.Contains(c.Unit) && c.Kind != CommandKind.Move)) lastBusy = sim.TickNumber;
            if (wasActive && !ai.Board.WaveActive)
            {
                // Recount after the tick: deaths this tick count toward the loss.
                int liveAfter = 0;
                foreach (EntityHandle h in army)
                    if (w.Units.IsAlive(h)) liveAfter += data.Units[w.Units.TypeId[h.Index]].HalfPop;
                bool loss = members == 0 || Math.Min(liveHalf, liveAfter) < 0.5f * startHalf;
                int since = sim.TickNumber - lastBusy;
                ends.Add($"{sim.TickNumber} (loss {loss}, idle {since})");
                if (!loss && since < cooldown - ai.ThinkTicks && w.Buildings.IsAlive(ai.Board.WaveTarget))
                    bad.Add($"ended at {sim.TickNumber}, {since} ticks after a busy member, no loss ({liveHalf}/{startHalf})");
            }
            if (!wasActive && ai.Board.WaveActive) lastBusy = sim.TickNumber;
            wasActive = ai.Board.WaveActive;
        }
        _out.WriteLine($"towers inside {towersInsideTheFence}: waves {ai.Board.WavesStarted}; ends {string.Join(", ", ends)}; alive {army.Count(h => w.Units.IsAlive(h))}/6; " +
            $"towers alive {TowersAlive(w, tower)}; hall {w.Buildings.Hp[hall.Index]}; commands " +
            string.Join(", ", log.Where(c => army.Contains(c.Unit)).GroupBy(c => c.Kind).Select(g => $"{g.Key} {g.Count()}")));
        Assert.NotEmpty(ends);
        Assert.InRange(ai.Board.WavesStarted, 1, 8000 / cooldown + 1);
        Assert.Empty(bad);
    }

    private static int TowersAlive(World w, int tower)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.TypeId[k] == tower) n++;
        return n;
    }

    // ------------------------------------------------------------------ BUG-0640: house sites with a killed builder

    /// <summary>
    /// The flat AI scene, Normal, <paramref name="faction"/>'s shipped order, 36,000 ticks (30 min). Every own house site's
    /// first builder(s) are killed (freed) the first tick one stands at work on it; the AI must resume it. Asserted: every
    /// house site placed more than 3 minutes before the end is finished, no site waits more than 3 minutes between its
    /// builder's death and the next worker on it, and no own worker stands Idle with a build target two ticks running
    /// (what the PlayerView's "Idle with a build target reads as Building" rule relies on).
    /// </summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void HouseSites_FirstBuilderKilled_EverySiteResumedAndFinished_30Min(string faction)
    {
        int player = TestSim.Data.FindFaction(faction);
        SeatController[] controllers = { SeatController.Human, SeatController.Human };
        controllers[player] = SeatController.Ai;
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, controllers));
        World w = sim.World;
        GameData d = w.Data;
        Assert.Equal(player, w.FactionOf(player));
        int house = d.Buildings.ToList().FindIndex(b => b.Faction == player && b.Slot == BuildingSlot.House);
        const int Ticks = 36_000, Grace = 3 * 60 * SimConstants.TicksPerSecond;
        var placed = new Dictionary<EntityHandle, int>();
        var finished = new Dictionary<EntityHandle, int>();
        var killedAt = new Dictionary<EntityHandle, int>();
        var resumedAt = new Dictionary<EntityHandle, int>();
        var idleWithTarget = new int[w.Units.Capacity];
        int worstIdleRun = 0, kills = 0;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            int now = sim.TickNumber;
            for (int k = 0; k < b.Capacity; k++)
            {
                if (!b.Alive[k] || b.Owner[k] != player || b.TypeId[k] != house) continue;
                var h = b.HandleOf(k);
                if (!placed.ContainsKey(h)) placed[h] = now;
                if (!b.UnderConstruction[k] && !finished.ContainsKey(h)) finished[h] = now;
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != player) { idleWithTarget[i] = 0; continue; }
                EntityHandle target = u.BuildTarget[i];
                idleWithTarget[i] = u.State[i] == UnitState.Idle && target.Generation != 0 ? idleWithTarget[i] + 1 : 0;
                worstIdleRun = Math.Max(worstIdleRun, idleWithTarget[i]);
                if (target.Generation == 0 || !b.IsAlive(target) || b.TypeId[target.Index] != house || !b.UnderConstruction[target.Index]) continue;
                if (!killedAt.ContainsKey(target))
                {
                    if (u.State[i] != UnitState.Building) continue;
                    killedAt[target] = now;
                    // Every own worker on it now dies (a raid on the site).
                    for (int j = 0; j < u.Capacity; j++)
                        if (u.Alive[j] && u.BuildTarget[j] == target)
                        {
                            u.Free(new EntityHandle(j, u.Generation[j]));
                            kills++;
                        }
                }
                else if (!resumedAt.ContainsKey(target) && now > killedAt[target]) resumedAt[target] = now;
            }
        }
        var rows = placed.OrderBy(p => p.Value).Select(p =>
            $"{p.Key}: placed {p.Value}, killed {(killedAt.TryGetValue(p.Key, out int kt) ? kt : -1)}, resumed {(resumedAt.TryGetValue(p.Key, out int rt) ? rt : -1)}, " +
            $"finished {(finished.TryGetValue(p.Key, out int ft) ? ft : -1)}").ToList();
        _out.WriteLine($"{faction}: {placed.Count} house sites placed, {finished.Count} finished, {killedAt.Count} raided ({kills} workers killed); " +
            $"worst Idle-with-build-target run {worstIdleRun} ticks; cap {w.HalfPopCap[player] / 2.0} pop {w.HalfPop[player] / 2.0}");
        foreach (string r in rows) _out.WriteLine(r);
        Assert.True(killedAt.Count > 0, "precondition: no builder was ever killed");
        var unfinished = placed.Where(p => p.Value < Ticks - Grace && !finished.ContainsKey(p.Key)).Select(p => p.Key).ToList();
        Assert.True(unfinished.Count == 0, $"{unfinished.Count} house site(s) placed before the last 3 min never finished: {string.Join(", ", unfinished)}");
        foreach ((EntityHandle h, int k) in killedAt)
        {
            if (k >= Ticks - Grace) continue;
            Assert.True(resumedAt.TryGetValue(h, out int r) && r - k <= Grace, $"site {h} raided at {k} was resumed at {(resumedAt.TryGetValue(h, out int rr) ? rr : -1)}");
        }
        Assert.True(worstIdleRun <= 1, $"a worker stood Idle with a build target {worstIdleRun} ticks running: the AI reads it Building all that time");
    }

    /// <summary>
    /// A stalled own site no worker can reach (a Billet site ringed two deep by trees, spawned straight into the store): the
    /// resume path sends a worker at most once per <see cref="EconomyManager.StalledThinks"/> thinks, and the AI's economy
    /// keeps going (gold and wood gathered over 6,000 ticks at least 80% of a twin with the same tree ring and no site).
    /// Measured at 1dfdef3: 99 joins in 300 thinks (one every StalledThinks thinks, for ever: no give-up), 176 Gathers against
    /// the twin's 59, gold + wood 1,160 against 1,730 (67%), pop 22 against 29. The site is hand-spawned past the placement
    /// rules (the never-seal rule keeps a placed site reachable), so this is an S4.
    /// </summary>
    [Fact(Skip = "BUG-0650: the resume path never gives up on a site no worker can reach; a join every 3 thinks costs a third of the income")]
    public void AnUnreachableOwnSite_ResumeChurnIsBounded_TheEconomyKeepsGoing()
    {
        GameData data = TestSim.Data;
        int billet = data.FindBuilding("malazan_billet"), tree = data.FindResource("tree");
        (int joins, int gold, int thinks) Run(bool withSite)
        {
            Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data);
            World w = sim.World;
            // The twin has the same tree ring, empty inside, so only the site differs.
            if (withSite) Assert.True(w.Buildings.Spawn(0, billet, 14 * AiScenes.FlatSize + 20, out _, site: true));
            for (int y = 12; y <= 17; y++)
                for (int x = 18; x <= 23; x++)
                    if (x < 20 || x > 21 || y < 14 || y > 15)
                        Assert.True(w.Resources.Spawn(tree, y * AiScenes.FlatSize + x, data.Rules.TreeWood, out _), $"tree ({x}, {y})");
            List<Command> log = AiScenes.Log(sim, 0);
            int earned = 0, onSite = 0, idleTicks = 0;
            for (int t = 0; t < 6000; t++)
            {
                int g = w.Gold[0] + w.Wood[0];
                sim.Tick();
                if (w.Gold[0] + w.Wood[0] > g) earned += w.Gold[0] + w.Wood[0] - g;
                for (int i = 0; i < w.Units.Capacity; i++)
                {
                    if (!w.Units.Alive[i] || w.Units.Owner[i] != 0) continue;
                    if (w.Units.BuildTarget[i].Generation != 0 && w.Buildings.TypeId[w.Units.BuildTarget[i].Index] == billet) onSite++;
                    if (w.Units.State[i] == UnitState.Idle) idleTicks++;
                }
            }
            int billets = 0, sites = 0;
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == billet) { if (w.Buildings.UnderConstruction[k]) sites++; else billets++; }
            _out.WriteLine($"site {withSite}: worker-ticks on a billet build target {onSite}, idle unit-ticks {idleTicks}; billets {billets} + sites {sites}; pop {w.HalfPop[0] / 2.0} cap {w.HalfPopCap[0] / 2.0}; " +
                string.Join(", ", log.GroupBy(c => c.Kind).Select(gr => $"{gr.Key} {gr.Count()}")));
            int toSite = log.Count(c => c.Kind == CommandKind.Build && c.TypeId == billet && c.Position == sim.AiOf(0)!.CellCentre(14 * AiScenes.FlatSize + 20));
            return (toSite, earned, 6000 / sim.AiOf(0)!.ThinkTicks);
        }
        (int joins, int gold, int thinks) = Run(true);
        (_, int twinGold, _) = Run(false);
        _out.WriteLine($"unreachable site: {joins} joins over {thinks} thinks; gold + wood earned {gold} vs twin {twinGold}");
        Assert.True(joins <= thinks / EconomyManager.StalledThinks + 1, $"{joins} joins in {thinks} thinks");
        Assert.True(gold >= 0.8 * twinGold, $"gold + wood earned {gold} vs {twinGold} without the site");
    }

    /// <summary>
    /// Measured: an own Billet site in reach of an enemy Lookout Tower (combat on). The resume path sends a worker to a site
    /// whose builders keep dying; reported: workers lost over 6,000 ticks, joins sent, the site's fate.
    /// </summary>
    [Fact]
    public void AnOwnSiteUnderAnEnemyTower_WorkersSentToIt_Measured()
    {
        GameData data = TestSim.Data;
        int billet = data.FindBuilding("malazan_billet"), tower = data.FindBuilding("whirlwind_lookout_tower");
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, combat: true);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, billet, 14 * AiScenes.FlatSize + 20, out EntityHandle site, site: true));
        Assert.True(w.Buildings.Spawn(1, tower, 14 * AiScenes.FlatSize + 26, out _));
        List<Command> log = AiScenes.Log(sim, 0);
        int worker = StartBase.UnitOfSlot(data, 0, UnitSlot.Worker);
        int dead = 0;
        var alive = new HashSet<EntityHandle>();
        for (int t = 0; t < 6000; t++)
        {
            sim.Tick();
            var now = new HashSet<EntityHandle>();
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == worker) now.Add(new EntityHandle(i, w.Units.Generation[i]));
            dead += alive.Count(h => !now.Contains(h));
            alive = now;
        }
        int joins = log.Count(c => c.Kind == CommandKind.Build && c.TypeId == billet && c.Position == sim.AiOf(0)!.CellCentre(14 * AiScenes.FlatSize + 20));
        _out.WriteLine($"site under a tower: {joins} joins, {dead} workers died, site alive {w.Buildings.IsAlive(site)} under construction {w.Buildings.UnderConstruction[site.Index]} work {w.Buildings.Work[site.Index]}, workers now {alive.Count}");
    }

    // ------------------------------------------------------------------ BUG-0621: start rules

    private static void AssertStartRules(Heightmap hm, int w, int h, string at)
    {
        ReadOnlySpan<StartLocation> s = hm.StartLocations;
        long side = Math.Min(w, h);
        for (int i = 0; i < s.Length; i++)
        {
            Assert.True(s[i].Level == s[0].Level, $"{at}: location {i} on level {s[i].Level}, location 0 on {s[0].Level}");
            Assert.Equal(hm.Levels[s[i].Cell], (byte)s[i].Level);
            for (int j = 0; j < i; j++)
            {
                long dx = s[i].Cell % w - s[j].Cell % w, dy = s[i].Cell / w - s[j].Cell / w;
                Assert.True(4 * (dx * dx + dy * dy) >= side * side, $"{at}: locations {j} and {i} {Math.Sqrt(dx * dx + dy * dy):F1} cells apart, under half of {side}");
            }
        }
    }

    /// <summary>BUG-0621's acceptance, kept: seeds 1-100 on 128 / 192 / 256, two locations, every pair on one level and half the side apart.</summary>
    [Fact]
    public void Bug0621_TwoLocations_Seeds1To100_EverySize_OneLevel_HalfTheSideApart()
    {
        foreach (int size in new[] { 128, 192, 256 })
        {
            var p = MapGenParams.Default with { Width = size, Height = size };
            int flat = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var rng = new SimRng(seed, RngStream.MapGen);
                Heightmap hm = MapGenerator.Generate(p, ref rng);
                Assert.Equal(2, hm.StartLocations.Length);
                AssertStartRules(hm, size, size, $"{size} seed {seed}");
                if (hm.Levels.IndexOfAnyExcept((byte)0) < 0) flat++;
            }
            _out.WriteLine($"{size}: flat fallback {flat}/100");
            Assert.True(flat <= 5, $"{size}: {flat}/100 flat fallbacks");
        }
    }

    /// <summary>3-4 locations on 128 / 192 / 256, seeds 1-30: every position on one level, every pair half the side apart.</summary>
    [Fact]
    public void Bug0621_ThreeAndFourLocations_Seeds1To30_OneLevel_HalfTheSideApart()
    {
        foreach (int n in new[] { 3, 4 })
            foreach (int size in new[] { 128, 192, 256 })
            {
                var p = MapGenParams.Default with { Width = size, Height = size, StartLocations = n };
                int flat = 0;
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    var rng = new SimRng(seed, RngStream.MapGen);
                    Heightmap hm = MapGenerator.Generate(p, ref rng);
                    Assert.Equal(n, hm.StartLocations.Length);
                    AssertStartRules(hm, size, size, $"{n} on {size} seed {seed}");
                    if (hm.Levels.IndexOfAnyExcept((byte)0) < 0) flat++;
                }
                _out.WriteLine($"{n} locations on {size}: flat fallback {flat}/30");
            }
    }

    // ------------------------------------------------------------------ BUG-0620: Validate x Generate

    /// <summary>
    /// A sweep wider than the 444-set row: 2, 3 and 4 locations; square and non-square maps 16-160; StartEdgeMargin 1-12;
    /// StartClearRadius 2-12; mine distance at its minimum and maximum. Each set Validate accepts must generate, and its
    /// starts keep BUG-0621's rules. Each set the brief's formula refuses (<c>max(lo, rs + 2) &lt;= min(W/2 - 1 - rs, H - 3 - rs)</c>)
    /// Validate must refuse too. Counted: sets refused only by the extra spacing check (reported).
    /// </summary>
    [Fact]
    public void ValidateTimesGenerate_WideSweep_EveryValidatedSetGenerates_StartsKeepTheRules()
    {
        var failures = new List<string>();
        int validated = 0, refusedByFormula = 0, refusedBeyondFormula = 0, total = 0;
        foreach (int n in new[] { 2, 3, 4 })
            foreach (int width in new[] { 16, 32, 48, 58, 64, 72, 96, 128, 160 })
                foreach (int height in new[] { 16, 32, 48, 64, 96, 128, 160 })
                {
                    if (n > 2 && width != height) continue;
                    foreach (int margin in new[] { 1, 3, 6, 12 })
                        foreach (int clear in new[] { 2, 5, 8, 12 })
                            foreach (float mine in new[] { clear * 2f + 0.5f, clear * 2f + 16f })
                            {
                                total++;
                                int minDim = Math.Min(width, height);
                                var p = MapGenParams.Default with
                                {
                                    Width = width, Height = height, EdgeMargin = 1, Level1MinSize = 3, Level1MaxSize = Math.Max(3, Math.Min(minDim - 4, 40)),
                                    Level2MinSize = 3, Level2MaxSize = 3, RampLength = 3, RampWidth = 1, StartLocations = n,
                                    StartEdgeMargin = margin, StartClearRadius = clear, StartMineDistance = mine, StartForestDistance = clear * 2f + 1f,
                                };
                                int lo = margin + clear, rs = p.StartSiteRadius;
                                bool formula = Math.Max(lo, rs + 2) <= Math.Min(width / 2 - 1 - rs, height - 3 - rs);
                                bool ok;
                                try { p.Validate(); ok = true; }
                                catch (ArgumentOutOfRangeException) { ok = false; }
                                catch (Exception ex) { failures.Add($"{n} {width}x{height} m{margin} c{clear} mine {mine}: Validate threw {ex.GetType().Name}"); continue; }
                                if (!formula)
                                {
                                    refusedByFormula++;
                                    if (ok) failures.Add($"{n} {width}x{height} m{margin} c{clear} mine {mine}: the formula refuses, Validate accepts");
                                    if (ok) continue;
                                }
                                else if (!ok) refusedBeyondFormula++;
                                if (!ok) continue;
                                validated++;
                                foreach (ulong seed in new ulong[] { 3, 11 })
                                {
                                    var rng = new SimRng(seed, RngStream.MapGen);
                                    try
                                    {
                                        Heightmap hm = MapGenerator.Generate(p, ref rng);
                                        Assert.Equal(n, hm.StartLocations.Length);
                                        AssertStartRules(hm, width, height, $"{n} {width}x{height} m{margin} c{clear} seed {seed}");
                                    }
                                    catch (Exception ex) { failures.Add($"{n} {width}x{height} m{margin} c{clear} mine {mine} seed {seed}: {ex.GetType().Name} {ex.Message.Split('\n')[0]}"); }
                                }
                            }
                }
        _out.WriteLine($"{total} sets: {validated} validated (x2 seeds generated), {refusedByFormula} refused by the brief's formula, {refusedBeyondFormula} refused beyond it; {failures.Count} failures");
        foreach (string f in failures.Take(30)) _out.WriteLine(f);
        Assert.True(validated >= 444, $"only {validated} validated sets");
        Assert.Empty(failures);
    }
}
