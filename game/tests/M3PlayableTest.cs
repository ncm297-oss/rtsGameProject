using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M3-V4: M3's "Playable" criterion as a script. The owner's ten-minute playtest (STATE "For your review", M3-V3) played
/// through the real HUD of a real <see cref="Match"/> (shipped data, 8x game speed, the sim ticked by its own
/// <see cref="SimRunner"/> from frame time), on seeds 1 and 6. Every action is injected input on the viewport
/// (<c>Viewport.PushInput</c>: mouse clicks, box drags, grid keys, minimap clicks to move the camera), never a sim command
/// or a controller call; each step is then checked against the sim, and the tick it completed at is printed. The tick
/// budget is fixed (<see cref="TickBudget"/>); no gold or wood is spawned (the economy pays for everything). At the end
/// the whole command stream the HUD sent is replayed into a bare twin sim (<see cref="ReplayPlayer"/>, a checkpoint every
/// tick): equal hashes prove the views and the HUD only read the sim.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/M3PlayableTest.tscn</c> (both seeds, about three minutes)
/// or <c>... -- --seed 6</c> for one. Prints "M3 PLAYABLE TEST PASS" and exits 0, else
/// "M3 PLAYABLE TEST FAIL &lt;step&gt;: &lt;what&gt;" and exits 1. <c>-- --break &lt;step number&gt;</c> makes that step expect the
/// wrong answer once, to show a failure names its step.
/// </remarks>
public partial class M3PlayableTest : Node
{
    /// <summary>Most sim ticks one seed may take (13 min 20 s of game time). After BUG-0145, ten runs (five under load): seed 1 11,871-11,997, seed 6 11,069-11,528, so at least 4,000 ticks (25 %) of margin.</summary>
    public const int TickBudget = 16000;

    private sealed class StepFailed : Exception
    {
        public StepFailed(string message) : base(message) { }
    }

    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private CommandCard _card = null!;
    private SelectionPanel _panel = null!;
    private ProductionQueueStrip _strip = null!;
    private RallyMarker _rally = null!;
    private ResourceBar _bar = null!;
    private Minimap _mini = null!;
    private BuildGhost _ghost = null!;

    private ulong _seed;
    private int _stepNo, _breakStep = -1;
    private string _step = "start";
    private bool _cursorOverride;
    private EntityHandle _builder;
    private int _mine, _retasked;
    private int _faction, _hall, _laborer, _infantry, _sapper, _age, _melee;
    private int _depotType;
    private int _barracksType, _armoryType, _billetType, _yardType, _towerType;
    private readonly List<EntityHandle> _starters = new();
    private readonly List<int> _taken = new();

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;
    private int Tick => _sim.TickNumber;

