using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;

namespace Rts.Game.Tests;

/// <summary>
/// QA attacks on M4-V5 (session 2026-10-09-0724) on the real Match scene. (1) The placement ghost at the fog edge (seeds 1
/// and 6): the House hovered over anchors whose footprint is part explored, part unexplored, and their explored
/// neighbours; at each the drawn anchor's reason, colour and text equal <c>CanPlace</c> there, "Unexplored" exactly when
/// <c>CanPlace</c> says so. (2) A right-click on the ghost of a building already gone (a House site cancelled unseen):
/// one Attack per selected unit on the remembered handle, the sim takes them, the units walk there, the ring stays on the
/// remembered footprint while the ghost is drawn, and the orders, the ghost and the ring end when the ground is seen;
/// ghosts drawn equal the sim's unseen entries every frame; twin. (3) 300 steady frames at <c>--units 990</c> (1,990
/// units) with a ghost drawn and the ring on it: 0 bytes. (4) <c>--no-fog</c>: a scouted, left hall is never a ghost,
/// and a click on it is an Attack on the live hall.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaGhostViewTest.tscn</c>; prints
/// "QA GHOST VIEW TEST PASS" and exits 0, or each failure and exits 1. Known-bug rows (BUG-0311) print "KNOWN" and fail
/// only with <c>-- --strict</c>.</remarks>
public partial class QaGhostViewTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private UnitViews _units = null!;
    private BuildingViews _buildings = null!;
    private CombatViews _combat = null!;
    private ProjectileViews _shots = null!;
    private TargetRing _ring = null!;
    private Minimap _mini = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private int _ghostBad, _ghostFrames;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await FogEdgeHover(1);
            await FogEdgeHover(6);
            await GoneGhostAttack(1);
            await GoneGhostAttack(6);
            await Steady(6, 990); // 990 + 5 workers a side leaves room in the 2,010-slot store for the scout
            await NoFog(1);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"QA GHOST VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA GHOST VIEW TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _fog = _match.GetNode<FogOfWar>("World3D/FogOfWar");
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        _shots = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        _ring = _match.GetNode<TargetRing>("World3D/TargetRing");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _ghostBad = _ghostFrames = 0;
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // ---- 1: the placement ghost at the fog edge ----

    private async Task FogEdgeHover(ulong seed)
    {
        StartMatch(seed, "--units", "1", "--zoom", "40");
        _sim.Tick();
        _sim.Tick();
        var ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        BuildingDef def = _data.Buildings[house];
        int w = W.NavGrid.Width;
        // Edge anchors: footprint part explored, part not. Every third one, up to 40, plus the anchor 3 cells further in.
        var spots = new List<int>();
        int seen = 0;
        for (int y = 2; y < W.NavGrid.Height - 6 && spots.Count < 80; y++)
            for (int x = 2; x < w - 6 && spots.Count < 80; x++)
            {
                int a = y * w + x, explored = ExploredCells(def, a);
                if (explored == 0 || explored == def.FootprintWidth * def.FootprintHeight) continue;
                if (seen++ % 3 != 0) continue;
                spots.Add(a);
            }
        if (!Check(spots.Count >= 20, $"edge hover seed {seed}: only {spots.Count} edge anchors")) { await EndMatch(); return; }
        ghost.Begin(house);
        string unexplored = UiText.Shared!.UnexploredPlacementText;
        int checks = 0, partialUnexplored = 0, fullyExplored = 0, bad = 0;
        foreach (int spot in spots)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(W.NavGrid, def, spot);
            _camera.SetFocus(c.X, c.Y);
            await Frame();
            ghost.ScreenOverride = _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y), c.Y));
            await Frame();
            await Frame();
            if (ghost.Anchor < 0) { if (bad++ < 5) Check(false, $"edge hover seed {seed}: no anchor at spot {spot}"); continue; }
            bool ok = W.CanPlace(0, house, ghost.Anchor, out PlacementError reason);
            int ex = ExploredCells(def, ghost.Anchor), all = def.FootprintWidth * def.FootprintHeight;
            bool wantText = reason == PlacementError.Unexplored;
            bool good = ghost.Visible && ghost.Valid == ok && ghost.Reason == reason
                && (ghost.ShownText == unexplored) == wantText
                && ghost.Box.MaterialOverride == (ok ? ghost.GreenMaterial : ghost.RedMaterial)
                && (!wantText || (ghost.ReasonLabel.Visible && ghost.ReasonLabel.Text == unexplored))
                // the sim rule (M4-3b): any unexplored footprint cell refuses (Blocked / OffMap / SealsGround may outrank it)
                && (ex == all || !ok);
            if (!good && bad++ < 5)
                Check(false, $"edge hover seed {seed}: anchor {ghost.Anchor} ({ex}/{all} explored): ghost valid {ghost.Valid} {ghost.Reason} '{ghost.ShownText}', CanPlace {ok} {reason}");
            checks++;
            if (ex > 0 && ex < all && reason == PlacementError.Unexplored) partialUnexplored++;
            if (ex == all) fullyExplored++;
        }
        ghost.ScreenOverride = null;
        ghost.End();
        GD.Print($"edge hover seed {seed}: {checks} anchors, {partialUnexplored} part-explored read Unexplored, {fullyExplored} fully explored; {bad} bad");
        Check(bad == 0 && partialUnexplored >= 10, $"edge hover seed {seed}: {bad} bad, {partialUnexplored} part-explored Unexplored");
        await EndMatch();
    }

    private int ExploredCells(BuildingDef def, int anchor)
    {
        int w = W.NavGrid.Width, n = 0;
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
            {
                int c = anchor + dy * w + dx;
                if ((uint)c < (uint)(w * W.NavGrid.Height) && W.Fog.IsExplored(0, c)) n++;
            }
        return n;
    }

    // ---- 2: a right-click on the ghost of a building already gone ----

    private async Task GoneGhostAttack(ulong seed)
    {
        StartMatch(seed, "--units", "1", "--zoom", "45");
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 1 && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker)
                _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(i, U.Generation[i])));
        BuildingStore b = W.Buildings;
        int hall = FirstBuildingOf(1), home = FirstBuildingOf(0);
        if (!Check(hall >= 0 && home >= 0, $"gone ghost seed {seed}: no halls")) { await EndMatch(); return; }
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, hall), oc = SelectionController.SiteCenter(W, home);
        System.Numerics.Vector2 toHome = System.Numerics.Vector2.Normalize(oc - hc), side = new(-toHome.Y, toHome.X);
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(1), BuildingSlot.House);
        int anchor = SiteNear(1, house, hc - toHome * 8f + side * 8f);
        int worker = WorkerOf(1);
        if (!Check(anchor >= 0 && worker >= 0, $"gone ghost seed {seed}: no House spot or worker")) { await EndMatch(); return; }
        _sim.Enqueue(Command.Build(1, new EntityHandle(worker, U.Generation[worker]), house, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        int site = -1;
        for (int t = 0; t < 900 && site < 0; t++)
        {
            _sim.Tick();
            site = b.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        }
        if (!Check(site >= 0, $"gone ghost seed {seed}: player 1 never placed the site")) { await EndMatch(); return; }
        var siteH = new EntityHandle(site, b.Generation[site]);
        System.Numerics.Vector2 sc = SelectionController.SiteCenter(W, site);
        // A worker scout beside the site (sees it at the next update), then home.
        int scoutType = WorkerType(W.FactionOf(0));
        System.Numerics.Vector2 scoutAt = sc + side * 6f;
        _sim.Enqueue(Command.SpawnUnit(0, scoutType, scoutAt));
        _sim.Tick();
        _sim.Tick();
        EntityHandle scout = default;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == scoutType && System.Numerics.Vector2.Distance(U.Position[i], scoutAt) < 4f) scout = new EntityHandle(i, U.Generation[i]);
        if (!Check(scout.Generation != 0, $"gone ghost seed {seed}: no scout")) { await EndMatch(); return; }
        for (int t = 0; t < 8; t++) { _sim.Tick(); await Frame(); CheckGhosts(seed); }
        Check(W.Fog.Ghosts(0)[site].Generation == siteH.Generation, $"gone ghost seed {seed}: the site never entered the list");
        _sim.Enqueue(Command.Move(0, scout, oc + toHome * 30f));
        for (int t = 0; t < 900 && !_buildings.IsGhostShown(site); t++) { _sim.Tick(); await Frame(); CheckGhosts(seed); }
        if (!Check(_buildings.IsGhostShown(site), $"gone ghost seed {seed}: no ghost once the scout left")) { await EndMatch(); return; }
        _sim.Enqueue(Command.Cancel(1, sc));
        for (int t = 0; t < 12; t++) { _sim.Tick(); await Frame(); CheckGhosts(seed); }
        if (!Check(!b.IsAlive(siteH) && _buildings.IsGhostShown(site), $"gone ghost seed {seed}: site alive {b.IsAlive(siteH)}, ghost {_buildings.IsGhostShown(site)}")) { await EndMatch(); return; }

        // Attackers by the own hall, selected; the right-click on the gone site's ghost.
        int army = FirstArmyType(W.FactionOf(0)), queued = 0;
        for (int r = 12; r < 40 && queued < 3; r += 3)
            for (int k = -6; k <= 6 && queued < 3; k += 4)
            {
                System.Numerics.Vector2 at = oc + toHome * r + side * k;
                if (!W.NavGrid.WorldToCell(at, out int cx, out int cy) || !W.NavGrid.IsPassable(cx, cy) || b.SlotAt(cx, cy) >= 0) continue;
                _sim.Enqueue(Command.SpawnUnit(0, army, at));
                queued++;
            }
        _sim.Tick();
        _sim.Tick();
        var selected = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == army && System.Numerics.Vector2.Distance(U.Position[i], oc) < 50f) selected.Add(new EntityHandle(i, U.Generation[i]));
        if (!Check(selected.Count >= 2, $"gone ghost seed {seed}: {selected.Count} attackers")) { await EndMatch(); return; }
        _sel.Selection.Clear();
        foreach (EntityHandle h in selected) _sel.Selection.Add(h);
        _camera.SetFocus(sc.X, sc.Y);
        await Frame();
        await Frame();
        Vector2 px = _camera.UnprojectPosition(new Vector3(sc.X, TerrainHeight.At(W.Heightmap, sc.X, sc.Y) + BuildingViews.BoxHeight / 2f, sc.Y));
        Check(_sel.EnemyAt(px, out EntityHandle picked, out bool isB) && isB && picked == siteH, $"gone ghost seed {seed}: EnemyAt gave {picked} building {isB}, want {siteH}");
        int attacks = _sel.IssuedCount(CommandKind.Attack), commands = _runner.Recorder!.CommandCount;
        Push(Button(MouseButton.Right, true, px));
        Push(Button(MouseButton.Right, false, px));
        await Frame();
        Check(_sel.IssuedCount(CommandKind.Attack) - attacks == selected.Count, $"gone ghost seed {seed}: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks for {selected.Count}");
        _sim.Tick();
        _sim.Tick();
        await Frame();
        int good = 0, accepted = 0;
        for (int k = commands; k < _runner.Recorder.CommandCount; k++)
        {
            Command c = _runner.Recorder.CommandAt(k);
            if (c.Kind == CommandKind.Attack && c.Player == 0 && c.Target == siteH && c.TargetIsBuilding) good++;
        }
        foreach (EntityHandle h in selected) if (U.Target[h.Index] == siteH) accepted++;
        Check(good == selected.Count && accepted == selected.Count, $"gone ghost seed {seed}: {good} recorded, {accepted} taken of {selected.Count}");
        Check(_ring.Shown && Math.Abs(_ring.Ring.Position.X - sc.X) < 1e-3f && Math.Abs(_ring.Ring.Position.Z - sc.Y) < 1e-3f,
            $"gone ghost seed {seed}: ring shown {_ring.Shown} at {_ring.Ring.Position}, want ({sc.X}, {sc.Y})");
        float before = MeanDistance(selected, sc);
        int ghostGone = -1, ordersEnded = -1, ringLeft = -1, ringWhileGhost = 0, framesWithGhost = 0;
        for (int t = 0; t < 2400 && (ghostGone < 0 || ordersEnded < 0); t++)
        {
            _sim.Tick();
            await Frame();
            CheckGhosts(seed);
            bool ghostNow = _buildings.IsGhostShown(site) && _buildings.GhostGeneration(site) == siteH.Generation;
            if (ghostNow) { framesWithGhost++; if (_ring.Shown) ringWhileGhost++; }
            if (ghostGone < 0 && !ghostNow) ghostGone = W.TickNumber;
            if (ordersEnded < 0 && selected.All(h => !U.IsAlive(h) || U.Target[h.Index] != siteH))
            {
                ordersEnded = W.TickNumber;
                GD.Print($"gone ghost seed {seed}: orders ended at {ordersEnded}: {Describe(selected, house, anchor)}");
            }
            if (ghostGone == W.TickNumber) GD.Print($"gone ghost seed {seed}: ghost gone at {ghostGone}: {Describe(selected, house, anchor)}");
            if (ringLeft < 0 && ghostGone >= 0 && !_ring.Shown) ringLeft = W.TickNumber;
        }
        float after = MeanDistance(selected, sc);
        GD.Print($"gone ghost seed {seed}: site {site} cancelled unseen, {good} Attacks on its ghost; walked {before:F1} -> {after:F1} m; ghost gone {ghostGone}, orders ended {ordersEnded}, ring off {ringLeft}; ring on {ringWhileGhost}/{framesWithGhost} ghost frames (mark time permitting); ghost mismatches {_ghostBad} over {_ghostFrames} frames with ghosts");
        Check(ghostGone > 0 && ordersEnded > 0, $"gone ghost seed {seed}: ghost gone {ghostGone}, orders ended {ordersEnded}");
        // BUG-0311 (sim): the order ends when a unit is within sight of the footprint rect's nearest point, while the fog
        // (cell centres) may show no footprint cell, so the ghost outlives the order. Reported; fails only with -- --strict.
        if (Math.Abs(ordersEnded - ghostGone) > VisionConstants.UpdateInterval)
        {
            string msg = $"gone ghost seed {seed}: KNOWN BUG-0311: orders ended at {ordersEnded}, ghost gone at {ghostGone}";
            GD.Print(msg);
            Check(!OS.GetCmdlineUserArgs().Contains("--strict"), msg);
        }
        Check(after < before - 10f, $"gone ghost seed {seed}: the units did not walk to the ghost ({before} -> {after} m)");
        Check(ringLeft < 0 || ringLeft >= ghostGone, $"gone ghost seed {seed}: ring left at {ringLeft}, before the ghost went at {ghostGone}");
        Check(!_ring.Shown, $"gone ghost seed {seed}: the ring still shows after the ghost went");
        Check(_ghostBad == 0, $"gone ghost seed {seed}: {_ghostBad} ghost mismatches");
        Twin(seed);
        await EndMatch();
    }

    // Diagnostics: each unit's distance to the remembered footprint rect, sight, levels, and the footprint's fog cells.
    private string Describe(List<EntityHandle> units, int type, int anchor)
    {
        BuildingDef def = _data.Buildings[type];
        int w = W.NavGrid.Width;
        const float cs = Rts.Sim.Map.MapConstants.CellSize;
        var min = new System.Numerics.Vector2(anchor % w * cs, anchor / w * cs);
        var max = min + new System.Numerics.Vector2(def.FootprintWidth * cs, def.FootprintHeight * cs);
        int vis = 0;
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (W.Fog.IsVisible(0, anchor + dy * w + dx)) vis++;
        var parts = new List<string> { $"footprint level {W.Heightmap.Levels[anchor]}, {vis} cells visible" };
        foreach (EntityHandle h in units)
        {
            if (!U.IsAlive(h)) { parts.Add("dead"); continue; }
            System.Numerics.Vector2 p = U.Position[h.Index];
            float d = System.Numerics.Vector2.Distance(p, System.Numerics.Vector2.Clamp(p, min, max));
            W.NavGrid.WorldToCell(p, out int cx, out int cy);
            parts.Add($"[{h.Index} d {d:F2} sight {_data.Units[U.TypeId[h.Index]].Sight} lvl {W.Heightmap.Levels[cy * w + cx]} tgt {U.Target[h.Index]} mode {U.Mode[h.Index]} state {U.State[h.Index]} stall {U.ChaseStall[h.Index]}]");
        }
        return string.Join(" ", parts);
    }

    private bool FootprintVisible(BuildingGhost g)
    {
        BuildingDef def = W.Data.Buildings[g.TypeId];
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (W.Fog.IsVisible(0, g.Cell + dy * W.NavGrid.Width + dx)) return true;
        return false;
    }

    // Ghost boxes drawn == the sim's known entries not drawn as themselves (and not over visible ground), every frame.
    private void CheckGhosts(ulong seed)
    {
        BuildingStore b = W.Buildings;
        ReadOnlySpan<BuildingGhost> list = W.Fog.Ghosts(0);
        int want = 0;
        for (int i = 0; i < b.Capacity; i++)
        {
            BuildingGhost g = list[i];
            bool unseen = g.Known && !(W.Fog.CanSeeBuilding(0, i) && b.Generation[i] == g.Generation) && !FootprintVisible(g); // M4-V6b, BUG-0310
            if (unseen) want++;
            bool drawn = _buildings.IsGhostShown(i) && _buildings.GhostBoxOf(i) is { Visible: true } && _buildings.GhostGeneration(i) == g.Generation;
            if (unseen != drawn && _ghostBad++ < 3) Check(false, $"seed {seed} tick {W.TickNumber}: slot {i} ghost drawn {drawn}, unseen entry {unseen}");
        }
        if (want != _buildings.GhostsShown && _ghostBad++ < 3) Check(false, $"seed {seed} tick {W.TickNumber}: {_buildings.GhostsShown} drawn, {want} unseen");
        if (want > 0) _ghostFrames++;
    }

    // ---- 3: 300 steady frames at 2,000 units with a ghost and the ring on it ----

    private async Task Steady(ulong seed, int perPlayer)
    {
        StartMatch(seed, "--units", perPlayer.ToString(CultureInfo.InvariantCulture));
        _sim.Tick();
        _sim.Tick();
        int spawned = U.Count;
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? east : west));
        int hall = FirstBuildingOf(1), home = FirstBuildingOf(0);
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, hall), oc = SelectionController.SiteCenter(W, home);
        _sim.Enqueue(Command.SpawnUnit(0, FirstArmyType(W.FactionOf(0)), hc + System.Numerics.Vector2.Normalize(oc - hc) * 10f));
        _sim.Tick();
        _sim.Tick();
        int scout = -1;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && System.Numerics.Vector2.Distance(U.Position[i], hc) < 13f) scout = i;
        if (Check(scout >= 0, "steady: no scout")) _sim.Enqueue(Command.Move(0, new EntityHandle(scout, U.Generation[scout]), oc));
        for (int t = 0; t < 240; t++)
        {
            _sim.Tick();
            SyncAll(0.5f);
        }
        // The ring on the hall's ghost (marked for longer than the measured frames).
        _ring.Show(new EntityHandle(hall, W.Buildings.Generation[hall]), true);
        SyncAll(0.5f);
        long bytes = 0;
        int ghostFrames = 0, ringFrames = 0;
        for (int f = 0; f < 300; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            SyncAll(f % 3 / 3f);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            if (_buildings.GhostsShown > 0) ghostFrames++;
            if (_ring.Shown) ringFrames++;
        }
        GD.Print($"steady (seed {seed}, {spawned} units spawned, {U.Count} alive at the end): 300 frames, {ghostFrames} with ghosts, {ringFrames} with the ring: {bytes} bytes");
        Check(spawned >= 1950, $"steady: only {spawned} units spawned");
        Check(ghostFrames == 300, $"steady: ghosts in {ghostFrames} of 300 frames");
        Check(bytes == 0, $"steady: 300 frames at {U.Count} units with ghosts allocated {bytes} bytes");
        await EndMatch();
    }

    private void SyncAll(float alpha)
    {
        _fog.Sync(W);
        _units.Sync(W, alpha, 0.016f);
        _buildings.Sync(W);
        _combat.Sync(W, alpha);
        _shots.Sync(W, alpha);
        _ring.Sync(W, alpha, 0.001f);
        _mini.SyncFog(W);
        if (W.TickNumber % Minimap.RefreshTicks == 0) _mini.Refresh(_sim);
    }

    // ---- 4: --no-fog ----

    private async Task NoFog(ulong seed)
    {
        StartMatch(seed, "--units", "1", "--zoom", "45", "--no-fog");
        _sim.Tick();
        _sim.Tick();
        int hall = FirstBuildingOf(1), home = FirstBuildingOf(0);
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, hall), oc = SelectionController.SiteCenter(W, home);
        _sim.Enqueue(Command.SpawnUnit(0, WorkerType(W.FactionOf(0)), hc + System.Numerics.Vector2.Normalize(oc - hc) * 10f));
        _sim.Tick();
        _sim.Tick();
        int scout = -1;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && System.Numerics.Vector2.Distance(U.Position[i], hc) < 13f) scout = i;
        if (Check(scout >= 0, "no-fog: no scout")) _sim.Enqueue(Command.Move(0, new EntityHandle(scout, U.Generation[scout]), oc));
        int ghostFrames = 0, simEntries = 0;
        for (int t = 0; t < 400; t++)
        {
            _sim.Tick();
            await Frame();
            if (_buildings.GhostsShown > 0 || _fog.View!.GhostCount > 0) ghostFrames++;
            if (W.Fog.GhostCount(0) > 0) simEntries++;
        }
        _camera.SetFocus(hc.X, hc.Y);
        await Frame();
        await Frame();
        Vector2 px = _camera.UnprojectPosition(new Vector3(hc.X, TerrainHeight.At(W.Heightmap, hc.X, hc.Y) + BuildingViews.BoxHeight / 2f, hc.Y));
        bool hit = _sel.EnemyAt(px, out EntityHandle picked, out bool isB);
        GD.Print($"no-fog (seed {seed}): {ghostFrames} frames with ghosts over 400 ticks ({simEntries} with sim entries); EnemyAt hall {hit} {picked} building {isB}");
        Check(ghostFrames == 0, $"no-fog: ghosts drawn in {ghostFrames} frames");
        Check(hit && isB && picked == new EntityHandle(hall, W.Buildings.Generation[hall]), $"no-fog: EnemyAt the hall gave {picked} building {isB}");
        await EndMatch();
    }

    // ---- helpers ----

    private int FirstBuildingOf(int player)
    {
        for (int i = 0; i < W.Buildings.Capacity; i++)
            if (W.Buildings.Alive[i] && W.Buildings.Owner[i] == player) return i;
        return -1;
    }

    private int WorkerOf(int player)
    {
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot == UnitSlot.Worker) return i;
        return -1;
    }

    private int WorkerType(int faction)
    {
        for (int t = 0; t < _data.Units.Length; t++)
            if (_data.Units[t].Faction == faction && _data.Units[t].Slot == UnitSlot.Worker) return t;
        return 0;
    }

    private int FirstArmyType(int faction)
    {
        for (int t = 0; t < _data.Units.Length; t++)
            if (_data.Units[t].Faction == faction && _data.Units[t].Slot != UnitSlot.Worker) return t;
        return 0;
    }

    private int SiteNear(int player, int type, System.Numerics.Vector2 near)
    {
        int w = W.NavGrid.Width, best = -1;
        float bestD = float.MaxValue;
        for (int y = 0; y < W.NavGrid.Height; y++)
            for (int x = 0; x < w; x++)
            {
                System.Numerics.Vector2 c = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[type], y * w + x);
                float d = System.Numerics.Vector2.DistanceSquared(c, near);
                if (d >= bestD || d > 400f || !W.CanPlace(player, type, y * w + x, out _)) continue;
                (best, bestD) = (y * w + x, d);
            }
        return best;
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker) { sum += U.Position[i]; n++; }
        return n > 0 ? sum / n : new System.Numerics.Vector2(float.MaxValue);
    }

    private float MeanDistance(List<EntityHandle> units, System.Numerics.Vector2 to)
    {
        float sum = 0f;
        foreach (EntityHandle h in units) sum += System.Numerics.Vector2.Distance(U.Position[h.Index], to);
        return sum / units.Count;
    }

    private void Twin(ulong seed)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick}");
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints equal");
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private static InputEventMouseButton Button(MouseButton b, bool pressed, Vector2 at) =>
        new() { ButtonIndex = b, Pressed = pressed, Position = at, GlobalPosition = at,
            ButtonMask = pressed ? (b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0 };

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
