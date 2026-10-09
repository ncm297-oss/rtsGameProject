using System;
using System.Collections.Generic;
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
/// QA M3-V3b (2026-10-07-1715): the card's greying against the real M3-6 gates, not stand-ins. After every real frame
/// (the card's own <c>_Process</c>), every production cell equals <c>CanTrain</c> / <c>CanResearch</c> (reason, Disabled,
/// text, dimmed name / hotkey at &lt;= 60 % alpha) through Age II's any-of rule in five phases: a bare Town Hall, one
/// hall, two halls of the same slot plus a Forge site (all "Locked"), two distinct finished halls (live), and Age II
/// queued then a hall destroyed (the queued item survives and completes). An independent oracle recomputes the any-of
/// rule from the store (own finished buildings of distinct slots among infantry / ranged / shock hall / forge). Then
/// allocation of every HUD element's Sync (panel, card, strip, rally, bar, minimap, building views) over 300 idle frames
/// and 300 repair ticks with a queue running and a rally set. Windowed only (<c>DisplayServer</c> not headless): pixel
/// reads of a greyed vs an enabled button's name contrast and of a Malazan site vs a finished building's hue.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV3bTest.tscn</c>; windowed (adds the pixel rows):
/// <c>&amp; $env:GODOT --path game res://tests/QaV3bTest.tscn</c>. Prints "QA M3-V3b TEST PASS" and exits 0, else each
/// failure and exit 1.
/// </remarks>
public partial class QaV3bTest : Node
{
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly BuildingSlot[] AgeSlots = { BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall, BuildingSlot.Forge };

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private CommandCard _card = null!;
    private SelectionPanel _panel = null!;
    private ProductionQueueStrip _strip = null!;
    private RallyMarker _rally = null!;
    private ResourceBar _bar = null!;
    private Minimap _mini = null!;
    private BuildingViews _bviews = null!;
    private int _hall, _age;
    private int _frames, _cells, _mismatch;
    private readonly HashSet<string> _seen = new();

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;
    private bool Windowed => DisplayServer.GetName() != "headless";

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared!;
            await StartMatch();
            await GatingPhases();
            await Allocation();
            if (Windowed) await Pixels();
            else GD.Print("QA M3-V3b NOTE: headless, pixel rows skipped (run windowed)");
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M3-V3b TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"QA M3-V3b TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("QA M3-V3b TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "6", "--units", "0", "--mute" }));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _strip = _match.GetNode<ProductionQueueStrip>("Hud/QueueStrip");
        _rally = _match.GetNode<RallyMarker>("World3D/RallyMarker");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _bviews = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        Tick(2);
        await Frames();
        _hall = HallSlot(0);
        _age = _data.FindTech("age_ii");
    }

    // ---- gating ----

    // The oracle: Age II's any-of rule from the store alone (own finished buildings, distinct slots of the four).
    private bool AgeOpenByOracle()
    {
        var slots = new HashSet<BuildingSlot>();
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && !B.UnderConstruction[k])
            {
                BuildingSlot s = _data.Buildings[B.TypeId[k]].Slot;
                if (AgeSlots.Contains(s)) slots.Add(s);
            }
        return slots.Count >= 2;
    }

    private async Task GatingPhases()
    {
        _sel.SelectBuilding(_hall);
        await Frames();
        var rng = new Random(1715);
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        int f = W.FactionOf(0);

        await Phase("bare hall", 100, rng, expectLocked: true);
        int barracks = Spawn(StartBase.BuildingOfSlot(_data, f, BuildingSlot.InfantryHall), taken);
        await Phase("one hall", 80, rng, expectLocked: true);
        int barracks2 = Spawn(StartBase.BuildingOfSlot(_data, f, BuildingSlot.InfantryHall), taken);
        // A Forge site (a worker's Build): a site doesn't count.
        int forgeType = StartBase.BuildingOfSlot(_data, f, BuildingSlot.Forge);
        int siteAnchor = FreeAnchor(forgeType, taken, 12f, 40f, allowRequires: false);
        taken.Add(siteAnchor);
        Check(ExploredGround.Scout(_sim, Workers()[0], _data.Buildings[forgeType], siteAnchor) >= 0, $"BUG-0274: {siteAnchor} not explored");
        SetMoney(5000, 5000);
        _sim.Enqueue(Command.Build(0, Workers()[0], forgeType, G.CellCenter(siteAnchor % G.Width, siteAnchor / G.Width)));
        Tick(2);
        int site = B.SlotAt(siteAnchor % G.Width, siteAnchor / G.Width);
        Check(site >= 0 && B.UnderConstruction[site], $"forge site not placed at {siteAnchor} (slot {site})");
        Check(barracks != barracks2 && B.Alive[barracks2], "second barracks missing");
        await Phase("two of one slot + a forge site", 80, rng, expectLocked: true, siteSlot: site);
        // Cancel the site (refund) and spawn a finished Forge: two distinct slots.
        if (site >= 0 && B.Alive[site]) _sim.Enqueue(Command.Cancel(0, SelectionController.SiteCenter(W, site)));
        Tick(2);
        int forge = Spawn(forgeType, taken);
        await Phase("two distinct", 80, rng, expectLocked: false);

        // Queue Age II through the card, then destroy the Forge: the item survives and completes.
        SetMoney(5000, 5000);
        await Frame();
        int cell = AgeCell();
        if (!Check(cell >= 0 && !_card.ButtonAt(cell).Disabled, $"two distinct: Age II button {cell} disabled / reason {(cell >= 0 ? _card.ReasonAt(cell) : -1)}")) return;
        int pending = _sim.PendingCommandCount;
        _card.Press(cell);
        Check(_sim.PendingCommandCount == pending + 1, $"Age II press enqueued {_sim.PendingCommandCount - pending} commands");
        Tick(2);
        Check(QueuedAge(), "Age II not in the hall's queue after the press");
        for (int k = 0; k < 10; k++) { await Frame(); CheckFrame("Age II queued", expectLocked: false); }
        DamageMethod.Invoke(B, new object[] { B.HandleOf(forge), 100000 });
        Tick(1);
        Check(!B.Alive[forge], "forge not destroyed");
        Check(!AgeOpenByOracle(), "oracle: still open after the forge died");
        Check(QueuedAge(), "Age II dropped from the queue when the forge died");
        int researchTicks = 0, inQueue = 0, queuedFrames = 0;
        while (!W.HasTech(0, _age) && researchTicks < 2000)
        {
            Tick(rng.Next(1, 30));
            researchTicks++;
            await Frame();
            CheckFrame("queued then hall destroyed", expectLocked: null);
            // M3-V4 (BUG-0126 item 2): the sim says Requires, the button says "In a queue".
            if (W.HasTech(0, _age)) continue;
            queuedFrames++;
            int c = AgeCell();
            if (c >= 0 && _card.ReasonAt(c) == (int)ResearchError.AlreadyQueued && _card.CostAt(c).Text == _ui.ResearchText(ResearchError.AlreadyQueued)) inQueue++;
        }
        Check(queuedFrames > 0 && inQueue == queuedFrames, $"queued Age II with a hall lost: 'In a queue' in {inQueue} of {queuedFrames} frames");
        Check(W.HasTech(0, _age), "Age II never completed after the forge was destroyed");
        GD.Print($"queued Age II survived the forge's death and completed ({researchTicks} frames)");
        // After Age II with one hall slot left the sim answers Requires before AlreadyResearched (M3-6's order); since M3-V4
        // (BUG-0126 item 2, was a NOTE: 'Locked' in 10 of 10 frames) the button reads "Researched" anyway.
        int lockedAfter = 0, researched = 0;
        for (int k = 0; k < 10; k++)
        {
            Tick(1);
            await Frame();
            if (CheckFrame("after Age II, one slot", expectLocked: null)) lockedAfter++;
            int c = AgeCell();
            W.CanResearch(0, _hall, _age, out ResearchError simSays);
            if (c >= 0 && simSays == ResearchError.Requires && _card.ReasonAt(c) == (int)ResearchError.AlreadyResearched
                && _card.CostAt(c).Text == _ui.ResearchText(ResearchError.AlreadyResearched)) researched++;
        }
        Check(lockedAfter == 0 && researched == 10, $"Age II researched, a hall slot lost: 'Locked' in {lockedAfter}, 'Researched' (sim Requires) in {researched} of 10 frames");
        GD.Print($"Age II researched, a hall slot lost: the button reads 'Researched' in {researched} of 10 frames (sim says Requires)");
        Spawn(forgeType, taken);
        for (int k = 0; k < 10; k++) { Tick(1); await Frame(); CheckFrame("after Age II, two slots", expectLocked: false); }

        Check(_mismatch == 0, $"greying: {_mismatch} mismatching cells");
        Check(_frames >= 400, $"only {_frames} frames");
        foreach (string want in new[] { "research Requires", "research None", "research CannotAfford", "research AlreadyQueued", "research AlreadyResearched" })
            Check(_seen.Contains(want), $"never saw {want} (saw {string.Join(", ", _seen.OrderBy(x => x))})");
        GD.Print($"greying vs real gates: {_frames} frames, {_cells} cells, {_mismatch} mismatches; seen {string.Join(", ", _seen.OrderBy(x => x))}");
    }

    private async Task Phase(string name, int frames, Random rng, bool expectLocked, int siteSlot = -1)
    {
        int locked = 0;
        for (int s = 0; s < frames; s++)
        {
            int op = rng.Next(5);
            if (op == 0) SetMoney(rng.Next(0, 800), rng.Next(0, 400));
            else if (op == 1) SetMoney(50000, 50000);
            else if (op == 2) SetMoney(400 + rng.Next(-1, 2), 200 + rng.Next(-1, 2)); // Age II's cost - 1 / cost / + 1
            else if (op == 3 && expectLocked)
            {
                // A press on a locked Age II enqueues nothing; a raw Research command is dropped by the sim.
                int cell = AgeCell();
                int start = _sim.PendingCommandCount;
                if (cell >= 0) _card.Press(cell);
                Check(_sim.PendingCommandCount == start, $"{name}: a locked Age II press enqueued {_sim.PendingCommandCount - start}");
                _sim.Enqueue(Command.Research(0, SelectionController.SiteCenter(W, _hall), _age));
            }
            Tick(rng.Next(0, 3));
            if (siteSlot >= 0) Check(B.Alive[siteSlot] && B.UnderConstruction[siteSlot], $"{name}: the forge site finished or vanished at frame {s}");
            await Frame();
            if (CheckFrame(name, expectLocked)) locked++;
        }
        if (expectLocked) Check(locked == frames && !W.HasTech(0, _age) && !QueuedAge(), $"{name}: Age II 'Locked' in {locked} of {frames} frames, queued {QueuedAge()}");
        else Check(locked == 0, $"{name}: Age II 'Locked' in {locked} of {frames} frames");
        GD.Print($"phase '{name}': {frames} frames, Age II Locked in {locked}");
    }

    // Checks the card after a real frame; true if the Age II cell shows Requires ("Locked").
    private bool CheckFrame(string name, bool? expectLocked)
    {
        _frames++;
        int sel = _sel.SelectedBuilding;
        if (sel != _hall) { _mismatch++; Check(false, $"{name}: selection moved to {sel}"); return false; }
        bool ageLocked = false;
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
                _seen.Add($"train {e}");
            }
            else
            {
                W.CanResearch(0, sel, _card.TypeAt(i), out ResearchError e);
                // M3-V4 (BUG-0126): the card shows Researched / In a queue over the sim's earlier reasons.
                e = ProductionMenu.ShownResearchReason(e, W.HasTech(0, _card.TypeAt(i)), ProductionMenu.IsTechQueued(W.Buildings, 0, _card.TypeAt(i)));
                reason = (int)e;
                text = e == ResearchError.None ? $"{_data.Techs[_card.TypeAt(i)].CostGold} / {_data.Techs[_card.TypeAt(i)].CostWood}" : _ui.ResearchText(e);
                _seen.Add($"research {e}");
                if (_card.TypeAt(i) == _age)
                {
                    ageLocked = e == ResearchError.Requires;
                    // The oracle: Requires exactly when fewer than two distinct finished slots stand (and Age II isn't
                    // researched / queued, which the sim checks after Requires... so only compare when not yet researched).
                    if (!W.HasTech(0, _age) && expectLocked.HasValue && (e == ResearchError.Requires) != !AgeOpenByOracle())
                    {
                        _mismatch++;
                        Check(false, $"{name}: sim says {e}, oracle open={AgeOpenByOracle()}");
                    }
                    if (expectLocked.HasValue && ageLocked != expectLocked.Value && _mismatch++ < 6)
                        Check(false, $"{name}: Age II reason {e}, expected locked={expectLocked}");
                    if (ageLocked && text != "Locked" && _mismatch++ < 6) Check(false, $"{name}: Requires text '{text}'");
                }
            }
            _cells++;
            float nameA = _card.NameAt(i).Modulate.A, hintA = _card.HintAt(i).Modulate.A;
            bool dimOk = reason != 0 ? nameA <= 0.6f && hintA <= 0.6f : nameA == 1f && hintA == 1f;
            bool same = _card.ReasonAt(i) == reason && _card.ButtonAt(i).Disabled == (reason != 0) && _card.CostAt(i).Text == text && _card.ButtonAt(i).Visible && dimOk;
            if (!same && _mismatch++ < 6)
                Check(false, $"{name} frame {_frames}: cell {i} ({a} {_card.TypeAt(i)}) shows {_card.ReasonAt(i)} disabled {_card.ButtonAt(i).Disabled} '{_card.CostAt(i).Text}' alpha {nameA}/{hintA}; sim {reason} '{text}'");
        }
        return ageLocked;
    }

    private int AgeCell() => Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Research && _card.TypeAt(i) == _age, -1);

    private bool QueuedAge()
    {
        for (int i = 0; i < B.QueueCount[_hall]; i++)
            if (B.QueueIsTechAt(_hall, i) && B.QueueTypeAt(_hall, i) == _age) return true;
        return false;
    }

    // ---- allocation ----

    // Every HUD element shown (panel on the damaged hall, card, strip with a running queue, rally flag, bar, minimap,
    // building views with a hp bar): 300 idle frames and 300 repair ticks, each element's Sync measured on its own.
    private async Task Allocation()
    {
        SetMoney(90000, 90000);
        _sel.SelectBuilding(_hall);
        int laborer = _data.UnitsTrainedAt(B.TypeId[_hall])[0];
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, _hall);
        for (int i = 0; i < 5; i++) _sim.Enqueue(Command.Train(0, hc, laborer));
        _sim.Enqueue(Command.SetRally(0, B.Cell[_hall], hc + new System.Numerics.Vector2(9f, 11f)));
        int max = _data.Buildings[B.TypeId[_hall]].Hp;
        DamageMethod.Invoke(B, new object[] { B.HandleOf(_hall), max / 2 });
        Tick(2);
        await Frames();
        Check(_strip.Visible && _panel.Visible && _card.Visible && _rally.Visible && B.HasRally[_hall], $"not every element visible: strip {_strip.Visible} panel {_panel.Visible} card {_card.Visible} rally {_rally.Visible}");
        var names = new[] { "panel", "card", "strip", "rally", "bar", "minimap", "buildings" };
        var part = new long[names.Length];
        long barOnChange = 0;
        void Measure(int n, bool tick)
        {
            Array.Clear(part);
            barOnChange = 0;
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
                _mini.Refresh(_sim);
                long b6 = GC.GetAllocatedBytesForCurrentThread();
                _bviews.Sync(W);
                long b7 = GC.GetAllocatedBytesForCurrentThread();
                long bar = b5 - b4;
                if (_bar.Builds + _bar.PopBuilds != builds) { barOnChange += bar; bar = 0; }
                part[0] += b1 - b0; part[1] += b2 - b1; part[2] += b3 - b2; part[3] += b4 - b3; part[4] += bar; part[5] += b6 - b5; part[6] += b7 - b6;
            }
        }
        string Parts() => string.Join(", ", names.Select((s, i) => $"{s} {part[i]}")) + $"; bar on value changes {barOnChange}";
        for (int f = 0; f < 3; f++) { _panel.Sync(); _card.Sync(); _strip.Sync(); _rally.Sync(W); _bar.Sync(W); _mini.Refresh(_sim); _bviews.Sync(W); }
        Measure(300, tick: false);
        Check(part.Sum() == 0 && barOnChange == 0, $"300 idle frames allocated: {Parts()}");
        GD.Print($"allocation idle (every element visible): {Parts()}");

        foreach (EntityHandle h in Workers().Take(3)) _sim.Enqueue(Command.Repair(0, h, hc));
        int hp0 = B.Hp[_hall];
        for (int t = 0; t < 400 && B.Hp[_hall] == hp0; t++) _sim.Tick();
        for (int f = 0; f < 3; f++) { _panel.Sync(); _card.Sync(); _strip.Sync(); _rally.Sync(W); _bar.Sync(W); _mini.Refresh(_sim); _bviews.Sync(W); }
        int hp1 = B.Hp[_hall], q0 = B.QueueCount[_hall];
        Measure(300, tick: true);
        Check(B.Hp[_hall] > hp1, $"repair did not run (hp {hp1} -> {B.Hp[_hall]})");
        Check(part.Sum() == 0, $"300 repair ticks with a running queue allocated (excluding the bar's value changes): {Parts()}");
        GD.Print($"allocation repair + production (hp {hp1} -> {B.Hp[_hall]}, queue {q0} -> {B.QueueCount[_hall]}): {Parts()}");
        _sel.ClearBuilding();
        await Frames();
    }

    // ---- pixels (windowed) ----

    private async Task Pixels()
    {
        // 1. A greyed vs an enabled button: a fresh bare-hall-like state is gone (Age II researched), so grey by money:
        // money 0 greys every train / research cell; money huge enables the laborer.
        _sel.SelectBuilding(_hall);
        System.Numerics.Vector2 hcen = SelectionController.SiteCenter(W, _hall);
        for (int guard = 0; guard < 20 && B.QueueCount[_hall] > 0; guard++) { _sim.Enqueue(Command.CancelTrain(0, hcen, 0)); Tick(2); }
        SetMoney(90000, 90000);
        await Frames();
        await Frames();
        int lab = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Train, -1);
        if (!Check(lab >= 0, "no train cell")) return;
        float enabled = NameContrast(lab, "v3b-card-enabled.png");
        SetMoney(0, 0);
        await Frames();
        await Frames();
        Check(_card.ButtonAt(lab).Disabled, "laborer not greyed at 0 money");
        float greyed = NameContrast(lab, "v3b-card-greyed.png");
        GD.Print($"pixels: brightest name pixel enabled {enabled:0.00}, greyed {greyed:0.00}");
        Check(enabled >= 0.95f && greyed <= 0.85f, $"name text on screen: brightest enabled {enabled:0.00} (want >= 0.95), greyed {greyed:0.00} (want <= 0.85)");
        SetMoney(90000, 90000);

        // 2. A Malazan site vs a finished Malazan building: hue on screen.
        int f = W.FactionOf(0);
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        int houseType = StartBase.BuildingOfSlot(_data, f, BuildingSlot.House);
        int a = FreeAnchor(houseType, taken, 10f, 40f, allowRequires: false);
        Check(ExploredGround.Scout(_sim, Workers()[1], _data.Buildings[houseType], a) >= 0, $"BUG-0274: {a} not explored");
        _sim.Enqueue(Command.Build(0, Workers()[1], houseType, G.CellCenter(a % G.Width, a / G.Width)));
        Tick(2);
        int site = B.SlotAt(a % G.Width, a / G.Width);
        if (!Check(site >= 0 && B.UnderConstruction[site], "pixel: no site")) return;
        _sel.ClearBuilding();
        (float siteHue, float siteSat) = await BoxHue(site);
        (float hallHue, float hallSat) = await BoxHue(_hall);
        float d = MathF.Abs(siteHue - hallHue);
        d = MathF.Min(d, 360f - d);
        GD.Print($"pixels: site hue {siteHue:0} (sat {siteSat:0.00}), finished Malazan hall hue {hallHue:0} (sat {hallSat:0.00}), apart {d:0} degrees");
        Check(d >= 60f, $"site and finished building hues only {d:0} degrees apart");
    }

    // The brightest luminance over cell i's name label rect.
    private float NameContrast(int i, string shot)
    {
        Image img = GetViewport().GetTexture().GetImage();
        Shot(img, shot);
        Rect2 r = _card.NameAt(i).GetGlobalRect();
        var lum = new List<float>();
        for (int y = (int)r.Position.Y; y < (int)r.End.Y; y++)
            for (int x = (int)r.Position.X; x < (int)r.End.X; x++)
            {
                if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
                Color c = img.GetPixel(x, y);
                lum.Add(0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B);
            }
        if (lum.Count == 0) return 0f;
        lum.Sort();
        // The brightest pixel: white text at full strength reaches 1; dimmed to 50 % over the translucent button it can't
        // (0.5 + 0.5 x the background under it; the background varies with the terrain behind the card, so no ratio).
        return lum[^1];
    }

    // The average hue (and saturation) of the box's front face just under its top edge, camera on the building at 30 m.
    private async Task<(float Hue, float Sat)> BoxHue(int slot)
    {
        System.Numerics.Vector2 c = SelectionController.SiteCenter(W, slot);
        _camera.SetZoom(30f);
        _camera.SetFocus(c.X, c.Y);
        await Frames();
        await Frames();
        float y = TerrainHeight.At(W.Heightmap, c.X, c.Y) + BuildingViews.BoxHeight * BuildingPicker.BoxRise(B, _data.Buildings, slot, BuildingViews.SiteMinHeight);
        Vector2 px = _camera.UnprojectPosition(new Vector3(c.X, y - 0.05f, c.Y + 0.6f));
        Image img = GetViewport().GetTexture().GetImage();
        Shot(img, $"v3b-hue-{slot}.png");
        float sx = 0f, sy = 0f, ss = 0f;
        int n = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                Color col = img.GetPixel(Math.Clamp((int)px.X + dx, 0, img.GetWidth() - 1), Math.Clamp((int)px.Y + dy, 0, img.GetHeight() - 1));
                float h = col.H * MathF.Tau;
                sx += MathF.Cos(h) * col.S;
                sy += MathF.Sin(h) * col.S;
                ss += col.S;
                n++;
            }
        float hue = MathF.Atan2(sy, sx) * 180f / MathF.PI;
        return (hue < 0f ? hue + 360f : hue, ss / n);
    }

    // ---- helpers ----

    // Saves the image when run with `-- --shots <dir>`.
    private static void Shot(Image img, string name)
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--shots") img.SavePng(System.IO.Path.Combine(args[i + 1], name));
    }

    private int Spawn(int type, List<int> taken)
    {
        int a = FreeAnchor(type, taken, 10f, 45f, allowRequires: true);
        taken.Add(a);
        _sim.Enqueue(Command.SpawnBuilding(0, type, G.CellCenter(a % G.Width, a / G.Width)));
        Tick(2);
        int slot = B.SlotAt(a % G.Width, a / G.Width);
        if (!Check(slot >= 0 && !B.UnderConstruction[slot], $"spawn of {_data.Buildings[type].Key} at {a} failed")) throw new InvalidOperationException("spawn");
        return slot;
    }

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[0] = gold;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[0] = wood;
    }

    private int FreeAnchor(int type, List<int> taken, float minD, float maxD, bool allowRequires)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 near = SelectionController.SiteCenter(W, _hall);
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < minD || d > maxD) continue;
            int x = cell % G.Width, y = cell / G.Width;
            if (taken.Any(t => Math.Abs(t % G.Width - x) < 8 && Math.Abs(t / G.Width - y) < 8)) continue;
            // BUG-0274: the sim refuses unexplored ground since M4-3b; a dev spawn ignores it, a Build path scouts first.
            bool ok = W.CanPlace(0, type, cell, out PlacementError r);
            if (!ok && r != PlacementError.CannotAfford && !(allowRequires && r == PlacementError.Requires && B.Fits(type, cell)) && !ExploredGround.IsUnexplored(r)) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 3f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 3f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        if (best < 0) throw new InvalidOperationException($"no anchor for {def.Key}");
        return best;
    }

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

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
