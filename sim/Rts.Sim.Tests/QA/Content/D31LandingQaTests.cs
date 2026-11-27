using System.Text.Json.Nodes;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D31 (session 2026-11-14-0616): the Teblor's finished numbers on the shipped data (<see cref="TestSim.Data"/>, no
/// copy). The Blood-oil Frenzy at its edges (a unit arriving after the cast, a second Shaman's cast refreshing it, death
/// while frenzied, the Anointing researched mid-Frenzy, the aftermath Slow meeting a War-dog's bite), the Gatherer's carry
/// 15 on a nearly empty mine and on the last tree, and a save and a recording made on the diff base (43ab896, data hash
/// 67269D955D18D885; fixtures under <c>QA/Content/Fixtures/pre_d31/</c>, written there by a throwaway test on a clone of
/// the base, never rewritten) refused with the player-facing text from <c>ui.json</c>.
/// </summary>
public class D31LandingQaTests(ITestOutputHelper output)
{
    private static GameData D => TestSim.Data;
    private static int Frenzy => D.FindStatus("teblor_frenzied");
    private static int Slowed => D.FindStatus("slowed");
    private static int Anointing => D.FindTech("teblor_blood_oil_anointing");
    private static float WarriorSpeed => D.Units[D.FindUnit("teblor_warrior")].SpeedPerTick;

    private static Simulation Flat() => new(new SimConfig(1, 2, 64, 8 * 64 + 32) { Data = D }, LocalMovementTests.Flat(48));

    private static int Ticks(World w, EntityHandle h, int status) => BloodOilScenes.TicksOf(w, h, status);

    private static int Entries(World w, EntityHandle h, int status)
    {
        int n = 0, head = h.Index * Rts.Sim.Abilities.StatusStore.PerUnit;
        for (int k = 0; k < w.Units.Statuses.Count[h.Index]; k++)
            if (w.Units.Statuses.StatusId[head + k] == status) n++;
        return n;
    }

