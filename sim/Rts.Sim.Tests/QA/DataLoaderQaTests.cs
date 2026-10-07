using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA;

/// <summary>QA attacks on the data loader (M1-2): every malformed input must become a DataError naming a file, never an exception.</summary>
[Collection(SerialCollection.Name)]
public class DataLoaderQaTests
{
    private const string MalazanUnits = "factions/malazan/units.json";
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public DataLoaderQaTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    /// <summary>Loads, failing the test on any exception, and checks every error names a file.</summary>
    private static DataLoadResult LoadNoThrow(string dir)
    {
        DataLoadResult? result = null;
        Exception? ex = Record.Exception(() => result = DataLoader.LoadAll(dir));
        Assert.True(ex == null, $"LoadAll threw {ex}");
        Assert.NotNull(result);
        foreach (DataError e in result!.Errors)
        {
            Assert.False(string.IsNullOrEmpty(e.File), $"error without a file: {e}");
            Assert.False(string.IsNullOrEmpty(e.Message), $"error without a message: {e}");
        }
        Assert.Equal(result.Ok, result.Errors.Count == 0);
        if (result.Ok) AssertSane(result.Data!);
        return result;
    }

    /// <summary>Invariants any successfully loaded GameData must satisfy (finite, positive where the sim divides or loops).</summary>
    private static void AssertSane(GameData d)
    {
        Assert.True(d.Rules.HalfPopCap > 0, $"HalfPopCap {d.Rules.HalfPopCap}");
        Assert.True(float.IsFinite(d.Rules.GoldPerTick) && d.Rules.GoldPerTick > 0);
        Assert.True(float.IsFinite(d.Rules.WoodPerTick) && d.Rules.WoodPerTick > 0);
        Assert.True(float.IsFinite(d.Rules.NodeSearchRadius));
        foreach (float m in d.DamageTable.Multipliers) Assert.True(float.IsFinite(m) && m >= 0, $"multiplier {m}");
        for (int i = 0; i < d.Units.Length; i++)
        {
            UnitDef u = d.Units[i];
            Assert.Equal(i, u.Id);
            Assert.True(u.Hp > 0);
            Assert.True(float.IsFinite(u.SpeedPerTick) && u.SpeedPerTick > 0, $"{u.Key} SpeedPerTick {u.SpeedPerTick}");
            Assert.True(float.IsFinite(u.Sight) && u.Sight > 0, $"{u.Key} Sight {u.Sight}");
            Assert.True(float.IsFinite(u.Radius));
            Assert.True(u.HalfPop >= 0 && u.TrainTicks > 0);
            Assert.True(u.Attack.CooldownTicks > 0 && u.Attack.WindupTicks >= 0);
            Assert.True(float.IsFinite(u.Attack.Range), $"{u.Key} Range {u.Attack.Range}");
            Assert.True(float.IsFinite(u.Attack.MinRange) && float.IsFinite(u.Attack.Splash));
            foreach (float b in u.Attack.BonusVs) Assert.True(float.IsFinite(b) && b > 0, $"{u.Key} bonus {b}");
            Assert.All(u.Requires, r => Assert.False(string.IsNullOrWhiteSpace(r), $"{u.Key} has a null/blank requires entry"));
            Assert.All(u.Tags, t => Assert.False(string.IsNullOrWhiteSpace(t), $"{u.Key} has a null/blank tag"));
        }
    }

    private static string Snapshot(GameData d) => JsonSerializer.Serialize(d);

    private static void ReplaceInFile(TestDataDir dir, string rel, string from, string to)
    {
        string path = dir.FullPath(rel);
        string text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
    }

    // ---------- raw-text faults: never throw, always an error naming the file ----------

