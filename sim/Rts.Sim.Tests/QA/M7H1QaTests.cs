using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-H1 (session 2026-11-11-1317), the fast rows: the forged <c>REGN</c> part at every field's edges (BUG-1165), the
/// regeneration step at both load places (BUG-1166) with the brief's values, every on-grid rate converting alike as a double
/// and as a float, and the faction folder-case message (BUG-1173 item 2) with more than one case-only folder. The 10,000
/// mutation fuzz and the 50 honest mid-match saves are in <c>Stress/M7H1RegnFuzzQaStressTests.cs</c>.
/// </summary>
[Collection(SerialCollection.Name)]
public class M7H1QaTests
{
    /// <summary>The offsets of one save's <c>REGN</c> part and the status part before it (unit capacity <c>Cap</c>).</summary>
    internal sealed record Regn(int Cap, int Ldt, int Acc, int Counts, int Total, int StatusCount, int StatusId, int Magnitude, int TicksRemaining, int SourcePlayer, int UnitsWithStatuses)
    {
        internal static Regn Find(byte[] bytes, int cap)
        {
            int regn = -1;
            for (int at = bytes.AsSpan().IndexOf("REGN"u8); at >= 0;)
            {
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4)) == cap) regn = at;
                int n = bytes.AsSpan(at + 1).IndexOf("REGN"u8);
                at = n < 0 ? -1 : at + 1 + n;
            }
            Assert.True(regn > 0, "no REGN part");
            int ldt = regn + 8, acc = ldt + 4 * cap + 4, counts = acc + 4 * cap + 4, total = counts + cap;
            Assert.Equal(cap, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(acc - 4)));
            Assert.Equal(cap, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(counts - 4)));
            // The status part ends just before the tag: Count, StatusId, Magnitude, TicksRemaining, PulseTicks, SourcePlayer,
            // _blind, _stealth (each with a length prefix), then three totals.
            int per = StatusStore.PerUnit * cap;
            int withStatuses = regn - 12;
            int countLen = withStatuses - (cap + 4) - (4 * cap + 4) - 5 * (4 * per + 4) - (4 * cap + 4);
            Assert.Equal(cap, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(countLen)));
            int statusId = countLen + 4 + 4 * cap + 4;
            Assert.Equal(per, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(statusId - 4)));
            int magnitude = statusId + 4 * per + 4, ticks = magnitude + 4 * per + 4, pulse = ticks + 4 * per + 4, source = pulse + 4 * per + 4;
            return new Regn(cap, ldt, acc, counts, total, countLen + 4, statusId, magnitude, ticks, source, withStatuses);
        }
    }

    /// <summary>
    /// A scene with regeneration state everywhere: four Malazan Heavy Infantry (1 HP/s after 6 s) hit at different ticks, two
    /// with a regenerating status, one Whirlwind unit (no faction regen) with one, and a Malazan unit killed (a freed slot that
    /// was used). Returns the sim at tick 40 and its save.
    /// </summary>
    internal static (Simulation Sim, byte[] Bytes, EntityHandle[] Units, int FreedSlot) Scene()
    {
        GameData d = RegenTests.s_regen.Value;
        Simulation sim = RegenTests.Scene(d);
        int heavy = d.FindUnit("malazan_heavy_infantry"), regen = d.FindStatus("regenerating");
        var hs = new EntityHandle[5];
        for (int k = 0; k < 4; k++) hs[k] = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 10 + 3 * k, 20));
        hs[4] = CombatScenes.Place(sim, 1, d.FindUnit("whirlwind_raider"), CombatScenes.At(sim, 30, 30));
        EntityHandle doomed = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 10, 30));
        sim.Tick();
        RegenTests.Hit(sim, hs[0], 30);
        RegenTests.Hit(sim, hs[1], 50);
        StatusSystem.Apply(sim.World, hs[1].Index, regen, 1f, 400, 0);
        StatusSystem.Apply(sim.World, hs[2].Index, regen, 0.5f, 400, 0);
        StatusSystem.Apply(sim.World, hs[4].Index, regen, 1f, 400, 1);
        RegenTests.Hit(sim, doomed, 10_000);
        while (sim.TickNumber < 39) sim.Tick();
        RegenTests.Hit(sim, hs[3], 5); // stamped 39, the tick about to run
        sim.Tick();
        Assert.Equal(40, sim.TickNumber);
        Assert.False(sim.World.Units.IsAlive(doomed));
        return (sim, SaveTestRun.Save(sim), hs, doomed.Index);
    }

    private static void W32(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);

    /// <summary>Each forged REGN field at its edges is refused <c>Malformed</c>; the honest edges load.</summary>
    [Theory]
    [InlineData("ldt-live=tick", true)]
    [InlineData("ldt-live=tick-1", true)]
    [InlineData("ldt-live=-1", true)]
    [InlineData("ldt-live=0", true)]
    [InlineData("ldt-live=tick+1", false)]
    [InlineData("ldt-live=max", false)]
    [InlineData("ldt-live=-2", false)]
    [InlineData("ldt-live=min", false)]
    [InlineData("acc-live=399", true)]
    [InlineData("acc-live=400", false)]
    [InlineData("acc-live=-1", false)]
    [InlineData("ldt-free=0", false)]
    [InlineData("ldt-free=tick", false)]
    [InlineData("ldt-freed=-2", false)]
    [InlineData("acc-free=1", false)]
    [InlineData("acc-freed=-1", false)]
    [InlineData("count-free=1", false)]
    [InlineData("count-live=2", false)]
    [InlineData("count-live=255", false)]
    [InlineData("count-live-without-status=1", false)]
    [InlineData("count-live-with-status=0", false)]
    [InlineData("total=+1", false)]
    [InlineData("total=-1", false)]
    [InlineData("total=0", false)]
    [InlineData("total=negative", false)]
    [InlineData("total=cap+1", false)]
    public void AForgedRegnField_AtItsEdges(string forge, bool loads)
    {
        (Simulation sim, byte[] bytes, EntityHandle[] hs, int freed) = Scene();
        GameData d = sim.World.Data;
        int cap = sim.World.Units.Capacity, tick = sim.TickNumber;
        Regn o = Regn.Find(bytes, cap);
        int total = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(o.Total));
        Assert.Equal(3, total);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(d, bytes));
        int live = hs[0].Index, never = cap - 1;
        Assert.False(sim.World.Units.Alive[never]);
        byte[] b = (byte[])bytes.Clone();
        switch (forge)
        {
            case "ldt-live=tick": W32(b, o.Ldt + 4 * live, tick); break;
            case "ldt-live=tick-1": W32(b, o.Ldt + 4 * live, tick - 1); break;
            case "ldt-live=-1": W32(b, o.Ldt + 4 * live, -1); break;
            case "ldt-live=0": W32(b, o.Ldt + 4 * live, 0); break;
            case "ldt-live=tick+1": W32(b, o.Ldt + 4 * live, tick + 1); break;
            case "ldt-live=max": W32(b, o.Ldt + 4 * live, int.MaxValue); break;
            case "ldt-live=-2": W32(b, o.Ldt + 4 * live, -2); break;
            case "ldt-live=min": W32(b, o.Ldt + 4 * live, int.MinValue); break;
            case "acc-live=399": W32(b, o.Acc + 4 * live, 399); break;
            case "acc-live=400": W32(b, o.Acc + 4 * live, 400); break;
            case "acc-live=-1": W32(b, o.Acc + 4 * live, -1); break;
            case "ldt-free=0": W32(b, o.Ldt + 4 * never, 0); break;
            case "ldt-free=tick": W32(b, o.Ldt + 4 * never, tick); break;
            case "ldt-freed=-2": W32(b, o.Ldt + 4 * freed, -2); break;
            case "acc-free=1": W32(b, o.Acc + 4 * never, 1); break;
            case "acc-freed=-1": W32(b, o.Acc + 4 * freed, -1); break;
            case "count-free=1": b[o.Counts + never] = 1; break;
            case "count-live=2": b[o.Counts + hs[1].Index] = 2; break;
            case "count-live=255": b[o.Counts + hs[1].Index] = 255; break;
            case "count-live-without-status=1": b[o.Counts + hs[0].Index] = 1; W32(b, o.Total, total + 1); break;
            case "count-live-with-status=0": b[o.Counts + hs[2].Index] = 0; W32(b, o.Total, total - 1); break;
            case "total=+1": W32(b, o.Total, total + 1); break;
            case "total=-1": W32(b, o.Total, total - 1); break;
            case "total=0": W32(b, o.Total, 0); break;
            case "total=negative": W32(b, o.Total, -1); break;
            case "total=cap+1": W32(b, o.Total, cap + 1); break;
            default: throw new ArgumentException(forge);
        }
        Assert.Equal(loads ? SaveError.None : SaveError.Malformed, SaveTestRun.TryLoad(d, SaveBytes.Rechecksum(b)));
    }

    /// <summary>
    /// A free slot carrying a whole forged regenerating status (the status part's entry and count, and the REGN count and total
    /// agreeing with it) is refused: freeing a slot clears its statuses, so no build writes one. Loaded, the next unit spawned
    /// into the slot would inherit a regenerating status nothing cast.
    /// </summary>
    [Fact(Skip = "BUG-1220: a free slot's forged status entries load (the status part checks ids, not that a free slot is empty)")]
    public void AFreeSlot_CarryingAConsistentForgedRegeneratingStatus_IsRefused()
    {
        (Simulation sim, byte[] bytes, _, _) = Scene();
        GameData d = sim.World.Data;
        int cap = sim.World.Units.Capacity, never = cap - 1;
        Regn o = Regn.Find(bytes, cap);
        byte[] b = (byte[])bytes.Clone();
        int slot = never * StatusStore.PerUnit;
        W32(b, o.StatusCount + 4 * never, 1);
        W32(b, o.StatusId + 4 * slot, d.FindStatus("regenerating"));
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o.Magnitude + 4 * slot), 100f);
        W32(b, o.TicksRemaining + 4 * slot, 10_000);
        W32(b, o.SourcePlayer + 4 * slot, 0);
        W32(b, o.UnitsWithStatuses, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o.UnitsWithStatuses)) + 1);
        b[o.Counts + never] = 1;
        W32(b, o.Total, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o.Total)) + 1);
        byte[] forged = SaveBytes.Rechecksum(b);
        SaveError error = SaveTestRun.TryLoad(d, forged);
        if (error == SaveError.Malformed) return;
        // Loaded: what it does. Fill every free slot so one lands in the forged one, then look at its statuses.
        Simulation loaded = SaveTestRun.Load(d, forged);
        UnitStore u = loaded.World.Units;
        for (int k = 0; k < 600; k++) loaded.Tick();
        string after600 = $"after 600 ticks: slot alive {u.Alive[never]}, statuses {u.Statuses.Count[never]}, total {u.Statuses.RegeneratingUnits}";
        int heavy = d.FindUnit("malazan_heavy_infantry");
        while (!u.Alive[never] && u.TrySpawn(0, heavy, d.Units[heavy], CombatScenes.At(loaded, 5, 5), out _)) { }
        Assert.Fail($"a free slot carrying a regenerating status loaded ({error}); {after600}; the unit spawned into it has regen {u.Statuses.HasRegen(never)}, statuses {u.Statuses.Count[never]}, total {u.Statuses.RegeneratingUnits}");
    }

    /// <summary>The brief's rates at both load places: only 0.05 (on the step) loads; each other is one error per place.</summary>
    [Theory]
    [InlineData(0.05, null)]
    [InlineData(0.049999, "not a multiple of 0.05")]
    [InlineData(0.1 + 1e-9, "not a multiple of 0.05")]
    [InlineData(1e300, "above the maximum")]
    [InlineData(-0.05, null)]
    [InlineData(99.95, null)]
    [InlineData(100, null)]
    [InlineData(100.05, "above the maximum")]
    [InlineData(0.15, null)]
    [InlineData(0.07, "not a multiple of 0.05")]
    public void ARegenRate_AtBothPlaces(double rate, string? error)
    {
        foreach (bool status in new[] { false, true })
        {
            using TestDataDir dir = TestDataDir.CopyOfShipped();
            if (status)
                dir.EditJson("factions/malazan/abilities.json", root =>
                {
                    JsonObject e = root["abilities"]![0]!["effects"]![0]!.AsObject();
                    e["status"] = "regenerating";
                    e["magnitude"] = rate;
                });
            else
                dir.EditJson("factions/malazan/faction.json", r => r["regen"] = new JsonObject { ["hpPerSecond"] = rate, ["delay"] = 6 });
            DataLoadResult r = DataLoader.LoadAll(dir.Path);
            string where = status ? "status magnitude" : "faction rate";
            if (rate < 0)
            {
                // Negative: refused by the positivity check, one error, never read as a heal or a damage.
                DataError e = Assert.Single(r.Errors);
                Assert.True(e.Path.EndsWith(status ? ".magnitude" : "regen.hpPerSecond"), $"{where}: {e}");
                continue;
            }
            if (error == null)
            {
                Assert.True(r.Ok, $"{where} {rate}: " + string.Join("; ", r.Errors));
                continue;
            }
            DataError only = Assert.Single(r.Errors);
            Assert.Equal(status ? "factions/malazan/abilities.json" : "factions/malazan/faction.json", only.File);
            Assert.True(only.Message.Contains(error), $"{where} {rate}: {only.Message}");
            if (error.StartsWith("not"))
                Assert.True(only.Message.Contains(rate.ToString(System.Globalization.CultureInfo.CurrentCulture)) || only.Message.Contains(rate.ToString()), $"{where}: the message does not name the value: {only.Message}");
        }
    }

    /// <summary>Every rate on the grid up to the maximum is on the step and converts alike as a double (faction) and a float (status).</summary>
    [Fact]
    public void EveryGridRate_IsOnTheStep_AndConvertsAlikeAsDoubleAndFloat()
    {
        int max = (int)Math.Round(DataLimits.MaxRegenPerSecond * 20);
        for (int n = 1; n <= max; n++)
        {
            double rate = n / 20.0;
            double written = double.Parse((n * 0.05).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(DataLoader.IsOnRegenStep(rate) && DataLoader.IsOnRegenStep(written), $"{written} is on the grid");
            Assert.Equal(n, DataLoader.RegenPerTick(written));
            Assert.Equal(n, DataLoader.RegenPerTick((float)written));
            Assert.False(DataLoader.IsOnRegenStep(written + 0.025), $"{written + 0.025} is a half step");
        }
    }

    private static void CaseRename(TestDataDir dir, string from, string to)
    {
        string tmp = dir.FullPath("factions/qa_tmp_" + to.ToLowerInvariant());
        Directory.Move(dir.FullPath("factions/" + from), tmp);
        Directory.Move(tmp, dir.FullPath("factions/" + to));
    }

    /// <summary>Both shipped folders renamed by case only: one order error per entry, each naming both spellings; nothing loads, no "not listed".</summary>
    [Fact]
    public void TwoFoldersDifferingOnlyInCase_EachIsOneOrderError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions();
        CaseRename(dir, "malazan", "Malazan");
        CaseRename(dir, "whirlwind", "WHIRLWIND");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        var order = r.Errors.Where(e => e.File == "common/factions.json").ToList();
        Assert.Equal(2, order.Count);
        Assert.Contains(order, e => e.Path == "order[0]" && e.Message.Contains("factions/Malazan/, not factions/malazan/"));
        Assert.Contains(order, e => e.Path == "order[1]" && e.Message.Contains("factions/WHIRLWIND/, not factions/whirlwind/"));
        Assert.DoesNotContain(r.Errors, e => e.Message.Contains("is not listed") || e.Message.Contains("has no folder"));
    }

    /// <summary>A case-only folder listed twice: the second entry is reported as listed twice (or at least once, clearly), never "not listed".</summary>
    [Fact(Skip = "BUG-1221: the second entry reads 'has no folder factions/whirlwind/', not 'listed twice'")]
    public void ACaseOnlyFolder_ListedTwice_IsNotReportedAsUnlisted()
    {
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions();
        CaseRename(dir, "whirlwind", "Whirlwind");
        dir.EditJson("common/factions.json", root => root["order"] = new JsonArray("malazan", "whirlwind", "whirlwind"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        var order = r.Errors.Where(e => e.File == "common/factions.json").ToList();
        Assert.True(order.TrueForAll(e => !e.Message.Contains("is not listed")), string.Join(" | ", order));
        Assert.True(order.Count(e => e.Path == "order[2]") == 1, string.Join(" | ", order));
        DataError second = order.Single(e => e.Path == "order[2]");
        Assert.True(second.Message.Contains("listed twice"), "order[2]: " + second.Message + " (all: " + string.Join(" | ", order) + ")");
    }
}