    public override async void _Ready()
    {
        string failure = "";
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new StepFailed("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new StepFailed("ui.json failed to load");
            var seeds = new List<ulong> { 1, 6 };
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--seed") seeds = new List<ulong> { ulong.Parse(args[i + 1]) };
                if (args[i] == "--break") _breakStep = int.Parse(args[i + 1]);
            }
            foreach (ulong seed in seeds) await PlaySeed(seed);
        }
        catch (StepFailed f)
        {
            failure = $"{_step} (seed {_seed}, tick {(_sim != null ? Tick : -1)}): {f.Message}";
            SaveReplay("fail");
        }
        catch (Exception ex)
        {
            // BUG-0148 item 4: every failure leaves a repro, not only a failed step.
            failure = $"{_step} (seed {_seed}, tick {(_sim != null ? Tick : -1)}): exception {ex}";
            try { SaveReplay("exception"); }
            catch (Exception save) { GD.Print($"  replay not saved: {save.Message}"); }
        }
        GD.Print(failure.Length == 0 ? "M3 PLAYABLE TEST PASS" : $"M3 PLAYABLE TEST FAIL {failure}");
        GetTree().Quit(failure.Length == 0 ? 0 : 1);
    }

    // ---- the script ----

    private async Task PlaySeed(ulong seed)
    {
        _seed = seed;
        _stepNo = 0;
        _step = "start";
        await StartMatch(seed);

        // 1. Box-select the five workers by the hall.
        Step("box-select the five workers");
        await LookAt(Centre(_hall));
        Vector2[] at = _starters.Select(h => ScreenOf(h.Index)).ToArray();
        await BoxDrag(at);
        Expect(_sel.Selection.Count == 5 && _starters.All(h => _sel.Selection.Contains(h)), $"selection {_sel.Selection.Count}, not the five workers");
        Done();

        // 2. Right-click the nearest gold mine: all five gather it.
        Step("right-click the nearest mine");
        int mine = _mine = NearestNode(ResourceKind.Gold, Centre(_hall));
        EntityHandle mineHandle = new(mine, W.Resources.Generation[mine]);
        await LookAt((Centre(_hall) + NodeCentre(mine)) / 2f);
        await RightClick(Screen(NodeCentre(mine), 1.6f));
        await Ticks(2);
        Expect(_starters.All(h => U.GatherNode[h.Index] == mineHandle), "not every worker's gather node is the mine");
        var gathered = new HashSet<int>();
        await Until(() =>
        {
            foreach (EntityHandle h in _starters) if (U.State[h.Index] == UnitState.Gathering) gathered.Add(h.Index);
            return gathered.Count == 5;
        }, "all five Gathering");
        Done();

        // 3. Click the Town Hall: its panel and card.
        Step("click the Town Hall");
        await SelectBuildingByClick(_hall);
        BuildingDef hallDef = _data.Buildings[B.TypeId[_hall]];
        Expect(_panel.NameLabel.Text == hallDef.DisplayName && _panel.HpText == $"{B.Hp[_hall]} / {hallDef.Hp}", $"panel '{_panel.NameLabel.Text}' '{_panel.HpText}'");
        Expect(_card.ActionAt(0) == CardCommand.Train && _card.TypeAt(0) == _laborer && _card.HintAt(0).Text == "Q" && _card.NameAt(0).Text == _data.Units[_laborer].DisplayName,
            $"cell Q: {_card.ActionAt(0)} {_card.TypeAt(0)} '{_card.HintAt(0).Text}'");
        W.CanResearch(0, _hall, _age, out ResearchError ageWhy);
        Expect(_card.ActionAt(1) == CardCommand.Research && _card.TypeAt(1) == _age && _card.HintAt(1).Text == "W" && _card.ButtonAt(1).Disabled
            && _card.CostAt(1).Text == _ui.ResearchText(ResearchError.Requires) && ageWhy == ResearchError.Requires,
            $"cell W: {_card.ActionAt(1)} {_card.TypeAt(1)} disabled {_card.ButtonAt(1).Disabled} '{_card.CostAt(1).Text}', sim {ageWhy}");
        Done();

        // 4. Q three times: three laborers queued, three squares, the head's bar filling.
        Step("Q three times");
        int gold0 = W.Gold[0];
        for (int k = 0; k < 3; k++) Key(Godot.Key.Q);
        await Ticks(2);
        int laborerCost = _data.Units[_laborer].CostGold;
        Expect(B.QueueCount[_hall] == 3 && W.Gold[0] == gold0 - 3 * laborerCost, $"queue {B.QueueCount[_hall]}, gold {gold0} -> {W.Gold[0]}");
        await Until(() => _strip.ShownCount == 3 && _strip.ShownFill > 0f && _strip.HeadBar.Size.X > 0f, "three squares and a head bar");
        Expect(Enumerable.Range(0, 3).All(k => _strip.ShownType(k) == _laborer && !_strip.ShownIsTech(k)), "the squares are not three laborers");
        Done();

        // 5. Click the third square: cancelled, its gold back.
        Step("click the third square");
        int gold1 = W.Gold[0];
        await LeftClick(_strip.ItemButton(2).GetGlobalRect().GetCenter());
        await Ticks(2);
        Expect(B.QueueCount[_hall] == 2 && W.Gold[0] == gold1 + laborerCost, $"queue {B.QueueCount[_hall]}, gold {gold1} -> {W.Gold[0]} (want +{laborerCost})");
        await Until(() => _strip.ShownCount == 2, "two squares");
        Done();

        // 6. Right-click a forest with the hall selected: the rally flag; the next laborer walks there and chops.
        Step("right-click a forest (rally)");
        int tree = NearestNode(ResourceKind.Wood, Centre(_hall));
        await LookAt(NodeCentre(tree));
        var oldLaborers = new HashSet<int>(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i] && U.TypeId[i] == _laborer));
        await RightClick(Screen(NodeCentre(tree), 2f));
        await Ticks(2);
        Expect(B.HasRally[_hall] && System.Numerics.Vector2.Distance(B.RallyPosition[_hall], NodeCentre(tree)) < 0.01f,
            $"rally {B.HasRally[_hall]} at {B.RallyPosition[_hall]}, tree at {NodeCentre(tree)}");
        await Until(() => _rally.ShownSlot == _hall, "the flag shown");
        // Two more laborers for wood with the gold the cancel gave back (the economy has to pay for the whole script).
        int queued = B.QueueCount[_hall], more = Math.Min(2, W.Gold[0] / laborerCost);
        for (int k = 0; k < more; k++) Key(Godot.Key.Q);
        await Ticks(2);
        Expect(B.QueueCount[_hall] >= queued + more - 1, $"queue {queued} -> {B.QueueCount[_hall]} after {more} more Q");
        int laborers = await TrainedAndChopping(oldLaborers, tree);
        GD.Print($"  {laborers} laborers born after the rally walked to the forest and gathered wood");
        Done();

        // 7. Workers: B, E, click on green: a Legion Barracks placed and built.
        Step("B, E: Legion Barracks");
        int barracks = await PlaceWithWorkers(BuildingSlot.InfantryHall, advanced: false, Godot.Key.E, _barracksType);
        Done();

        // 7b. B, W: a Quartermaster's Depot by the forest, and one by the mine when it is far from the hall (drop-offs: the
        // economy has to pay for the whole script; on seed 6 both are a long walk from the hall).
        Step("B, W: Depot by the forest");
        await PlaceWithWorkers(BuildingSlot.Camp, advanced: false, Godot.Key.W, _depotType, NodeCentre(tree), 3f, 14f);
        Done();
        float mineDistance = System.Numerics.Vector2.Distance(NodeCentre(mine), Centre(_hall));
        if (mineDistance > 35f)
        {
            Step($"B, W: Depot by the mine ({mineDistance:0} m from the hall)");
            await PlaceWithWorkers(BuildingSlot.Camp, advanced: false, Godot.Key.W, _depotType, NodeCentre(mine), 3f, 12f);
            Done();
        }

        // 8. B, A: the Armory.
        Step("B, A: Armory");
        int armory = await PlaceWithWorkers(BuildingSlot.Forge, advanced: false, Godot.Key.A, _armoryType);
        Done();

        // 9. B, Q: a Billet; the cap goes from 10 to 18 in the resource bar.
        Step("B, Q: Billet, cap 10 -> 18");
        int cap0 = W.HalfPopCap[0];
        Expect(_bar.ShownHalfPopCap == cap0 && _bar.PopLabel.Text.EndsWith($"/ {PopText.Format(cap0)}"), $"bar '{_bar.PopLabel.Text}' before, cap {cap0}");
        await PlaceWithWorkers(BuildingSlot.House, advanced: false, Godot.Key.Q, _billetType);
        await Ticks(1);
        int cap1 = W.HalfPopCap[0];
        Expect(cap1 == cap0 + _data.Buildings[_billetType].HalfPopProvided && _bar.ShownHalfPopCap == cap1 && _bar.PopLabel.Text.EndsWith($"/ {PopText.Format(cap1)}"),
            $"cap {PopText.Format(cap0)} -> {PopText.Format(cap1)}, bar '{_bar.PopLabel.Text}'");
        GD.Print($"  pop cap {PopText.Format(cap0)} -> {PopText.Format(cap1)} ('{_bar.PopLabel.Text}')");
        // Room for more: two laborers (they follow the rally to the forest) when the gold is there.
        await SelectBuildingByClick(_hall);
        int extra = Math.Min(2, W.Gold[0] / laborerCost), q0 = B.QueueCount[_hall];
        for (int k = 0; k < extra; k++) Key(Godot.Key.Q);
        await Ticks(2);
        Expect(B.QueueCount[_hall] >= q0 + extra - 1, $"queue {q0} -> {B.QueueCount[_hall]} after {extra} Q");
        GD.Print($"  {extra} more laborers queued");
        Done();

        // 10. Barracks and Armory finished: Age II is live at the hall; W researches it; "Age II" flashes.
        Step("Age II live, W");
        await SelectBuildingByClick(_hall);
        Expect(_card.TypeAt(1) == _age && _card.ReasonAt(1) != (int)ResearchError.Requires, $"Age II still '{_card.CostAt(1).Text}' with the Barracks and Armory up");
        await UntilAfford(_data.Techs[_age].CostGold, _data.Techs[_age].CostWood, "Age II's cost");
        await Until(() => _card.ReasonAt(1) == 0 && !_card.ButtonAt(1).Disabled, "Age II's button live");
        Key(Godot.Key.W);
        await Ticks(2);
        Expect(ProductionMenu.IsTechQueued(B, 0, _age), "Age II not queued after W");
        await Until(() => Enumerable.Range(0, _strip.ShownCount).Any(k => _strip.ShownIsTech(k) && _strip.ShownType(k) == _age), "Age II in the queue strip");
        Done();

        // 11. While it researches: V, W shows the Engineers' Yard greyed "Locked", its ghost red "Locked", nothing placed.
        Step("V, W before Age II: Locked");
        await SelectBuilder();
        Key(Godot.Key.V);
        await Frames(2);
        int yardCell = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Place && _card.TypeAt(i) == _yardType, -1);
        int towerCell = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Place && _card.TypeAt(i) == _towerType, -1);
        string locked = _ui.PlacementText(PlacementError.Requires);
        Expect(_card.AdvancedMenuOpen && yardCell == 1 && towerCell == 0, $"advanced menu {_card.AdvancedMenuOpen}, yard on {yardCell}, tower on {towerCell}");
        foreach (int c in new[] { towerCell, yardCell })
            Expect(_card.ReasonAt(c) == (int)PlacementError.Requires && _card.CostAt(c).Text == locked && _card.NameAt(c).Modulate.A <= 0.6f,
                $"cell {c}: reason {_card.ReasonAt(c)} '{_card.CostAt(c).Text}' alpha {_card.NameAt(c).Modulate.A}");
        Key(Godot.Key.W);
        int spot = await HoverSpot(_yardType, ignoreRequires: true, Centre(_hall), 12f, 40f);
        await Until(() => _ghost.Active && _ghost.Anchor == spot, "the ghost on the spot");
        Expect(!_ghost.Valid && _ghost.Reason == PlacementError.Requires && _ghost.ShownText == locked && _ghost.ReasonLabel.Text == locked && _ghost.Box.MaterialOverride == _ghost.RedMaterial,
            $"ghost valid {_ghost.Valid} reason {_ghost.Reason} '{_ghost.ShownText}'");
        int pending = _sim.PendingCommandCount;
        await LeftClick(FootprintScreen(spot, _yardType));
        Expect(_sim.PendingCommandCount == pending && _ghost.Active, $"a click on the Locked ghost enqueued {_sim.PendingCommandCount - pending}");
        Key(Godot.Key.Escape);
        await Frames(1);
        Expect(!_card.MenuOpen && !_ghost.Active, "Esc left the menu open");
        Done();

        // 12. Age II completes: "Age II" flashes in the bar.
        Step("Age II researched, flash");
        int flashes = _bar.AgeFlashes;
        await Until(() => W.Age(0) == 2, "Age 2");
        await Frames(1);
        Expect(_bar.AgeFlashes == flashes + 1 && _bar.AgeLabel.Visible && _bar.AgeLabel.Text == _data.Techs[_age].DisplayName, $"flash {_bar.AgeFlashes} visible {_bar.AgeLabel.Visible} '{_bar.AgeLabel.Text}'");
        Done();

        // 13. The Armory: Melee Weapons live, Melee Weapons II "Locked"; its grid key researches Melee Weapons. (The card
        // lists the common upgrades in id order: Armor Q, Armor II W, Melee Weapons E, Melee Weapons II R ...)
        Step("Armory: Melee Weapons");
        await SelectBuildingByClick(armory);
        int melee2 = _data.FindTech("melee_weapons_2");
        int meleeCell = CellOf(CardCommand.Research, _melee), melee2Cell = CellOf(CardCommand.Research, melee2);
        Expect(meleeCell >= 0 && _card.ReasonAt(meleeCell) is 0 or (int)ResearchError.CannotAfford,
            $"Melee Weapons on cell {meleeCell}, reason {(meleeCell >= 0 ? _card.ReasonAt(meleeCell) : -1)}");
        Expect(melee2Cell >= 0 && _card.ReasonAt(melee2Cell) == (int)ResearchError.Requires && _card.CostAt(melee2Cell).Text == _ui.ResearchText(ResearchError.Requires),
            $"Melee Weapons II on cell {melee2Cell}: reason {(melee2Cell >= 0 ? _card.ReasonAt(melee2Cell) : -1)} '{(melee2Cell >= 0 ? _card.CostAt(melee2Cell).Text : "")}'");
        await UntilAfford(_data.Techs[_melee].CostGold, _data.Techs[_melee].CostWood, "Melee Weapons' cost");
        await Until(() => _card.ReasonAt(meleeCell) == 0, "Melee Weapons' button live");
        Key(KeyOf(meleeCell));
        await Ticks(2);
        Expect(ProductionMenu.IsTechQueued(B, 0, _melee), $"Melee Weapons not queued after {_card.GridKey(meleeCell)}");
        GD.Print($"  Melee Weapons on {_card.GridKey(meleeCell)} queued; Melee Weapons II on {_card.GridKey(melee2Cell)} reads '{_card.CostAt(melee2Cell).Text}'");
        Done();

        // 14. V, W after Age II: the Engineers' Yard is live, placed on green and built.
        Step("V, W: Engineers' Yard");
        // BUG-0274: out to 60 m, since the explored ground within 40 m of the hall is already spaced out by the other
        // buildings on some seeds (seed 6); nearest first, so a seed with room nearer is unchanged.
        int yard = await PlaceWithWorkers(BuildingSlot.SiegeWorks, advanced: true, Godot.Key.W, _yardType, maxD: 60f);
        Done();

        // 15. A Heavy Infantry from the Barracks (Q), then a Sapper from the Yard (W): Age 2 and a live Sapper.
        Step("Barracks: Heavy Infantry");
        EntityHandle infantry = await TrainAt(barracks, _infantry);
        Done();
        Step("Engineers' Yard: Sapper");
        EntityHandle sapper = await TrainAt(yard, _sapper);
        Expect(W.Age(0) == 2 && U.Alive[sapper.Index], "no live Sapper in Age 2");
        Done();

        // 16. Select the Heavy Infantry: the panel reads its attack with the green "+1" once Melee Weapons is in.
        Step("Heavy Infantry panel: attack +1");
        await Until(() => W.HasTech(0, _melee), "Melee Weapons researched");
        Expect(U.Alive[infantry.Index] && U.Generation[infantry.Index] == infantry.Generation, "the Heavy Infantry died");
        await LookAt(U.Position[infantry.Index]);
        await LeftClick(ScreenOf(infantry.Index));
        await Frames(2);
        Expect(_sel.Selection.Count == 1 && _sel.Selection.Contains(infantry), $"selection {_sel.Selection.Count}, not the Heavy Infantry");
        Expect(_panel.NameLabel.Text == _data.Units[_infantry].DisplayName && _panel.StatBonus(1).Visible && _panel.StatBonus(1).Text == "+1"
            && W.TechBonus(0, _infantry, TechStat.Attack) == 1f,
            $"panel '{_panel.NameLabel.Text}' attack bonus visible {_panel.StatBonus(1).Visible} '{_panel.StatBonus(1).Text}'");
        // BUG-0148 item 2: the "+1" is drawn green (more green than red or blue), not merely present.
        Color bonus = _panel.StatBonus(1).GetThemeColor("font_color");
        Expect(bonus.G > 0.5f && bonus.G > bonus.R + 0.3f && bonus.G > bonus.B + 0.3f, $"the attack bonus is not green: {bonus}");
        Done();

        // The hash twin: the command stream the HUD sent, replayed into a bare sim, hashes equal every tick.
        Step("hash twin");
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Expect(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"  {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints (one a tick) equal in a bare twin");
        Done();
        GD.Print($"seed {seed}: Age {W.Age(0)}, Sapper {sapper.Index} alive, Heavy Infantry attack +1, {Tick} of {TickBudget} ticks, {_retasked} idle laborers re-tasked{(_cursorOverride ? " (ghost cursor via ScreenOverride: headless has no pointer)" : "")}; no gold or wood spawned");
        _match.QueueFree();
        await Frames(2);
    }

    // The recorded match so far as a replay file in the user data folder (a failure's repro: `Rts.Cli play <file>`).
    private void SaveReplay(string why)
    {
        if (_runner?.Recorder == null) return;
        string path = System.IO.Path.Combine(OS.GetUserDataDir(), $"m3playable-seed{_seed}-{why}-tick{Tick}.replay");
        ReplayFormat.WriteFile(_runner.Recorder.ToReplay(), path);
        GD.Print($"  replay saved: {path}");
    }

    // ---- composite steps ----

    // Waits until a laborer born after the rally (not in `known`) stands Gathering wood; returns how many were born by then.
    // BUG-0148 item 3: the wood it gathers is the rally tree or, if that was felled first, a tree of the rally forest: within
    // the sim's node search radius (rules.json nodeSearchRadius) of the rally tree, where a Gather on a felled tree resolves.
    private async Task<int> TrainedAndChopping(HashSet<int> known, int rallyTree)
    {
        System.Numerics.Vector2 rallyAt = NodeCentre(rallyTree);
        float reach = _data.Rules.NodeSearchRadius;
        int gatherer = -1, gathered = -1;
        var born = new List<int>();
        await Until(() =>
        {
            for (int i = 0; i < U.Capacity; i++)
                if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == _laborer && !known.Contains(i) && !born.Contains(i)) born.Add(i);
            foreach (int i in born)
            {
                EntityHandle node = U.GatherNode[i];
                if (U.State[i] == UnitState.Gathering && W.Resources.IsAlive(node) && _data.Resources[W.Resources.TypeId[node.Index]].Resource == ResourceKind.Wood)
                {
                    gatherer = i;
                    gathered = node.Index;
                    return true;
                }
            }
            return false;
        }, "a new laborer gathering wood");
        if (gathered >= 0)
        {
            float d = System.Numerics.Vector2.Distance(NodeCentre(gathered), rallyAt);
            Expect(gathered == rallyTree || d <= reach, $"laborer {gatherer} gathers tree {gathered}, {d:F1} m from the rally tree {rallyTree} (more than {reach} m: not the rally forest)");
            GD.Print($"  laborer {gatherer} gathers {(gathered == rallyTree ? "the rally tree" : $"tree {gathered}, {d:F1} m from the rally tree")}");
        }
        return born.Count;
    }

    // The builder (one start worker, clicked) selected: B (or V), the grid key, a green spot clicked; waits until the site is
    // finished, then right-clicks the mine with the builder so it mines until the next building.
    private async Task<int> PlaceWithWorkers(BuildingSlot slot, bool advanced, Godot.Key key, int type,
        System.Numerics.Vector2? near = null, float minD = 12f, float maxD = 40f)
    {
        BuildingDef def = _data.Buildings[type];
        await UntilAfford(def.CostGold, def.CostWood, $"{def.DisplayName}'s cost");
        await SelectBuilder();
        Key(advanced ? Godot.Key.V : Godot.Key.B);
        await Frames(1);
        Expect(_card.MenuOpen && _card.AdvancedMenuOpen == advanced, $"menu open {_card.MenuOpen}, advanced {_card.AdvancedMenuOpen}");
        int cell = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Place && _card.TypeAt(i) == type, -1);
        Expect(cell >= 0 && _card.GridKey(cell) == key.ToString(), $"{def.DisplayName} on cell {cell} ('{(cell >= 0 ? _card.GridKey(cell) : "")}'), want {key}");
        Expect(_card.ReasonAt(cell) == 0 && _card.CostAt(cell).Text == $"{def.CostGold} / {def.CostWood}" && slot == def.Slot,
            $"{def.DisplayName}'s button greyed: reason {_card.ReasonAt(cell)} '{_card.CostAt(cell).Text}'");
        Key(key);
        int spot = await HoverSpot(type, ignoreRequires: false, near ?? Centre(_hall), minD, maxD);
        await Until(() => _ghost.Active && _ghost.TypeId == type && _ghost.Anchor == spot && _ghost.Valid, "a green ghost on the spot",
            () => $"ghost active {_ghost.Active} type {_ghost.TypeId} anchor {_ghost.Anchor} (spot {spot}) valid {_ghost.Valid} reason {_ghost.Reason}");
        Expect(_ghost.Box.MaterialOverride == _ghost.GreenMaterial && _ghost.ShownText.Length == 0, "the ghost is not drawn green");
        await LeftClick(FootprintScreen(spot, type));
        Expect(!_card.MenuOpen && !_ghost.Active, "the menu stayed open after a green click");
        await Ticks(2);
        int site = B.SlotAt(spot % G.Width, spot / G.Width);
        Expect(site >= 0 && B.TypeId[site] == type && B.Cell[site] == spot && B.Owner[site] == 0, $"no {def.DisplayName} site at {spot} (slot {site})");
        _taken.Add(spot);
        GD.Print($"  {def.DisplayName} site placed at anchor {spot} on tick {Tick}");
        // Builds until finished. A site whose work stands still for 800 ticks gets another worker, as a player would: the
        // laborer nearest the site (not beside the stuck builder) clicked and right-clicked onto the site (a joining Build),
        // then the builder from here on. Logged with the stuck builder's state, and the replay saved: a walker wedged
        // against gatherers standing in a two-cell corridor is a sim movement bug (seen once in five two-seed runs).
        int lastWork = -1, stillSince = Tick;
        while (!(B.Alive[site] && !B.UnderConstruction[site]))
        {
            Expect(B.Alive[site], $"the {def.DisplayName} site vanished");
            if (Tick > TickBudget)
                throw new StepFailed($"{def.DisplayName} finished: not by tick {TickBudget}; work {B.Work[site]} / {B.WorkNeeded(type)}; builder {U.State[_builder.Index]} at {U.Position[_builder.Index]}, site centre {Centre(site)}; {Census()}");
            if (B.Work[site] != lastWork) (lastWork, stillSince) = (B.Work[site], Tick);
            else if (Tick - stillSince >= 800)
            {
                GD.Print($"  tick {Tick}: the {def.DisplayName} site stalled at work {B.Work[site]}; builder {U.State[_builder.Index]} at {U.Position[_builder.Index]} stuck");
                SaveReplay("stall");
                _builder = await SendAnotherBuilder(site);
                stillSince = Tick;
            }
            await Frame();
        }
        Expect(_sel.Selection.Count == 1 && _sel.Selection.Contains(_builder), "the builder is no longer selected");
        await LookAt(NodeCentre(_mine));
        await RightClick(Screen(NodeCentre(_mine), 1.6f));
        await Ticks(2);
        Expect(U.GatherNode[_builder.Index].Index == _mine, "the builder did not go back to the mine");
        return site;
    }

    // Clicks the building, waits for its cost, presses its grid key; waits until the unit stands; returns it.
    private async Task<EntityHandle> TrainAt(int building, int unitType)
    {
        await SelectBuildingByClick(building);
        int cell = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Train && _card.TypeAt(i) == unitType, -1);
        Expect(cell >= 0, $"{_data.Units[unitType].DisplayName} not on the card");
        UnitDef def = _data.Units[unitType];
        await UntilAfford(def.CostGold, def.CostWood, $"{def.DisplayName}'s cost");
        await Until(() => _card.ReasonAt(cell) == 0, $"{def.DisplayName}'s button live ({_card.CostAt(cell).Text})");
        var before = new HashSet<int>(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i] && U.TypeId[i] == unitType));
        Key(KeyOf(cell));
        await Ticks(2);
        Expect(B.QueueCount[building] > 0 && Enumerable.Range(0, B.QueueCount[building]).Any(k => B.QueueTypeAt(building, k) == unitType && !B.QueueIsTechAt(building, k)),
            $"{def.DisplayName} not queued after {_card.GridKey(cell)}");
        int unit = -1;
        await Until(() =>
        {
            for (int i = 0; i < U.Capacity && unit < 0; i++)
                if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == unitType && !before.Contains(i)) unit = i;
            return unit >= 0;
        }, $"a {def.DisplayName} trained");
        return new EntityHandle(unit, U.Generation[unit]);
    }

    // ---- setup ----

    private async Task StartMatch(ulong seed)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", seed.ToString(), "--units", "0", "--mute", "--speed", "8" }));
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false; // the injected cursor sits on the minimap or the card, never meant as a pan
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _strip = _match.GetNode<ProductionQueueStrip>("Hud/QueueStrip");
        _rally = _match.GetNode<RallyMarker>("World3D/RallyMarker");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        Expect(_runner.GameSpeed == 8.0 && _runner.Recorder != null, $"speed {_runner.GameSpeed}, recorder {_runner.Recorder != null}");
        _faction = W.FactionOf(0);
        _laborer = StartBase.UnitOfSlot(_data, _faction, UnitSlot.Worker);
        _infantry = StartBase.UnitOfSlot(_data, _faction, UnitSlot.Line);
        _sapper = StartBase.UnitOfSlot(_data, _faction, UnitSlot.Unique);
        _barracksType = StartBase.BuildingOfSlot(_data, _faction, BuildingSlot.InfantryHall);
        _armoryType = StartBase.BuildingOfSlot(_data, _faction, BuildingSlot.Forge);
        _depotType = StartBase.BuildingOfSlot(_data, _faction, BuildingSlot.Camp);
        _billetType = StartBase.BuildingOfSlot(_data, _faction, BuildingSlot.House);
        _yardType = StartBase.BuildingOfSlot(_data, _faction, BuildingSlot.SiegeWorks);
        _towerType = StartBase.BuildingOfSlot(_data, _faction, BuildingSlot.CasterHall);
        _age = _data.FindTech("age_ii");
        _melee = _data.FindTech("melee_weapons_1");
        await Until(() => B.Count >= 2 && U.Count >= 10, "the start bases");
        _hall = -1;
        for (int k = 0; k < B.Capacity && _hall < 0; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) _hall = k;
        Expect(_hall >= 0, "no Town Hall");
        _starters.Clear();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == _laborer) _starters.Add(new EntityHandle(i, U.Generation[i]));
        Expect(_starters.Count == 5, $"{_starters.Count} start workers");
        _builder = _starters[0];
        _taken.Clear();
        _taken.Add(B.Cell[_hall]);
        _retasked = 0;
        GD.Print($"seed {seed}: match up at tick {Tick} (8x, shipped data, {_data.Factions[_faction].DisplayName}, tick budget {TickBudget})");
    }

    // ---- input (everything goes through the viewport) ----

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void Key(Godot.Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private async Task LeftClick(Vector2 at)
    {
        await Hover(at);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task RightClick(Vector2 at)
    {
        await Hover(at);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, ButtonMask = MouseButtonMask.Right });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    // A box drag round the screen points, 14 px of margin.
    private async Task BoxDrag(Vector2[] points)
    {
        Vector2 lo = points.Aggregate((a, b) => a.Min(b)) - new Vector2(14f, 14f), hi = points.Aggregate((a, b) => a.Max(b)) + new Vector2(14f, 14f);
        await Hover(lo);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = lo, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseMotion { Position = (lo + hi) / 2f, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseMotion { Position = hi, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = hi });
        await Frames(1);
    }

    // Moves the cursor: a motion event. Headless Godot has no pointer, so when the viewport's mouse position doesn't follow
    // the event the ghost is told the same point (its test seam), recorded once in the log.
    private async Task Hover(Vector2 at)
    {
        Push(new InputEventMouseMotion { Position = at });
        if (GetViewport().GetMousePosition() != at)
        {
            _ghost.ScreenOverride = at;
            _cursorOverride = true;
        }
        await Frames(1);
    }

    // The camera: a left click on the minimap at the map point (the player's jump), then a frame for the camera to move.
    private async Task LookAt(System.Numerics.Vector2 point)
    {
        MinimapTransform fit = _mini.Fit;
        System.Numerics.Vector2 px = fit.ToPixel(point);
        Vector2 at = _mini.GetGlobalRect().Position + new Vector2(px.X, px.Y);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        await Frames(2);
        Expect(System.Numerics.Vector2.Distance(_camera.Focus, point) < 1f, $"minimap click: camera at {_camera.Focus}, wanted {point}");
    }

    private async Task SelectBuildingByClick(int slot)
    {
        System.Numerics.Vector2 c = Centre(slot);
        await LookAt(c);
        await LeftClick(Screen(c, BuildingViews.BoxHeight * BuildingPicker.BoxRise(B, _data.Buildings, slot, BuildingViews.SiteMinHeight)));
        await Frames(2);
        Expect(_sel.SelectedBuilding == slot && _sel.Selection.Count == 0, $"click on building {slot}: selected building {_sel.SelectedBuilding}, {_sel.Selection.Count} units");
        Expect(_card.ActionAt(0) is CardCommand.Train or CardCommand.Research && _panel.NameLabel.Text == _data.Buildings[B.TypeId[slot]].DisplayName,
            $"card {_card.ActionAt(0)}, panel '{_panel.NameLabel.Text}'");
    }

    // A left click on the builder (one of the start workers) wherever it is: it alone is selected.
    private Task SelectBuilder() => SelectAlone(_builder);

    private async Task SelectAlone(EntityHandle unit)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await LookAt(U.Position[unit.Index]);
            await LeftClick(ScreenOf(unit.Index));
            await Frames(1);
            if (_sel.Selection.Count == 1 && _sel.Selection.Contains(unit)) break; // else another worker stood nearer the cursor: again
        }
        Expect(_sel.Selection.Count == 1 && _sel.Selection.Contains(unit) && _sel.ActiveSubgroupIsWorker && _sel.SelectedBuilding < 0,
            $"worker {unit.Index} not selected alone ({_sel.Selection.Count} units)");
    }

    // The laborer nearest the site, 10 m or more from the stuck builder, clicked and right-clicked onto the site.
    private async Task<EntityHandle> SendAnotherBuilder(int site)
    {
        System.Numerics.Vector2 c = Centre(site), stuck = U.Position[_builder.Index];
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0 || U.TypeId[i] != _laborer || i == _builder.Index) continue;
            float d = System.Numerics.Vector2.Distance(U.Position[i], c);
            if (d < bestD && System.Numerics.Vector2.Distance(U.Position[i], stuck) >= 10f) (best, bestD) = (i, d);
        }
        Expect(best >= 0, "no other laborer to send to the stalled site");
        var h = new EntityHandle(best, U.Generation[best]);
        await SelectAlone(h);
        await LookAt(c);
        await RightClick(Screen(c, 0.5f));
        await Ticks(2);
        Expect(U.BuildTarget[best].Index == site, $"laborer {best} not building the site after the right click (state {U.State[best]})");
        GD.Print($"  tick {Tick}: laborer {best} ({bestD:0} m away) right-clicked onto the site; it builds from here on");
        return h;
    }

    // ---- waiting and checking ----

    private void Step(string name)
    {
        _stepNo++;
        _step = $"{_stepNo}. {name}";
    }

    private void Done()
    {
        if (_breakStep == _stepNo)
        {
            _breakStep = -1;
            Expect(false, "deliberately broken by --break");
        }
        GD.Print($"seed {_seed} step {_step}: done at tick {Tick} (gold {W.Gold[0]}, wood {W.Wood[0]}; {Census()})");
    }

    private void Expect(bool ok, string message)
    {
        if (!ok) throw new StepFailed(message);
    }

    private async Task Until(Func<bool> done, string what, Func<string>? detail = null)
    {
        while (!done())
        {
            if (Tick > TickBudget) throw new StepFailed($"{what}: not by tick {TickBudget} (gold {W.Gold[0]}, wood {W.Wood[0]}){(detail != null ? "; " + detail() : "")}");
            await Frame();
        }
    }

    // Waits for the money. Every 100 ticks an idle laborer (a rally tree felled before it was born) is put to work as a
    // player would: clicked, then a right click on the nearest live tree; the selection is clicked back afterwards.
    private async Task UntilAfford(int gold, int wood, string what)
    {
        int checkedAt = Tick;
        while (!(W.Gold[0] >= gold && W.Wood[0] >= wood))
        {
            if (Tick > TickBudget) throw new StepFailed($"{what}: not by tick {TickBudget} (gold {W.Gold[0]}, wood {W.Wood[0]}); {Census()}");
            if (Tick - checkedAt >= 100)
            {
                checkedAt = Tick;
                await RetaskIdle();
            }
            await Frame();
        }
    }

    // BUG-0145: a gatherer on its loop reads Idle for single ticks between legs (waiting at the mine's edge, retrying), and
    // a player would never pull it off a live node. Only a laborer whose node is gone (or who has no gather order: the sim
    // clears GatherNode when a loop ends) is really idle.
    private bool OffTheLoop(int i) => !W.Resources.IsAlive(U.GatherNode[i]);

    private async Task RetaskIdle()
    {
        int idle = -1;
        for (int i = 0; i < U.Capacity && idle < 0; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == _laborer && U.State[i] == UnitState.Idle && U.QueueCount[i] == 0 && i != _builder.Index
                && OffTheLoop(i)) idle = i;
        if (idle < 0) return;
        int building = _sel.SelectedBuilding;
        bool builder = _sel.Selection.Count == 1 && _sel.Selection.Contains(_builder);
        var h = new EntityHandle(idle, U.Generation[idle]);
        await LookAt(U.Position[idle]);
        await LeftClick(ScreenOf(idle));
        await Frames(1);
        if (_sel.Selection.Count == 1 && _sel.Selection.Contains(h))
        {
            int tree = NearestNode(ResourceKind.Wood, U.Position[idle]);
            await LookAt(NodeCentre(tree));
            await RightClick(Screen(NodeCentre(tree), 2f));
            await Ticks(2);
            _retasked++;
            GD.Print($"  tick {Tick}: an idle laborer clicked and sent to the tree at {NodeCentre(tree)} ({(U.GatherNode[idle].Index == tree ? "gathering it" : "walking")})");
        }
        if (building >= 0) await SelectBuildingByClick(building);
        else if (builder) await SelectBuilder();
    }

    // Player 0's workers by state and by the kind of node they gather (a timeout's detail).
    private string Census()
    {
        var counts = new SortedDictionary<string, int>();
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0 || U.TypeId[i] != _laborer) continue;
            EntityHandle n = U.GatherNode[i];
            string node = W.Resources.IsAlive(n) ? _data.Resources[W.Resources.TypeId[n.Index]].Resource.ToString() : "none";
            string key = $"{U.State[i]}/{node}/q{U.QueueCount[i]}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return "workers " + string.Join(", ", counts.Select(kv => $"{kv.Key} x{kv.Value}"));
    }

    // Waits until n more ticks have run, then one frame so every view has drawn them.
    private async Task Ticks(int n)
    {
        int until = Tick + n;
        await Until(() => Tick >= until, $"{n} ticks");
        await Frame();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    // ---- geometry ----

    private System.Numerics.Vector2 Centre(int building) => SelectionController.SiteCenter(W, building);

    private System.Numerics.Vector2 NodeCentre(int node)
    {
        ResourceDef def = _data.Resources[W.Resources.TypeId[node]];
        int a = W.Resources.Cell[node];
        return new System.Numerics.Vector2(a % G.Width + def.FootprintWidth / 2f, a / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize;
    }

    // Screen pixel of the point `up` meters above the terrain at a map point.
    private Vector2 Screen(System.Numerics.Vector2 p, float up) =>
        _camera.UnprojectPosition(new Vector3(p.X, TerrainHeight.At(W.Heightmap, p.X, p.Y) + up, p.Y));

    private Vector2 ScreenOf(int unit) => _sel.TryScreenPosition(unit, out Vector2 s) ? s : throw new StepFailed($"unit {unit} behind the camera");

    // Where the cursor goes for a ghost at `anchor`: the centre of the cell the ghost's rule maps back to it (the anchor
    // plus half the footprint, PlacementGhost.Anchor); for an even footprint the footprint's centre is a cell corner.
    private Vector2 FootprintScreen(int anchor, int type)
    {
        BuildingDef def = _data.Buildings[type];
        return Screen(G.CellCenter(anchor % G.Width + def.FootprintWidth / 2, anchor / G.Width + def.FootprintHeight / 2), 0f);
    }

    private Godot.Key KeyOf(int cell) => Enum.Parse<Godot.Key>(_card.GridKey(cell));

    private int CellOf(CardCommand action, int type) =>
        Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == action && _card.TypeAt(i) == type, -1);

    private int NearestNode(ResourceKind kind, System.Numerics.Vector2 from)
    {
        ResourceStore r = W.Resources;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i] || _data.Resources[r.TypeId[i]].Resource != kind) continue;
            float d = System.Numerics.Vector2.Distance(NodeCentre(i), from);
            if (d < bestD) (best, bestD) = (i, d);
        }
        Expect(best >= 0, $"no {kind} node");
        return best;
    }

    // The cursor on the nearest spot (FindSpots) whose pixel, with the camera on it, maps back to it: the camera ray there
    // meets the ground in that cell, not a rise in front of it. Returns the spot.
    private async Task<int> HoverSpot(int type, bool ignoreRequires, System.Numerics.Vector2 near, float minD, float maxD)
    {
        int tried = 0;
        foreach (int cell in FindSpots(type, near, ignoreRequires, minD, maxD))
        {
            if (tried++ >= 12) break;
            await LookAt(StartBase.FootprintCenter(G, _data.Buildings[type], cell));
            Vector2 at = FootprintScreen(cell, type);
            Vector3 o = _camera.ProjectRayOrigin(at), d = _camera.ProjectRayNormal(at);
            if (!GroundPicker.TryPick(W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit)
                || PlacementGhost.Anchor(G, _data.Buildings[type], new System.Numerics.Vector2(hit.X, hit.Z)) != cell) continue;
            await Hover(at);
            return cell;
        }
        throw new StepFailed($"no visible spot for {_data.Buildings[type].DisplayName} in {tried} tries");
    }

    // Spots the sim would accept (or would but for the type's requires) 12-40 m from `near`, nearest first, clear of the
    // other sites and of units. The test's own oracle: the ghost then has to agree.
    private IEnumerable<int> FindSpots(int type, System.Numerics.Vector2 near, bool ignoreRequires, float minD, float maxD)
    {
        BuildingDef def = _data.Buildings[type];
        var spots = new List<(float D, int Cell)>();
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d < minD || d > maxD) continue;
            int x = cell % G.Width, y = cell / G.Width;
            if (_taken.Any(t => Math.Abs(t % G.Width - x) < 7 && Math.Abs(t / G.Width - y) < 7)) continue;
            spots.Add((d, cell));
        }
        foreach ((float _, int cell) in spots.OrderBy(s => s.D))
        {
            // BUG-0274: a green spot must lie on ground the player has explored (the sim refuses the rest since M4-3b); a
            // Locked one answers Requires first either way.
            if (!ignoreRequires && !ExploredGround.Footprint(W, def, cell)) continue;
            bool ok = W.CanPlace(0, type, cell, out PlacementError why);
            if (!ok && !(ignoreRequires && why == PlacementError.Requires && B.Fits(type, cell))) continue;
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            bool clear = true;
            for (int i = 0; i < U.Capacity && clear; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 4f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 4f) clear = false;
            if (clear) yield return cell;
        }
    }
}
