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
/// QA M3-V4 (2026-10-07-2014), BUG-0126 items 1-2: the B / V build menus' greying against <c>World.CanPlace</c> and an
/// independent oracle (every required tech researched, an own <b>finished</b> building of every required type) after
/// every real frame, both menus alternating, through: a bare start; a Barracks site (doesn't unlock the Corral); a
/// finished Barracks (Corral live); the Barracks destroyed (Corral locked again); two halls and Age II queued (the Cadre
/// Tower and the Engineers' Yard still locked); a hall lost while Age II is queued; Age II researched (live); and the
/// last hall lost after it (still live: the tower needs the tech only). Money swings 0 .. plenty: a Place button greys
/// only for <c>Requires</c>, never for money. A locked button stays pressable; its ghost reads "Locked" every frame while
/// locked, and turns from red "Locked" the frame after Age II completes. Age II's own button on the hall reads
/// "In a queue" / "Researched" per <c>HasTech</c> / the queue whatever the sim's first reason.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV4Test.tscn</c>; prints "QA M3-V4 TEST PASS".</remarks>
public partial class QaV4Test : Node
{
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private CommandCard _card = null!;
    private BuildGhost _ghost = null!;
    private int _hall, _age, _tower, _yard, _corral;
    private int _frames, _cells, _mismatch, _lockedSeen, _liveSeen, _poorLive;
    private EntityHandle _worker;
    private readonly List<int> _taken = new();

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
            _ui = UiText.Shared!;
            await StartMatch();
            await Phases();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA M3-V4 TEST FAIL: {f}");
        if (_failures.Count > 40) GD.Print($"QA M3-V4 TEST FAIL: ... {_failures.Count - 40} more");
        if (_failures.Count == 0) GD.Print("QA M3-V4 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
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
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        Tick(2);
        await Frame();
        await Frame();
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) _hall = k;
        _taken.Add(B.Cell[_hall]);
        _age = _data.FindTech("age_ii");
        _tower = _data.FindBuilding("malazan_cadre_tower");
        _yard = _data.FindBuilding("malazan_engineers_yard");
        _corral = _data.FindBuilding("malazan_wickan_corral");
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot == UnitSlot.Worker) { _worker = new EntityHandle(i, U.Generation[i]); break; }
    }

    private async Task Phases()
    {
        var rng = new Random(2014);
        int f = W.FactionOf(0);
        int barracksType = StartBase.BuildingOfSlot(_data, f, BuildingSlot.InfantryHall);
        int forgeType = StartBase.BuildingOfSlot(_data, f, BuildingSlot.Forge);

        await MenuPhase("bare", 60, rng, towerLocked: true, corralLocked: true);

        // A Barracks site: a site doesn't count for the Corral's requires.
        SetMoney(5000, 5000);
        int siteAnchor = FreeAnchor(barracksType, 12f, 40f);
        Check(ExploredGround.Scout(_sim, _worker, _data.Buildings[barracksType], siteAnchor) >= 0, $"BUG-0274: {siteAnchor} not explored");
        _sim.Enqueue(Command.Build(0, _worker, barracksType, G.CellCenter(siteAnchor % G.Width, siteAnchor / G.Width)));
        Tick(2);
        int site = B.SlotAt(siteAnchor % G.Width, siteAnchor / G.Width);
        Check(site >= 0 && B.UnderConstruction[site], "barracks site not placed");
        _sim.Enqueue(Command.Stop(0, _worker));
        Tick(1);
        await MenuPhase("barracks site", 40, rng, towerLocked: true, corralLocked: true, tickMax: 0);
        _sim.Enqueue(Command.Cancel(0, SelectionController.SiteCenter(W, site)));
        Tick(2);

        int barracks = Spawn(barracksType);
        await MenuPhase("barracks", 40, rng, towerLocked: true, corralLocked: false);

        // The Corral's ghost up (live), then the Barracks destroyed: the button and the ghost both go "Locked".
        await OpenMenu(advanced: false);
        int corralCell = CellOf(_corral);
        _card.Press(corralCell);
        _ghost.ScreenOverride = GetViewport().GetVisibleRect().Size / 2f;
        await Frame();
        await Frame();
        Check(_ghost.Active && _ghost.TypeId == _corral && _ghost.Reason != PlacementError.Requires, $"corral ghost with a barracks: active {_ghost.Active} reason {_ghost.Reason}");
        DamageMethod.Invoke(B, new object[] { B.HandleOf(barracks), 1_000_000 });
        Tick(1);
        await Frame();
        Check(!B.Alive[barracks], "barracks not destroyed");
        CheckCells("barracks destroyed, ghost up");
        Check(_ghost.Active && _ghost.Reason == PlacementError.Requires && _ghost.ShownText == _ui.PlacementText(PlacementError.Requires),
            $"corral ghost after the barracks died: reason {_ghost.Reason} '{_ghost.ShownText}'");
        _card.CloseMenu();
        await Frame();
        await MenuPhase("barracks destroyed", 40, rng, towerLocked: true, corralLocked: true);

        // Two distinct halls, Age II queued at the hall, the Tower's ghost up the whole time.
        barracks = Spawn(barracksType);
        int forge = Spawn(forgeType);
        SetMoney(5000, 5000);
        _sim.Enqueue(Command.Research(0, SelectionController.SiteCenter(W, _hall), _age));
        Tick(2);
        Check(ProductionMenu.IsTechQueued(B, 0, _age), "Age II not queued");
        await MenuPhase("age II queued", 40, rng, towerLocked: true, corralLocked: false, tickMax: 1);
        DamageMethod.Invoke(B, new object[] { B.HandleOf(forge), 1_000_000 });
        Tick(1);
        await MenuPhase("age II queued, forge lost", 40, rng, towerLocked: true, corralLocked: false, tickMax: 1);
        await AgeButton("queued, forge lost", ResearchError.AlreadyQueued);

        // The Tower's ghost red "Locked" every frame until Age II completes, then not Requires the next frame.
        await OpenMenu(advanced: true);
        _card.Press(CellOf(_tower));
        _ghost.ScreenOverride = GetViewport().GetVisibleRect().Size / 2f;
        int lockedFrames = 0, guard = 0;
        while (!W.HasTech(0, _age) && guard++ < 4000)
        {
            Tick(rng.Next(1, 12));
            await Frame();
            CheckCells("age II researching, tower ghost");
            if (W.HasTech(0, _age)) break;
            if (_ghost.Active && _ghost.Reason == PlacementError.Requires && _ghost.ShownText == _ui.PlacementText(PlacementError.Requires)) lockedFrames++;
            else Check(false, $"tower ghost while Age II researches: active {_ghost.Active} reason {_ghost.Reason} '{_ghost.ShownText}'");
        }
        Check(W.HasTech(0, _age), "Age II never completed");
        await Frame();
        Check(_ghost.Active && _ghost.Reason != PlacementError.Requires, $"tower ghost after Age II: reason {_ghost.Reason}");
        CheckCells("age II done, tower ghost");
        GD.Print($"tower ghost 'Locked' in {lockedFrames} researching frames; after Age II: {_ghost.Reason}");
        _card.CloseMenu();
        await Frame();
        await MenuPhase("age II researched", 40, rng, towerLocked: false, corralLocked: false);
        await AgeButton("researched, one hall slot", ResearchError.AlreadyResearched);

        // Every hall lost after Age II: the tower still needs only the tech; the Corral needs the Barracks again.
        DamageMethod.Invoke(B, new object[] { B.HandleOf(barracks), 1_000_000 });
        Tick(1);
        await MenuPhase("researched, halls lost", 40, rng, towerLocked: false, corralLocked: true);
        await AgeButton("researched, halls lost", ResearchError.AlreadyResearched);

        Check(_mismatch == 0, $"{_mismatch} mismatching cells");
        Check(_lockedSeen > 50 && _liveSeen > 200 && _poorLive > 20, $"coverage: {_lockedSeen} locked, {_liveSeen} live, {_poorLive} live while broke");
        GD.Print($"build-menu greying: {_frames} frames, {_cells} Place cells, {_mismatch} mismatches; {_lockedSeen} Locked, {_liveSeen} live ({_poorLive} of them with no money)");
    }

    private async Task MenuPhase(string name, int frames, Random rng, bool towerLocked, bool corralLocked, int tickMax = 3)
    {
        for (int s = 0; s < frames; s++)
        {
            if (s % 10 == 0) await OpenMenu(advanced: (s / 10) % 2 == 1);
            int op = rng.Next(3);
            if (op == 0) SetMoney(0, 0);
            else if (op == 1) SetMoney(50000, 50000);
            else SetMoney(rng.Next(0, 300), rng.Next(0, 300));
            Tick(rng.Next(0, tickMax + 1));
            await Frame();
            CheckCells(name);
            int t = CellOf(_tower), y = CellOf(_yard), c = CellOf(_corral);
            if (t >= 0) Check(_card.ReasonAt(t) == (towerLocked ? (int)PlacementError.Requires : 0), $"{name}: tower reason {_card.ReasonAt(t)}, want locked={towerLocked}");
            if (y >= 0) Check(_card.ReasonAt(y) == (towerLocked ? (int)PlacementError.Requires : 0), $"{name}: yard reason {_card.ReasonAt(y)}, want locked={towerLocked}");
            if (c >= 0) Check(_card.ReasonAt(c) == (corralLocked ? (int)PlacementError.Requires : 0), $"{name}: corral reason {_card.ReasonAt(c)}, want locked={corralLocked}");
        }
        GD.Print($"phase '{name}': {frames} frames checked");
    }

    private async Task OpenMenu(bool advanced)
    {
        if (!(_sel.Selection.Count == 1 && _sel.Selection.Contains(_worker))) _sel.SelectOnly(_worker);
        _card.CloseMenu();
        await Frame();
        Check(_card.OpenMenu(advanced), $"menu (advanced {advanced}) did not open");
        await Frame();
    }

    // Every Place cell against CanPlace(NoAnchor) and the oracle.
    private void CheckCells(string name)
    {
        _frames++;
        if (!_card.MenuOpen) { Check(false, $"{name}: no menu open"); return; }
        for (int i = 0; i < CommandCard.Cells; i++)
        {
            if (_card.ActionAt(i) != CardCommand.Place) continue;
            _cells++;
            int type = _card.TypeAt(i);
            BuildingDef def = _data.Buildings[type];
            bool open = def.RequiresTechs.All(x => W.HasTech(0, x))
                && def.RequiresBuildings.All(x => Enumerable.Range(0, B.Capacity).Any(k => B.Alive[k] && B.Owner[k] == 0 && B.TypeId[k] == x && !B.UnderConstruction[k]));
            W.CanPlace(0, type, BuildMenu.NoAnchor, out PlacementError sim);
            int want = open ? 0 : (int)PlacementError.Requires;
            string text = open ? $"{def.CostGold} / {def.CostWood}" : _ui.PlacementText(PlacementError.Requires);
            float alpha = open ? 1f : CommandCard.DimAlpha;
            bool ok = _card.ReasonAt(i) == want && (int)BuildMenu.ShownPlaceReason(sim) == want && _card.CostAt(i).Text == text
                && !_card.ButtonAt(i).Disabled && _card.NameAt(i).Modulate.A == alpha && _card.HintAt(i).Modulate.A == alpha;
            if (open) { _liveSeen++; if (W.Gold[0] < def.CostGold || W.Wood[0] < def.CostWood) _poorLive++; }
            else _lockedSeen++;
            if (!ok && _mismatch++ < 8)
                Check(false, $"{name} frame {_frames}: cell {i} {def.Key}: shows {_card.ReasonAt(i)} '{_card.CostAt(i).Text}' disabled {_card.ButtonAt(i).Disabled} alpha {_card.NameAt(i).Modulate.A}; sim {sim}, oracle open {open}");
        }
    }

    private async Task AgeButton(string name, ResearchError want)
    {
        _card.CloseMenu();
        _sel.SelectBuilding(_hall);
        await Frame();
        await Frame();
        int c = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Research && _card.TypeAt(i) == _age, -1);
        W.CanResearch(0, _hall, _age, out ResearchError sim);
        Check(c >= 0 && _card.ReasonAt(c) == (int)want && _card.CostAt(c).Text == _ui.ResearchText(want) && _card.ButtonAt(c).Disabled,
            $"{name}: Age II shows {(c >= 0 ? _card.ReasonAt(c) : -1)} '{(c >= 0 ? _card.CostAt(c).Text : "")}', want {want} (sim {sim})");
        GD.Print($"Age II button, {name}: '{(c >= 0 ? _card.CostAt(c).Text : "")}' (sim {sim})");
    }

    private int CellOf(int type) => Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Place && _card.TypeAt(i) == type, -1);

    private int Spawn(int type)
    {
        int a = FreeAnchor(type, 10f, 45f);
        _sim.Enqueue(Command.SpawnBuilding(0, type, G.CellCenter(a % G.Width, a / G.Width)));
        Tick(2);
        int slot = B.SlotAt(a % G.Width, a / G.Width);
        if (!Check(slot >= 0 && !B.UnderConstruction[slot], $"spawn of {_data.Buildings[type].Key} at {a} failed")) throw new InvalidOperationException("spawn");
        return slot;
    }

    private int FreeAnchor(int type, float minD, float maxD)
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
            if (_taken.Any(t => Math.Abs(t % G.Width - x) < 8 && Math.Abs(t / G.Width - y) < 8)) continue;
            // BUG-0274: the sim refuses unexplored ground since M4-3b; a dev spawn ignores it, a Build path scouts first.
            bool ok = W.CanPlace(0, type, cell, out PlacementError r);
            if (!ok && r != PlacementError.CannotAfford && !(r == PlacementError.Requires && B.Fits(type, cell)) && !ExploredGround.IsUnexplored(r)) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 3f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 3f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        if (best < 0) throw new InvalidOperationException($"no anchor for {def.Key}");
        _taken.Add(best);
        return best;
    }

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[0] = gold;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[0] = wood;
    }

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
