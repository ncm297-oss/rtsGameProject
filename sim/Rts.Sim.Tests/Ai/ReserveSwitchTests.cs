using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-8 (Request 55, docs/03 "Implementation (M7-8)"): a build-order step's two switches on a staged seat, each beside its
/// twin without the switch. <c>throughReserve</c> on a train step: the step is paid from the whole bank, through an earlier
/// step's reserve; production outside the build order still spends only above the reserve. <c>noReserve</c> on a build /
/// research / expand step: never the think's reserved step (the next reservable step may be), still sent once the bank
/// covers it. The scenes are <see cref="ReserveTests"/>': a Hard Malazan seat (0.5 s thinks, no slack) against a human seat
/// on the flat map with a finished Barracks and Crossbow Range, every Malazan opening given the test's order, the reserve
/// gate open from the start (<c>ai.reserveArmyShare</c> 0), the purse set just before a think.
/// </summary>
public class ReserveSwitchTests
{
    private readonly ITestOutputHelper _out;

    public ReserveSwitchTests(ITestOutputHelper output) => _out = output;

    /// <summary>A build-order step as ai.json writes it, with the M7-8 switches written only when asked (true or false).</summary>
    internal static JsonObject Step(string trigger, double at, string what, string? id, int count = 0, bool? through = null, bool? noReserve = null)
    {
        var step = new JsonObject { ["at"] = new JsonObject { [trigger] = at }, ["do"] = what };
        if (id != null) step["id"] = id;
        if (count > 0) step["count"] = count;
        if (through != null) step["throughReserve"] = through.Value;
        if (noReserve != null) step["noReserve"] = noReserve.Value;
        return step;
    }

