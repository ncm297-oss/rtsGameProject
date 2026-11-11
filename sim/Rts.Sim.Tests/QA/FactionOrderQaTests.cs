using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-3 (session 2026-11-10-1236): attacks on the append-only faction order (<c>common/factions.json</c>), the pack's
/// per-unit worth (BUG-1168) and the hard-cap load check (BUG-1167). The id rule is checked as a property under every order
/// (not against a pinned table): factions in list order, unit and building ids grouped by faction in that order and ordinal
/// within a faction, every reference (trainedAt, a faction's roster) resolving to the same key. The third faction here is a
/// <b>full</b> one (every file a shipped faction has, abilities and ai.json included), and it plays as an AI seat.
/// </summary>
[Collection(SerialCollection.Name)]
public class FactionOrderQaTests(ITestOutputHelper output)
{
    private const string OrderFile = "common/factions.json";

    // ------------------------------------------------------------------ a full third faction

    /// <summary>
    /// Adds <c>teblor</c> as a full copy of Malazan: every file (abilities.json too), ids renamed. Its abilities are renamed
    /// <c>zteblor_*</c> so they sort after every shipped ability (BUG-1171: a <c>teblor_*</c> ability would renumber
    /// <c>telas_fire</c>), its tech <c>teblor_supply</c>. <paramref name="order"/> is the whole order list.
    /// </summary>
    internal static void AddFullTeblor(TestDataDir dir, string[] order, string abilityPrefix = "zteblor_", string tech = "teblor_supply")
    {
        string from = dir.FullPath("factions/malazan"), to = dir.FullPath("factions/teblor");
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            string text = File.ReadAllText(file)
                .Replace("malazan_", "teblor_")
                .Replace("moranth_supply", tech)
                .Replace("\"telas_fire\"", $"\"{abilityPrefix}telas_fire\"")
                .Replace("\"cusser\"", $"\"{abilityPrefix}cusser\"");
            File.WriteAllText(Path.Combine(to, Path.GetFileName(file)), text);
        }
        dir.EditJson("factions/teblor/faction.json", root => root["id"] = "teblor");
        WriteOrder(dir, order);
    }

    private static void WriteOrder(TestDataDir dir, string[] order) =>
        File.WriteAllText(dir.FullPath(OrderFile), "{\"order\": [" + string.Join(", ", order.Select(o => $"\"{o}\"")) + "]}");

    private static GameData Load(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }

    private static GameData LoadWith(string[] order, bool teblor, string abilityPrefix = "zteblor_", string tech = "teblor_supply")
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        if (teblor) AddFullTeblor(dir, order, abilityPrefix, tech);
        else WriteOrder(dir, order);
        return Load(dir);
    }

    private static readonly Lazy<GameData> s_full = new(() => LoadWith(new[] { "malazan", "whirlwind", "teblor" }, teblor: true));

    // ------------------------------------------------------------------ the id rule under every order

    public static IEnumerable<object[]> Orders() => new[]
    {
        new object[] { "malazan,whirlwind", false },
        new object[] { "whirlwind,malazan", false },
        new object[] { "malazan,whirlwind,teblor", true },
        new object[] { "teblor,malazan,whirlwind", true },
        new object[] { "malazan,teblor,whirlwind", true },
        new object[] { "whirlwind,teblor,malazan", true },
    };

    /// <summary>
    /// Under each order: factions are the list; every unit / building id is grouped by faction in list order and ordinal
    /// within; every faction's roster names only its own types; trainedAt resolves to the same key; FindUnit / FindBuilding /
    /// FindFaction invert the tables; the same order loads to the same ContentHash twice. M7-3b (BUG-1171): tech ids are the
    /// common techs first, then grouped by faction in list order, and ability ids grouped by faction, each ordinal within;
    /// every reference to them (a unit's abilities, a requires list, the age tech) resolves to the same key. The Teblor copy
    /// here names its abilities <c>a_*</c> and its tech <c>aa_supply</c>, sorting before every shipped key (and before
    /// <c>age_ii</c>), so an ordinal table would renumber them all.
    /// </summary>
    [Theory]
    [MemberData(nameof(Orders))]
    public void TheIdRule_HoldsUnderEveryOrder(string list, bool teblor)
    {
        string[] order = list.Split(',');
        GameData d = LoadWith(order, teblor, abilityPrefix: "a_", tech: "aa_supply");
        Assert.Equal(order, d.Factions.Select(f => f.Key));
        for (int f = 0; f < d.Factions.Length; f++)
        {
            Assert.Equal(f, d.Factions[f].Id);
            Assert.Equal(f, d.FindFaction(order[f]));
        }
        CheckGrouped(d.Units.Select(u => (u.Key, u.Faction, u.Id)).ToArray(), "unit");
        CheckGrouped(d.Buildings.Select(b => (b.Key, b.Faction, b.Id)).ToArray(), "building");
        foreach (UnitDef u in d.Units)
        {
            Assert.Equal(u.Id, d.FindUnit(u.Key));
            Assert.StartsWith(order[u.Faction] + "_", u.Key);
            Assert.Equal(u.TrainedAt, d.Buildings[u.TrainedAtTypeId].Key);
            Assert.Equal(u.Faction, d.Buildings[u.TrainedAtTypeId].Faction);
        }
        foreach (BuildingDef b in d.Buildings)
        {
            Assert.Equal(b.Id, d.FindBuilding(b.Key));
            Assert.StartsWith(order[b.Faction] + "_", b.Key);
        }
        foreach (FactionDef f in d.Factions)
            foreach (int u in f.Units) Assert.Equal(f.Id, d.Units[u].Faction);
        // Trains is ascending by unit id and names only the building's own faction's units.
        for (int b = 0; b < d.Buildings.Length; b++)
        {
            var t = d.UnitsTrainedAt(b);
            for (int k = 1; k < t.Length; k++) Assert.True(t[k - 1] < t[k], $"{d.Buildings[b].Key}: trains not ascending");
            foreach (int u in t) Assert.Equal(d.Buildings[b].Faction, d.Units[u].Faction);
        }
        CheckGrouped(d.Techs.Select(t => (t.Key, t.Faction, t.Id)).ToArray(), "tech");
        CheckGrouped(d.Abilities.Select(a => (a.Key, a.Faction, a.Id)).ToArray(), "ability");
        GameData s = TestSim.Data;
        foreach (TechDef t in d.Techs)
        {
            Assert.Equal(t.Id, d.FindTech(t.Key));
            foreach (int r in t.RequiresTechs) Assert.Contains(d.Techs[r].Faction, new[] { -1, t.Faction });
            int shipped = s.FindTech(t.Key);
            if (shipped < 0) continue;
            // The same tech, renumbered only: its requires name the same keys.
            Assert.Equal(s.Techs[shipped].RequiresTechs.Select(r => s.Techs[r].Key).Order(StringComparer.Ordinal),
                t.RequiresTechs.Select(r => d.Techs[r].Key).Order(StringComparer.Ordinal));
            Assert.Equal(s.Techs[shipped].Faction < 0 ? "" : s.Factions[s.Techs[shipped].Faction].Key, t.Faction < 0 ? "" : d.Factions[t.Faction].Key);
        }
        Assert.All(d.AgeTechs, t => Assert.Equal(-1, d.Techs[t].Faction));
        Assert.Equal(s.AgeTechs.Select(t => s.Techs[t].Key), d.AgeTechs.Select(t => d.Techs[t].Key));
        foreach (AbilityDef a in d.Abilities) Assert.Equal(a.Id, d.FindAbility(a.Key));
        foreach (UnitDef u in d.Units)
        {
            foreach (int a in u.Abilities) Assert.Equal(u.Faction, d.Abilities[a].Faction);
            int shipped = s.FindUnit(u.Key);
            if (shipped >= 0) Assert.Equal(s.Units[shipped].Abilities.Select(a => s.Abilities[a].Key), u.Abilities.Select(a => d.Abilities[a].Key));
        }
        // The common techs come first whatever the order, so their ids never move.
        foreach (TechDef t in s.Techs)
            if (t.Faction < 0) Assert.Equal(t.Id, d.FindTech(t.Key));
        Assert.Equal(d.ContentHash(), LoadWith(order, teblor, abilityPrefix: "a_", tech: "aa_supply").ContentHash());
        output.WriteLine($"{list}: {d.Units.Length} unit types, {d.Buildings.Length} buildings, hash {d.ContentHash():X16}");
    }

    private static void CheckGrouped((string Key, int Faction, int Id)[] rows, string what)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            Assert.Equal(i, rows[i].Id);
            if (i == 0) continue;
            (string pk, int pf, _) = rows[i - 1];
            (string k, int f, _) = rows[i];
            Assert.True(pf < f || pf == f && string.CompareOrdinal(pk, k) < 0, $"{what} {i - 1} '{pk}' (faction {pf}) before {i} '{k}' (faction {f})");
        }
    }

    /// <summary>A middle insert renumbers exactly the factions after it: Malazan keeps every id, Whirlwind moves by Teblor's count.</summary>
    [Fact]
    public void ATeblorInTheMiddle_KeepsMalazanAndMovesWhirlwind()
    {
        GameData d = LoadWith(new[] { "malazan", "teblor", "whirlwind" }, teblor: true);
        GameData s = TestSim.Data;
        int teblorUnits = d.Factions[1].Units.Length, teblorBuildings = d.Buildings.Count(b => b.Faction == 1);
        foreach (UnitDef u in s.Units)
            Assert.Equal(u.Id + (u.Faction == 0 ? 0 : teblorUnits), d.FindUnit(u.Key));
        foreach (BuildingDef b in s.Buildings)
            Assert.Equal(b.Id + (b.Faction == 0 ? 0 : teblorBuildings), d.FindBuilding(b.Key));
    }

    // ------------------------------------------------------------------ the error rows

    /// <summary>Order-file shapes the developer's rows don't cover; each the exact error count at the order file.</summary>
    [Theory]
    [InlineData("{\"order\": null}", 1, "order")]
    [InlineData("{\"order\": \"malazan\"}", 1, "$.order")]
    [InlineData("{\"order\": []}", 2, "order")] // both folders unlisted: one each
    [InlineData("{\"order\": [null, \"whirlwind\"]}", 2, "order")] // the null entry, and malazan unlisted
    [InlineData("{\"order\": [\"malazan\", \"whirlwind\", \"whirlwind\", \"whirlwind\"]}", 2, "order[")] // one per repeat
    [InlineData("{\"order\": [\"malazan\", \"whirlwind\", \"\"]}", 1, "order[2]")]
    [InlineData("{\"order\": [\"malazan\", \"whirlwind\", \"andii\", \"teblor\"]}", 2, "order[")] // two unknowns, one each
    [InlineData("[\"malazan\", \"whirlwind\"]", 1, "$")]
    [InlineData("{\"order\": [\"malazan\", \"whirlwind\"], \"order\": [\"whirlwind\", \"malazan\"]}", 1, "")] // duplicate key: whatever the reader does, never silently a second order
    public void AnOrderFileShape_IsItsErrorCount_AllInTheOrderFile(string json, int errors, string pathStart)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath(OrderFile), json);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        foreach (DataError e in r.Errors) output.WriteLine(e.ToString());
        if (json.Contains("\"order\": [\"whirlwind\", \"malazan\"]") && r.Ok)
        {
            // System.Text.Json takes the last duplicate: if it loads, it must be the order it says (not silently the first).
            Assert.Equal(new[] { "whirlwind", "malazan" }, r.Data!.Factions.Select(f => f.Key));
            return;
        }
        Assert.False(r.Ok);
        Assert.Equal(errors, r.Errors.Count);
        Assert.All(r.Errors, e => Assert.Equal(OrderFile, e.File));
        Assert.All(r.Errors, e => Assert.StartsWith(pathStart, e.Path));
    }

    /// <summary>An unlisted folder and an entry without a folder at the same time: two errors, naming each.</summary>
    [Fact]
    public void AnUnlistedFolder_AndAnUnknownEntry_AreTwoErrors()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        AddFullTeblor(dir, new[] { "malazan", "whirlwind", "andii" });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        foreach (DataError e in r.Errors) output.WriteLine(e.ToString());
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Path == "order[2]" && e.Message.Contains("'andii'"));
        Assert.Contains(r.Errors, e => e.Path == "order" && e.Message.Contains("factions/teblor/"));
    }

    /// <summary>Exactly <see cref="DataLimits.MaxFactions"/> real factions (six Malazan copies added) load; the ids of the first two stay pinned.</summary>
    [Fact]
    public void EightRealFactions_Load_AndTheShippedIdsHold()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        var order = new List<string> { "malazan", "whirlwind" };
        for (int k = 0; k < DataLimits.MaxFactions - 2; k++)
        {
            string id = $"copy{k}";
            string to = dir.FullPath($"factions/{id}");
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(dir.FullPath("factions/malazan")))
            {
                string text = File.ReadAllText(file).Replace("malazan_", id + "_").Replace("moranth_supply", id + "_supply")
                    .Replace("\"telas_fire\"", $"\"z{id}_telas_fire\"").Replace("\"cusser\"", $"\"z{id}_cusser\"");
                File.WriteAllText(Path.Combine(to, Path.GetFileName(file)), text);
            }
            dir.EditJson($"factions/{id}/faction.json", root => root["id"] = id);
            order.Add(id);
        }
        WriteOrder(dir, order.ToArray());
        GameData d = Load(dir);
        Assert.Equal(DataLimits.MaxFactions, d.Factions.Length);
        foreach (UnitDef u in TestSim.Data.Units) Assert.Equal(u.Id, d.FindUnit(u.Key));
        foreach (BuildingDef b in TestSim.Data.Buildings) Assert.Equal(b.Id, d.FindBuilding(b.Key));
        // A ninth real folder is refused at "order" (the limit), whatever else.
        Directory.CreateDirectory(dir.FullPath("factions/copy9"));
        foreach (string file in Directory.GetFiles(dir.FullPath("factions/copy0")))
            File.WriteAllText(Path.Combine(dir.FullPath("factions/copy9"), Path.GetFileName(file)), File.ReadAllText(file).Replace("copy0", "copy9"));
        order.Add("copy9");
        WriteOrder(dir, order.ToArray());
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == OrderFile && e.Path == "order" && e.Message.Contains("above the maximum"));
    }

    // ------------------------------------------------------------------ BUG-1171 (techs and abilities)

    /// <summary>
    /// BUG-1171's symptom, pinned so its fix shows: a full Teblor whose abilities are named <c>teblor_*</c> (the natural name)
    /// renumbered the shipped <c>telas_fire</c> even appended last; with <c>zteblor_*</c> it didn't. M7-3b fixed it: the
    /// first assertion is flipped (no shipped ability moves, whatever the Teblor names).
    /// </summary>
    [Fact]
    public void ATeblorAbility_NamedNaturally_RenumbersTelasFire_Bug1171Symptom()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        AddFullTeblor(dir, new[] { "malazan", "whirlwind", "teblor" }, abilityPrefix: "teblor_");
        GameData d = Load(dir);
        int shipped = TestSim.Data.FindAbility("telas_fire");
        Assert.Equal(shipped, d.FindAbility("telas_fire"));
        foreach (AbilityDef a in TestSim.Data.Abilities) Assert.Equal(a.Id, d.FindAbility(a.Key));
        Assert.Equal(shipped, s_full.Value.FindAbility("telas_fire"));
        Assert.Equal(TestSim.Data.FindAbility("cusser"), s_full.Value.FindAbility("cusser"));
    }

    // ------------------------------------------------------------------ a Teblor AI seat plays

    private static void Invariants(Simulation sim, string at)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        float max = Math.Max(w.NavGrid.Width, w.NavGrid.Height) * Rts.Sim.Map.MapConstants.CellSize;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2Check(u.Position[i], max, $"{at}: unit {i}");
            int hpMax = w.Data.Units[u.TypeId[i]].Hp * 10; // techs may raise max hp; a loose sanity bound
            Assert.True(u.Hp[i] >= 1 && u.Hp[i] <= Math.Max(hpMax, 1_000_000), $"{at}: unit {i} hp {u.Hp[i]}");
        }
        for (int p = 0; p < w.Gold.Length; p++)
        {
            Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"{at}: player {p} resources {w.Gold[p]} / {w.Wood[p]}");
            Assert.True(w.HalfPop[p] >= 0, $"{at}: player {p} half-pop {w.HalfPop[p]}");
        }
    }

    private static void Vector2Check(System.Numerics.Vector2 v, float max, string at) =>
        Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && v.X >= 0 && v.Y >= 0 && v.X <= max && v.Y <= max, $"{at} at {v}");

    private static (ulong Hash, int TeblorCommands, int TeblorUnits, int Casts) PlayTeblor(GameData d, PlayerSeat[] seats, ulong seed, int ticks, bool check)
    {
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, data: d, raiders: 2, casters: 1);
        int teblor = d.FindFaction("teblor");
        var cmds = new int[seats.Length];
        var casts = new int[seats.Length];
        for (int p = 0; p < seats.Length; p++)
        {
            int pp = p;
            sim.AiOf(p)!.SentForTests = c => { cmds[pp]++; if (c.Kind == CommandKind.UseAbility) casts[pp]++; };
        }
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            if (check && t % 10 == 0) Invariants(sim, $"seed {seed} tick {sim.TickNumber}");
        }
        int tp = Array.FindIndex(seats, s => s.Faction == teblor);
        int units = 0;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == tp) units++;
        return (sim.StateHash(), cmds[tp], units, casts[tp]);
    }

    /// <summary>
    /// A full Teblor AI seat plays a three-seat free-for-all (Malazan Normal, Whirlwind Hard, Teblor Hard) and a Teblor v
    /// Malazan duel, 6,000 ticks each: the invariants every 10 ticks, the Teblor seat sends commands and keeps units, and
    /// the run hashes the same twice.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(3UL, 3)]
    [InlineData(11UL, 2)]
    public void AFullTeblorAiSeat_Plays_AndIsDeterministic(ulong seed, int players)
    {
        GameData d = s_full.Value;
        PlayerSeat[] seats = players == 3
            ? new PlayerSeat[] { new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal), new(d.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Hard), new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Hard) }
            : new PlayerSeat[] { new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Hard), new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal) };
        var a = PlayTeblor(d, seats, seed, 6000, check: true);
        var b = PlayTeblor(d, seats, seed, 6000, check: false);
        output.WriteLine($"seed {seed} {players} seats: hash {a.Hash:X16}, teblor commands {a.TeblorCommands}, units {a.TeblorUnits}, casts {a.Casts}");
        Assert.Equal(a.Hash, b.Hash);
        Assert.True(a.TeblorCommands > 20, $"the Teblor seat sent {a.TeblorCommands} commands");
    }

    /// <summary>Process-level determinism probe: prints the hash of the 3-seat Teblor run for comparison across processes (run with RTS_QA_XPROC set).</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AFullTeblorThreeSeatRun_PrintsItsHash()
    {
        GameData d = s_full.Value;
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Hard), new(2, SeatController.Ai, AiDifficulty.Hard) };
        var a = PlayTeblor(d, seats, 5UL, 4000, check: false);
        string line = $"XPROC {a.Hash:X16} {d.ContentHash():X16}";
        output.WriteLine(line);
        string? file = Environment.GetEnvironmentVariable("RTS_QA_XPROC");
        if (file != null) File.AppendAllText(file, line + Environment.NewLine);
    }

    /// <summary>
    /// The Teblor copy is Malazan under other ids, so a Teblor seat must play exactly as a Malazan seat: Teblor (Hard) v
    /// Malazan (Normal) and Malazan v Malazan send the same commands (kind, tick, unit, position) on every tick, the
    /// trained / built type ids mapped by key. Proof that no system keys behaviour on a faction's id or position.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(3UL)]
    [InlineData(11UL)]
    public void ATeblorSeat_PlaysExactlyAsAMalazanSeat(ulong seed)
    {
        GameData d = s_full.Value;
        List<string> Log(string first)
        {
            PlayerSeat[] seats = { new(d.FindFaction(first), SeatController.Ai, AiDifficulty.Hard), new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal) };
            Simulation sim = AiScenes.Generated(seats, seed, false, out _, data: d, raiders: 2, casters: 1);
            var log = new List<string>();
            for (int p = 0; p < 2; p++)
            {
                int pp = p;
                sim.AiOf(p)!.SentForTests = c =>
                {
                    string type = c.Kind switch
                    {
                        CommandKind.Train => d.Units[c.TypeId].Key.Replace("teblor_", "malazan_"),
                        CommandKind.Build => d.Buildings[c.TypeId].Key.Replace("teblor_", "malazan_"),
                        CommandKind.Research => d.Techs[c.TypeId].Key.Replace("teblor_supply", "moranth_supply"),
                        _ => c.TypeId.ToString(),
                    };
                    log.Add(FormattableString.Invariant($"{sim.TickNumber} p{pp} {c.Kind} {type} {c.Position.X:R},{c.Position.Y:R} {c.Unit.Index} {c.Target.Index}"));
                };
            }
            for (int t = 0; t < 6000; t++) sim.Tick();
            return log;
        }
        List<string> teblor = Log("teblor"), malazan = Log("malazan");
        output.WriteLine($"seed {seed}: {teblor.Count} commands");
        Assert.True(teblor.Count > 50);
        for (int i = 0; i < Math.Min(teblor.Count, malazan.Count); i++)
            if (teblor[i] != malazan[i]) Assert.Fail($"command {i}: teblor '{teblor[i]}' v malazan '{malazan[i]}'");
        Assert.Equal(malazan.Count, teblor.Count);
    }

    // ------------------------------------------------------------------ BUG-1168 worth edges

    /// <summary>A pack of 8 at 7 / 0 (each unit worth 0 / 0) and at 8 / 7 (1 / 0); a pack of 8 at 800 / 16 (100 / 2).</summary>
    [Theory]
    [InlineData(7, 0, 0, 0)]
    [InlineData(8, 7, 1, 0)]
    [InlineData(800, 16, 100, 2)]
    [InlineData(0, 0, 0, 0)]
    public void APackOfEight_WorthIsIntegerDivision(int gold, int wood, int unitGold, int unitWood)
    {
        GameData d = AiScenes.DataWith(dir =>
        {
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "8");
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "pop", "1");
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "cost", $"{{\"gold\": {gold}, \"wood\": {wood}}}");
        });
        UnitDef u = d.Units[d.FindUnit("malazan_heavy_infantry")];
        Assert.Equal((gold, wood, 8), (u.CostGold, u.CostWood, u.SpawnCount));
        Assert.Equal((unitGold, unitWood, unitGold + unitWood), (u.UnitCostGold, u.UnitCostWood, u.UnitWorth));
    }

    private static readonly Lazy<GameData> s_zeroDogs = new(() => AiScenes.DataWith(dir =>
    {
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "8");
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "pop", "1");
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "cost", "{\"gold\": 7, \"wood\": 0}");
    }));

    /// <summary>
    /// Zero-worth pack units (7 / 0 over 8) in a Normal AI's army with enemy raiders in its base: ArmySize counts 0, the
    /// army-value sample 0, the defense runs without a crash or a NaN, and every think after a warm-up allocates nothing.
    /// </summary>
    [Fact]
    public void ZeroWorthPackUnits_InTheArmy_AndUnderAttack_CountZero_AndNothingBreaks()
    {
        GameData d = s_zeroDogs.Value;
        int heavy = d.FindUnit("malazan_heavy_infantry");
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), d);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_garrison_keep"), 20 * AiScenes.FlatSize + 20, out _));
        for (int k = 0; k < 16; k++) CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(14 + k % 8, 14 + k / 8));
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 1, d.FindUnit("whirlwind_raider"), w.NavGrid.CellCenter(26 + k, 22));
        AiPlayer ai = sim.AiOf(0)!;
        for (int t = 0; t < 6 * ai.ThinkTicks; t++)
        {
            sim.Tick();
            Invariants(sim, $"tick {sim.TickNumber}");
        }
        ai.ArmySize(out int half, out int cost);
        output.WriteLine($"army half {half}, cost {cost}; defenses {ai.Board.Defenses}, defending {ai.Board.Defending}, intruder worth {ai.IntruderWorth}");
        Assert.Equal(0, cost);
        Assert.True(half > 0);
        int before = w.Ledger.Stats.SampleCount;
        w.Ledger.Stats.Sample(w.Units, d);
        Assert.Equal(0, w.Ledger.Stats.Samples(0)[before]);
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        AllocationProbe.AssertZero(() => ai.Think(), output);
    }

    /// <summary>0 B a think with packs queued (Heavy Infantry a pack of 3, a barracks with a queue the AI fills).</summary>
    [Fact]
    public void AThink_WithPacksQueued_AllocatesNothing()
    {
        GameData d = AiScenes.DataWith(dir =>
        {
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
            dir.EditJson("factions/malazan/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                    o!["buildOrder"] = AiScenes.BuildOrder(("time", 0, "train", "malazan_heavy_infantry", 5));
            });
        });
        int heavy = d.FindUnit("malazan_heavy_infantry");
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), d);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 40 * AiScenes.FlatSize + 12, out EntityHandle barracks));
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        AiPlayer ai = sim.AiOf(0)!;
        for (int t = 0; t < 400 && ai.Queued[heavy] < 6; t++) sim.Tick();
        Assert.True(ai.Queued[heavy] >= 6, $"queued {ai.Queued[heavy]}");
        Assert.Equal(0, ai.Queued[heavy] % 3);
        AllocationProbe.AssertZero(() => ai.Think(), output);
        Assert.True(w.Buildings.QueueCount[barracks.Index] > 0);
    }

    // ------------------------------------------------------------------ BUG-1167 hard-cap edges

    /// <summary>
    /// The check reads rules.popCap, not a constant: with popCap 20 a pop 20 unit loads and a pop 20.5 one doesn't; a pack of
    /// 8 x 2.5 (20) loads and 8 x 3 (24) doesn't; and with popCap 2 every shipped unit above it is one error each, no more.
    /// </summary>
    [Theory]
    [InlineData(20, "20", null, true)]
    [InlineData(20, "20.5", null, false)]
    [InlineData(20, "2.5", "8", true)]
    [InlineData(20, "3", "8", false)]
    [InlineData(100, "0.5", "8", true)]
    public void TheCapCheck_ReadsRulesPopCap(int popCap, string pop, string? spawnCount, bool ok)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["popCap"] = popCap);
        dir.SetUnitField("malazan", "malazan_crossbowman", "pop", pop);
        if (spawnCount != null) dir.SetUnitField("malazan", "malazan_crossbowman", "spawnCount", spawnCount);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        foreach (DataError e in r.Errors) output.WriteLine(e.ToString());
        if (ok)
        {
            Assert.True(r.Ok);
            return;
        }
        DataError one = Assert.Single(r.Errors);
        Assert.EndsWith(spawnCount == null ? ".pop" : ".spawnCount", one.Path);
        Assert.Contains($"popCap {popCap}", one.Message);
    }

    [Fact]
    public void ATinyPopCap_IsOneErrorPerUnitAboveIt()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["popCap"] = 1);
        int above = TestSim.Data.Units.Count(u => u.HalfPop * u.SpawnCount > 2);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        foreach (DataError e in r.Errors) output.WriteLine(e.ToString());
        Assert.Equal(above, r.Errors.Count(e => e.Message.Contains("could never start")));
        // The only other error is the existing rules check (ai.houseWhenPopWithin 2 above the cap 1).
        Assert.Equal(above + 1, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Path == "ai.houseWhenPopWithin");
    }

    [Fact]
    public void ABrokenPopCap_IsReportedOnce_NotAtEveryUnit()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["popCap"] = -5);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        foreach (DataError e in r.Errors) output.WriteLine(e.ToString());
        Assert.DoesNotContain(r.Errors, e => e.Message.Contains("could never start"));
        Assert.Single(r.Errors);
    }
}