    /// <summary>Casts and ticks until <paramref name="target"/> is Frenzied; returns the tick it first was.</summary>
    private static int CastOnto(Simulation sim, EntityHandle shaman, EntityHandle target)
    {
        BloodOilScenes.Cast(sim, 0, shaman);
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            if (Ticks(sim.World, target, Frenzy) > 0) return sim.TickNumber;
        }
        throw new Xunit.Sdk.XunitException("the cast never reached the Warrior");
    }

    /// <summary>A selfAura is applied once, at the cast: a Warrior that arrives inside the 8 m afterwards is never Frenzied.</summary>
    [Fact]
    public void AWarriorArrivingInTheAuraAfterTheCast_IsNotFrenzied()
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle shaman = BloodOilScenes.Place(sim, 0, "teblor_shaman", At(sim, 10, 20));
        EntityHandle first = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 12, 20));
        EntityHandle late = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 30, 20)); // 20 m away at the cast
        w.Fog.Update();
        CastOnto(sim, shaman, first);
        Assert.Equal(-1, Ticks(w, late, Frenzy));
        sim.Enqueue(Command.Move(0, late, At(sim, 11, 21)));
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            Assert.Equal(-1, Ticks(w, late, Frenzy));
        }
        Assert.True(Vector2Distance(w, late, shaman) < 8f, "the late Warrior never got inside the 8 m");
    }

    private static float Vector2Distance(World w, EntityHandle a, EntityHandle b) =>
        System.Numerics.Vector2.Distance(w.Units.Position[a.Index], w.Units.Position[b.Index]);

    /// <summary>
    /// A second Shaman's cast 100 ticks into the first Frenzy refreshes it to the full 10 s (one entry, not two), and the
    /// aftermath Slow fires once, 80 ticks, after the refreshed entry runs out.
    /// </summary>
    [Fact]
    public void ASecondShamansCast_RefreshesTheFrenzy_TheAftermathFiresOnce()
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle s1 = BloodOilScenes.Place(sim, 0, "teblor_shaman", At(sim, 10, 20));
        EntityHandle s2 = BloodOilScenes.Place(sim, 0, "teblor_shaman", At(sim, 10, 22));
        EntityHandle war = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 12, 21));
        w.Fog.Update();
        int start = CastOnto(sim, s1, war);
        for (int t = 0; t < 100; t++) sim.Tick();
        int before = Ticks(w, war, Frenzy);
        BloodOilScenes.Cast(sim, 0, s2);
        int refreshedAt = -1;
        for (int t = 0; t < 60 && refreshedAt < 0; t++)
        {
            sim.Tick();
            if (Ticks(w, war, Frenzy) > before) refreshedAt = sim.TickNumber;
            before = Ticks(w, war, Frenzy);
        }
        Assert.True(refreshedAt > 0, "the second cast never refreshed the Frenzy");
        Assert.Equal(200, Ticks(w, war, Frenzy));
        int slowStarts = 0, slowTicks = 0, frenzyEnd = -1;
        bool hadSlow = false;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            Assert.True(Entries(w, war, Frenzy) <= 1 && Entries(w, war, Slowed) <= 1, $"tick {sim.TickNumber}: a status stacked");
            bool slow = Ticks(w, war, Slowed) > 0;
            if (slow && !hadSlow) slowStarts++;
            if (slow) slowTicks++;
            hadSlow = slow;
            if (frenzyEnd < 0 && Ticks(w, war, Frenzy) < 0) frenzyEnd = sim.TickNumber;
        }
        output.WriteLine($"first Frenzy at {start}, refreshed at {refreshedAt}, ended at {frenzyEnd}; slow starts {slowStarts}, slow ticks {slowTicks}");
        Assert.Equal(refreshedAt + 200, frenzyEnd);
        Assert.Equal(1, slowStarts);
        Assert.Equal(80, slowTicks);
        Assert.Equal(WarriorSpeed, w.Units.Speed[war.Index]);
    }

    /// <summary>
    /// A Warrior killed while Frenzied gets no aftermath, its slot's statuses are cleared, and a Warrior spawned into the
    /// freed slot starts clean (no Frenzy, no Slow, its own speed) through the time the aftermath would have run.
    /// </summary>
    [Fact]
    public void AWarriorKilledWhileFrenzied_LeavesNoAftermath_TheNextUnitInItsSlotIsClean()
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle shaman = BloodOilScenes.Place(sim, 0, "teblor_shaman", At(sim, 10, 20));
        EntityHandle war = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 12, 20));
        w.Fog.Update();
        CastOnto(sim, shaman, war);
        sim.Enqueue(Command.Move(0, shaman, At(sim, 10, 2)));
        for (int t = 0; t < 150; t++) sim.Tick(); // 50 ticks of Frenzy left
        Assert.True(Ticks(w, war, Frenzy) > 0);
        CombatSystem.HitUnit(w, new PendingHit(default, 1, war, false, 1_000_000));
        sim.Tick();
        Assert.False(w.Units.IsAlive(war));
        Assert.Equal(0, w.Units.Statuses.Count[war.Index]);
        EntityHandle next = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 30, 30));
        output.WriteLine($"dead slot {war.Index}, next unit's slot {next.Index}");
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            Assert.Equal(0, w.Units.Statuses.Count[next.Index]);
            Assert.Equal(WarriorSpeed, w.Units.Speed[next.Index]);
        }
    }

    /// <summary>
    /// The Anointing researched mid-Frenzy does not lengthen the entry already running (docs/03 M7-4: decided at the
    /// application): that Frenzy runs its 10 s and its aftermath its 4 s; the next cast, after the 40 s cooldown, is 14 s.
    /// </summary>
    [Fact]
    public void TheAnointingResearchedMidFrenzy_LeavesTheRunningOne_TheNextCastIs14s()
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle shaman = BloodOilScenes.Place(sim, 0, "teblor_shaman", At(sim, 10, 20));
        EntityHandle war = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 12, 20));
        ProductionSystem.Researched(w, 0, D.FindTech("age_ii"));
        w.Fog.Update();
        int start = CastOnto(sim, shaman, war);
        Assert.Equal(200, Ticks(w, war, Frenzy));
        for (int t = 0; t < 50; t++) sim.Tick();
        ProductionSystem.Researched(w, 0, Anointing);
        Assert.True(w.HasTech(0, Anointing));
        int end = -1, slowAtEnd = -1;
        for (int t = 0; t < 400 && end < 0; t++)
        {
            sim.Tick();
            if (Ticks(w, war, Frenzy) < 0)
            {
                end = sim.TickNumber;
                slowAtEnd = Ticks(w, war, Slowed);
            }
        }
        Assert.Equal(start + 200, end);
        Assert.Equal(80, slowAtEnd); // the aftermath is not named by the tech: 4 s either way
        // The cooldown (40 s from the cast) runs out; the next cast is the anointed 14 s.
        while (sim.TickNumber < start + 40 * SimConstants.TicksPerSecond + 20) sim.Tick();
        Assert.Equal(-1, Ticks(w, war, Frenzy));
        CastOnto(sim, shaman, war);
        Assert.Equal(280, Ticks(w, war, Frenzy));
    }

    /// <summary>
    /// The aftermath Slow (0.2, 80 ticks) and an enemy War-dog's bite (Slowed 0.2 for 2 s, onHit) on the same Warrior are one
    /// Slowed entry by the stacking rule: speed x 0.8 at most, never x 0.64; the aftermath's 80 ticks are not cut to the
    /// bite's 40, and a bite during the aftermath does not push it past 80.
    /// </summary>
    [Fact]
    public void TheAftermathSlow_AndAWarDogsBite_AreOneSlow_NotStacked()
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle shaman = BloodOilScenes.Place(sim, 0, "teblor_shaman", At(sim, 10, 20));
        EntityHandle war = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 12, 20));
        EntityHandle dog = BloodOilScenes.Place(sim, 1, "teblor_war_dog", At(sim, 13, 20));
        w.Units.Hp[dog.Index] = 1_000_000;
        w.Units.Hp[war.Index] = 1_000_000; // only the bites' slows matter here
        w.Fog.Update();
        sim.Enqueue(Command.Attack(1, dog, war, false));
        sim.Enqueue(Command.Attack(0, war, dog, false));
        CastOnto(sim, shaman, war);
        sim.Enqueue(Command.Move(0, shaman, At(sim, 10, 2)));
        int frenzyEnd = -1, bitesDuringAftermath = 0, maxSlowTicks = 0;
        int prevSlow = -1;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            int slow = Ticks(w, war, Slowed);
            Assert.True(Entries(w, war, Slowed) <= 1, $"tick {sim.TickNumber}: two Slowed entries");
            float speed = w.Units.Speed[war.Index];
            Assert.True(speed == WarriorSpeed || speed == WarriorSpeed * 0.8f, $"tick {sim.TickNumber}: speed {speed} (base {WarriorSpeed})");
            if (slow > 0) Assert.Equal(WarriorSpeed * 0.8f, speed);
            if (frenzyEnd < 0 && Ticks(w, war, Frenzy) < 0)
            {
                frenzyEnd = sim.TickNumber;
                Assert.Equal(80, slow); // the bite's shorter entry is lengthened to the aftermath's
            }
            if (frenzyEnd > 0 && sim.TickNumber > frenzyEnd && prevSlow > 0 && slow >= prevSlow) bitesDuringAftermath++; // refreshed, not counted down
            maxSlowTicks = Math.Max(maxSlowTicks, slow);
            prevSlow = slow;
        }
        output.WriteLine($"Frenzy ended at {frenzyEnd}; refreshes by a bite after it {bitesDuringAftermath}; longest Slowed entry seen {maxSlowTicks}");
        Assert.True(frenzyEnd > 0);
        Assert.True(maxSlowTicks <= 80, $"a Slowed entry of {maxSlowTicks} ticks");
    }

    private const int HallX = 8, HallY = 7, NodeX = 16, NodeY = 8;

    private static (Simulation Sim, EntityHandle Worker, EntityHandle Node) LonghouseAnd(string resource, int amount)
    {
        Simulation sim = new(new SimConfig(5, 1, 32, 144) { Data = D }, ResourceMaps.Flat(32, 20));
        EntityHandle node = ResourceMaps.Spawn(sim.World, D.FindResource(resource), NodeX, NodeY, amount);
        GatherMaps.Building(sim, HallX, HallY, type: D.FindBuilding("teblor_longhouse"));
        EntityHandle worker = GatherMaps.Unit(sim, GatherMaps.At(sim, 13, 6), type: D.FindUnit("teblor_gatherer"));
        sim.Enqueue(Command.Gather(0, worker, GatherMaps.At(sim, NodeX + 1, NodeY)));
        return (sim, worker, node);
    }

    /// <summary>
    /// The shipped Gatherer (carry 15) on a mine or the one tree with less than a load left (3, 12: below and above the rules'
    /// 10): it takes exactly what was left, the node is gone (a tree's cell passable again), nothing goes negative and no
    /// gold or wood is made or lost, every tick for 1,500 ticks.
    /// </summary>
    [Theory]
    [InlineData("gold_mine", 3)]
    [InlineData("gold_mine", 12)]
    [InlineData("tree", 3)]
    [InlineData("tree", 12)]
    public void TheCarry15Gatherer_OnTheLastOfANode_TakesWhatIsLeft_NothingMadeOrLost(string resource, int left)
    {
        Assert.Equal(15, D.CarryOf(D.FindUnit("teblor_gatherer")));
        (Simulation sim, EntityHandle worker, EntityHandle node) = LonghouseAnd(resource, left);
        World w = sim.World;
        (long Gold, long Wood) total = GatherMaps.Conserved(w);
        int peak = 0;
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            int c = w.Units.Cargo[worker.Index];
            Assert.True(c >= 0 && c <= 15, $"tick {sim.TickNumber}: cargo {c}");
            Assert.True(w.Gold[0] >= 0 && w.Wood[0] >= 0);
            Assert.Equal(total, GatherMaps.Conserved(w));
            peak = Math.Max(peak, c);
        }
        bool wood = resource == "tree";
        int banked = wood ? w.Wood[0] : w.Gold[0];
        int start = (int)(wood ? total.Wood : total.Gold) - left;
        output.WriteLine($"{resource} {left}: peak cargo {peak}, cargo now {w.Units.Cargo[worker.Index]}, banked +{banked - start}, node alive {w.Resources.IsAlive(node)}, state {w.Units.State[worker.Index]}");
        Assert.Equal(left, peak);
        Assert.False(w.Resources.IsAlive(node));
        Assert.Equal(left, w.Units.Cargo[worker.Index] + (banked - start));
        if (wood) Assert.True(w.NavGrid.IsPassable(NodeX, NodeY), "the last tree's cell stayed blocked");
    }

    private static string Fixture(string name) =>
        Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "QA", "Content", "Fixtures", "pre_d31", name);

    private static string UiText(string section, string key)
    {
        JsonNode ui = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "ui.json")))!;
        return (string)ui[section]![key]!;
    }

    /// <summary>
    /// A save made on the diff base (a Teblor Frenzy running) is refused DataMismatch, no sim back; the player reads
    /// <c>save.data_mismatch</c>. The fixture's own hash is the base's (67269D955D18D885), so only the landing refuses it.
    /// </summary>
    [Fact]
    public void ASaveMadeBeforeD31_IsRefusedForItsDataHash_WithTheUiText()
    {
        byte[] bytes = Convert.FromBase64String(File.ReadAllText(Fixture("save_teblor_frenzy.b64")));
        ulong saved = BitConverter.ToUInt64(bytes, SaveBytes.HeaderOffsets(bytes).DataHash);
        Assert.Equal(0x67269D955D18D885UL, saved);
        Assert.NotEqual(saved, D.ContentHash());
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(D, bytes)); // M6-S14: format 10, refused first by the format
        // BUG-1859: the relabel is valid because the fixture seats only humans, and format 11 differs from 10 only in the number for such saves.
        byte[] relabelled = (byte[])bytes.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(relabelled.AsSpan(SaveFormat.MagicLength), SaveFormat.CurrentVersion);
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(D, SaveBytes.Rechecksum(relabelled)));
        string text = UiText("save", "data_mismatch");
        output.WriteLine($"save refused: {SaveError.DataMismatch} -> \"{text}\"");
        Assert.Contains("different game data", text, StringComparison.Ordinal);
    }

    /// <summary>A recording made on the diff base reads, then is refused before tick 1 (DataMismatch); the player reads <c>load.refused</c>.</summary>
    [Fact]
    public void ARecordingMadeBeforeD31_IsRefusedBeforeTick1_WithTheUiText()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(Fixture("replay_teblor_train.replay"), out Replay? replay));
        Assert.Equal(0x67269D955D18D885UL, replay!.DataHash);
        ReplayResult result = ReplayPlayer.Run(replay, D);
        Assert.Equal(ReplayError.DataMismatch, result.Error);
        Assert.Equal(0, result.TicksRun);
        string text = UiText("load", "refused");
        output.WriteLine($"replay refused: {result.Error} -> \"{text}\"");
        Assert.Contains("different game data", text, StringComparison.Ordinal);
    }
}