    /// <summary>
    /// Shipped data with every Malazan opening's build order <paramref name="steps"/>, Heavy Infantry alone in its compositions,
    /// the scout home, the reserve gate open; with <paramref name="expandNow"/> every difficulty's <c>expandAfter</c> 0.
    /// Set, not added: a copy of data already carrying the switches is the same copy (rule 58).
    /// </summary>
    internal static GameData Data(bool expandNow, params JsonObject[] steps) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                var order = new JsonArray();
                foreach (JsonObject s in steps) order.Add(s.DeepClone());
                o!["buildOrder"] = order;
                o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_heavy_infantry": 1 } }, { "age": 2, "units": { "malazan_heavy_infantry": 1 } }]""");
            }
            if (expandNow) foreach (string k in new[] { "easy", "normal", "hard" }) root["difficulties"]![k]!["expandAfter"] = 0;
        });
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["scoutAfter"] = 3600;
            r["ai"]!["reserveArmyShare"] = 0;
        });
    });

    /// <summary>The flat scene with a finished Barracks and Crossbow Range for player 0; with <paramref name="expansionMine"/> the middle mine an expand step builds by.</summary>
    internal static Simulation Scene(GameData data, bool expansionMine = false)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), data, expansionMine: expansionMine);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 16, out _));
        return sim;
    }

    /// <summary>A think's reserve and what production may spend, as the seam saw them at the think's end.</summary>
    internal sealed record Seen(int Step, int Gold, int Wood, float Share, bool InfantryAboveReserve);

    /// <summary>Runs to the next think with player 0's purse set to <paramref name="gold"/> / <paramref name="wood"/> just before it; the commands it sent and what it saw.</summary>
    internal static (List<Command> Sent, Seen Seen) Think(Simulation sim, List<Command> log, int gold, int wood)
    {
        AiPlayer ai = sim.AiOf(0)!;
        Seen? seen = null;
        int infantry = sim.World.Data.FindUnit("malazan_heavy_infantry");
        UnitDef hi = sim.World.Data.Units[infantry];
        ai.ThoughtForTests = a => seen = new Seen(a.ReserveStep, a.ReserveGold, a.ReserveWood, a.WantedGoldShare(), a.CanAffordAboveReserve(hi.CostGold, hi.CostWood));
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        BuildMaps.SetTotals(sim, 0, gold, wood);
        int before = log.Count;
        sim.Tick();
        Assert.NotNull(seen);
        return (log.Skip(before).ToList(), seen!);
    }

    private static int Unit(GameData d, string key) => d.FindUnit(key);

    // ---------- throughReserve ----------

    private static readonly Lazy<GameData>[] s_through =
    {
        new(() => Data(false, Step("time", 0, "build", "malazan_armory"), Step("time", 0, "train", "malazan_crossbowman", 1))),
        new(() => Data(false, Step("time", 0, "build", "malazan_armory"), Step("time", 0, "train", "malazan_crossbowman", 1, through: true))),
        new(() => Data(true, Step("time", 0, "expand", null), Step("time", 0, "train", "malazan_crossbowman", 1))),
        new(() => Data(true, Step("time", 0, "expand", null), Step("time", 0, "train", "malazan_crossbowman", 1, through: true))),
    };

    /// <summary>
    /// Criterion 5. A build step (the Armory, 100 / 100) or an expand step (a Keep, 275 / 275) holds the reserve; the train
    /// step after it (a Crossbowman, 36 / 45) is due and the bank pays it but not above the reserve (90 / 90 against the
    /// Armory, 250 / 250 against the Keep). Without the switch it is not sent; with <c>throughReserve</c> it is sent the same
    /// think. Either way the reserve stands to the think's end and production's Heavy Infantry (54 / 20) does not fit above
    /// it, so production outside the build order trains nothing.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AThroughReserveTrainStep_SpendsThroughTheReserve_ProductionStillDoesNot(bool expand, bool through)
    {
        GameData d = s_through[(expand ? 2 : 0) + (through ? 1 : 0)].Value;
        Assert.Equal(through, d.Ai[d.FindFaction("malazan")].Openings[0].BuildOrder[1].ThroughReserve);
        Simulation sim = Scene(d, expansionMine: expand);
        List<Command> log = AiScenes.Log(sim, 0);
        int bank = expand ? 250 : 90;
        (List<Command> sent, Seen seen) = Think(sim, log, bank, bank);
        Assert.Equal(0, seen.Step);
        Assert.Equal(expand ? (275, 275) : (100, 100), (seen.Gold, seen.Wood));
        Assert.False(seen.InfantryAboveReserve);
        int crossbow = Unit(d, "malazan_crossbowman"), infantry = Unit(d, "malazan_heavy_infantry");
        Assert.Equal(through, sent.Any(c => c.Kind == CommandKind.Train && c.TypeId == crossbow));
        Assert.DoesNotContain(sent, c => c.Kind == CommandKind.Train && c.TypeId == infantry);
        Assert.DoesNotContain(sent, c => c.Kind == CommandKind.Build);
    }

    /// <summary>
    /// A <c>throughReserve</c> train step before the reserved step (nothing reserved yet when it acts) and with no reserve at
    /// all spends as before: the switch only matters with an earlier reserved step. Both twins send the Crossbowman.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATrainStepBeforeTheReservedStep_IsPaidFromTheWholeBank_EitherWay(bool through)
    {
        GameData d = Data(false, Step("time", 0, "train", "malazan_crossbowman", 1, through: through ? true : null), Step("time", 0, "build", "malazan_armory"));
        Simulation sim = Scene(d);
        List<Command> log = AiScenes.Log(sim, 0);
        (List<Command> sent, Seen seen) = Think(sim, log, 90, 90);
        Assert.Equal(1, seen.Step);
        Assert.Contains(sent, c => c.Kind == CommandKind.Train && c.TypeId == Unit(d, "malazan_crossbowman"));
    }

    // ---------- noReserve ----------

    private static readonly Lazy<GameData> s_noReserveAlone = new(() =>
        Data(false, Step("time", 0, "build", "malazan_armory", noReserve: true), Step("time", 0, "train", "malazan_crossbowman", 1)));

    private static readonly Lazy<GameData> s_reserveAlone = new(() =>
        Data(false, Step("time", 0, "build", "malazan_armory"), Step("time", 0, "train", "malazan_crossbowman", 1)));

    /// <summary>
    /// Criterion 6, no step behind it. The Armory (100 / 100) with <c>noReserve</c>, the bank 60 / 60: no reserved step, the
    /// reserve 0 / 0, the gather share <c>ai.goldShare</c> unshifted, and the later Crossbowman step (36 / 45) is paid from
    /// the whole bank. The twin without the switch reserves the Armory and holds the Crossbowman.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ANoReserveStep_IsNeverTheReservedStep_ALaterTrainStepIsPaidFromTheWholeBank(bool noReserve)
    {
        GameData d = (noReserve ? s_noReserveAlone : s_reserveAlone).Value;
        Assert.Equal(noReserve, d.Ai[d.FindFaction("malazan")].Openings[0].BuildOrder[0].NoReserve);
        Simulation sim = Scene(d);
        List<Command> log = AiScenes.Log(sim, 0);
        (List<Command> sent, Seen seen) = Think(sim, log, 60, 60);
        bool crossbow = sent.Any(c => c.Kind == CommandKind.Train && c.TypeId == Unit(d, "malazan_crossbowman"));
        if (noReserve)
        {
            Assert.Equal((-1, 0, 0), (seen.Step, seen.Gold, seen.Wood));
            Assert.Equal(d.AiRules.GoldShare, seen.Share);
            Assert.True(crossbow);
        }
        else
        {
            Assert.Equal((0, 100, 100), (seen.Step, seen.Gold, seen.Wood));
            Assert.False(crossbow);
        }
        Assert.DoesNotContain(sent, c => c.Kind == CommandKind.Build);
    }

    /// <summary>
    /// Criterion 6, a second unpaid reservable step behind it: the Armory with <c>noReserve</c>, then Age II (400 / 200;
    /// the scene's Barracks and Crossbow Range meet its requires), the bank 60 / 60 over several thinks: the reserved step
    /// is Age II's (step 1) at every think, never the Armory's.
    /// </summary>
    [Fact]
    public void BehindANoReserveStep_TheNextReservableStepIsReserved()
    {
        GameData d = Data(false, Step("time", 0, "build", "malazan_armory", noReserve: true), Step("time", 0, "research", "age_ii"));
        Simulation sim = Scene(d);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 5; k++)
        {
            (_, Seen seen) = Think(sim, log, 60, 60);
            Assert.Equal((1, 400, 200), (seen.Step, seen.Gold, seen.Wood));
        }
    }

    /// <summary>
    /// Criterion 6, the switched step is still sent: short (60 / 60) it waits unreserved; at the first think whose bank covers
    /// it (300 / 300) and that has a builder free it is sent, at the same think as its twin without the switch (the workers
    /// walk to their nodes first: the first think with one free, as in <see cref="ReserveTests"/>).
    /// </summary>
    [Fact]
    public void ANoReserveStep_IsSentTheFirstThinkTheBankCoversIt_AsItsTwin()
    {
        int First(GameData d)
        {
            Simulation sim = Scene(d);
            List<Command> log = AiScenes.Log(sim, 0);
            Assert.DoesNotContain(Think(sim, log, 60, 60).Sent, c => c.Kind == CommandKind.Build);
            for (int k = 0; k < 40; k++)
                if (Think(sim, log, 300, 300).Sent.Any(c => c.Kind == CommandKind.Build && c.TypeId == d.FindBuilding("malazan_armory"))) return k;
            return -1;
        }
        int on = First(s_noReserveAlone.Value), off = First(s_reserveAlone.Value);
        _out.WriteLine($"the Armory sent at the 300 / 300 think {on} (switch on), {off} (off)");
        Assert.True(on >= 0, "the noReserve Armory was never sent");
        Assert.Equal(off, on);
    }

    /// <summary>
    /// <c>noReserve</c> on an expand step and on a research step: neither reserved (the reserve 0 / 0), each twin without the
    /// switch reserved at the same purse.
    /// </summary>
    [Theory]
    [InlineData("expand", true)]
    [InlineData("expand", false)]
    [InlineData("research", true)]
    [InlineData("research", false)]
    public void NoReserve_OnAnExpandOrResearchStep_KeepsItUnreserved(string what, bool noReserve)
    {
        bool expand = what == "expand";
        GameData d = Data(expand, Step("time", 0, what, expand ? null : "age_ii", noReserve: noReserve ? true : null));
        Simulation sim = Scene(d, expansionMine: expand);
        List<Command> log = AiScenes.Log(sim, 0);
        (_, Seen seen) = Think(sim, log, 60, 60);
        Assert.Equal(noReserve ? -1 : 0, seen.Step);
    }

    /// <summary>The allocation row, in the serial collection (BUG-1020).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// Criterion 11: a think over a build order with both switches (a noReserve Armory, a reserved Age II, a throughReserve
        /// Crossbowman step behind it that the bank pays, production held above the reserve) allocates nothing.
        /// </summary>
        [Fact]
        public void AThinkOverSwitchedSteps_AllocatesNothing()
        {
            GameData d = Data(false, Step("time", 0, "build", "malazan_armory", noReserve: true), Step("time", 0, "research", "age_ii"),
                Step("time", 0, "train", "malazan_crossbowman", 50, through: true));
            Simulation sim = Scene(d);
            AiPlayer ai = sim.AiOf(0)!;
            List<Command> log = AiScenes.Log(sim, 0);
            for (int k = 0; k < 10; k++)
            {
                (_, Seen seen) = Think(sim, log, 90, 90);
                Assert.Equal(1, seen.Step);
            }
            ai.ThoughtForTests = null;
            ai.SentForTests = null;
            while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
            BuildMaps.SetTotals(sim, 0, 90, 90);
            AllocationProbe.AssertZero(() => ai.Think(), _out);
        }
    }
}
