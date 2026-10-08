using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

/// <summary>
/// QA M3-V3 (2026-10-07-1415): attacks on the selection panel, production card, queue strip, rally and the BUG-0108 /
/// 0109 / 0110 fixes in the real Match scene. ui.json roots and the forward <c>requires</c> keys; the card's greying vs
/// <c>CanTrain</c> / <c>CanResearch</c> after every real frame across hostile states (pop exactly at the cap, queue full,
/// a tech queued at another building, money moved between the tick and the frame, cost - 1 / cost / cost + 1); the strip
/// vs the store every frame through cancel at head / tail / middle and a building destroyed mid-queue; the right-click
/// context point at every pixel of a hall's box at 20 / 30 / 60 m zoom and on sloped ground; a 200-point flick-then-click
/// sweep of the ghost, also through the real input path (<c>Input.ParseInputEvent</c>); minimap right-click with a
/// building, units or a site selected; allocation over 300 frames idle and with repair / production / gathering running.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV3Test.tscn</c>; prints "QA M3-V3 TEST PASS" and exits 0, else each failure and exit 1. Rows for open bugs print "QA M3-V3 KNOWN BUG-nnnn" and don't fail unless run with <c>-- --strict</c>.</remarks>
public partial class QaV3Test : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private CommandCard _card = null!;
    private BuildGhost _ghost = null!;
    private SelectionPanel _panel = null!;
    private ProductionQueueStrip _strip = null!;
    private RallyMarker _rally = null!;
    private ResourceBar _bar = null!;
    private Minimap _mini = null!;

    private int _hall, _barracks, _armory, _armory2;
    private readonly HashSet<string> _greySeen = new();

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            UiJsonRows();
            _ui = UiText.Shared!;
            await StartMatch();
            await BareHallGreying();
            await SpawnBuildings();
            await GreyingEveryFrame();
            await QueueStripEveryFrame();
            await RightClickSweep();
            await FlickThenClick();
            await FlickThroughRealInput();
            await MinimapRally();
            await Allocation();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Input.ActionRelease("order_queue");
        foreach (string f in _failures.Take(60)) GD.Print($"QA M3-V3 TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"QA M3-V3 TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("QA M3-V3 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // ---- ui.json ----

    // BUG-0110 roots; the forward `requires` key in each reason section (missing / empty / non-string = one error naming it);
    // a missing key for an existing member of each new section; a section of the wrong kind; extra keys accepted.
    private void UiJsonRows()
    {
        foreach (string root in new[] { "[]", "null", "42", "\"ui\"", "true", "false", "[{}]", "3.5e9" })
        {
            var e = new List<string>();
            UiText? r = UiText.Parse(root, e);
            Check(r == null && e.Count == 1, $"BUG-0110 root '{root}': {(r == null ? "null" : "a UiText")}, {e.Count} errors [{string.Join("; ", e)}]");
        }
        var e0 = new List<string>();
        Check(UiText.Parse("{}", e0) == null && e0.Count >= 7, $"empty object: {e0.Count} errors");

        string good = File.ReadAllText(UiText.DefaultPath).Replace("\r\n", "\n");
        var ok = new List<string>();
        Check(UiText.Parse(good, ok) != null && ok.Count == 0, $"shipped ui.json: {string.Join("; ", ok)}");
        using var doc = System.Text.Json.JsonDocument.Parse(good);
        foreach (string section in new[] { "placement", "train", "research" })
        {
            // Rewrite one key of one section through a JSON round trip, so the row doesn't depend on the file's formatting.
            foreach ((string label, string? value) in new[] { ("missing", (string?)null), ("empty", "\"\""), ("number", "7"), ("null", "null"), ("object", "{}") })
            {
                string json = Rewrite(doc, section, UiText.ForwardKey, value);
                var e = new List<string>();
                UiText? r = UiText.Parse(json, e);
                Check(r == null && e.Count == 1 && e[0].Contains($"{section}.{UiText.ForwardKey}"), $"{section}.requires {label}: {(r == null ? "null" : "a UiText")} [{string.Join("; ", e)}]");
            }
        }
        // A missing key for an existing member still fails fast, in each section.
        foreach ((string section, string key) in new[] { ("train", "locked_by_requirement"), ("train", "cannot_afford"), ("research", "already_queued"), ("research", "not_researched_here"), ("states", "returning"), ("hud", "needs"), ("placement", "seals_ground") })
        {
            var e = new List<string>();
            UiText? r = UiText.Parse(Rewrite(doc, section, key, null), e);
            Check(r == null && e.Count == 1 && e[0].Contains($"{section}.{key}"), $"missing {section}.{key}: [{string.Join("; ", e)}]");
        }
        // Wrong-kind sections are errors (not silently empty texts).
        foreach (string section in new[] { "train", "research", "states", "hud" })
        {
            var e = new List<string>();
            UiText? r = UiText.Parse(ReplaceSection(doc, section, "[]"), e);
            Check(r == null && e.Count >= 1 && e.Any(x => x.Contains(section)), $"section {section} as []: [{string.Join("; ", e)}]");
            e.Clear();
            r = UiText.Parse(ReplaceSection(doc, section, null), e);
            Check(r == null && e.Count >= 1 && e.Any(x => x.Contains(section)), $"section {section} missing: [{string.Join("; ", e)}]");
        }
        // Extra keys in every section are accepted.
        var ex = new List<string>();
        string extra = good;
        foreach (string section in new[] { "placement", "train", "research", "states", "hud" })
            extra = Rewrite(System.Text.Json.JsonDocument.Parse(extra), section, "zz_future_member", "\"Later\"");
        Check(UiText.Parse(extra, ex) != null && ex.Count == 0, $"extra keys refused: {string.Join("; ", ex)}");
    }

    // The JSON with `section.key` set to `value` (raw JSON) or removed (null).
    private static string Rewrite(System.Text.Json.JsonDocument doc, string section, string key, string? value)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        var s = root[section]!.AsObject();
        s.Remove(key);
        if (value != null) s[key] = System.Text.Json.Nodes.JsonNode.Parse(value);
        return root.ToJsonString();
    }

    private static string ReplaceSection(System.Text.Json.JsonDocument doc, string section, string? value)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        root.Remove(section);
        if (value != null) root[section] = System.Text.Json.Nodes.JsonNode.Parse(value);
        return root.ToJsonString();
    }

    // ---- match ----

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" }));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _strip = _match.GetNode<ProductionQueueStrip>("Hud/QueueStrip");
        _rally = _match.GetNode<RallyMarker>("World3D/RallyMarker");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        Tick(2);
        await Frames();
    }

    private async Task SpawnBuildings()
    {
        _hall = HallSlot(0);
        int f = W.FactionOf(0);
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        int Spawn(BuildingSlot slot)
        {
            int type = StartBase.BuildingOfSlot(_data, f, slot);
            int a = FreeAnchor(type, taken, 10f, 45f);
            taken.Add(a);
            _sim.Enqueue(Command.SpawnBuilding(0, type, G.CellCenter(a % G.Width, a / G.Width)));
            return a;
        }
        int[] anchors = { Spawn(BuildingSlot.InfantryHall), Spawn(BuildingSlot.Forge), Spawn(BuildingSlot.Forge) };
        Tick(2);
        int[] slots = anchors.Select(a => B.SlotAt(a % G.Width, a / G.Width)).ToArray();
        if (!Check(slots.All(s => s >= 0), "spawn failed")) throw new InvalidOperationException("no buildings");
        (_barracks, _armory, _armory2) = (slots[0], slots[1], slots[2]);
        SetMoney(5000, 5000);
        await Frames();
    }

    // ---- greying ----

    // BUG-0124: before any hall stands, Age II at the Town Hall reads research.requires ("Locked") every frame whatever the
    // money, a press enqueues nothing, and a Research command for it is dropped by the sim.
    private async Task BareHallGreying()
    {
        _hall = HallSlot(0);
        _sel.SelectBuilding(_hall);
        var rng = new Random(124);
        var into = new ProductionEntry[15];
        int age = _data.FindTech("age_ii"), frames = 0, mismatch = 0, locked = 0;
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, _hall);
        for (int s = 0; s < 60; s++)
        {
            int op = rng.Next(4);
            if (op == 0) SetMoney(rng.Next(0, 800), rng.Next(0, 500));
            else if (op == 1) SetMoney(50000, 50000);
            else if (op == 2) _sim.Enqueue(Command.Research(0, hc, age));
            Tick(rng.Next(0, 4));
            await Frame();
            frames++;
            CheckCardFrame(frames, _greySeen, into, ref mismatch);
            int cell = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Research && _card.TypeAt(i) == age, -1);
            if (cell >= 0 && _card.ReasonAt(cell) == (int)ResearchError.Requires && _card.ButtonAt(cell).Disabled && _card.CostAt(cell).Text == "Locked") locked++;
            if (cell >= 0 && s % 10 == 0)
            {
                int start = _sim.PendingCommandCount;
                _card.Press(cell);
                Check(_sim.PendingCommandCount == start, $"bare hall: an Age II press enqueued {Describe(Pending(start))}");
            }
        }
        Tick(2);
        Check(mismatch == 0 && locked == frames && B.QueueCount[_hall] == 0 && !W.HasTech(0, age),
            $"bare hall: {mismatch} mismatches, Age II 'Locked' in {locked} of {frames} frames, queue {B.QueueCount[_hall]}");
        GD.Print($"bare hall greying: {frames} frames, Age II locked in {locked}, research presses / commands enqueued nothing");
    }

    // After a real frame (the card's own _Process), every production cell equals the sim's verdict now. States: random
    // money incl. cost - 1 / cost / cost + 1 of a random entry, set between the tick and the frame; pop exactly at the
    // cap; queues filled to 5; a tech queued at the other armory; techs completed; cancels; selection hops.
    private async Task GreyingEveryFrame()
    {
        var rng = new Random(31);
        int[] buildings = { _hall, _barracks, _armory, _armory2 };
        HashSet<string> seen = _greySeen; // the bare-hall phase's verdicts count too (research Requires)
        int cells = 0, frames = 0, atCapFrames = 0, mismatch = 0;
        var into = new ProductionEntry[15];
        // One Forge tech researched up front, so AlreadyResearched shows.
        int armor = _data.FindTech("armor_1");
        if (armor >= 0)
        {
            _sim.Enqueue(Command.Research(0, SelectionController.SiteCenter(W, _armory), armor));
            Tick(_data.Techs[armor].ResearchTicks + 4);
            Check(W.HasTech(0, armor), "armor_1 not researched");
        }
        for (int s = 0; s < 400; s++)
        {
            if (s == 200) RaisePopCap(); // the second half starts under the cap again
            int op = rng.Next(8);
            if (op == 3 && rng.Next(3) != 0) op = 7;
            int k = buildings[rng.Next(buildings.Length)];
            int n = ProductionMenu.Entries(_data, B.TypeId[k], into);
            System.Numerics.Vector2 c = SelectionController.SiteCenter(W, k);
            switch (op)
            {
                case 0:
                case 1:
                    if (n > 0)
                    {
                        ProductionEntry e = into[rng.Next(n)];
                        for (int j = rng.Next(1, 6); j > 0; j--) _sim.Enqueue(e.IsTech ? Command.Research(0, c, e.TypeId) : Command.Train(0, c, e.TypeId));
                    }
                    break;
                case 2:
                    if (B.QueueCount[k] > 0) _sim.Enqueue(Command.CancelTrain(0, c, rng.Next(2) == 0 ? 0 : B.QueueCount[k] - 1));
                    break;
                case 3:
                    // Pop exactly at the cap (or just under / over by a worker's spawn).
                    FillPopTo(W.HalfPopCap[0] - 2 * rng.Next(0, 2));
                    break;
                case 4:
                    // The same tech at both armories: the second must read AlreadyQueued.
                    int melee = _data.FindTech(rng.Next(2) == 0 ? "melee_weapons_1" : "armor_1");
                    if (melee >= 0)
                    {
                        _sim.Enqueue(Command.Research(0, SelectionController.SiteCenter(W, _armory), melee));
                        _sim.Enqueue(Command.Research(0, SelectionController.SiteCenter(W, _armory2), melee));
                    }
                    break;
                default:
                    SetMoney(rng.Next(0, 800), rng.Next(0, 500));
                    break;
            }
            Tick(rng.Next(0, 6));
            // Money moved after the tick, before the frame: exactly around a random entry's cost.
            if (n > 0 && rng.Next(3) == 0)
            {
                ProductionEntry e = into[rng.Next(n)];
                int g = e.IsTech ? _data.Techs[e.TypeId].CostGold : _data.Units[e.TypeId].CostGold;
                int w = e.IsTech ? _data.Techs[e.TypeId].CostWood : _data.Units[e.TypeId].CostWood;
                int d = rng.Next(-1, 2);
                SetMoney(Math.Max(0, g + d), Math.Max(0, w + (d == 0 ? 0 : rng.Next(-1, 2))));
            }
            if (rng.Next(3) == 0) _sel.SelectBuilding(buildings[rng.Next(buildings.Length)]);
            else if (_sel.SelectedBuilding < 0) _sel.SelectBuilding(k);
            await Frame();
            frames++;
            if (W.HalfPop[0] == W.HalfPopCap[0]) atCapFrames++;
            cells += CheckCardFrame(frames, seen, into, ref mismatch);
        }
        Check(mismatch == 0, $"greying: {mismatch} mismatching frames/cells");
        GD.Print($"greying every frame: {frames} frames, {cells} cells, {atCapFrames} frames at the pop cap; seen {string.Join(", ", seen.OrderBy(x => x))}");
        foreach (string w in new[] { "train QueueFull", "train CannotAfford", "train None", "research AlreadyQueued", "research AlreadyResearched", "research CannotAfford", "research QueueFull", "research Requires" })
            Check(seen.Any(x => x == w || x == w + " @cap"), $"greying never saw {w}");
        Check(seen.Any(x => x.EndsWith("@cap")), "greying never ran at the pop cap");
        await ClearQueues(buildings);
    }

    // The card after a real frame vs the sim's verdict now, cell by cell: reason, Disabled, the cost / reason text, and the
    // layout equal to the selected building's menu. Returns the production cells checked.
    private int CheckCardFrame(int frames, HashSet<string> seen, ProductionEntry[] into, ref int mismatch)
    {
        int sel = _sel.SelectedBuilding;
        int cells = 0;
        for (int i = 0; i < CommandCard.Cells; i++)
        {
            CardCommand a = _card.ActionAt(i);
            if (a is not (CardCommand.Train or CardCommand.Research)) continue;
            int reason;
            string text;
            if (a == CardCommand.Train)
            {
                W.CanTrain(0, sel, _card.TypeAt(i), out TrainError e);
                reason = (int)e;
                text = e == TrainError.None ? $"{_data.Units[_card.TypeAt(i)].CostGold} / {_data.Units[_card.TypeAt(i)].CostWood}" : _ui.TrainText(e);
                seen.Add($"train {e}{(W.HalfPop[0] >= W.HalfPopCap[0] ? " @cap" : "")}");
            }
            else
            {
                W.CanResearch(0, sel, _card.TypeAt(i), out ResearchError e);
                // M3-V4 (BUG-0126): the card shows Researched / In a queue over the sim's earlier reasons.
                e = ProductionMenu.ShownResearchReason(e, W.HasTech(0, _card.TypeAt(i)), ProductionMenu.IsTechQueued(W.Buildings, 0, _card.TypeAt(i)));
                reason = (int)e;
                text = e == ResearchError.None ? $"{_data.Techs[_card.TypeAt(i)].CostGold} / {_data.Techs[_card.TypeAt(i)].CostWood}" : _ui.ResearchText(e);
                seen.Add($"research {e}");
            }
            cells++;
            // BUG-0123: a greyed button's name and hotkey are dimmed (<= 60 % alpha), an enabled one's are not.
            float alpha = reason != 0 ? CommandCard.DimAlpha : 1f;
            bool dim = _card.NameAt(i).Modulate.A == alpha && _card.HintAt(i).Modulate.A == alpha && (reason == 0 || alpha <= 0.6f);
            bool same = _card.ReasonAt(i) == reason && _card.ButtonAt(i).Disabled == (reason != 0) && _card.CostAt(i).Text == text && _card.ButtonAt(i).Visible && dim;
            if (!same && mismatch++ < 6)
                Check(false, $"greying frame {frames}: building {sel} cell {i} ({a} {_card.TypeAt(i)}) shows {_card.ReasonAt(i)} disabled {_card.ButtonAt(i).Disabled} '{_card.CostAt(i).Text}' alpha {_card.NameAt(i).Modulate.A}/{_card.HintAt(i).Modulate.A}, sim {reason} '{text}'");
        }
        // The card's layout is the selected building's menu.
        int want = sel >= 0 ? ProductionMenu.Entries(_data, B.TypeId[sel], into) : 0;
        int shown = Enumerable.Range(0, CommandCard.Cells).Count(i => _card.ActionAt(i) is CardCommand.Train or CardCommand.Research);
        if (shown != want && mismatch++ < 6) Check(false, $"greying frame {frames}: card shows {shown} production cells, menu has {want}");
        return cells;
    }

    // ---- queue strip ----

    // The strip vs the store after every frame through cancel at head / tail / middle and the building destroyed mid-queue.
    private async Task QueueStripEveryFrame()
    {
        SetMoney(50000, 50000);
        RaisePopCap();
        int laborer = _data.UnitsTrainedAt(B.TypeId[_hall])[0];
        int soldier = _data.UnitsTrainedAt(B.TypeId[_barracks])[0];
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, _hall), bc = SelectionController.SiteCenter(W, _barracks);
        _sel.SelectBuilding(_hall);
        for (int i = 0; i < 5; i++) _sim.Enqueue(Command.Train(0, hc, laborer));
        _sim.Enqueue(Command.Train(0, hc, laborer)); // a sixth: dropped (queue full)
        int frames = 0, bad = 0;
        var script = new Dictionary<int, Action>
        {
            [6] = () => _sim.Enqueue(Command.CancelTrain(0, hc, 0)),                    // head
            [12] = () => _sim.Enqueue(Command.CancelTrain(0, hc, B.QueueCount[_hall] - 1)), // tail
            [18] = () => _sim.Enqueue(Command.CancelTrain(0, hc, 1)),                    // middle
            [24] = () => { _sim.Enqueue(Command.CancelTrain(0, hc, 0)); _sim.Enqueue(Command.CancelTrain(0, hc, 0)); }, // two heads in one tick
            [30] = () => { for (int i = 0; i < 5; i++) _sim.Enqueue(Command.Train(0, hc, laborer)); },
        };
        for (int f = 0; f < 120; f++)
        {
            if (script.TryGetValue(f, out Action? act)) act();
            Tick(f % 3);
            await Frame();
            frames++;
            if (!StripMatches(_hall, out string why) && bad++ < 6) Check(false, $"strip frame {f}: {why}");
        }
        // Barracks: 5 soldiers, selected; destroyed mid-queue.
        _sel.SelectBuilding(_barracks);
        for (int i = 0; i < 5; i++) _sim.Enqueue(Command.Train(0, bc, soldier));
        Tick(10);
        await Frame();
        Check(StripMatches(_barracks, out string w0) && _strip.ShownCount == 5, $"barracks strip before destruction: {w0}, shown {_strip.ShownCount}");
        DamageMethod.Invoke(B, new object[] { B.HandleOf(_barracks), 1_000_000 });
        Check(!B.Alive[_barracks], "barracks didn't die");
        await Frame();
        bool gone = _strip.ShownCount == 0 && Enumerable.Range(0, QueueStrip.MaxItems).All(k => !_strip.ItemButton(k).Visible)
            && _sel.SelectedBuilding == -1 && !_rally.Visible && _panel.ShownKind == 0
            && Enumerable.Range(0, CommandCard.Cells).All(i => _card.ActionAt(i) is not (CardCommand.Train or CardCommand.Research));
        Check(gone, $"destroyed mid-queue, one frame later: strip {_strip.ShownCount}, selected {_sel.SelectedBuilding}, panel {_panel.ShownKind}, card cell 0 {_card.ActionAt(0)}");
        int s0 = _sim.PendingCommandCount;
        _strip.ItemPressed(0);
        _card.Press(0);
        Check(_sim.PendingCommandCount == s0, "a press on the dead barracks' strip / card enqueued");
        GD.Print($"queue strip every frame: {frames} frames on the hall, barracks destroyed with 5 queued -> strip hidden in 1 frame");
        // Respawn a barracks for later rows.
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        int type = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.InfantryHall);
        int a = FreeAnchor(type, taken, 10f, 45f);
        _sim.Enqueue(Command.SpawnBuilding(0, type, G.CellCenter(a % G.Width, a / G.Width)));
        Tick(2);
        _barracks = B.SlotAt(a % G.Width, a / G.Width);
        await ClearQueues(new[] { _hall });
    }

    private bool StripMatches(int slot, out string why)
    {
        int n = B.QueueCount[slot];
        why = "";
        if (_strip.ShownCount != n) { why = $"shows {_strip.ShownCount}, queue {n}"; return false; }
        for (int k = 0; k < QueueStrip.MaxItems; k++)
        {
            bool vis = _strip.ItemButton(k).Visible;
            if (k >= n) { if (vis) { why = $"item {k} visible past {n}"; return false; } continue; }
            if (!vis || _strip.ShownType(k) != B.QueueTypeAt(slot, k) || _strip.ShownIsTech(k) != B.QueueIsTechAt(slot, k))
            {
                why = $"item {k}: shows {_strip.ShownType(k)}/{_strip.ShownIsTech(k)} vis {vis}, store {B.QueueTypeAt(slot, k)}/{B.QueueIsTechAt(slot, k)}";
                return false;
            }
        }
        float fill = n == 0 ? 0f : Math.Clamp(B.Progress[slot] / (float)B.ItemTicks(slot, 0), 0f, 1f);
        if (!Mathf.IsEqualApprox(_strip.ShownFill, fill) || !Mathf.IsEqualApprox(_strip.HeadBar.Size.X, (ProductionQueueStrip.ItemWidth - 2f) * fill))
        {
            why = $"fill {_strip.ShownFill} bar {_strip.HeadBar.Size.X}, store {fill}";
            return false;
        }
        return true;
    }

    // ---- BUG-0108 ----

    // Every pixel of a damaged hall's projected box at 20 / 30 / 60 m zoom, and of a house on the most sloped open
    // footprint near a cliff or ramp: where the own-box pick (what a left click selects) is the building, the right
    // click's context target must be that building's footprint centre. Also counts pixels where terrain in front of the box
    // hides it (the ray meets the ground first, outside the footprint) yet the box is still picked.
    private async Task RightClickSweep()
    {
        int max = _data.Buildings[B.TypeId[_hall]].Hp;
        if (B.Hp[_hall] >= max) DamageMethod.Invoke(B, new object[] { B.HandleOf(_hall), max / 4 });
        await Select(Workers().Take(3).ToList());
        foreach (float zoom in new[] { 20f, 30f, 60f }) await SweepBox(_hall, zoom, $"hall @{zoom}");

        // A house on sloped ground near a cliff / ramp.
        var flags = (NavFlags[])typeof(NavGrid).GetField("_flags", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!;
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        BuildingDef hd = _data.Buildings[house];
        int best = -1;
        float bestSlope = -1f;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            int x = cell % G.Width, y = cell / G.Width;
            if (x < 2 || y < 2 || x + hd.FootprintWidth + 2 >= G.Width || y + hd.FootprintHeight + 2 >= G.Height) continue;
            bool nearLip = false;
            for (int dy = -2; dy <= hd.FootprintHeight + 1 && !nearLip; dy++)
                for (int dx = -2; dx <= hd.FootprintWidth + 1 && !nearLip; dx++)
                    nearLip = (flags[(y + dy) * G.Width + x + dx] & (NavFlags.Cliff | NavFlags.Ramp)) != 0;
            if (!nearLip || !B.Fits(house, cell)) continue;
            float x0 = x * MapConstants.CellSize, y0 = y * MapConstants.CellSize, x1 = x0 + hd.FootprintWidth * MapConstants.CellSize, y1 = y0 + hd.FootprintHeight * MapConstants.CellSize;
            float h0 = TerrainHeight.At(W.Heightmap, x0, y0), h1 = TerrainHeight.At(W.Heightmap, x1, y0), h2 = TerrainHeight.At(W.Heightmap, x0, y1), h3 = TerrainHeight.At(W.Heightmap, x1, y1);
            float slope = Math.Max(Math.Max(h0, h1), Math.Max(h2, h3)) - Math.Min(Math.Min(h0, h1), Math.Min(h2, h3));
            if (slope > bestSlope) (best, bestSlope) = (cell, slope);
        }
        if (!Check(best >= 0, "no lip footprint")) return;
        _sim.Enqueue(Command.SpawnBuilding(0, house, G.CellCenter(best % G.Width, best / G.Width)));
        Tick(2);
        int lip = B.SlotAt(best % G.Width, best / G.Width);
        if (!Check(lip >= 0, $"lip house not spawned at {best}")) return;
        int hmax = _data.Buildings[house].Hp;
        DamageMethod.Invoke(B, new object[] { B.HandleOf(lip), hmax / 3 });
        GD.Print($"lip house at cell {best}, corner height spread {bestSlope:0.##} m");
        foreach (float zoom in new[] { 20f, 60f }) await SweepBox(lip, zoom, $"lip house @{zoom}");

        // M3-V3b: a house just north of a rise (the camera looks north, so the higher ground is between it and the
        // camera): the rise hides the box's lower part, and those pixels must not pick it (PickRay's terrain occlusion).
        int behind = -1;
        float bestRise = 0f;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            int x = cell % G.Width, y = cell / G.Width;
            if (x < 2 || y < 2 || x + hd.FootprintWidth + 2 >= G.Width || y + hd.FootprintHeight + 4 >= G.Height || !B.Fits(house, cell)) continue;
            float h0 = TerrainHeight.At(W.Heightmap, (x + hd.FootprintWidth / 2f) * MapConstants.CellSize, (y + hd.FootprintHeight / 2f) * MapConstants.CellSize);
            float front = 0f;
            for (int dx = 0; dx < hd.FootprintWidth; dx++)
                for (int dy = 1; dy <= 2; dy++)
                    front = Math.Max(front, TerrainHeight.At(W.Heightmap, (x + dx + 0.5f) * MapConstants.CellSize, (y + hd.FootprintHeight + dy - 0.5f) * MapConstants.CellSize) - h0);
            if (front > bestRise) (behind, bestRise) = (cell, front);
        }
        if (!Check(behind >= 0 && bestRise >= 2f, $"no house spot behind a rise (best {bestRise:0.#} m)")) return;
        _sim.Enqueue(Command.SpawnBuilding(0, house, G.CellCenter(behind % G.Width, behind / G.Width)));
        Tick(2);
        int hidden = B.SlotAt(behind % G.Width, behind / G.Width);
        if (!Check(hidden >= 0, $"house behind the rise not spawned at {behind}")) return;
        DamageMethod.Invoke(B, new object[] { B.HandleOf(hidden), hmax / 3 });
        GD.Print($"house behind a rise at cell {behind}: ground {bestRise:0.#} m higher just south of it");
        foreach (float zoom in new[] { 20f, 30f }) await SweepBox(hidden, zoom, $"house behind a rise @{zoom}");
    }

    private async Task SweepBox(int slot, float zoom, string what)
    {
        System.Numerics.Vector2 c = SelectionController.SiteCenter(W, slot);
        _camera.SetZoom(zoom);
        _camera.SetFocus(c.X, c.Y);
        await Frames();
        BuildingDef def = _data.Buildings[B.TypeId[slot]];
        int a = B.Cell[slot];
        float cs = MapConstants.CellSize;
        float x0 = a % G.Width * cs, z0 = a / G.Width * cs, x1 = x0 + def.FootprintWidth * cs, z1 = z0 + def.FootprintHeight * cs;
        float baseY = TerrainHeight.At(W.Heightmap, c.X, c.Y), top = baseY + BuildingViews.BoxHeight;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (float x in new[] { x0, x1 })
            foreach (float y in new[] { baseY, top })
                foreach (float z in new[] { z0, z1 })
                {
                    Vector2 p = _camera.UnprojectPosition(new Vector3(x, y, z));
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
        int onBox = 0, wrong = 0, occluded = 0, sent = 0, badSent = 0;
        string firstWrong = "";
        for (int py = (int)minY - 1; py <= (int)maxY + 1; py++)
            for (int px = (int)minX - 1; px <= (int)maxX + 1; px++)
            {
                var pix = new Vector2(px + 0.5f, py + 0.5f);
                if (_sel.PickBuilding(pix) != slot) continue;
                onBox++;
                bool ok = _sel.ContextTarget(pix, out System.Numerics.Vector2 at, out int hit) && hit == slot && at == c;
                if (!ok)
                {
                    if (wrong++ == 0) firstWrong = $"pixel {pix}: target {at} building {hit}";
                }
                // Occlusion: the ground pick lands in front of the box, outside the footprint.
                System.Numerics.Vector2 g = Pick(pix);
                if (float.IsFinite(g.X) && BuildingPicker.SlotAt(B, G, g) != slot)
                {
                    Vector3 o = _camera.ProjectRayOrigin(pix);
                    float groundDist = new Vector3(g.X, TerrainHeight.At(W.Heightmap, g.X, g.Y), g.Y).DistanceTo(o);
                    float boxDist = EntryDistance(o, _camera.ProjectRayNormal(pix), x0, x1, baseY, top, z0, z1);
                    if (groundDist < boxDist - 0.05f) occluded++;
                }
                // The real right-click path on a sample of the pixels.
                if (onBox % 211 == 1)
                {
                    int start = _sim.PendingCommandCount;
                    RightClick(pix);
                    List<Command> s = Pending(start);
                    sent++;
                    if (!(s.Count == 3 && s.All(x => x.Kind == CommandKind.Repair && x.Position == c)) && badSent++ == 0)
                        Check(false, $"{what}: real right-click at {pix} sent {Describe(s)}");
                    if (_sim.PendingCommandCount > W.Config.CommandCapacity / 2) Tick(2);
                }
            }
        Tick(2);
        Check(onBox > 0 && wrong == 0, $"BUG-0108 {what}: {wrong} of {onBox} box pixels resolve elsewhere ({firstWrong})");
        GD.Print($"BUG-0108 sweep {what}: {onBox} box pixels, {wrong} wrong, {occluded} terrain-occluded but picked, {sent} real right-clicks ({badSent} bad)");
        // M3-V3b: PickRay lets terrain occlude, so no pixel whose ray meets the ground in front of the box picks it.
        Check(occluded == 0, $"{what}: {occluded} pixels where terrain hides the box still pick the building");
        _camera.SetZoom(30f);
    }

    private static float EntryDistance(Vector3 o, Vector3 d, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        float t0 = 0f, t1 = float.MaxValue;
        void Slab(float oo, float dd, float lo, float hi)
        {
            if (dd == 0f) { if (oo < lo || oo > hi) t0 = float.MaxValue; return; }
            float a = (lo - oo) / dd, b = (hi - oo) / dd;
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a);
            t1 = Math.Min(t1, b);
        }
        Slab(o.X, d.X, x0, x1);
        Slab(o.Y, d.Y, y0, y1);
        Slab(o.Z, d.Z, z0, z1);
        return t0 <= t1 ? t0 * d.Length() : float.MaxValue;
    }

    // ---- BUG-0109 ----

    // 200 flicks: the ghost drawn at A, then a click at B before the next Sync. A Build goes only to B's own anchor (never
    // the drawn one when they differ); a green B is built when the frame's CanPlace call is free; at most one CanPlace a
    // frame. Shift keeps the ghost.
    private async Task FlickThenClick()
    {
        SetMoney(50000, 50000);
        await Select(Workers().Take(2).ToList());
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        if (!Check(_ghost.Active, "flick: no ghost")) return;
        int type = _ghost.TypeId;
        FocusOn(HallCenter(0));
        await Frames();
        Vector2 view = GetViewport().GetVisibleRect().Size;
        var rng = new Random(109);
        int built = 0, wrongAnchor = 0, ignored = 0, redOrOff = 0, maxCallsPerFrame = 0, sameAnchor = 0, missedGreen = 0;
        Input.ActionPress("order_queue");
        for (int i = 0; i < 200; i++)
        {
            Vector2 pa = new((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y * 0.7f);
            Vector2 pb = rng.Next(4) == 0 ? pa + new Vector2(rng.Next(-6, 7), rng.Next(-6, 7)) : new((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y * 0.7f);
            _ghost.ScreenOverride = pa;
            await Frame();
            int drawn = _ghost.Anchor;
            bool drawnValid = _ghost.Valid && _ghost.Visible;
            int callsAtFrame = _ghost.CanPlaceCalls;
            ulong frame = Engine.GetProcessFrames();
            int expect = AnchorUnder(pb, type);
            bool expectGreen = expect >= 0 && W.CanPlace(0, type, expect, out _);
            int ig = _ghost.ClicksIgnored, start = _sim.PendingCommandCount;
            LeftClick(pb);
            _ghost.ScreenOverride = pb; // the cursor is where it clicked
            List<Command> s = Pending(start);
            if (s.Count > 0)
            {
                built++;
                if (!s.All(x => x.Kind == CommandKind.Build && x.Position == PlacementGhost.AnchorPoint(G, expect))) wrongAnchor++;
            }
            else if (_ghost.ClicksIgnored > ig) ignored++;
            else if (expectGreen && expect != drawn) missedGreen++;
            else redOrOff++;
            if (expect == drawn) sameAnchor++;
            await Frame();
            maxCallsPerFrame = Math.Max(maxCallsPerFrame, _ghost.CanPlaceCalls - callsAtFrame);
            // Keep money and the command queue healthy; built sites are cancelled so the map stays open.
            if (_sim.PendingCommandCount > 64) Tick(2);
            if (i % 25 == 24) CancelOwnSites();
        }
        Input.ActionRelease("order_queue");
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        Tick(2);
        CancelOwnSites();
        await Frames();
        GD.Print($"BUG-0109 flick sweep (test path): 200 clicks, {built} built, {wrongAnchor} at a wrong anchor, {ignored} ignored (call spent), {missedGreen} green but not built, {redOrOff} red/off, {sameAnchor} on the drawn anchor; max CanPlace calls per frame {maxCallsPerFrame}");
        Check(wrongAnchor == 0, $"BUG-0109: {wrongAnchor} flick clicks built away from the click's anchor");
        Check(maxCallsPerFrame <= 1, $"BUG-0109: {maxCallsPerFrame} CanPlace calls in one frame");
        Check(missedGreen == 0, $"flick: {missedGreen} green clicks neither built nor counted as ignored");
    }

    // The same through the real input path: Input.ParseInputEvent delivers the click at the start of the next frame,
    // after the previous frame's Sync drew the old anchor. Reports how many clicks on a different green anchor are dropped.
    private async Task FlickThroughRealInput()
    {
        SetMoney(50000, 50000);
        await Select(Workers().Take(2).ToList());
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        if (!Check(_ghost.Active, "real flick: no ghost")) return;
        int type = _ghost.TypeId;
        FocusOn(HallCenter(0));
        await Frames();
        Vector2 view = GetViewport().GetVisibleRect().Size;
        var rng = new Random(1109);
        int built = 0, wrongAnchor = 0, greenDifferent = 0, greenDifferentDropped = 0, maxCalls = 0;
        Input.ActionPress("order_queue");
        for (int i = 0; i < 100; i++)
        {
            Vector2 pa = new((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y * 0.7f);
            Vector2 pb = new((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y * 0.7f);
            // Frame N: the cursor sweeps onto A (this frame's Sync draws A, spending its CanPlace call) and the button goes
            // down at B; Godot delivers the click at the start of the next main-loop iteration, before that frame's Sync.
            _ghost.ScreenOverride = pa;
            int expect = AnchorUnder(pb, type);
            bool green = expect >= 0 && W.CanPlace(0, type, expect, out _);
            int start = _sim.PendingCommandCount, calls = _ghost.CanPlaceCalls;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pb, GlobalPosition = pb });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pb, GlobalPosition = pb });
            int drawn = AnchorUnder(pa, type);
            await Frame();
            await Frame();
            await Frame();
            List<Command> s = Pending(start);
            maxCalls = Math.Max(maxCalls, _ghost.CanPlaceCalls - calls);
            if (i < 3) GD.Print($"real flick {i}: drawn {drawn}, click anchor {expect} green {green}, sent {s.Count}, ignored total {_ghost.ClicksIgnored}, calls +{_ghost.CanPlaceCalls - calls}");
            if (s.Count > 0)
            {
                built++;
                if (!s.All(x => x.Kind == CommandKind.Build && x.Position == PlacementGhost.AnchorPoint(G, expect))) wrongAnchor++;
            }
            if (green && expect != drawn)
            {
                greenDifferent++;
                if (s.Count == 0) greenDifferentDropped++;
            }
            if (_sim.PendingCommandCount > 64) Tick(2);
            if (i % 25 == 24) CancelOwnSites();
        }
        Input.ActionRelease("order_queue");
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        Tick(2);
        CancelOwnSites();
        await Frames();
        GD.Print($"BUG-0109 flick sweep (real input): 100 clicks, {built} built, {wrongAnchor} wrong anchor, {greenDifferentDropped} of {greenDifferent} green clicks off the drawn anchor dropped; CanPlace calls in the click frame <= {maxCalls}");
        Check(wrongAnchor == 0, $"BUG-0109 real input: {wrongAnchor} clicks built away from the click's anchor");
    }

    private int AnchorUnder(Vector2 screen, int type)
    {
        System.Numerics.Vector2 g = Pick(screen);
        if (!float.IsFinite(g.X)) return -1;
        return PlacementGhost.Anchor(G, _data.Buildings[type], g);
    }

    private void CancelOwnSites()
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && B.UnderConstruction[k]) _sim.Enqueue(Command.Cancel(0, SelectionController.SiteCenter(W, k)));
        Tick(2);
        // Workers keep their Build orders for sites that vanished: stop them.
        foreach (EntityHandle h in Workers()) _sim.Enqueue(Command.Stop(0, h));
        Tick(2);
    }

    // ---- minimap rally ----

    private async Task MinimapRally()
    {
        MinimapTransform fit = _mini.Fit;
        System.Numerics.Vector2 target = HallCenter(0) + new System.Numerics.Vector2(-18f, 14f);
        System.Numerics.Vector2 mp = fit.ToPixel(target);
        fit.TryToMap(mp, out System.Numerics.Vector2 back);
        void MiniRight() => _mini._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(mp.X, mp.Y) });

        // A building selected: one SetRally at that point, no unit order.
        _sel.SelectBuilding(_barracks);
        await Frames();
        int start = _sim.PendingCommandCount;
        MiniRight();
        List<Command> s = Pending(start);
        Check(s.Count == 1 && s[0].Kind == CommandKind.SetRally && s[0].TypeId == B.Cell[_barracks] && System.Numerics.Vector2.Distance(s[0].Position, back) < 1e-3f,
            $"minimap right-click, building selected: {Describe(s)}");
        Tick(2);
        await Frames();
        Check(_rally.ShownSlot == _barracks && _rally.ShownTarget == B.RallyPosition[_barracks], $"minimap rally flag: slot {_rally.ShownSlot}");

        // Units selected: a Move per unit, no rally.
        List<EntityHandle> two = Workers().Take(2).ToList();
        await Select(two);
        start = _sim.PendingCommandCount;
        MiniRight();
        s = Pending(start);
        Check(s.Count == 2 && s.All(x => x.Kind == CommandKind.Move), $"minimap right-click, units selected: {Describe(s)}");
        Tick(2);

        // A site selected: nothing.
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        int a = FreeAnchor(house, taken, 10f, 45f);
        _sim.Enqueue(Command.Build(0, two[0], house, PlacementGhost.AnchorPoint(G, a)));
        Tick(40);
        int site = B.SlotAt(a % G.Width, a / G.Width);
        if (Check(site >= 0 && B.UnderConstruction[site], "minimap: no site"))
        {
            _sel.SelectBuilding(site);
            await Frames();
            start = _sim.PendingCommandCount;
            MiniRight();
            Check(_sim.PendingCommandCount == start, $"minimap right-click with a site selected: {Describe(Pending(start))}");
            _sim.Enqueue(Command.Cancel(0, SelectionController.SiteCenter(W, site)));
            Tick(2);
        }
        // A building selected, then it dies: the next minimap right-click is a no-op, not a stale rally.
        _sel.SelectBuilding(_armory2);
        await Frames();
        DamageMethod.Invoke(B, new object[] { B.HandleOf(_armory2), 1_000_000 });
        start = _sim.PendingCommandCount;
        MiniRight();
        Check(_sim.PendingCommandCount == start, $"minimap right-click after the selected building died: {Describe(Pending(start))}");
        await Frames();
    }

    // ---- allocation ----

    // 300 frames with the panel, card, strip, flag and bar up: idle, then repair (hp changes every tick), then production
    // running (head fill moves every tick), then one gathering unit selected (state changes). Only the HUD Syncs are measured.
    private async Task Allocation()
    {
        SetMoney(50000, 50000);
        RaisePopCap();
        _sel.SelectBuilding(_hall);
        await Frames();
        _sim.Enqueue(Command.SetRally(0, B.Cell[_hall], HallCenter(0) + new System.Numerics.Vector2(10f, 12f)));
        Tick(2);
        void SyncAll()
        {
            _panel.Sync();
            _card.Sync();
            _strip.Sync();
            _rally.Sync(W);
            _bar.Sync(W);
        }
        // Per element (panel, card, strip, rally, bar), so a failing row names its source.
        var part = new long[5];
        long barQuiet = 0; // bar bytes on frames where no number it shows changed (must stay 0: the M2-H2 rule)
        long Measure(int n, bool tick)
        {
            Array.Clear(part);
            barQuiet = 0;
            long total = 0;
            for (int f = 0; f < n; f++)
            {
                if (tick) _sim.Tick();
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                _panel.Sync();
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                _card.Sync();
                long b2 = GC.GetAllocatedBytesForCurrentThread();
                _strip.Sync();
                long b3 = GC.GetAllocatedBytesForCurrentThread();
                _rally.Sync(W);
                int builds = _bar.Builds + _bar.PopBuilds;
                long b4 = GC.GetAllocatedBytesForCurrentThread();
                _bar.Sync(W);
                long b5 = GC.GetAllocatedBytesForCurrentThread();
                if (_bar.Builds + _bar.PopBuilds == builds) barQuiet += b5 - b4;
                part[0] += b1 - b0; part[1] += b2 - b1; part[2] += b3 - b2; part[3] += b4 - b3; part[4] += b5 - b4;
                total += b5 - b0;
            }
            return total;
        }
        string Parts() => $"panel {part[0]}, card {part[1]}, strip {part[2]}, rally {part[3]}, bar {part[4]}";
        SyncAll();
        long idle = Measure(300, tick: false);
        Check(idle == 0, $"300 idle HUD syncs allocated {idle} bytes");

        // Repair: damage the hall, three workers repair it; hp rises every tick while the hall is selected.
        int max = _data.Buildings[B.TypeId[_hall]].Hp;
        if (B.Hp[_hall] > max / 3) DamageMethod.Invoke(B, new object[] { B.HandleOf(_hall), B.Hp[_hall] - max / 3 });
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, _hall);
        foreach (EntityHandle h in Workers().Take(3)) _sim.Enqueue(Command.Repair(0, h, hc));
        _sel.SelectBuilding(_hall);
        for (int t = 0; t < 200 && !RepairRunning(); t++) _sim.Tick();
        SyncAll();
        int hp0 = B.Hp[_hall], rebuilds0 = _panel.Rebuilds;
        long repair = Measure(300, tick: true);
        int hpChanges = _panel.Rebuilds - rebuilds0;
        GD.Print($"allocation: idle {idle} B; repair {repair} B over 300 ticks (hp {hp0} -> {B.Hp[_hall]}, panel rebuilds {hpChanges}; {Parts()})");
        // BUG-0123: the panel, card, strip and flag allocate nothing while hp moves every tick. Repair spends money, so the
        // resource bar rebuilds its gold / wood text when a total changes (its documented rule); it may allocate only then.
        Check(repair - part[4] == 0 && barQuiet == 0, $"BUG-0123: 300 HUD syncs while the selected hall is repaired (hp {hp0} -> {B.Hp[_hall]}, {hpChanges} text rebuilds) allocated {repair} bytes ({Parts()}; bar on quiet frames {barQuiet})");

        // Production running: head fill moves every tick.
        int laborer = _data.UnitsTrainedAt(B.TypeId[_hall])[0];
        for (int i = 0; i < 5; i++) _sim.Enqueue(Command.Train(0, hc, laborer));
        Tick(2);
        SyncAll();
        long production = Measure(100, tick: true);
        GD.Print($"allocation: production running {production} B over 100 ticks (bar/text changes included; {Parts()})");

        // One gathering worker selected: state changes, no hp.
        EntityHandle w = Workers()[3];
        int mine = NearestMine(HallCenter(0));
        if (mine >= 0) _sim.Enqueue(Command.Gather(0, w, NodeCenter(mine)));
        await Select(new List<EntityHandle> { w });
        SetMoney(50000, 50000);
        Tick(2);
        _panel.Sync();
        long unitBytes = 0;
        int stateChanges = _panel.Rebuilds;
        for (int f = 0; f < 300; f++)
        {
            _sim.Tick();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            _panel.Sync();
            _strip.Sync();
            _rally.Sync(W);
            unitBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
        }
        GD.Print($"allocation: gathering worker panel {unitBytes} B over 300 ticks ({_panel.Rebuilds - stateChanges} state rewrites)");
        Check(unitBytes == 0, $"one gathering worker's panel allocated {unitBytes} bytes over 300 ticks");
    }

    private bool RepairRunning()
    {
        int max = _data.Buildings[B.TypeId[_hall]].Hp;
        int before = B.Hp[_hall];
        _sim.Tick();
        return B.Hp[_hall] > before && B.Hp[_hall] < max;
    }

    // ---- helpers ----

    private void FillPopTo(int halfTarget)
    {
        int laborer = _data.UnitsTrainedAt(B.TypeId[_hall])[0];
        int hp = _data.Units[laborer].HalfPop;
        System.Numerics.Vector2 near = OpenGroundNear(HallCenter(0), 9f);
        for (int guard = 0; guard < 40 && W.HalfPop[0] + hp <= halfTarget; guard++)
        {
            _sim.Enqueue(Command.SpawnUnit(0, laborer, near + new System.Numerics.Vector2(guard % 6 * 1.1f, guard / 6 * 1.1f)));
            Tick(2);
        }
    }

    private void RaisePopCap()
    {
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        for (int i = 0; i < 4; i++)
        {
            int a = FreeAnchor(house, taken, 10f, 60f);
            if (a < 0) break;
            taken.Add(a);
            _sim.Enqueue(Command.SpawnBuilding(0, house, G.CellCenter(a % G.Width, a / G.Width)));
        }
        Tick(2);
    }

    private async Task ClearQueues(int[] buildings)
    {
        foreach (int k in buildings)
            if (B.Alive[k])
                for (int i = B.QueueCount[k] - 1; i >= 0; i--) _sim.Enqueue(Command.CancelTrain(0, SelectionController.SiteCenter(W, k), i));
        Tick(2);
        await Frames();
    }

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        var g = (int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!;
        var w = (int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!;
        g[0] = gold;
        w[0] = wood;
    }

    private int FreeAnchor(int type, List<int> taken, float minD, float maxD)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 near = HallCenter(0);
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < minD || d > maxD) continue;
            int x = cell % G.Width, y = cell / G.Width;
            if (taken.Any(t => Math.Abs(t % G.Width - x) < 8 && Math.Abs(t / G.Width - y) < 8)) continue;
            bool ok = W.CanPlace(0, type, cell, out PlacementError r);
            // A locked type answers Requires before any map rule: probe the map rule itself (the dev spawn ignores requirements).
            if (!ok && r != PlacementError.CannotAfford && !(r == PlacementError.Requires && B.Fits(type, cell))) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 3f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 3f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        return best;
    }

    private List<Command> Pending(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private static string Describe(List<Command> list) =>
        $"[{string.Join(", ", list.Select(c => $"{c.Kind}{(c.IsQueued ? "+q" : "")} u{c.Unit.Index} t{c.TypeId} ({c.Position.X:0.##},{c.Position.Y:0.##})"))}]";

    private List<EntityHandle> Workers()
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot == UnitSlot.Worker) list.Add(new EntityHandle(i, U.Generation[i]));
        return list;
    }

    private int HallSlot(int player)
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == player && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException($"player {player} has no hall");
    }

    private System.Numerics.Vector2 HallCenter(int player) => SelectionController.SiteCenter(W, HallSlot(player));

    private int NearestMine(System.Numerics.Vector2 from)
    {
        ResourceStore r = W.Resources;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i] || _data.Resources[r.TypeId[i]].Resource != ResourceKind.Gold) continue;
            float d = System.Numerics.Vector2.Distance(NodeCenter(i), from);
            if (d < bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    private System.Numerics.Vector2 NodeCenter(int node)
    {
        ResourceDef def = _data.Resources[W.Resources.TypeId[node]];
        int c = W.Resources.Cell[node];
        return new System.Numerics.Vector2((c % G.Width + def.FootprintWidth / 2f) * MapConstants.CellSize, (c / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize);
    }

    private System.Numerics.Vector2 OpenGroundNear(System.Numerics.Vector2 p, float min)
    {
        G.WorldToCell(p, out int px, out int py);
        for (int r = (int)(min / MapConstants.CellSize); r < 30; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    System.Numerics.Vector2 c = G.CellCenter(px + dx, py + dy);
                    if (!StartLayout.IsOpen(G, px + dx, py + dy) || SelectionController.NodeAt(W, c) >= 0 || BuildingPicker.SlotAt(B, G, c) >= 0) continue;
                    return c;
                }
        throw new InvalidOperationException("no open ground");
    }

    private async Task Select(List<EntityHandle> units)
    {
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
    }

    private void Key(Godot.Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
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

    private void Known(string bug, bool ok, string message)
    {
        if (ok)
        {
            GD.Print($"QA M3-V3 KNOWN {bug} row now passes: {message}");
            return;
        }
        GD.Print($"QA M3-V3 KNOWN {bug} (open): {message}");
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--strict") >= 0) _failures.Add($"{bug}: {message}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