    [Fact]
    public void TruncatedUnitsFile_AtEveryCut_YieldsErrorNotException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        byte[] original = File.ReadAllBytes(dir.FullPath(MalazanUnits));
        int cuts = 0;
        for (int len = 0; len < original.Length - 3; len += len < 64 ? 1 : 17)
        {
            File.WriteAllBytes(dir.FullPath(MalazanUnits), original.AsSpan(0, len).ToArray());
            DataLoadResult r = LoadNoThrow(dir.Path);
            Assert.False(r.Ok, $"truncated at {len} bytes but loaded");
            Assert.Contains(r.Errors, e => e.File == MalazanUnits);
            cuts++;
        }
        Assert.True(cuts > 300);
    }

    [Fact]
    public void Utf8Bom_IsAccepted()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string rel in new[] { MalazanUnits, "common/rules.json", "factions/whirlwind/faction.json" })
        {
            byte[] body = File.ReadAllBytes(dir.FullPath(rel));
            File.WriteAllBytes(dir.FullPath(rel), new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray());
        }
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
    }

    [Theory]
    [InlineData("")]                                // empty file
    [InlineData("   \n\t ")]                        // whitespace only
    [InlineData("null")]                            // root null
    [InlineData("[]")]                              // root array
    [InlineData("42")]                              // root number
    [InlineData("\"units\"")]                       // root string
    [InlineData("{\"units\": {}}")]                 // object where array expected
    [InlineData("{\"units\": 5}")]
    [InlineData("{\"units\": [1, 2]}")]             // numbers where objects expected
    [InlineData("{\"units\": [\"x\"]}")]
    [InlineData("{\"units\": [{\"id\": 5}]}")]      // number where string expected
    [InlineData("{\"units\": [{\"hp\": \"55\"}]}")] // string where int expected
    [InlineData("{\"units\": [{\"hp\": 55.5}]}")]   // fraction where int expected
    [InlineData("{\"units\": [{\"hp\": true}]}")]
    [InlineData("{\"units\": [{\"hp\": 1e10}]}")]   // int overflow
    [InlineData("{\"units\": [{\"hp\": 99999999999999999999999}]}")]
    [InlineData("{\"units\": [{\"speed\": NaN}]}")] // non-standard literals
    [InlineData("{\"units\": [{\"speed\": Infinity}]}")]
    [InlineData("{\"units\": [{\"speed\": \"NaN\"}]}")]
    [InlineData("{\"units\": [{\"speed\": 1e400}]}")]
    [InlineData("{\"units\": [{\"attack\": []}]}")]
    [InlineData("{\"units\": [{\"attack\": {\"bonusVs\": []}}]}")]
    [InlineData("{\"units\": [{\"attack\": {\"bonusVs\": {\"heavy\": \"x\"}}}]}")]
    [InlineData("{\"units\": [{\"requires\": \"age_ii\"}]}")]
    [InlineData("{\"units\": [], }")]               // trailing comma
    [InlineData("// comment\n{\"units\": []}")]     // comments
    [InlineData("{\"units\": []} {\"units\": []}")] // two roots
    [InlineData("{'units': []}")]                   // single quotes
    [InlineData("{\"units\": [{\"displayName\": \"\\uD800\"}]}")] // lone surrogate escape
    [InlineData("{\"units\": [{\"id\": \"malazan_\u00e9\"}]}")]   // non-ASCII id
    [InlineData("{\"units\": [{\"id\": \"MALAZAN_X\"}]}")]
    [InlineData("{\"units\": [{\"id\": \"_x\"}]}")]
    [InlineData("{\"units\": [{\"id\": \"\"}]}")]
    [InlineData("{\"units\": [null]}")]
    [InlineData("{\"units\": [{}]}")]
    public void MalformedUnitsFile_YieldsErrorInThatFile_NoException(string content)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath(MalazanUnits), content, new UTF8Encoding(false));
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == MalazanUnits);
    }

    [Fact]
    public void InvalidUtf8Bytes_YieldErrorNotException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        byte[] body = File.ReadAllBytes(dir.FullPath(MalazanUnits));
        int at = Encoding.UTF8.GetString(body).IndexOf("Laborer", StringComparison.Ordinal);
        Assert.True(at > 0);
        body[at] = 0xFF;
        body[at + 1] = 0xC3; // truncated multi-byte sequence
        File.WriteAllBytes(dir.FullPath(MalazanUnits), body);
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == MalazanUnits);
    }

    [Fact]
    public void DeeplyNestedJson_YieldsErrorNotException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string deep = new string('[', 5000) + new string(']', 5000);
        File.WriteAllText(dir.FullPath(MalazanUnits), "{\"units\": " + deep + "}");
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok);
    }

    [Fact]
    public void RequiredFileIsADirectory_YieldsErrorNotException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath("common/rules.json"));
        Directory.CreateDirectory(dir.FullPath("common/rules.json"));
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "common/rules.json");
    }

    [Fact]
    public void StrayFactionFolder_YieldsErrorsNotException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        Directory.CreateDirectory(Path.Combine(dir.Path, "factions", "Empty Folder"));
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File.StartsWith("factions/Empty Folder/", StringComparison.Ordinal));
    }

    [Fact]
    public void FactionsFolderMissing_YieldsErrorNotException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        Directory.Delete(Path.Combine(dir.Path, "factions"), recursive: true);
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File.StartsWith("factions", StringComparison.Ordinal));
    }

    [Fact]
    public void LockedFile_YieldsErrorNotException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        using (new FileStream(dir.FullPath(MalazanUnits), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            DataLoadResult r = LoadNoThrow(dir.Path);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.File == MalazanUnits);
        }
    }

    // ---------- numeric edges: out-of-range values must be rejected, not turned into infinities ----------

    [Theory] // regression: BUG-0007
    [InlineData("speed", "1e300", "units[2].speed")]
    [InlineData("sight", "1e300", "units[2].sight")]
    [InlineData("attack.range", "1e300", "units[2].attack.range")]
    [InlineData("attack.splash", "1e300", "units[2].attack.splash")]
    [InlineData("attack.bonusVs", "{\"heavy\": 1e300}", "units[2].attack.bonusVs.heavy")]
    public void HugeFloat_IsRejected_NotStoredAsInfinity(string field, string raw, string path)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", field, raw);
        DataLoadResult r = LoadNoThrow(dir.Path); // AssertSane fails here if it loaded an Infinity
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == MalazanUnits && e.Path == path);
    }

    [Fact] // regression: BUG-0007
    public void HugePopCap_IsRejected_NotOverflowedToNegative()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", root => root["popCap"] = 2_000_000_000);
        DataLoadResult r = LoadNoThrow(dir.Path); // AssertSane fails if HalfPopCap wrapped negative
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "common/rules.json" && e.Path == "popCap");
    }

    [Theory]
    [InlineData("attack.cooldown", "1e10", "units[2].attack.cooldown")]
    [InlineData("attack.windup", "1e10", "units[2].attack.windup")]
    [InlineData("trainTime", "1e10", "units[2].trainTime")]
    [InlineData("pop", "1e10", "units[2].pop")]
    [InlineData("attack.cooldown", "0.001", "units[2].attack.cooldown")] // rounds to 0 ticks
    [InlineData("radius", "1e300", "units[2].radius")]
    public void HugeOrTinyTimesAndPop_YieldOneErrorAtThatField(string field, string raw, string path)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", field, raw);
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok);
        DataError e = Assert.Single(r.Errors);
        _out.WriteLine(e.ToString());
        Assert.Equal(MalazanUnits, e.File);
        Assert.Equal(path, e.Path);
    }

    [Theory]
    [InlineData("pop", "0")]
    [InlineData("armor", "0")]
    [InlineData("attack.windup", "0")]
    [InlineData("cost", "{\"gold\": 0, \"wood\": 0}")]
    [InlineData("tags", "[]")]
    [InlineData("requires", "[]")]
    public void ZeroAndEmptyWhereAllowed_LoadsClean(string field, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", field, raw);
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
    }

    [Fact] // regression: BUG-0009
    public void NullEntryInRequiresOrTags_IsRejected()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "requires", "[null]");
        dir.SetUnitField("malazan", "malazan_sapper", "tags", "[\"\", null]");
        DataLoadResult r = LoadNoThrow(dir.Path); // AssertSane fails if a null string reached GameData
        Assert.False(r.Ok);
    }

    // ---------- faction template coverage (docs/02 "Faction template": every faction fills seven slots) ----------

    [Fact] // regression: BUG-0010
    public void FactionWithEmptyUnitsList_IsRejected()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath("factions/whirlwind/units.json"), "{\"units\": []}");
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok, "whirlwind has zero units but the data loaded");
        Assert.Contains(r.Errors, e => e.File.StartsWith("factions/whirlwind/", StringComparison.Ordinal));
    }

    [Fact] // regression: BUG-0010
    public void FactionWithTwoUnitsInOneSlot_IsRejected()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_sapper", "slot", "\"ranged\""); // now no unique, two ranged
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok, "malazan has two ranged units and no unique but the data loaded");
    }

    // ---------- duplicate JSON keys: the parser must not silently keep the last one ----------

    [Fact] // regression: BUG-0008
    public void DuplicateBonusVsKey_IsReported()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        ReplaceInFile(dir, MalazanUnits, "\"heavy\": 1.3", "\"heavy\": 1.3, \"heavy\": 9.0");
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok, "duplicate bonusVs key silently accepted; crossbowman bonus vs heavy = " +
            (r.Data == null ? "?" : r.Data.Units[r.Data.FindUnit("malazan_crossbowman")].Attack.BonusVs[r.Data.DamageTable.ArmorClassKeys.IndexOf("heavy")].ToString()));
        Assert.Contains(r.Errors, e => e.File == MalazanUnits);
    }

    [Fact] // regression: BUG-0008
    public void DuplicatePropertyInUnit_IsReported()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        ReplaceInFile(dir, MalazanUnits, "\"hp\": 55,", "\"hp\": 55, \"hp\": 5500,");
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok, "duplicate 'hp' silently accepted; crossbowman hp = " +
            (r.Data == null ? "?" : r.Data.Units[r.Data.FindUnit("malazan_crossbowman")].Hp.ToString()));
        Assert.Contains(r.Errors, e => e.File == MalazanUnits);
    }

    // ---------- seeded byte-level fuzz ----------

    [Fact]
    public void ByteMutationFuzz_NeverThrows_AndLoadedDataStaysSane()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string[] files = { MalazanUnits, "factions/whirlwind/units.json", "common/rules.json", "common/damage_table.json", "factions/malazan/faction.json" };
        var originals = files.ToDictionary(f => f, f => File.ReadAllBytes(dir.FullPath(f)));
        byte[] tokens = Encoding.ASCII.GetBytes("{}[]\",:-.0123456789eE nultrfa\\");
        var rng = new Random(20261003); // test-side fuzz input only
        int ok = 0, failed = 0;
        for (int iter = 0; iter < 400; iter++)
        {
            string target = files[rng.Next(files.Length)];
            byte[] body = (byte[])originals[target].Clone();
            int edits = 1 + rng.Next(3);
            for (int k = 0; k < edits; k++)
            {
                int at = rng.Next(body.Length);
                body[at] = rng.Next(4) == 0 ? (byte)rng.Next(256) : tokens[rng.Next(tokens.Length)];
            }
            File.WriteAllBytes(dir.FullPath(target), body);
            DataLoadResult r = LoadNoThrow(dir.Path);
            if (r.Ok) ok++; else failed++;
            File.WriteAllBytes(dir.FullPath(target), originals[target]);
        }
        Assert.True(failed > 0 && ok + failed == 400);
    }

    // ---------- scale ----------

    [Fact]
    [Trait("Category", "Perf")]
    public void TenThousandUnitFile_LoadsUnderTwoSeconds()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanUnits, root =>
        {
            var units = root["units"]!.AsArray();
            var template = units[2]!.ToJsonString();
            for (int i = 0; i < 10_000; i++)
            {
                var u = System.Text.Json.Nodes.JsonNode.Parse(template)!.AsObject();
                u["id"] = $"malazan_bulk_{i:D5}";
                units.Add(u);
            }
        });
        Assert.True(new FileInfo(dir.FullPath(MalazanUnits)).Length > 3_000_000);
        DataLoader.LoadAll(TestDataDir.Shipped); // warm up
        DataLoadResult? loaded = null;
        var sw = new System.Diagnostics.Stopwatch();
        Action load = () =>
        {
            sw.Start();
            loaded = LoadNoThrow(dir.Path);
            sw.Stop();
        };
        long allocated = AllocationProbe.Measure(load);
        DataLoadResult r = loaded!;
        _out.WriteLine($"10k-unit load: {sw.ElapsedMilliseconds} ms, {allocated / 1_000_000} MB allocated");
        // M3-6 (BUG-0010): one unit per template slot, so each bulk copy of the Crossbowman (ranged) is one error at its
        // slot; every unit is still read and validated in full, which is what this times.
        Assert.Equal(10_000, r.Errors.Count);
        Assert.All(r.Errors, e => Assert.Contains("slot 'ranged' is already filled by 'malazan_crossbowman'", e.Message));
        // ~0.2-0.3 s alone, ~0.7 s under the parallel suite; 2 s leaves headroom on a slower machine.
        Assert.True(sw.ElapsedMilliseconds < 2000, $"10k-unit load took {sw.ElapsedMilliseconds} ms");
        Assert.True(allocated < 200_000_000, $"10k-unit load allocated {allocated / 1_000_000} MB");
    }

    [Fact]
    public void TenThousandDuplicateIds_YieldOneErrorEach()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanUnits, root =>
        {
            var units = root["units"]!.AsArray();
            var template = units[2]!.ToJsonString();
            for (int i = 0; i < 2_000; i++) units.Add(System.Text.Json.Nodes.JsonNode.Parse(template));
        });
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.Equal(2_000, r.Errors.Count);
        Assert.All(r.Errors, e => Assert.Contains("duplicate unit id 'malazan_crossbowman'", e.Message));
    }

    // ---------- determinism of the result ----------

    [Fact]
    public void RenamedTempCopy_GivesIdenticalGameData()
    {
        DataLoadResult a = LoadNoThrow(TestDataDir.Shipped);
        using TestDataDir copy = TestDataDir.CopyOfShipped();
        // Move it to a differently named (and deeper, unicode) root folder.
        string moved = Path.Combine(Path.GetDirectoryName(copy.Path)!, "renamed \u00e9 copy " + Path.GetFileName(copy.Path), "data");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(copy.Path, moved);
        try
        {
            DataLoadResult b = LoadNoThrow(moved);
            DataLoadResult c = LoadNoThrow(moved + Path.DirectorySeparatorChar); // trailing separator
            Assert.Equal(Snapshot(a.Data!), Snapshot(b.Data!));
            Assert.Equal(Snapshot(a.Data!), Snapshot(c.Data!));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(moved)!, recursive: true);
        }
    }

    [Fact]
    public void ErrorList_IsIdenticalAcrossLoads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "armorClass", "\"titanium\"");
        dir.SetUnitField("whirlwind", "whirlwind_zealot", "attack.bonusVs", "{\"zz\": 1, \"aa\": 2, \"mm\": -1}");
        dir.EditJson("common/rules.json", root => root.Remove("treeWood"));
        string first = string.Join("\n", LoadNoThrow(dir.Path).Errors);
        for (int i = 0; i < 5; i++) Assert.Equal(first, string.Join("\n", LoadNoThrow(dir.Path).Errors));
        Assert.True(first.Split('\n').Length >= 5, first);
    }

    [Fact]
    public void FindUnitAndFindFaction_RoundTripEveryKey()
    {
        GameData d = LoadNoThrow(TestDataDir.Shipped).Data!;
        foreach (UnitDef u in d.Units) Assert.Equal(u.Id, d.FindUnit(u.Key));
        foreach (FactionDef f in d.Factions) Assert.Equal(f.Id, d.FindFaction(f.Key));
        Assert.Equal(-1, d.FindUnit(""));
        Assert.Equal(-1, d.FindUnit("MALAZAN_CROSSBOWMAN"));
        Assert.Equal(-1, d.FindUnit("zzzz"));
        Assert.Equal(-1, d.FindFaction("a"));
        foreach (FactionDef f in d.Factions)
            Assert.All(f.Units, id => Assert.Equal(f.Id, d.Units[id].Faction));
        Assert.Equal(d.Units.Length, d.Factions.Sum(f => f.Units.Length));
    }

    // ---------- GameData shape: nothing mutable reaches tick code ----------

    private static readonly Type[] DefTypes =
        { typeof(GameData), typeof(DamageTable), typeof(RulesDef), typeof(FactionDef), typeof(UnitDef), typeof(AttackDef) };

    [Fact]
    public void GameDataTypes_ExposeOnlyImmutableMembers()
    {
        foreach (Type t in DefTypes)
        {
            Assert.True(t.IsSealed, $"{t.Name} should be sealed");
            Assert.Empty(t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
            Assert.Empty(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                MethodInfo? set = p.SetMethod;
                if (set != null)
                {
                    bool initOnly = set.ReturnParameter.GetRequiredCustomModifiers()
                        .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
                    Assert.True(initOnly || !set.IsPublic, $"{t.Name}.{p.Name} has a public mutable setter");
                }
                Type pt = p.PropertyType;
                bool allowed = pt.IsPrimitive || pt.IsEnum || pt == typeof(string)
                    || DefTypes.Contains(pt)
                    || (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(ImmutableArray<>));
                Assert.True(allowed, $"{t.Name}.{p.Name} is {pt.Name}, not an immutable member type");
            }
        }
    }
}
