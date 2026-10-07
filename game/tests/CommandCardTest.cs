using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M3-V2 criteria 1-6 and 8 on the real Match scene: command card, build menus, placement ghost, building selection and Cancel, right-click Repair / join, idle allocation.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/CommandCardTest.tscn</c>; prints "COMMAND CARD
/// TEST PASS" and exits 0, or prints each failure and exits 1. The test ticks the sim itself (SimRunner disabled) and
/// drives input through <see cref="SelectionController._UnhandledInput"/> and the card's button signals, so every key and
/// click goes down the real path. Commands are read back from the sim's pending queue (reflection, read only). Windowed
/// with <c>-- --shots &lt;dir&gt;</c> it also saves the card (soldiers, workers), the B menu, a green and a red ghost, and a
/// selected site with its Cancel.
/// </remarks>
public partial class CommandCardTest : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string? _shots;

    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private CommandCard _card = null!;
    private BuildGhost _ghost = null!;
    private BuildingOutline _outline = null!;
    private BuildingViews _buildings = null!;
    private Sfx _sfx = null!;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Input.ActionRelease("order_queue");
        foreach (string f in _failures) GD.Print($"COMMAND CARD TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("COMMAND CARD TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
        GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;
        UiTextRows();
        _ui = UiText.Shared!;

        await StartMatch();
        await CardContents();
        await ButtonsEqualKeys();
        await Menus();
        await GhostColour();
        await Placement();
        await SiteSelection();
        await Repair();
        await IdleAllocation();
        await EndMatch();
    }

    // ui.json: every key the card and ghost need is present and non-empty; a missing key fails loudly with its name.
    private void UiTextRows()
    {
        var errors = new List<string>();
        UiText? ui = UiText.Load(UiText.DefaultPath, errors);
        if (!Check(ui != null && errors.Count == 0, $"shipped ui.json: {string.Join("; ", errors)}")) return;
        foreach (CardCommand c in Enum.GetValues<CardCommand>())
        {
            if (UiText.CommandIds[(int)c].Length == 0) continue;
            Check(ui!.CommandName(c).Length > 0 && ui.CommandHint(c).Length > 0, $"ui.json: {c} has no text");
        }
        foreach (PlacementError r in Enum.GetValues<PlacementError>())
            if (r != PlacementError.None) Check(ui!.PlacementText(r).Length > 0, $"ui.json: no placement text for {r}");
        Check(ui!.BasicMenu.Length == 6 && ui.AdvancedMenu.Length == 3, $"ui.json menus {ui.BasicMenu.Length} / {ui.AdvancedMenu.Length}");

        string json = File.ReadAllText(UiText.DefaultPath);
        foreach ((string from, string expect) in new[]
        {
            ("\"stop\": { \"displayName\": \"Stop\", \"hotkeyHint\": \"S\" },", "missing commands.stop"),
            ("\"seals_ground\": \"Would wall ground off\",", "missing placement.seals_ground"),
            ("\"hotkeyHint\": \"M\"", "missing commands.move.hotkeyHint"),
        })
        {
            errors.Clear();
            string broken = json.Replace(from, from.Contains("hotkeyHint\": \"M\"") ? "\"other\": \"M\"" : "");
            Check(broken != json, $"ui.json row: '{from}' not found in the file");
            UiText? bad = UiText.Parse(broken, errors);
            Check(bad == null && errors.Count == 1 && errors[0].Contains(expect), $"broken ui.json ({expect}): {string.Join("; ", errors)}");
        }
        errors.Clear();
        Check(UiText.Parse(json.Replace("\"forge\"", "\"smithy\""), errors) == null && errors.Count == 1 && errors[0].Contains("buildMenus.basic[5]"),
            $"bad slot id: {string.Join("; ", errors)}");
        errors.Clear();
        Check(UiText.Parse("{", errors) == null && errors.Count == 1, "invalid JSON accepted");
        errors.Clear();
        Check(UiText.Load(Path.Combine(Path.GetTempPath(), "rts-no-such-dir", "ui.json"), errors) == null && errors.Count == 1 && errors[0].Contains("cannot read"), "missing file accepted");
        Check(UiText.PlacementKey(PlacementError.UnitInTheWay) == "unit_in_the_way" && UiText.PlacementKey(PlacementError.SealsGround) == "seals_ground",
            "placement keys are not the snake_case enum names");
    }

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "20", "--mute" }));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        _outline = _match.GetNode<BuildingOutline>("World3D/BuildingOutline");
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _sfx = _match.GetNode<Sfx>("Sfx");
        Check(_sel.Card == _card && _card.Visible, "the match didn't connect the card");
        Tick(2);
        await Frames();
        FocusOn(HallCenter(0));
        await Frames();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // Criterion 1: card contents per selection kind, labels from ui.json, the active Tab subgroup decides B / V.
    private async Task CardContents()
    {
        await Select(new List<EntityHandle>());
        CheckCells("empty selection", new Dictionary<int, CardCommand>());

        var unitCard = new Dictionary<int, CardCommand>
        {
            [5] = CardCommand.AttackMove, [6] = CardCommand.Stop, [7] = CardCommand.Hold, [8] = CardCommand.Move,
        };
        await Select(Soldiers(5));
        CheckCells("soldiers", unitCard);
        await Shot("card-soldiers");

        var workerCard = new Dictionary<int, CardCommand>(unitCard) { [13] = CardCommand.BuildAdvanced, [14] = CardCommand.BuildBasic };
        await Select(Workers());
        CheckCells("workers", workerCard);
        await Shot("card-workers");

        // Mixed: B / V only while the worker subgroup is active (Tab cycles it).
        await Select(Workers().Concat(Soldiers(3)).ToList());
        bool sawWorker = false, sawSoldier = false;
        for (int k = 0; k < _sel.Subgroups.Count; k++)
        {
            bool worker = _data.Units[_sel.Subgroups.ActiveType].Slot == UnitSlot.Worker;
            CheckCells($"mixed, active {_data.Units[_sel.Subgroups.ActiveType].Key}", worker ? workerCard : unitCard);
            sawWorker |= worker;
            sawSoldier |= !worker;
            Key(Godot.Key.Tab);
            await Frames();
        }
        Check(sawWorker && sawSoldier, $"mixed selection: subgroups {_sel.Subgroups.Count}, worker {sawWorker}, soldier {sawSoldier}");
        // Contents are written only on a change.
        int layouts = _card.Layouts;
        for (int f = 0; f < 10; f++) await Frame();
        Check(_card.Layouts == layouts, $"steady frames rewrote the card {_card.Layouts - layouts} times");
    }

    // Criterion 1: each button enqueues the same commands as its key (and B / V open the same menu).
    private async Task ButtonsEqualKeys()
    {
        List<EntityHandle> soldiers = Soldiers(4);
        Vector2 ground = await OnScreen(OpenGroundNear(HallCenter(0), 14f));
        foreach (bool shift in new[] { false, true })
        {
            if (shift) Input.ActionPress("order_queue");
            foreach ((Godot.Key key, int cell, bool click) in new[] { (Godot.Key.S, 6, false), (Godot.Key.H, 7, false), (Godot.Key.A, 5, true), (Godot.Key.M, 8, true) })
            {
                await Select(soldiers);
                int start = _sim.PendingCommandCount;
                Key(key);
                if (click) LeftClick(ground);
                List<Command> byKey = Pending(start);
                Tick(1);
                await Select(soldiers);
                start = _sim.PendingCommandCount;
                if (!Check(_card.ActionAt(cell) != CardCommand.None, $"cell {cell} empty on the soldier card")) continue;
                _card.ButtonAt(cell).EmitSignal(BaseButton.SignalName.Pressed);
                if (click) LeftClick(ground);
                List<Command> byButton = Pending(start);
                Tick(1);
                string what = $"{key}{(shift ? " + shift" : "")}";
                Check(byKey.Count == soldiers.Count && Same(byKey, byButton), $"{what}: key {Describe(byKey)} vs button {Describe(byButton)}");
                Check(byKey.All(c => c.IsQueued == shift), $"{what}: queued flag");
                CommandKind want = key switch { Godot.Key.S => CommandKind.Stop, Godot.Key.H => CommandKind.HoldPosition, Godot.Key.A => CommandKind.AttackMove, _ => CommandKind.Move };
                Check(byButton.All(c => c.Kind == want), $"{what}: kinds {Describe(byButton)}");
            }
            if (shift) Input.ActionRelease("order_queue");
        }
        // Through the viewport (GUI routing, as QaH1 clicks): an empty card cell lets the click through to the map, a
        // visible button takes it. A armed, a click on the hidden Q cell is the attack-move's point.
        await Select(soldiers);
        Key(Godot.Key.A);
        int pending = _sim.PendingCommandCount;
        Rect2 q = _card.ButtonAt(0).GetGlobalRect();
        Check(!_card.ButtonAt(0).Visible, "the Q cell is visible on a unit card");
        PushClick(q.GetCenter());
        List<Command> through = Pending(pending);
        Check(through.Count == soldiers.Count && through.All(c => c.Kind == CommandKind.AttackMove), $"click on an empty card cell: {Describe(through)}, want attack-moves");
        Tick(1);
        Key(Godot.Key.A);
        pending = _sim.PendingCommandCount;
        PushClick(_card.ButtonAt(6).GetGlobalRect().GetCenter());
        List<Command> onButton = Pending(pending);
        Check(onButton.Count == soldiers.Count && onButton.All(c => c.Kind == CommandKind.Stop) && !_sel.Targeting,
            $"click on the Stop button while A-targeting: {Describe(onButton)}, targeting {_sel.Targeting}");
        Tick(1);

        // Stop the soldiers' holds again so later placements aren't blocked by them.
        await Select(soldiers);
        Key(Godot.Key.S);
        Tick(2);

        // B / V: key and button open the same menu.
        foreach ((Godot.Key key, int cell, bool advanced) in new[] { (Godot.Key.B, 14, false), (Godot.Key.V, 13, true) })
        {
            await Select(Workers());
            Key(key);
            await Frames();
            bool keyOpen = _card.MenuOpen && _card.AdvancedMenuOpen == advanced;
            Key(Godot.Key.Escape);
            await Frames();
            Check(!_card.MenuOpen, $"Esc left the {key} menu open");
            _card.ButtonAt(cell).EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
            Check(keyOpen && _card.MenuOpen && _card.AdvancedMenuOpen == advanced, $"{key}: key opened {keyOpen}, button opened {_card.MenuOpen} advanced {_card.AdvancedMenuOpen}");
            Key(Godot.Key.Escape);
            await Frames();
        }
    }

    // Criterion 2: B lists the six Age I buildings, V the three Age II ones, data names in slot order on the grid keys; cost from data; Esc / right-click close.
    private async Task Menus()
    {
        int faction = W.FactionOf(0);
        FactionDef f = _data.Factions[faction];
        string[] keys = { "Q", "W", "E", "R", "T", "A", "S", "D", "F" };
        foreach ((Godot.Key key, BuildingSlot[] slots) in new[]
        {
            (Godot.Key.B, new[] { BuildingSlot.House, BuildingSlot.Camp, BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall, BuildingSlot.Forge }),
            (Godot.Key.V, new[] { BuildingSlot.CasterHall, BuildingSlot.SiegeWorks, BuildingSlot.WatchTower }),
        })
        {
            await Select(Workers());
            Key(key);
            await Frames();
            if (!Check(_card.MenuOpen, $"{key} opened no menu")) continue;
            var names = new List<string>();
            for (int i = 0; i < CommandCard.Cells; i++)
            {
                if (i >= slots.Length)
                {
                    Check(_card.ActionAt(i) == CardCommand.None && !_card.ButtonAt(i).Visible, $"{key} menu: cell {i} not empty");
                    continue;
                }
                int type = StartBase.BuildingOfSlot(_data, faction, slots[i]);
                BuildingDef def = _data.Buildings[type];
                Button b = _card.ButtonAt(i);
                names.Add(_card.NameAt(i).Text);
                Check(_card.ActionAt(i) == CardCommand.Place && _card.TypeAt(i) == type && b.Visible, $"{key} menu cell {i}: {_card.ActionAt(i)} type {_card.TypeAt(i)}, want {def.Key}");
                Check(_card.NameAt(i).Text == def.DisplayName, $"{key} menu cell {i}: label '{_card.NameAt(i).Text}', want '{def.DisplayName}'");
                Check(_card.HintAt(i).Text == keys[i] && _card.GridKey(i) == keys[i], $"{key} menu cell {i}: hint '{_card.HintAt(i).Text}', want {keys[i]}");
                string cost = $"{f.GoldName} {def.CostGold}  {f.WoodName} {def.CostWood}";
                Check(b.TooltipText.Contains(def.Description) && b.TooltipText.Contains(cost), $"{key} menu cell {i}: tooltip '{b.TooltipText}' lacks description or '{cost}'");
                Check(_card.CostAt(i).Text == $"{def.CostGold} / {def.CostWood}", $"{key} menu cell {i}: cost label '{_card.CostAt(i).Text}'");
            }
            GD.Print($"{key} menu: {string.Join(", ", names)}");
            if (key == Godot.Key.B) await Shot("menu-basic");
            Key(Godot.Key.Escape);
            await Frames();
            Check(!_card.MenuOpen && _card.ActionAt(14) == CardCommand.BuildBasic, $"Esc didn't close the {key} menu");
        }

        // A right click closes the menu and orders nothing.
        await Select(Workers());
        Key(Godot.Key.B);
        int pending = _sim.PendingCommandCount;
        RightClick(await OnScreen(OpenGroundNear(HallCenter(0), 12f)));
        await Frames();
        Check(!_card.MenuOpen && _sim.PendingCommandCount == pending, $"right-click: menu open {_card.MenuOpen}, {_sim.PendingCommandCount - pending} commands");

        // In a menu, A picks the sixth entry (the Forge) instead of arming attack-move; S does nothing.
        Key(Godot.Key.B);
        Key(Godot.Key.A);
        int forge = StartBase.BuildingOfSlot(_data, faction, BuildingSlot.Forge);
        Check(_ghost.Active && _ghost.TypeId == forge && !_sel.Targeting, $"A in the menu: ghost {_ghost.Active} type {_ghost.TypeId}, targeting {_sel.Targeting}");
        pending = _sim.PendingCommandCount;
        Key(Godot.Key.S);
        Check(_sim.PendingCommandCount == pending && _ghost.TypeId == forge, "S in the menu ordered a stop or changed the ghost");
        Key(Godot.Key.Escape);
        await Frames();
        Check(!_ghost.Active && !_card.MenuOpen, "Esc left the ghost up");
        // Soldiers selected: B is not the card's.
        await Select(Soldiers(3));
        Key(Godot.Key.B);
        await Frames();
        Check(!_card.MenuOpen, "B opened a menu for soldiers");
    }

    // Criterion 3: the ghost's colour equals CanPlace every frame for 200 random cursor points; at most one call a frame; red text from ui.json.
    private async Task GhostColour()
    {
        // A soldier holding beside the hall gives UnitInTheWay spots.
        await Select(Soldiers(1));
        Key(Godot.Key.H);
        Tick(2);
        await Select(Workers());
        Key(Godot.Key.B);
        Key(Godot.Key.E); // Infantry Hall, 3 x 3
        int type = _ghost.TypeId;
        Check(type == StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.InfantryHall), "E didn't pick the infantry hall");
        Dictionary<string, string> placementJson = PlacementTextFromFile();
        FocusOn(HallCenter(0));
        await Frames();
        var rng = new Random(1);
        Vector2 view = GetViewport().GetVisibleRect().Size;
        int green = 0, red = 0, hidden = 0, maxCalls = 0;
        var reasons = new SortedSet<string>();
        for (int k = 0; k < 200; k++)
        {
            if (k % 10 == 9) Tick(1); // the answer must follow the sim, not only the cursor
            var screen = new Vector2((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y);
            _ghost.ScreenOverride = screen;
            int calls = _ghost.CanPlaceCalls;
            await Frame();
            await Frame();
            maxCalls = Math.Max(maxCalls, _ghost.CanPlaceCalls - calls);
            if (!_ghost.Visible)
            {
                hidden++;
                continue;
            }
            int want = PlacementGhost.Anchor(G, _data.Buildings[type], Pick(screen));
            bool ok = W.CanPlace(0, type, _ghost.Anchor, out PlacementError reason);
            Check(_ghost.Anchor == want, $"point {k}: ghost anchor {_ghost.Anchor}, cursor anchor {want}");
            Check(_ghost.Valid == ok && _ghost.Reason == reason && _ghost.Box.MaterialOverride == (ok ? _ghost.GreenMaterial : _ghost.RedMaterial),
                $"point {k}: ghost {_ghost.Valid} {_ghost.Reason}, CanPlace {ok} {reason}");
            string text = ok ? "" : placementJson[UiText.PlacementKey(reason)];
            Check(_ghost.ShownText == text && _ghost.ReasonLabel.Text == text && _ghost.ReasonLabel.Visible == !ok, $"point {k}: text '{_ghost.ShownText}', want '{text}'");
            if (ok) green++;
            else
            {
                red++;
                reasons.Add(reason.ToString());
            }
        }
        // Two Syncs in one frame: the second never asks again (once per frame), and the box stays on the answered anchor.
        int before = _ghost.CanPlaceCalls, anchor = _ghost.Anchor;
        _ghost.ScreenOverride = new Vector2(view.X * 0.3f, view.Y * 0.4f);
        _ghost.Sync();
        int afterFirst = _ghost.CanPlaceCalls;
        _ghost.ScreenOverride = new Vector2(view.X * 0.7f, view.Y * 0.6f);
        _ghost.Sync();
        Check(_ghost.CanPlaceCalls - before <= 1 && _ghost.CanPlaceCalls == afterFirst, $"two Syncs in a frame made {_ghost.CanPlaceCalls - before} calls");
        GD.Print($"ghost: {green} green, {red} red ({string.Join(", ", reasons)}), {hidden} off the map; most CanPlace calls in a frame {maxCalls}; anchor {anchor}");
        Check(maxCalls <= 1, $"CanPlace called {maxCalls} times in one frame");
        Check(green > 20 && red > 20 && reasons.Contains("Blocked"), $"ghost rows too one-sided: {green} green, {red} red");
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        await Frames();
    }

    // Criterion 4: a green click is one Build per selected live worker (same type and anchor); Shift keeps the ghost; red does nothing, silently.
    private async Task Placement()
    {
        List<EntityHandle> workers = Workers();
        List<EntityHandle> three = workers.Take(3).ToList();
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        int barracks = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.InfantryHall);

        // A dead worker and a soldier in the selection get nothing.
        EntityHandle doomed = workers[3];
        List<EntityHandle> selection = three.Append(doomed).Concat(Soldiers(1)).ToList();
        await Select(selection);
        while (!_sel.ActiveSubgroupIsWorker)
        {
            Key(Godot.Key.Tab);
            await Frames();
        }
        U.Free(doomed); // dies before the click
        Key(Godot.Key.B);
        Key(Godot.Key.E);
        int spot = GreenAnchor(barracks, HallCenter(0), exclude: null);
        if (!Check(spot >= 0, "no green spot for an infantry hall")) return;
        await Aim(barracks, spot);
        Check(_ghost.Valid && _ghost.Anchor == spot, $"aimed ghost: anchor {_ghost.Anchor} valid {_ghost.Valid}, want {spot}");
        await Shot("ghost-green");
        await SoundGap();
        int sounds = _sfx.PlayCount(SfxEvent.Command), start = _sim.PendingCommandCount;
        LeftClick(_ghost.ScreenOverride!.Value);
        List<Command> sent = Pending(start);
        System.Numerics.Vector2 point = PlacementGhost.AnchorPoint(G, spot);
        Check(sent.Count == 3 && sent.All(c => c.Kind == CommandKind.Build && c.TypeId == barracks && c.Position == point && !c.IsQueued)
            && sent.Select(c => c.Unit).SequenceEqual(three), $"green click sent {Describe(sent)}, want 3 Builds of {barracks} at {point}");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, "green click: no Command sound");
        Check(!_ghost.Active && !_card.MenuOpen, "green click without Shift left the ghost up");
        Tick(2);
        await Frames();
        int site = B.SlotAt(spot % G.Width, spot / G.Width);
        Check(site >= 0 && B.UnderConstruction[site] && B.TypeId[site] == barracks && B.Cell[site] == spot, "the Builds placed no site at the anchor");
        foreach (EntityHandle h in three) Check(site >= 0 && U.BuildTarget[h.Index] == B.HandleOf(site), $"worker {h.Index} isn't building the site");
        _barracksSite = site;

        // Wood is now short of a second infantry hall: a red "Can't afford" ghost, and its click does nothing, silently.
        await Select(three);
        Key(Godot.Key.B);
        Key(Godot.Key.E);
        int poor = GreenAnchorIgnoringCost(barracks, HallCenter(0), spot);
        await Aim(barracks, poor);
        // The previous ghost was green: a new red one must not keep its colour (found in the 1131 screenshot).
        Check(!_ghost.Valid && _ghost.Reason == PlacementError.CannotAfford && _ghost.ShownText == _ui.PlacementText(PlacementError.CannotAfford)
            && _ghost.Box.MaterialOverride == _ghost.RedMaterial, $"poor ghost: valid {_ghost.Valid} {_ghost.Reason} '{_ghost.ShownText}', red material {_ghost.Box.MaterialOverride == _ghost.RedMaterial}");
        await Shot("ghost-red");
        await SoundGap();
        sounds = _sfx.PlayCount(SfxEvent.Command);
        start = _sim.PendingCommandCount;
        LeftClick(_ghost.ScreenOverride!.Value);
        Check(_sim.PendingCommandCount == start && _sfx.PlayCount(SfxEvent.Command) == sounds && _ghost.Active, "red click enqueued, sounded or closed the ghost");
        // A blocked spot (the hall) is red too.
        await Aim(barracks, B.Cell[HallSlot(0)]);
        start = _sim.PendingCommandCount;
        LeftClick(_ghost.ScreenOverride!.Value);
        Check(!_ghost.Valid && _sim.PendingCommandCount == start, $"click on the hall: valid {_ghost.Valid}, {_sim.PendingCommandCount - start} commands");

        // Shift: houses (50 wood each, 50 left: one more fits); the ghost stays up and a second click enqueues again (queued behind the first).
        Key(Godot.Key.Escape);
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        Check(_ghost.TypeId == house, "Q didn't pick the house");
        int h1 = GreenAnchor(house, HallCenter(0), exclude: spot);
        await Aim(house, h1);
        Input.ActionPress("order_queue");
        start = _sim.PendingCommandCount;
        LeftClick(_ghost.ScreenOverride!.Value);
        List<Command> first = Pending(start);
        Check(_ghost.Active && _card.MenuOpen, "Shift + click closed the ghost");
        int h2 = GreenAnchor(house, HallCenter(0), exclude: spot, also: h1);
        await Aim(house, h2);
        start = _sim.PendingCommandCount;
        LeftClick(_ghost.ScreenOverride!.Value);
        List<Command> second = Pending(start);
        Input.ActionRelease("order_queue");
        Check(first.Count == 3 && first.All(c => c.Kind == CommandKind.Build && c.TypeId == house && c.Position == PlacementGhost.AnchorPoint(G, h1) && !c.IsQueued),
            $"shift click 1: {Describe(first)}");
        Check(second.Count == 3 && second.All(c => c.Kind == CommandKind.Build && c.TypeId == house && c.Position == PlacementGhost.AnchorPoint(G, h2) && c.IsQueued),
            $"shift click 2: {Describe(second)}");
        Key(Godot.Key.Escape);
        Tick(2);
        await Frames();
        _houseSite = B.SlotAt(h1 % G.Width, h1 / G.Width);
        Check(_houseSite >= 0 && B.UnderConstruction[_houseSite], "the first house site wasn't placed");
    }

    private int _barracksSite = -1, _houseSite = -1;

    // Criterion 5: a click on a site box selects it alone, the card shows Cancel, B / the button enqueue one Cancel at its
    // centre; the box goes the frame after the sim frees it; a dead or reused slot clears the selection; boxes never select buildings.
    private async Task SiteSelection()
    {
        int site = _barracksSite;
        if (!Check(site >= 0 && B.Alive[site], "no site to select")) return;
        await Select(Workers());
        Vector2 at = await BoxScreen(site);
        LeftClick(at);
        await Frames();
        Check(_sel.SelectedBuilding == site && _sel.Selection.Count == 0, $"site click: building {_sel.SelectedBuilding}, {_sel.Selection.Count} units");
        CheckCells("site", new Dictionary<int, CardCommand> { [14] = CardCommand.Cancel });
        Check(_card.NameAt(14).Text == _ui.CommandName(CardCommand.Cancel) && _card.HintAt(14).Text == _ui.CommandHint(CardCommand.Cancel), "Cancel label not from ui.json");
        Check(_outline.Visible && _outline.ShownSlot == site, $"outline on {_outline.ShownSlot}, visible {_outline.Visible}");
        var overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        Check(_match.GetNode<Label>("DebugOverlay/Label").Text.Contains("sel building"), "F12 label doesn't say 'sel building'");
        Key(Godot.Key.Tab);
        await Frames();
        Check(_sel.SelectedBuilding == site && _card.ActionAt(14) == CardCommand.Cancel, "Tab changed a building selection");
        FocusOn(SelectionController.SiteCenter(W, site));
        await Shot("site-cancel");

        // A box over the site never selects it.
        Vector2 c = await BoxScreen(site);
        _sel.BoxSelect(c - new Vector2(80f, 80f), c + new Vector2(80f, 80f), add: false);
        await Frames();
        Check(_sel.SelectedBuilding == -1, "a box selected a building");
        LeftClick(c);
        await Frames();

        // The B key: exactly one Cancel at the footprint centre; the site goes and the selection with it, the frame after.
        int start = _sim.PendingCommandCount;
        Key(Godot.Key.B);
        List<Command> sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Cancel && sent[0].Position == SelectionController.SiteCenter(W, site) && sent[0].Player == 0,
            $"B on a site sent {Describe(sent)}");
        int siteGen = B.Generation[site], siteCell = B.Cell[site];
        Tick(2); // a command applies on the second tick after it is enqueued (stamped TickNumber + 1)
        Check(!B.Alive[site] || B.Generation[site] != siteGen, $"the Cancel didn't free the site (gen {siteGen} -> {B.Generation[site]}, cell {siteCell} -> {B.Cell[site]}, site {B.UnderConstruction[site]})");
        await Frames();
        Check(!_buildings.IsShown(site) && !_buildings.ViewOf(site)!.Visible, "the site's box outlived it by a frame");
        Check(_sel.SelectedBuilding == -1 && !_outline.Visible && _card.ActionAt(14) == CardCommand.None, $"freed site still selected ({_sel.SelectedBuilding}) or outlined");

        // The button: the house site, Cancel through the button; meanwhile a slot reused in the same tick clears the selection.
        site = _houseSite;
        if (!Check(site >= 0 && B.Alive[site], "no house site")) return;
        LeftClick(await BoxScreen(site));
        await Frames();
        Check(_sel.SelectedBuilding == site, $"house site click selected {_sel.SelectedBuilding}");
        start = _sim.PendingCommandCount;
        _card.ButtonAt(14).EmitSignal(BaseButton.SignalName.Pressed);
        sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Cancel && sent[0].Position == SelectionController.SiteCenter(W, site), $"Cancel button sent {Describe(sent)}");
        // A Build the same tick takes the freed slot back (free list): the selection must not follow it.
        int house = B.TypeId[site];
        int gen = B.Generation[site];
        int spot = GreenAnchor(house, HallCenter(0), exclude: B.Cell[site]);
        _sim.Enqueue(Command.Build(0, Workers()[0], house, PlacementGhost.AnchorPoint(G, spot)));
        Tick(2);
        Check(B.Alive[site] && B.Generation[site] != gen && B.Cell[site] == spot, $"slot {site} wasn't reused (alive {B.Alive[site]}, cell {B.Cell[site]} want {spot})");
        Check(_sel.SelectedBuilding == -1, "a reused slot kept the selection");
        await Frames();
        Check(!_outline.Visible && _card.ActionAt(14) == CardCommand.None, "outline or Cancel stayed on a reused slot");

        // A finished building: selected alone, its card empty for now; clicking it with units selected drops the units.
        int hall = HallSlot(0);
        await Select(Soldiers(3));
        LeftClick(await BoxScreen(hall));
        await Frames();
        Check(_sel.SelectedBuilding == hall && _sel.Selection.Count == 0, $"hall click: {_sel.SelectedBuilding}, {_sel.Selection.Count} units");
        CheckCells("finished building", new Dictionary<int, CardCommand>());
        // An enemy building is not selectable (own only, like units).
        int enemy = HallSlot(1);
        LeftClick(await BoxScreen(enemy));
        await Frames();
        Check(_sel.SelectedBuilding == -1, $"enemy hall click selected {_sel.SelectedBuilding}");
        await Select(new List<EntityHandle>());
    }

    // Criterion 6: right-click on an own damaged finished building with 3 workers is 3 Repairs (Shift queues), on an own site 3 joining Builds, on an enemy building a Move.
    private async Task Repair()
    {
        List<EntityHandle> three = Workers().Take(3).ToList();
        int hall = HallSlot(0);
        int max = _data.Buildings[B.TypeId[hall]].Hp;
        Vector2 at = await GroundScreen(hall);
        await Select(three);
        int start = _sim.PendingCommandCount;
        RightClick(at);
        List<Command> sent = Pending(start);
        Check(sent.Count == 3 && sent.All(c => c.Kind == CommandKind.Move), $"right-click on a full-hp hall: {Describe(sent)}, want 3 Moves");

        DamageMethod.Invoke(B, new object[] { B.HandleOf(hall), max / 3 });
        Check(B.Hp[hall] < max, "damage didn't take");
        foreach (bool shift in new[] { false, true })
        {
            await Select(three.Concat(Soldiers(1)).ToList());
            if (shift) Input.ActionPress("order_queue");
            start = _sim.PendingCommandCount;
            RightClick(at);
            sent = Pending(start);
            if (shift) Input.ActionRelease("order_queue");
            List<Command> repairs = sent.Where(c => c.Kind == CommandKind.Repair).ToList();
            Check(sent.Count == 4 && repairs.Count == 3 && repairs.Select(c => c.Unit).SequenceEqual(three) && sent.Count(c => c.Kind == CommandKind.Move) == 1
                && sent.All(c => c.IsQueued == shift) && repairs.All(c => B.SlotAt((int)(c.Position.X / MapConstants.CellSize), (int)(c.Position.Y / MapConstants.CellSize)) == hall),
                $"right-click on the damaged hall (shift {shift}): {Describe(sent)}");
        }
        Tick(3);
        Check(three.All(h => U.BuildTarget[h.Index] == B.HandleOf(hall)), "the Repairs didn't take");

        // An own site: 3 Builds of its type at its anchor (they join it).
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        int site = -1;
        for (int k = 0; k < B.Capacity; k++) if (B.Alive[k] && B.Owner[k] == 0 && B.UnderConstruction[k]) site = k;
        if (Check(site >= 0, "no own site to join"))
        {
            await Select(three);
            start = _sim.PendingCommandCount;
            RightClick(await GroundScreen(site));
            sent = Pending(start);
            Check(sent.Count == 3 && sent.All(c => c.Kind == CommandKind.Build && c.TypeId == B.TypeId[site] && c.Position == PlacementGhost.AnchorPoint(G, B.Cell[site]) && !c.IsQueued),
                $"right-click on an own site: {Describe(sent)}");
            Tick(2);
            Check(three.All(h => U.BuildTarget[h.Index] == B.HandleOf(site)), "the joining Builds didn't join the site");
        }

        // The enemy hall (damaged too): Moves.
        int enemy = HallSlot(1);
        DamageMethod.Invoke(B, new object[] { B.HandleOf(enemy), 100 });
        await Select(three);
        start = _sim.PendingCommandCount;
        RightClick(await GroundScreen(enemy));
        sent = Pending(start);
        Check(sent.Count == 3 && sent.All(c => c.Kind == CommandKind.Move), $"right-click on the enemy hall: {Describe(sent)}");
        Tick(1);
        GD.Print($"repair: hall {B.Hp[hall]} / {max}, site {site}, house type {house}");
    }

    // Criterion 8: 300 idle frames with the card open (a build menu) and a ghost up, and with a building selected: 0 bytes.
    private async Task IdleAllocation()
    {
        FocusOn(HallCenter(0));
        await Select(Workers());
        Key(Godot.Key.B);
        Key(Godot.Key.W);
        Vector2 view = GetViewport().GetVisibleRect().Size;
        _ghost.ScreenOverride = view / 2f;
        await Frames();
        Check(_ghost.Active && _ghost.Visible, "allocation row: no ghost on show");
        _card.Sync();
        _ghost.Sync();
        _sel.SyncOutline(W);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++)
        {
            _card.Sync();
            _ghost.Sync();
            _sel.SyncOutline(W);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"300 idle Syncs of card + ghost + outline allocated {bytes} bytes");

        // The ghost following a moving cursor (one CanPlace a frame), measured around its own work only.
        _ghost.ProcessMode = ProcessModeEnum.Disabled;
        var rng = new Random(2);
        long moving = 0;
        int calls = _ghost.CanPlaceCalls;
        for (int f = 0; f < 100; f++)
        {
            _ghost.ScreenOverride = new Vector2((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y);
            await Frame();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            _ghost.Sync();
            moving += GC.GetAllocatedBytesForCurrentThread() - b0;
        }
        _ghost.ProcessMode = ProcessModeEnum.Inherit;
        Check(moving == 0 && _ghost.CanPlaceCalls - calls > 50, $"moving ghost: {moving} bytes over {_ghost.CanPlaceCalls - calls} CanPlace calls");

        // Real frames: nothing rewritten, no CanPlace without a tick or a move.
        _ghost.ScreenOverride = view / 2f;
        await Frames();
        int layouts = _card.Layouts, canPlace = _ghost.CanPlaceCalls, outline = _outline.Updates;
        for (int f = 0; f < 300; f++) await Frame();
        Check(_card.Layouts == layouts && _ghost.CanPlaceCalls == canPlace && _outline.Updates == outline,
            $"idle frames: layouts {layouts} -> {_card.Layouts}, CanPlace {canPlace} -> {_ghost.CanPlaceCalls}, outline {outline} -> {_outline.Updates}");
        Key(Godot.Key.Escape);

        // A selected building, idle.
        _sel.SelectBuilding(HallSlot(0));
        await Frames();
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++)
        {
            _card.Sync();
            _sel.SyncOutline(W);
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0 && _outline.Visible, $"300 idle Syncs with a building selected allocated {bytes} bytes");
        GD.Print($"idle: card + ghost + outline 0 bytes ({bytes}); moving ghost {moving} bytes over {_ghost.CanPlaceCalls - calls} calls");
        _ghost.ScreenOverride = null;
    }

    // ---- helpers ----

    private void CheckCells(string what, Dictionary<int, CardCommand> want)
    {
        for (int i = 0; i < CommandCard.Cells; i++)
        {
            CardCommand w = want.TryGetValue(i, out CardCommand c) ? c : CardCommand.None;
            Button b = _card.ButtonAt(i);
            if (!Check(_card.ActionAt(i) == w && b.Visible == (w != CardCommand.None), $"{what}: cell {i} is {_card.ActionAt(i)} (visible {b.Visible}), want {w}")) continue;
            if (w is CardCommand.None or CardCommand.Place) continue;
            Check(_card.NameAt(i).Text == _ui.CommandName(w) && _card.HintAt(i).Text == _ui.CommandHint(w), $"{what}: cell {i} reads '{_card.NameAt(i).Text}' / '{_card.HintAt(i).Text}'");
        }
    }

    private Dictionary<string, string> PlacementTextFromFile()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(UiText.DefaultPath));
        var d = new Dictionary<string, string>();
        foreach (JsonProperty p in doc.RootElement.GetProperty("placement").EnumerateObject()) d[p.Name] = p.Value.GetString()!;
        return d;
    }

    private List<Command> Pending(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private static bool Same(List<Command> a, List<Command> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Kind == p.Second.Kind && p.First.Unit == p.Second.Unit && p.First.Flags == p.Second.Flags
            && p.First.Position == p.Second.Position && p.First.TypeId == p.Second.TypeId && p.First.Player == p.Second.Player);

    private static string Describe(List<Command> list) =>
        $"[{string.Join(", ", list.Select(c => $"{c.Kind}{(c.IsQueued ? "+q" : "")} u{c.Unit.Index} t{c.TypeId} ({c.Position.X:0.#},{c.Position.Y:0.#})"))}]";

    private List<EntityHandle> Workers()
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot == UnitSlot.Worker) list.Add(new EntityHandle(i, U.Generation[i]));
        return list;
    }

    private List<EntityHandle> Soldiers(int n)
    {
        System.Numerics.Vector2 hall = HallCenter(0);
        return Enumerable.Range(0, U.Capacity)
            .Where(i => U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker)
            .OrderBy(i => System.Numerics.Vector2.Distance(U.Position[i], hall)).Take(n)
            .Select(i => new EntityHandle(i, U.Generation[i])).ToList();
    }

    private int HallSlot(int player)
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == player && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException($"player {player} has no hall");
    }

    private System.Numerics.Vector2 HallCenter(int player)
    {
        int k = HallSlot(player);
        return StartBase.FootprintCenter(G, _data.Buildings[B.TypeId[k]], B.Cell[k]);
    }

    // The nearest anchor to `near` where CanPlace says yes and no unit stands within a cell of the footprint.
    private int GreenAnchor(int type, System.Numerics.Vector2 near, int? exclude, int? also = null) => Anchor(type, near, exclude, also, ignoreCost: false);

    private int GreenAnchorIgnoringCost(int type, System.Numerics.Vector2 near, int exclude) => Anchor(type, near, exclude, null, ignoreCost: true);

    private int Anchor(int type, System.Numerics.Vector2 near, int? exclude, int? also, bool ignoreCost)
    {
        BuildingDef def = _data.Buildings[type];
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < 9f || d > 30f) continue;
            if (Overlaps(def, cell, exclude, type) || Overlaps(def, cell, also, type)) continue;
            bool ok = W.CanPlace(0, type, cell, out PlacementError r);
            if (!(ok || (ignoreCost && r == PlacementError.CannotAfford))) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 2f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 2f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        return best;
    }

    // True when a footprint of def at cell comes within a cell of the other anchor's footprint (same type assumed for its size, padded).
    private bool Overlaps(BuildingDef def, int cell, int? other, int type)
    {
        if (other is not int o) return false;
        int w = G.Width, x = cell % w, y = cell / w, ox = o % w, oy = o / w;
        const int pad = 4;
        return x < ox + pad + 3 && ox < x + def.FootprintWidth + pad && y < oy + pad + 3 && oy < y + def.FootprintHeight + pad;
    }

    // Puts the ghost's cursor on the middle of the footprint anchored at `anchor` and lets it answer.
    private async Task Aim(int type, int anchor)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 p = G.CellCenter(anchor % G.Width + def.FootprintWidth / 2, anchor / G.Width + def.FootprintHeight / 2);
        _ghost.ScreenOverride = await OnScreen(p);
        await Frames();
    }

    // Screen point of a building box's middle (half its drawn height).
    private async Task<Vector2> BoxScreen(int slot)
    {
        System.Numerics.Vector2 c = SelectionController.SiteCenter(W, slot);
        FocusOn(c);
        await Frames();
        float rise = BuildingPicker.BoxRise(B, _data.Buildings, slot, BuildingViews.SiteMinHeight);
        return _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y) + BuildingViews.BoxHeight * rise / 2f, c.Y));
    }

    // A screen point whose ground pick lies inside the building's footprint (the box's base corner nearest the camera is avoided: aim at the centre of its top).
    private async Task<Vector2> GroundScreen(int slot)
    {
        System.Numerics.Vector2 c = SelectionController.SiteCenter(W, slot);
        Vector2 screen = await OnScreen(c);
        Check(BuildingPicker.SlotAt(B, G, Pick(screen)) == slot, $"the screen point of building {slot} doesn't pick it");
        return screen;
    }

    private System.Numerics.Vector2 OpenGroundNear(System.Numerics.Vector2 p, float min)
    {
        G.WorldToCell(p, out int px, out int py);
        for (int r = (int)(min / MapConstants.CellSize); r < 30; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    System.Numerics.Vector2 c = G.CellCenter(px + dx, py + dy);
                    if (StartLayout.IsOpen(G, px + dx, py + dy) && SelectionController.NodeAt(W, c) < 0 && BuildingPicker.SlotAt(B, G, c) < 0) return c;
                }
        throw new InvalidOperationException("no open ground");
    }

    private async Task Select(List<EntityHandle> units)
    {
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        await Frames();
    }

    private void Key(Godot.Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void PushClick(Vector2 at)
    {
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void LeftClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private async Task<Vector2> OnScreen(System.Numerics.Vector2 ground)
    {
        FocusOn(ground);
        await Frame();
        return _camera.UnprojectPosition(new Vector3(ground.X, TerrainHeight.At(W.Heightmap, ground.X, ground.Y), ground.Y));
    }

    private void FocusOn(System.Numerics.Vector2 ground)
    {
        _camera.SetZoom(30f);
        _camera.SetFocus(ground.X, ground.Y);
    }

    private System.Numerics.Vector2 Pick(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        if (!GroundPicker.TryPick(W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit))
            return new(float.NaN, float.NaN);
        return new(hit.X, hit.Z);
    }

    private async Task SoundGap() => await ToSignal(GetTree().CreateTimer(Sfx.MinGapMs / 1000.0 + 0.02), SceneTreeTimer.SignalName.Timeout);

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        await Frames();
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/card-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
