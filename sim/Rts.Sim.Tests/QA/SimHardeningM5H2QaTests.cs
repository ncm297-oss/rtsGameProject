using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-H2 (session 2026-10-16-0847): attacks on the hardening's smaller items beyond the collapse rule
/// (<see cref="CollapseRuleQaTests"/>): BUG-0702 item 2's no-stacking rule against the judging rule (docs/03 "Implementation
/// (M5-4c)": a cast that didn't land is sent again, at most <see cref="AbilityManager.MaxTries"/> times at the same point).
/// </summary>
public class SimHardeningM5H2QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningM5H2QaTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => AbilityManagerQaTests.Data());

    /// <summary>
    /// BUG-0760: one Priest beside a group, the test stopping it every tick it casts, so no storm ever lands. The judging rule
    /// (docs/03) sends a cast that didn't land again at the next think, up to <see cref="AbilityManager.MaxTries"/> times at
    /// the same point, as it does for Telas Fire (<c>AbilityManagerTests.ACastThatNeverLands_*</c>). The no-stacking rule's
    /// record of the Priest's own first send ("a storm runs here until sent + cast + duration") must not stand in for a storm
    /// that never came: it blocks the retries for the whole window.
    /// </summary>
    [Fact]
    public void ASandstormThatNeverLands_IsSentAgain_UpToMaxTries()
    {
        Simulation sim = AbilityManagerQaTests.Scene(s_data.Value, faction: 1);
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 rally = ai.Board.Rally;
        EntityHandle priest = CombatScenes.Place(sim, 0, sim.World.Data.FindUnit("whirlwind_priest"), rally);
        for (int k = 0; k < 4; k++)
        {
            EntityHandle e = CombatScenes.Place(sim, 1, sim.World.Data.FindUnit("malazan_heavy_infantry"), rally + new Vector2(10f, k - 1.5f));
            CombatScenes.Spot(sim, 0, e);
        }
        var casts = new List<Command>();
        ai.SentForTests = c => { if (c.Kind == CommandKind.UseAbility) casts.Add(c); };
        AbilityDef storm = AbilityManagerQaTests.Ability(sim.World.Data, "sandstorm");
        int until = sim.TickNumber + 12 * ai.ThinkTicks;
        int landed = 0;
        while (sim.TickNumber < until)
        {
            if (sim.World.Units.CastAbility[priest.Index] >= 0) sim.Enqueue(Command.Stop(0, priest));
            sim.Tick();
            foreach (Abilities.AbilityEvent e in sim.World.AbilityEvents) if (e.Resolved) landed++;
        }
        _out.WriteLine($"a storm that never lands: {casts.Count} sends in 12 thinks ({landed} landed); window sent + cast + duration = {storm.CastTicks + storm.DurationTicks} ticks, think {ai.ThinkTicks} ticks");
        Assert.Equal(0, landed);
        Assert.True(casts.Count == AbilityManager.MaxTries,
            $"{casts.Count} sends of a storm that never landed (docs/03: sent again up to {AbilityManager.MaxTries} times; the no-stacking record of its own failed send blocks the retries)");
    }

    /// <summary>
    /// BUG-0760 re-check (fix round 1): three Priests by one group; the first send is cancelled once mid-cast (a Stop), every
    /// later cast is left alone. The cancelled send is forgotten, so a storm is sent at the point again within two thinks; and
    /// once a storm has landed no second one is sent near it before its window (sent + cast + duration) ends, though two
    /// Priests stand free with ready cooldowns the whole time (the landed caster's moved cooldown keeps its record).
    /// </summary>
    [Fact]
    public void ThreePriests_TheFirstStormCancelled_IsSentAgain_ThenNoSecondStormOnTheLandedOne()
    {
        Simulation sim = AbilityManagerQaTests.Scene(s_data.Value, faction: 1);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        Vector2 rally = ai.Board.Rally;
        int priest = w.Data.FindUnit("whirlwind_priest");
        for (int k = 0; k < 3; k++) CombatScenes.Place(sim, 0, priest, rally + new Vector2(0f, 1.5f * k));
        for (int k = 0; k < 4; k++)
        {
            EntityHandle e = CombatScenes.Place(sim, 1, w.Data.FindUnit("malazan_heavy_infantry"), rally + new Vector2(10f, k - 1.5f));
            CombatScenes.Spot(sim, 0, e);
        }
        var casts = new List<Command>();
        ai.SentForTests = c => { if (c.Kind == CommandKind.UseAbility) casts.Add(c); };
        AbilityDef storm = AbilityManagerQaTests.Ability(w.Data, "sandstorm");
        int window = storm.CastTicks + storm.DurationTicks;
        int cancelledAt = -1, landedAt = -1, landedSend = -1;
        int until = sim.TickNumber + 30 * ai.ThinkTicks;
        while (sim.TickNumber < until)
        {
            if (cancelledAt < 0 && casts.Count == 1 && w.Units.State[casts[0].Unit.Index] == UnitState.Casting)
            {
                sim.Enqueue(Command.Stop(0, casts[0].Unit));
                cancelledAt = sim.TickNumber;
            }
            sim.Tick();
            foreach (Abilities.AbilityEvent e in w.AbilityEvents)
                if (e.Resolved && landedAt < 0) { landedAt = sim.TickNumber; landedSend = casts.Count - 1; }
        }
        string all = string.Join(", ", casts.Select(c => $"{c.Unit.Index}@{c.Tick}"));
        _out.WriteLine($"cancelled at {cancelledAt}, first landed at {landedAt} (send #{landedSend}); sends {all}; window {window}, think {ai.ThinkTicks}");
        Assert.True(cancelledAt > 0, "the first cast never started");
        Assert.True(casts.Count >= 2 && casts[1].Tick - cancelledAt <= 2 * ai.ThinkTicks, $"no re-send within two thinks of the cancel: {all}");
        Assert.True(landedAt > 0, $"no storm landed: {all}");
        Command landed = casts[landedSend];
        foreach (Command c in casts)
            if (c.Tick > landed.Tick && c.Tick < landed.Tick + window)
                Assert.True(Vector2.Distance(c.Position, landed.Position) > storm.Radius, $"a storm sent at {c.Tick} on the one sent at {landed.Tick} (landed {landedAt}): {all}");
    }

    /// <summary>
    /// The four new <c>Blackboard.RecentCast*</c> arrays (BUG-0760) are AI state, outside <see cref="Simulation.StateHash"/>
    /// like the rest of the Blackboard (docs/03): scribbling over them leaves the hash unchanged.
    /// </summary>
    [Fact]
    public void TheRecentCastArrays_StayOutOfTheStateHash()
    {
        Simulation sim = AbilityManagerQaTests.Scene(s_data.Value, faction: 1);
        Blackboard b = sim.AiOf(0)!.Board;
        ulong before = sim.StateHash();
        for (int k = 0; k < AbilityManager.MaxRecentCasts; k++)
        {
            b.RecentCastSlot[k] = 1000 + k;
            b.RecentCastGeneration[k] = 77;
            b.RecentCastIndex[k] = 3;
            b.RecentCastReadyAt[k] = 123456;
        }
        Assert.Equal(before, sim.StateHash());
    }

    /// <summary>
    /// BUG-0760 fix vs BUG-0702 item 2, seeded: a Whirlwind AI seat with 2-6 Priests by its rally point and 1-3 wandering enemy
    /// groups; the test cancels a casting Priest now and then (a Stop, about one tick in 25), so sends fail and are retried.
    /// At every Sandstorm send an oracle checks the no-stacking rule from the sim's side: no earlier Sandstorm send within its
    /// radius whose window (sent + cast + duration) still runs has landed or is still being cast or walked to. Twins hash equal
    /// after every tick.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1, 30)]
    [InlineData(501, 30)]
    public void PriestsWithRandomCancels_NeverStackAStormOnALandedOrRunningOne_TwinsEqual(int firstSeed, int scenes)
    {
        int sends = 0, landed = 0, cancels = 0, retriesAfterCancel = 0;
        for (int seed = firstSeed; seed < firstSeed + scenes; seed++)
        {
            List<ulong> a = RunPriestScene(seed, check: true, ref sends, ref landed, ref cancels, ref retriesAfterCancel);
            int s2 = 0, l2 = 0, c2 = 0, r2 = 0;
            List<ulong> b = RunPriestScene(seed, check: false, ref s2, ref l2, ref c2, ref r2);
            Assert.Equal(a.Count, b.Count);
            for (int t = 0; t < a.Count; t++) Assert.True(a[t] == b[t], $"seed {seed}: the twins differ after tick {t}");
        }
        _out.WriteLine($"{scenes} scenes: {sends} storm sends, {landed} landed, {cancels} cancels, {retriesAfterCancel} sends near a cancelled one");
        Assert.True(landed > scenes, $"too few storms landed to mean anything ({landed})");
        Assert.True(retriesAfterCancel > 0, "no send ever followed a cancelled one at its point");
    }

    private sealed record Send(EntityHandle Unit, int Index, Vector2 Point, int Tick)
    {
        public bool Landed { get; set; }
        public bool Cancelled { get; set; }
    }

    private static List<ulong> RunPriestScene(int seed, bool check, ref int sends, ref int landed, ref int cancels, ref int retries)
    {
        var rng = new Random(seed);
        Simulation sim = AbilityManagerQaTests.Scene(s_data.Value, (AiDifficulty)rng.Next(3), faction: 1, combat: rng.Next(2) == 0);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 rally = ai.Board.Rally;
        float mapSize = AiScenes.FlatSize * Map.MapConstants.CellSize;
        Vector2 Clamp(Vector2 p) => Vector2.Clamp(p, new Vector2(3f), new Vector2(mapSize - 3f));
        AbilityDef storm = AbilityManagerQaTests.Ability(w.Data, "sandstorm");
        int window = storm.CastTicks + storm.DurationTicks;
        int priestType = w.Data.FindUnit("whirlwind_priest");
        int[] enemyTypes = w.Data.Factions[0].Units.Where(u => w.Data.Units[u].Slot != UnitSlot.Worker).ToArray();
        int priests = 2 + rng.Next(5);
        for (int k = 0; k < priests; k++)
            CombatScenes.Place(sim, 0, priestType, Clamp(rally + new Vector2(rng.Next(-30, 31) / 10f, rng.Next(-30, 31) / 10f)));
        var enemies = new List<EntityHandle>();
        int groups = 1 + rng.Next(3);
        for (int g = 0; g < groups; g++)
        {
            float angle = rng.Next(360) * MathF.PI / 180f, dist = rng.Next(4, 22), spread = rng.Next(0, 40) / 10f;
            Vector2 at = rally + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
            int n = 2 + rng.Next(5);
            for (int k = 0; k < n; k++)
            {
                EntityHandle h = CombatScenes.Place(sim, 1, enemyTypes[rng.Next(enemyTypes.Length)], Clamp(at + new Vector2((float)(rng.NextDouble() - 0.5) * spread, (float)(rng.NextDouble() - 0.5) * spread)));
                CombatScenes.Spot(sim, 0, h);
                enemies.Add(h);
            }
        }
        var list = new List<Send>();
        int localSends = 0, localRetries = 0;
        ai.SentForTests = c =>
        {
            if (c.Kind != CommandKind.UseAbility) return;
            int i = c.Unit.Index;
            int abilityId = w.Data.Units[w.Units.TypeId[i]].Abilities[c.TypeId];
            if (abilityId != storm.Id) return;
            foreach (Send s in list)
            {
                if (c.Tick >= s.Tick + window || Vector2.Distance(s.Point, c.Position) > storm.Radius) continue;
                if (s.Cancelled) localRetries++;
                bool running = w.Units.IsAlive(s.Unit) && w.Units.CastAbility[s.Unit.Index] == s.Index
                    && Vector2.DistanceSquared(w.Units.CastPoint[s.Unit.Index], s.Point) < 1e-4f;
                bool sameThink = s.Tick == c.Tick;
                if (check)
                    Assert.False(s.Landed || running || sameThink,
                        $"seed {seed} tick {sim.TickNumber}: Sandstorm sent to {c.Unit} at {c.Position} within {storm.Radius} m of {s.Unit}'s sent at {s.Tick} ({(s.Landed ? "landed" : sameThink ? "the same think" : "still casting / walking")}), window to {s.Tick + window}");
            }
            list.Add(new Send(c.Unit, c.TypeId, c.Position, c.Tick));
            localSends++;
        };
        var hashes = new List<ulong>();
        int until = sim.TickNumber + 30 * ai.ThinkTicks;
        while (sim.TickNumber < until)
        {
            if (sim.TickNumber % 40 == 0)
                foreach (EntityHandle e in enemies)
                    if (w.Units.IsAlive(e) && rng.Next(4) == 0)
                        sim.Enqueue(Command.Move(1, e, Clamp(w.Units.Position[e.Index] + new Vector2(rng.Next(-40, 41) / 10f, rng.Next(-40, 41) / 10f))));
            foreach (Send s in list)
            {
                if (s.Landed || s.Cancelled || !w.Units.IsAlive(s.Unit) || w.Units.CastAbility[s.Unit.Index] != s.Index) continue;
                if (rng.Next(25) != 0) continue;
                sim.Enqueue(Command.Stop(0, s.Unit));
                s.Cancelled = true;
                cancels++;
            }
            sim.Tick();
            hashes.Add(sim.StateHash());
            foreach (Abilities.AbilityEvent e in w.AbilityEvents)
            {
                if (e.Owner != 0 || !e.Resolved || e.Ability != storm.Id) continue;
                landed++;
                for (int k = list.Count - 1; k >= 0; k--)
                    if (list[k].Unit == e.Caster && !list[k].Cancelled) { list[k].Landed = true; break; }
            }
        }
        sends += localSends;
        retries += localRetries;
        return hashes;
    }
}
