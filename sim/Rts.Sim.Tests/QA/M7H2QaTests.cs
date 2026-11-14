using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-H2 (session 2026-11-14-0616), the status part's new load checks (BUG-1220) at their edges: a free slot with a
/// stealth flag or a blind but no entry, a live slot whose entry count disagrees with its stealth / blind by one, the totals
/// off by one in both directions; and honest saves the checks must keep loading: two blinds (the tie rule) and two
/// stealthed statuses on one unit, entries ending at different ticks, saved at every tick and round-tripped by hash.
/// </summary>
[Collection(SerialCollection.Name)]
public class M7H2QaTests(ITestOutputHelper output)
{
    private static int R32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));

    private static void W32(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);

    /// <summary>Three Malazan Heavy Infantry: one stealthed, one blinded, one plain; saved at tick 3. Offsets of the status part.</summary>
    private static (GameData D, byte[] Bytes, int Cap, int S, int B, int P, int Never, M7H1QaTests.Regn O) Scene()
    {
        GameData d = TestSim.Data;
        Simulation sim = RegenTests.Scene(d);
        int heavy = d.FindUnit("malazan_heavy_infantry");
        EntityHandle s = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 10, 10));
        EntityHandle b = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 20, 10));
        EntityHandle p = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 30, 10));
        sim.Tick();
        StatusSystem.Apply(sim.World, s.Index, d.FindStatus("stealthed"), 1f, 400, 0);
        StatusSystem.Apply(sim.World, b.Index, d.FindStatus("blinded"), 1f, 400, 1);
        sim.Tick();
        sim.Tick();
        int cap = sim.World.Units.Capacity;
        Assert.False(sim.World.Units.Alive[cap - 1]);
        byte[] bytes = SaveTestRun.Save(sim);
        return (d, bytes, cap, s.Index, b.Index, p.Index, cap - 1, M7H1QaTests.Regn.Find(bytes, cap));
    }

    [Theory]
    [InlineData("honest", true)]
    [InlineData("free slot: stealth flag, no entry, total follows", false)]
    [InlineData("free slot: stealth flag, no entry, total not", false)]
    [InlineData("free slot: blind, no entry, total follows", false)]
    [InlineData("free slot: blind, no entry, total not", false)]
    [InlineData("free slot: one stealthed entry, every count follows", false)]
    [InlineData("live stealthed: entry count 1->0, units-with-statuses follows", false)]
    [InlineData("live blinded: entry count 1->0, units-with-statuses follows", false)]
    [InlineData("live stealthed: stealth 1->255", false)]
    [InlineData("live plain: stealth 0->1, no total", false)]
    [InlineData("live blinded: blind id past the last status", false)]
    [InlineData("live blinded: blind -1 stored (BlindOf -2)", false)]
    [InlineData("all three totals +1", false)]
    [InlineData("all three totals -1", false)]
    [InlineData("stealthed and plain swap their stealth bytes", false)]
    public void AForgedStatusCount_AtTheEdges(string forge, bool loads)
    {
        (GameData d, byte[] bytes, int cap, int s, int bl, int p, int never, M7H1QaTests.Regn o) = Scene();
        int uws = o.UnitsWithStatuses, blindedUnits = uws + 4, stealthedUnits = uws + 8;
        int stealth = uws - cap, blind = stealth - 4 - 4 * cap;
        Assert.Equal(cap, R32(bytes, stealth - 4));
        Assert.Equal(cap, R32(bytes, blind - 4));
        Assert.Equal((2, 1, 1), (R32(bytes, uws), R32(bytes, blindedUnits), R32(bytes, stealthedUnits)));
        int blindedId = d.FindStatus("blinded");
        byte[] b = (byte[])bytes.Clone();
        switch (forge)
        {
            case "honest": break;
            case "free slot: stealth flag, no entry, total follows": b[stealth + never] = 1; W32(b, stealthedUnits, 2); break;
            case "free slot: stealth flag, no entry, total not": b[stealth + never] = 1; break;
            case "free slot: blind, no entry, total follows": W32(b, blind + 4 * never, blindedId + 1); W32(b, blindedUnits, 2); break;
            case "free slot: blind, no entry, total not": W32(b, blind + 4 * never, blindedId + 1); break;
            case "free slot: one stealthed entry, every count follows":
                W32(b, o.StatusCount + 4 * never, 1);
                W32(b, o.StatusId + 4 * never * StatusStore.PerUnit, d.FindStatus("stealthed"));
                W32(b, o.TicksRemaining + 4 * never * StatusStore.PerUnit, 400);
                b[stealth + never] = 1;
                W32(b, stealthedUnits, 2);
                W32(b, uws, 3);
                break;
            case "live stealthed: entry count 1->0, units-with-statuses follows": W32(b, o.StatusCount + 4 * s, 0); W32(b, uws, 1); break;
            case "live blinded: entry count 1->0, units-with-statuses follows": W32(b, o.StatusCount + 4 * bl, 0); W32(b, uws, 1); break;
            case "live stealthed: stealth 1->255": b[stealth + s] = 255; break;
            case "live plain: stealth 0->1, no total": b[stealth + p] = 1; break;
            case "live blinded: blind id past the last status": W32(b, blind + 4 * bl, d.Statuses.Length + 1); break;
            case "live blinded: blind -1 stored (BlindOf -2)": W32(b, blind + 4 * bl, -1); break;
            case "all three totals +1": W32(b, uws, 3); W32(b, blindedUnits, 2); W32(b, stealthedUnits, 2); break;
            case "all three totals -1": W32(b, uws, 1); W32(b, blindedUnits, 0); W32(b, stealthedUnits, 0); break;
            case "stealthed and plain swap their stealth bytes": b[stealth + s] = 0; b[stealth + p] = 1; break;
            default: throw new ArgumentException(forge);
        }
        Assert.Equal(loads ? SaveError.None : SaveError.Malformed, SaveTestRun.TryLoad(d, SaveBytes.Rechecksum(b)));
    }

    /// <summary>
    /// Not in BUG-1220's scope (a note, not a bound): a live slot's entry count raised by one over a zeroed entry (status id 0,
    /// 0 ticks left). Printed so the report can say what the load does with it.
    /// </summary>
    [Fact]
    public void ALiveSlotsEntryCount_RaisedOverAZeroedEntry_WhatTheLoadDoes()
    {
        (GameData d, byte[] bytes, int cap, int s, _, int p, _, M7H1QaTests.Regn o) = Scene();
        byte[] plain = (byte[])bytes.Clone();
        W32(plain, o.StatusCount + 4 * p, 1);
        W32(plain, o.UnitsWithStatuses, 3);
        byte[] stealthed = (byte[])bytes.Clone();
        W32(stealthed, o.StatusCount + 4 * s, 2);
        output.WriteLine($"status 0 is '{d.Statuses[0].Key}' ({d.Statuses[0].Kind})");
        output.WriteLine($"plain slot count 0->1 (total follows): {SaveTestRun.TryLoad(d, SaveBytes.Rechecksum(plain))}");
        output.WriteLine($"stealthed slot count 1->2: {SaveTestRun.TryLoad(d, SaveBytes.Rechecksum(stealthed))}");
    }

    /// <summary>A copy with a second blind (smaller sight, so it wins the tie rule while it lasts) and a second stealthed status.</summary>
    private static readonly Lazy<GameData> s_twoOfEach = new(() => AiScenes.DataWith(dir => dir.EditJson("common/statuses.json", root =>
    {
        JsonArray a = root["statuses"]!.AsArray();
        a.Add(JsonNode.Parse("{\"id\":\"qa_deep_blind\",\"displayName\":\"Deep blind\",\"description\":\"QA\",\"kind\":\"blind\",\"sight\":1,\"reach\":1}"));
        a.Add(JsonNode.Parse("{\"id\":\"qa_equal_blind\",\"displayName\":\"Equal blind\",\"description\":\"QA\",\"kind\":\"blind\",\"sight\":2,\"reach\":3}"));
        a.Add(JsonNode.Parse("{\"id\":\"qa_veil\",\"displayName\":\"Veil\",\"description\":\"QA\",\"kind\":\"stealthed\"}"));
    })));

    /// <summary>
    /// Honest saves through every blind / stealth transition: one unit with three blinds (one winning on sight, two tied on
    /// sight and reach, so the id decides) ending at different ticks, one with two stealthed statuses ending apart, one dying
    /// with both kinds on it (its slot freed). Saved at every tick to 60: each loads, and the loaded sim's hash 30 ticks on
    /// equals the original's.
    /// </summary>
    [Fact]
    public void HonestSaves_ThroughEveryBlindAndStealthTransition_AllLoad()
    {
        GameData d = s_twoOfEach.Value;
        Simulation sim = RegenTests.Scene(d);
        int heavy = d.FindUnit("malazan_heavy_infantry");
        EntityHandle a = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 10, 10));
        EntityHandle v = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 20, 10));
        EntityHandle doomed = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 30, 10));
        sim.Tick();
        int blinded = d.FindStatus("blinded"), deep = d.FindStatus("qa_deep_blind"), equal = d.FindStatus("qa_equal_blind");
        int stealthed = d.FindStatus("stealthed"), veil = d.FindStatus("qa_veil");
        StatusSystem.Apply(sim.World, a.Index, equal, 1f, 40, 1);
        StatusSystem.Apply(sim.World, a.Index, blinded, 1f, 25, 1);
        StatusSystem.Apply(sim.World, a.Index, deep, 1f, 10, 1);
        StatusSystem.Apply(sim.World, v.Index, veil, 1f, 15, 0);
        StatusSystem.Apply(sim.World, v.Index, stealthed, 1f, 35, 0);
        StatusSystem.Apply(sim.World, doomed.Index, blinded, 1f, 400, 1);
        StatusSystem.Apply(sim.World, doomed.Index, veil, 1f, 400, 0);
        StatusStore st = sim.World.Units.Statuses;
        Assert.Equal(deep, st.BlindOf(a.Index));
        int saves = 0;
        var blindsSeen = new HashSet<int>();
        var resumed = new List<Simulation>();
        while (sim.TickNumber < 60)
        {
            if (sim.TickNumber == 20) RegenTests.Hit(sim, doomed, 100_000);
            sim.Tick();
            blindsSeen.Add(st.BlindOf(a.Index));
            byte[] bytes = SaveTestRun.Save(sim);
            Assert.Equal(SaveError.None, SaveTestRun.TryLoad(d, bytes));
            Simulation loaded = SaveTestRun.Load(d, bytes);
            Assert.Equal(st.BlindOf(a.Index), loaded.World.Units.Statuses.BlindOf(a.Index));
            Assert.Equal(st.HasStealth(v.Index), loaded.World.Units.Statuses.HasStealth(v.Index));
            Assert.Equal((st.UnitsWithStatuses, st.BlindedUnits, st.StealthedUnits),
                (loaded.World.Units.Statuses.UnitsWithStatuses, loaded.World.Units.Statuses.BlindedUnits, loaded.World.Units.Statuses.StealthedUnits));
            if (sim.TickNumber % 10 == 5 && sim.TickNumber > 20) resumed.Add(loaded); // after the direct hit (not a command, so not replayed)
            saves++;
        }
        // Each resumed save, run on to tick 60, is the uninterrupted run.
        foreach (Simulation r in resumed)
        {
            while (r.TickNumber < 60) r.Tick();
            Assert.Equal(sim.StateHash(), r.StateHash());
        }
        Assert.False(sim.World.Units.IsAlive(doomed));
        // deep (to tick 11), then blinded (sight 2, reach 3, lower id than its tie) to 26, then equal to 41, then none.
        Assert.Equal(new HashSet<int> { deep, blinded, equal, -1 }, blindsSeen);
        output.WriteLine($"{saves} honest saves loaded; blinds seen {string.Join(",", blindsSeen)}");
    }
}
