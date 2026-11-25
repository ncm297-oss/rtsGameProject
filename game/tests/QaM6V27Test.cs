using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V27 (session 2026-11-25-0924): the order notices' timing on the real Match, and BUG-1682's click fix in the
/// cases the developer's rows and M6-VH9's oracle do not reach.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V27Test.tscn</c>; prints
/// "QA M6-V27 TEST PASS" or FAIL lines. Rows:
/// (CL) fog on, a live enemy Keep drawn in front of an own Keep (pixel scan against an oracle of the test's own, slot by slot):
/// a left click there selects nothing, an own Keep drawn first is still selected, EnemyAt gives the enemy Keep, and a real right
/// click there with an own soldier selected makes it attack that Keep;
/// (CS) the same with an enemy building SITE (a foundation) in front of the own Keep;
/// (CG) an enemy Keep on a higher terrace, seen by a scout then out of sight (low ground can't see up), remembered as a ghost in
/// front of an own building on the low ground: the left click selects nothing (the GhostOnRay path), the right click attacks it;
/// (T1) a line held by a pause (the tree paused 2 s: the age does not move, the line still shows, then it ends on time);
/// (T2) at game speed 2x the line lasts the same view time (2.5 s), not half;
/// (T3) two refusals of one unit five ticks apart: one line, restarted, the second error's text, two sounds;
/// (T4) a refused unit then selected: the line stays, one per unit;
/// (T5) a Teblor worker (the 1.5x faction): the line sits above its overlay line and on screen at zoom 20 and 60;
/// (T6) a refusal in the first tick after a save is loaded (StartLoaded): one sound, the line.
/// </remarks>
public partial class QaM6V27Test : Node
{
    private const string Tag = "QA M6-V27 TEST";
    private const float Tie = 0.03f;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SimRunner _runner = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private OrderNoticeView _notices = null!;
    private Sfx _sfx = null!;
    private bool[] _mask = Array.Empty<bool>();

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            await LiveEnemyRows(site: false);
            await LiveEnemyRows(site: true);
            await GhostRows();
            await TimingRows();
            await LoadedRow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the oracle (slot by slot, independent of the controller's walk) ----

    // The nearest drawn building of `owner` (any when negative) among those `shown` (all alive when empty), and its distance.
    private (int Slot, float T) Nearest(Vector3 o, Vector3 d, int owner, ReadOnlySpan<bool> shown)
    {
        var so = new NVector3(o.X, o.Y, o.Z);
        var sd = new NVector3(d.X, d.Y, d.Z);
        if (_mask.Length < B.Capacity) _mask = new bool[B.Capacity];
        int best = -1;
        float bestT = float.PositiveInfinity;
        for (int s = 0; s < B.Capacity; s++)
        {
            if (!B.Alive[s] || (owner >= 0 && B.Owner[s] != owner)) continue;
            if (!shown.IsEmpty && (s >= shown.Length || !shown[s])) continue;
            Array.Clear(_mask);
            _mask[s] = true;
            int got = BuildingPicker.PickRay(B, _data.Buildings, W.NavGrid, W.Heightmap, -1, so, sd, _views.PickTops, _views.SiteHeights,
                BuildingViews.SiteMinHeight, _mask, out float boxT);
            if (got != s) continue;
            float drawn = _views.ShownAsModel(s) ? _views.ModelRayDistance(s, o, d) : boxT;
            if (drawn < bestT) { best = s; bestT = drawn; }
        }
        return (best, bestT);
    }

    private (int Slot, float T) NearestGhost(FogView fog, Vector3 o, Vector3 d)
    {
        var so = new NVector3(o.X, o.Y, o.Z);
        var sd = new NVector3(d.X, d.Y, d.Z);
        ReadOnlySpan<bool> shown = fog.GhostShown;
        if (_mask.Length < shown.Length) _mask = new bool[shown.Length];
        int best = -1;
        float bestT = float.PositiveInfinity;
        for (int s = 0; s < shown.Length; s++)
        {
            if (!shown[s]) continue;
            Array.Clear(_mask);
            _mask[s] = true;
            int got = BuildingPicker.PickGhostRay(_mask.AsSpan(0, shown.Length), fog.Ghosts, _data.Buildings, W.NavGrid, W.Heightmap, so, sd, _views.PickTops,
                _views.SiteHeights, BuildingViews.SiteMinHeight, out float boxT);
            if (got != s) continue;
            float drawn = _views.GhostShownAsModel(s) ? _views.GhostModelRayDistance(s, o, d) : boxT;
            if (drawn < bestT) { best = s; bestT = drawn; }
        }
        return (best, bestT);
    }

    private float NodeT(Vector3 o, Vector3 d)
    {
        ResourceStore r = W.Resources;
        int node = ResourcePicker.PickRay(W.NavGrid, W.Data.Resources, r.Alive, r.TypeId, r.Cell, W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z),
            PropsView.Shape, out float t);
        return node < 0 ? float.PositiveInfinity : t;
    }

    private float UnitT(Vector3 o, Vector3 d, ReadOnlySpan<bool> shown)
    {
        UnitStore u = W.Units;
        int unit = UnitPicker.PickRay(shown, u.PrevPosition, u.Position, u.Radius, W.Heightmap, 1f, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z),
            UnitViews.ExtraBodyHeight, out float t);
        return unit < 0 ? float.PositiveInfinity : t;
    }

    // Scans the screen: where an own building is drawn and `front` (an enemy live building / site, or a ghost) is drawn before it
    // with no unit or tree in front of either, the left click must select nothing and EnemyAt must give that enemy; where the own
    // building is drawn first, the left click must select it. Returns (front pixels, own pixels, a front pixel for the right click).
    private (int Front, int Own, Vector2 At, int Target) Scan(string label, FogView? fog, bool ghosts, int frontSlot)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        UnitStore u = W.Units;
        ReadOnlySpan<bool> unitsShown = fog != null ? fog.UnitShown : u.Alive;
        ReadOnlySpan<bool> bShown = fog != null ? fog.BuildingShown : ReadOnlySpan<bool>.Empty;
        int front = 0, own = 0, bad = 0, ties = 0;
        Vector2 at = new(-1, -1);
        int target = -1;
        var examples = new List<string>();
        for (float py = 4f; py < screen.Size.Y - 4f; py += 8f)
            for (float px = 4f; px < screen.Size.X - 4f; px += 8f)
            {
                var p = new Vector2(px, py);
                Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                (int ownSlot, float ownT) = Nearest(o, d, 0, bShown);
                if (ownSlot < 0) continue;
                (int enemySlot, float enemyT) = Nearest(o, d, 1, bShown);
                (int ghostSlot, float ghostT) = ghosts && fog != null ? NearestGhost(fog, o, d) : (-1, float.PositiveInfinity);
                float frontT = Math.Min(enemyT, ghostT);
                float unitT = UnitT(o, d, unitsShown), nodeT = NodeT(o, d);
                float firstT = Math.Min(ownT, frontT);
                if (unitT < firstT + Tie || nodeT < firstT + Tie || Math.Abs(ownT - frontT) < Tie || Math.Abs(enemyT - ghostT) < Tie) { ties++; continue; }
                if (NearOwnUnit(p, 40f)) { ties++; continue; }
                _sel.ClearBuilding();
                _sel.ClearResource();
                _sel.Selection.Clear();
                Click(p);
                if (frontT < ownT)
                {
                    front++;
                    bool enemy = _sel.EnemyAt(p, out EntityHandle h, out bool isB);
                    int wantSlot = ghostT < enemyT ? ghostSlot : enemySlot;
                    bool okEnemy = enemy && isB && (ghostT < enemyT ? h == fog!.GhostHandle(ghostSlot) : h.Index == enemySlot);
                    if (_sel.SelectedBuilding != -1 || !okEnemy)
                    {
                        bad++;
                        if (examples.Count < 8) examples.Add($"{p}: {(ghostT < enemyT ? "ghost" : "enemy")} {wantSlot} at {frontT:0.00} in front of own {ownSlot} at {ownT:0.00}: selected {_sel.SelectedBuilding}, EnemyAt {enemy} {h.Index} building {isB}");
                    }
                    if (frontSlot < 0 || wantSlot == frontSlot) { if (target < 0) { at = p; target = wantSlot; } }
                }
                else
                {
                    own++;
                    if (_sel.SelectedBuilding != ownSlot)
                    {
                        bad++;
                        if (examples.Count < 8) examples.Add($"{p}: own {ownSlot} drawn first at {ownT:0.00} (front at {frontT:0.00}): selected {_sel.SelectedBuilding}");
                    }
                }
            }
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        string msg = $"{label}: {front} pixels with an enemy drawn in front of an own building (left click must select nothing), {own} with the own building first, {bad} wrong, {ties} ties / near units skipped";
        GD.Print($"  {msg}");
        foreach (string e in examples) GD.Print($"    {e}");
        Check(bad == 0, msg);
        return (front, own, at, target);
    }

    // A real right click at `p` with an own soldier selected: the controller sends one Attack on `target` (a building). The
    // scenes run with combat off, so the sim's own answer is not read; the command is.
    private static readonly System.Reflection.FieldInfo CommandsField =
        typeof(Simulation).GetField("_commands", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private async Task RightClickAttacks(string label, Vector2 p, EntityHandle target, int soldier)
    {
        await Frames(1);
        var h = new EntityHandle(soldier, W.Units.Generation[soldier]);
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        _sel.Selection.Add(h);
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        int from = q.Count;
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = p });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = p });
        var sent = new List<Command>();
        for (int i = from; i < q.Count; i++) sent.Add(q[i]);
        bool ok = sent.Count == 1 && sent[0].Kind == CommandKind.Attack && sent[0].TargetIsBuilding && sent[0].Target == target && sent[0].Unit == h;
        GD.Print($"  {label}: right click at {p} with a soldier selected sent {sent.Count} command(s): {(sent.Count > 0 ? $"{sent[0].Kind} target {sent[0].Target} building {sent[0].TargetIsBuilding}" : "none")} (want Attack on {target})");
        Check(ok, $"{label}: the right click on the enemy building in front did not send an Attack on it ({sent.Count} sent{(sent.Count > 0 ? $", {sent[0].Kind} {sent[0].Target} building {sent[0].TargetIsBuilding}" : "")})");
        _sel.Selection.Clear();
        _sim.Tick();
        _sim.Tick();
        _sim.Enqueue(Command.Stop(0, h));
        _sim.Tick();
        _sim.Tick();
    }

    // ---- (CL) / (CS) a live enemy building or site in front of an own Keep, fog on ----

    private async Task LiveEnemyRows(bool site)
    {
        string row = site ? "(CS)" : "(CL)";
        StartMatch("--players", "2", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep");
        int laborer = _data.FindUnit("malazan_laborer"), heavy = _data.FindUnit("malazan_heavy_infantry");
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, 10f);
        int house = _data.FindBuilding("malazan_billet"), barracks = _data.FindBuilding("malazan_barracks");
        Spawn(0, keep, near + new NVector2(-40f, -30f)); // keeps player 0 in (a House alone would leave it out)
        Spawn(1, keep, near + new NVector2(40f, 40f)); // keeps player 1 in
        int own = Spawn(0, house, near);
        // South (+z) is toward the camera: an enemy Barracks right against the low own House's south side is drawn in front of it.
        int enemy = -1;
        NavGrid g = W.NavGrid;
        int hx = own >= 0 ? B.Cell[own] % g.Width : 0, hy = own >= 0 ? B.Cell[own] / g.Width : 0;
        var anchors = new List<int>();
        for (int gap = 0; gap <= 2; gap++)
            for (int dx = -1; dx <= 0; dx++)
            {
                int a = (hy + _data.Buildings[house].FootprintHeight + gap) * g.Width + hx + dx;
                if (B.Fits(barracks, a)) anchors.Add(a);
            }
        if (!site)
        {
            foreach (int a in anchors)
            {
                if (enemy >= 0) break;
                enemy = Spawn(1, barracks, PlacementGhost.AnchorPoint(g, a), exact: true);
            }
        }
        else
        {
            int worker = SpawnUnit(1, laborer, near + new NVector2(14f, 18f));
            if (worker >= 0)
            {
                var wh = new EntityHandle(worker, W.Units.Generation[worker]);
                _sim.Enqueue(Command.Move(1, wh, near + new NVector2(8f, 8f))); // explore the site first
                for (int t = 0; t < 200; t++) _sim.Tick();
                foreach (int anchor in anchors)
                {
                    if (enemy >= 0) break;
                    bool can = W.CanPlace(1, barracks, anchor, out PlacementError why);
                    if (!can) { GD.Print($"  {row} anchor {anchor % g.Width},{anchor / g.Width}: {why}"); continue; }
                    _sim.Enqueue(Command.Build(1, wh, barracks, PlacementGhost.AnchorPoint(g, anchor)));
                    for (int t = 0; t < 3000; t++)
                    {
                        _sim.Tick();
                        int s = B.SlotAt(anchor % g.Width, anchor / g.Width);
                        if (s < 0 || B.Cell[s] != anchor) continue;
                        enemy = s;
                        if (B.Work[s] >= B.WorkNeeded(barracks) * 40 / 100) break;
                    }
                }
                GD.Print($"  {row} builder {worker}: state {W.Units.State[worker]}, at {W.Units.Position[worker]}, site {enemy}");
                if (enemy >= 0)
                {
                    _sim.Enqueue(Command.Move(1, wh, near + new NVector2(30f, 30f)));
                    for (int t = 0; t < 150; t++) _sim.Tick();
                }
            }
        }
        int soldier = SpawnUnit(0, heavy, near + new NVector2(-14f, -10f));
        if (!Check(own >= 0 && enemy >= 0 && soldier >= 0, $"{row} staging: own {own}, enemy {enemy}, soldier {soldier}")) { await EndMatch(); return; }
        if (site) Check(B.UnderConstruction[enemy], $"{row} the enemy building is not a site");
        _views.Sync(W);
        NVector2 ownC = Centre(own), enC = Centre(enemy);
        GD.Print($"  {row} own House {own} at {ownC}, enemy {(site ? $"site ({B.Work[enemy]} / {B.WorkNeeded(barracks)} work)" : "Barracks")} {enemy} at {enC}");
        int fronts = 0;
        bool clicked = false;
        foreach (float zoom in new[] { 20f, 40f, 60f })
        {
            _camera.SetZoom(zoom);
            NVector2 c = Centre(own);
            _camera.SetFocus(c.X, c.Y + 5f);
            await Frames(2);
            FogView fog = _sel.Fog!.Refreshed(W)!;
            Check(fog.BuildingShown[enemy], $"{row} the enemy building is not shown to player 0");
            (int front, _, Vector2 at, int target) = Scan($"{row} zoom {zoom}{(site ? " (site)" : "")}", fog, ghosts: false, enemy);
            fronts += front;
            if (!clicked && target >= 0) { clicked = true; await RightClickAttacks($"{row} zoom {zoom}", at, B.HandleOf(target), soldier); }
        }
        Check(clicked, $"{row} no right click was made");
        Check(fronts > 0, $"{row} no pixel had the enemy building drawn in front of the own House: the row tested nothing");
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // ---- (CG) a remembered enemy Keep on high ground in front of an own building on low ground ----

    private async Task GhostRows()
    {
        int keep = _data.FindBuilding("malazan_garrison_keep"), house = _data.FindBuilding("malazan_billet");
        int laborer = _data.FindUnit("malazan_laborer"), heavy = _data.FindUnit("malazan_heavy_infantry");
        int totalFront = 0;
        bool clicked = false;
        for (int seed = 1; seed <= 12 && totalFront == 0; seed++)
        {
            StartMatch("--players", "2", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high", "--seed", $"{seed}");
            NavGrid g = W.NavGrid;
            // An enemy Keep anchor on level L+1 whose north edge is 3-6 cells south of an own House on level L (camera looks
            // north: the terrace is in front); more than 4 m (the lip) between them.
            int ownAnchor = -1, enemyAnchor = -1;
            BuildingDef kd = _data.Buildings[keep], hd = _data.Buildings[house];
            for (int cy = 4; cy + kd.FootprintHeight < g.Height - 4 && enemyAnchor < 0; cy++)
                for (int cx = 4; cx + kd.FootprintWidth < g.Width - 4 && enemyAnchor < 0; cx++)
                {
                    int a = cy * g.Width + cx;
                    int lv = g.LevelAt(cx, cy);
                    if (lv < 1 || !B.Fits(keep, a) || !FlatAround(cx, cy, kd, lv)) continue;
                    for (int gap = 3; gap <= 6 && enemyAnchor < 0; gap++)
                    {
                        int hy = cy - gap - hd.FootprintHeight, hx = cx + 1;
                        if (hy < 4) continue;
                        int ha = hy * g.Width + hx;
                        if (g.LevelAt(hx, hy) >= lv || !B.Fits(house, ha) || !FlatAround(hx, hy, hd, g.LevelAt(hx, hy))) continue;
                        ownAnchor = ha;
                        enemyAnchor = a;
                    }
                }
            if (enemyAnchor < 0) { GD.Print($"  (CG) seed {seed}: no terrace pair"); await EndMatch(); continue; }
            Spawn(1, keep, PlacementGhost.AnchorPoint(g, enemyAnchor), exact: true);
            int enemy = B.SlotAt(enemyAnchor % g.Width, enemyAnchor / g.Width);
            // Keep player 1 in elsewhere, and a scout of player 0 on the terrace beside the enemy Keep.
            NVector2 ec = StartBase.FootprintCenter(g, kd, enemyAnchor);
            int scout = SpawnUnit(0, laborer, ec + new NVector2(0f, 6f));
            if (scout < 0) scout = SpawnUnit(0, laborer, ec + new NVector2(6f, 0f));
            for (int t = 0; t < 40; t++) _sim.Tick();
            bool seen = enemy >= 0 && W.Fog.CanSeeBuilding(0, enemy);
            int own = Spawn(0, house, PlacementGhost.AnchorPoint(g, ownAnchor), exact: true);
            int soldier = SpawnUnit(0, heavy, StartBase.FootprintCenter(g, hd, ownAnchor) + new NVector2(-10f, -8f));
            if (!seen || own < 0 || scout < 0 || soldier < 0)
            {
                GD.Print($"  (CG) seed {seed}: staging failed (enemy {enemy}, seen {seen}, own {own}, scout {scout}, soldier {soldier})");
                await EndMatch();
                continue;
            }
            _views.Sync(W);
            W.Units.Free(new EntityHandle(scout, W.Units.Generation[scout]));
            for (int t = 0; t < 600 && !_views.IsGhostShown(enemy); t++)
            {
                _sim.Tick();
                _views.Sync(W);
            }
            if (!_views.IsGhostShown(enemy))
            {
                GD.Print($"  (CG) seed {seed}: the enemy Keep is still seen from the own House (not a ghost)");
                await EndMatch();
                continue;
            }
            GD.Print($"  (CG) seed {seed}: enemy Keep {enemy} on level {g.LevelAt(enemyAnchor % g.Width, enemyAnchor / g.Width)} remembered (model {_views.GhostShownAsModel(enemy)}), own House {own} on level {g.LevelAt(ownAnchor % g.Width, ownAnchor / g.Width)}");
            foreach (float zoom in new[] { 20f, 40f, 60f })
            {
                _camera.SetZoom(zoom);
                NVector2 c = Centre(own);
                _camera.SetFocus(c.X, c.Y + 6f);
                await Frames(2);
                FogView fog = _sel.Fog!.Refreshed(W)!;
                (int front, _, Vector2 at, int target) = Scan($"(CG) seed {seed} zoom {zoom}", fog, ghosts: true, -1);
                totalFront += front;
                if (!clicked && target >= 0) { clicked = true; await RightClickAttacks($"(CG) zoom {zoom} ghost", at, fog.GhostHandle(target), soldier); }
            }
            _camera.SetZoom(CameraLimits.DefaultZoom);
            await EndMatch();
        }
        Check(clicked, "(CG) no right click on the ghost was made");
        Check(totalFront > 0, "(CG) no seed staged a ghost drawn in front of an own building: the GhostOnRay path was not reached");
    }

    private bool FlatAround(int cx, int cy, BuildingDef def, int level)
    {
        NavGrid g = W.NavGrid;
        for (int y = cy - 1; y <= cy + def.FootprintHeight; y++)
            for (int x = cx - 1; x <= cx + def.FootprintWidth; x++)
                if (!g.InBounds(x, y) || !g.IsPassable(x, y) || g.LevelAt(x, y) != level || B.SlotAt(x, y) >= 0) return false;
        return true;
    }

    // ---- (T) timing on the live runner ----

    private async Task TimingRows()
    {
        StartLive(new[] { "--units", "0", "--seed", "3", "--players", "2", "--mute", "--faction", "1", "teblor" });
        while (_sim.TickNumber < 4) await Frames(1);
        List<EntityHandle> mine = Workers(0);
        if (!Check(mine.Count >= 3, $"(T) only {mine.Count} workers")) { await EndLive(); return; }
        int hall = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.TownHall);
        int hut = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        string afford = _ui.PlacementText(PlacementError.CannotAfford), blocked = _ui.PlacementText(PlacementError.Blocked);
        EntityHandle w = mine[0];

        // (T1) pause.
        Focus(w);
        int slot = await Refuse(w, hall, PlacementError.CannotAfford);
        if (Check(slot >= 0, "(T1) no line"))
        {
            await Frames(3);
            float age0 = _notices.Lines.Age[slot];
            GetTree().Paused = true;
            ulong start = Time.GetTicksUsec();
            while (Time.GetTicksUsec() - start < 2_000_000UL) await Frames(1);
            float agePaused = _notices.Lines.Age[slot];
            bool shownPaused = _notices.Label(slot).Visible;
            GetTree().Paused = false;
            ulong resumed = Time.GetTicksUsec();
            while (_notices.Lines.Active[slot] && Time.GetTicksUsec() - resumed < 5_000_000UL) await Frames(1);
            double after = (Time.GetTicksUsec() - resumed) / 1e6;
            GD.Print($"  (T1) age {age0:0.000} s before a 2 s pause, {agePaused:0.000} s after it (shown {shownPaused}); gone {after:0.00} s after the resume (want ~{NoticeLines.LifetimeSeconds - age0:0.00})");
            Check(Math.Abs(agePaused - age0) < 1e-4f && shownPaused, $"(T1) the pause aged the line {age0} -> {agePaused} (shown {shownPaused})");
            Check(Math.Abs(after - (NoticeLines.LifetimeSeconds - age0)) < 0.4, $"(T1) the line ended {after:0.00} s after the resume, want {NoticeLines.LifetimeSeconds - age0:0.00}");
        }

        // (T2) game speed 2x: the line lasts the view's 2.5 s, not 1.25.
        _runner.GameSpeed = 2.0;
        slot = await Refuse(w, hall, PlacementError.CannotAfford);
        if (Check(slot >= 0, "(T2) no line"))
        {
            ulong start = Time.GetTicksUsec();
            int startTick = _sim.TickNumber;
            while (_notices.Lines.Active[slot] && Time.GetTicksUsec() - start < 6_000_000UL) await Frames(1);
            double secs = (Time.GetTicksUsec() - start) / 1e6;
            int ticks = _sim.TickNumber - startTick;
            GD.Print($"  (T2) at 2x: the line lasted {secs:0.00} s of view time over {ticks} sim ticks");
            Check(Math.Abs(secs - NoticeLines.LifetimeSeconds) < 0.4, $"(T2) at 2x the line lasted {secs:0.00} s");
        }
        _runner.GameSpeed = 1.0;

        // (T3) two refusals five ticks apart: restart, not stack; two sounds; the second error's text.
        int sounds = _sfx.PlayCount(SfxEvent.Refused);
        long restarts = _notices.Lines.Restarted;
        int s1 = await Refuse(w, hall, PlacementError.CannotAfford);
        int at1 = _sim.TickNumber;
        while (_sim.TickNumber < at1 + 5) await Frames(1);
        float ageBefore = s1 >= 0 ? _notices.Lines.Age[s1] : -1f;
        int s2 = await Refuse(w, hut, PlacementError.Blocked);
        int lines = 0;
        for (int i = 0; i < _notices.Lines.Capacity; i++) if (_notices.Lines.Active[i] && _notices.Lines.Unit[i].Index == w.Index) lines++;
        string text = s2 >= 0 ? _notices.Label(s2).Text : "";
        GD.Print($"  (T3) first line slot {s1} (age {ageBefore:0.00} s at the second), second slot {s2}: {lines} line(s) over the unit, reads '{text}', restarts +{_notices.Lines.Restarted - restarts}, sounds +{_sfx.PlayCount(SfxEvent.Refused) - sounds}");
        Check(s1 >= 0 && s1 == s2 && lines == 1 && text == blocked && _notices.Lines.Restarted - restarts == 1 && _sfx.PlayCount(SfxEvent.Refused) - sounds == 2 && ageBefore > 0.1f,
            $"(T3) slots {s1}/{s2}, {lines} lines, '{text}' (want '{blocked}'), restarts {_notices.Lines.Restarted - restarts}, sounds {_sfx.PlayCount(SfxEvent.Refused) - sounds}");

        // (T4) refused, then selected: the line stays, still one.
        EntityHandle w2 = mine[1];
        int s4 = await Refuse(w2, hall, PlacementError.CannotAfford);
        _sel.Selection.Clear();
        _sel.Selection.Add(w2);
        await Frames(3);
        int over = 0;
        for (int i = 0; i < _notices.Lines.Capacity; i++) if (_notices.Lines.Active[i] && _notices.Lines.Unit[i] == w2 && _notices.Label(i).Visible) over++;
        GD.Print($"  (T4) refused then selected: {over} visible line(s), reads '{(s4 >= 0 ? _notices.Label(s4).Text : "")}'");
        Check(s4 >= 0 && over == 1 && _notices.Label(s4).Text == afford, $"(T4) {over} lines over the selected unit");
        _sel.Selection.Clear();

        // (T5) a Teblor worker at zoom 20 and 60: above the overlay line, on screen. Fog is on (the default): own units always seen.
        float[] overlay = _match.GetNode<UnitViews>("World3D/UnitViews").OverlayLines;
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
        {
            _camera.SetZoom(zoom);
            EntityHandle w3 = mine[2];
            Focus(w3);
            int s5 = await Refuse(w3, hall, PlacementError.CannotAfford);
            await Frames(2);
            if (!Check(s5 >= 0, $"(T5) zoom {zoom}: no line")) continue;
            Label3D label = _notices.Label(s5);
            Vector3 ground = UnitViews.GroundPoint(W, w3.Index, (float)_runner.Alpha);
            float line = overlay[W.Units.TypeId[w3.Index]];
            Camera3D cam = GetViewport().GetCamera3D();
            Vector2 sp = cam.UnprojectPosition(label.GlobalPosition);
            bool onScreen = GetViewport().GetVisibleRect().HasPoint(sp);
            float above = label.GlobalPosition.Y - ground.Y;
            GD.Print($"  (T5) {_data.Units[W.Units.TypeId[w3.Index]].Key} zoom {zoom}: overlay line {line:0.00} m, words {above:0.00} m over the ground, screen {sp:0}, visible {label.Visible}, fog shows unit {_sel.Fog?.Refreshed(W)?.UnitShown[w3.Index]}");
            Check(label.Visible && onScreen && above > line, $"(T5) zoom {zoom}: words {above:0.00} m over the ground, overlay line {line:0.00}, on screen {onScreen}");
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndLive();
    }

    // ---- (T6) a refusal in the first tick after a load ----

    private async Task LoadedRow()
    {
        // Stage in a bare sim: a worker of player 0 with 8 queued orders; save; load into a Match; enqueue a 9th before the
        // first tick runs: refused on arrival in the first tick after the load.
        StartLive(new[] { "--units", "0", "--seed", "4", "--players", "2", "--mute" });
        while (_sim.TickNumber < 4) await Frames(1);
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        EntityHandle w = Workers(0)[0];
        NVector2 pos = W.Units.Position[w.Index];
        _sim.Enqueue(Command.Move(0, w, pos + new NVector2(0f, 30f)));
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) _sim.Enqueue(Command.Move(0, w, pos + new NVector2(k, 25f), queued: true));
        _sim.Tick();
        _sim.Tick();
        var stream = new MemoryStream();
        bool saved = _sim.TrySave(stream, out SaveError err);
        await EndLive();
        if (!Check(saved, $"(T6) save failed: {err}")) return;
        stream.Position = 0;
        if (!Check(Simulation.TryLoad(_data, stream, out Simulation? loaded, out SaveError lerr) && loaded != null, $"(T6) load failed: {lerr}")) return;
        Check(loaded!.World.DroppedOrders.Length == 0, "(T6) the loaded sim lists dropped orders before a tick");
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _match.StartLoaded(_data, loaded, LaunchOptions.Parse(new[] { "--mute" }), "qa-m6v27.save");
        Bind();
        Check(_notices.Enabled, "(T6) the notices are off in a loaded match");
        int sounds = _sfx.PlayCount(SfxEvent.Refused);
        int loadTick = _sim.TickNumber;
        int listedAt = -1;
        void Log(Simulation sim)
        {
            if (listedAt < 0)
                foreach (DroppedOrder e in sim.World.DroppedOrders) if (e.Unit == w && e.QueueFull) listedAt = sim.TickNumber;
        }
        _runner.Ticked += Log;
        _sim.Enqueue(Command.Move(0, w, pos + new NVector2(4f, 4f), queued: true));
        // One tick a frame (the runner off, ticked by hand as the runner does: Tick, then Ticked) so the frame after the entry is seen.
        var raise = typeof(SimRunner).GetField("Ticked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        for (int k = 0; k < 4 && listedAt < 0; k++)
        {
            _sim.Tick();
            ((Action<Simulation>?)raise?.GetValue(_runner))?.Invoke(_sim);
        }
        _runner.Ticked -= Log;
        await Frames(1);
        int slot = LineOf(w);
        string want = _ui.Orders(OrdersText.QueueFull);
        GD.Print($"  (T6) loaded at tick {loadTick}; the ninth order listed at tick {listedAt} (tick {listedAt - loadTick} after the load): sounds +{_sfx.PlayCount(SfxEvent.Refused) - sounds}, line '{(slot >= 0 ? _notices.Label(slot).Text : "none")}'");
        Check(listedAt > loadTick && listedAt <= loadTick + 2, $"(T6) the ninth order was listed at tick {listedAt}, loaded at {loadTick}");
        Check(slot >= 0 && _notices.Label(slot).Visible && _notices.Label(slot).Text == want && _sfx.PlayCount(SfxEvent.Refused) - sounds == 1,
            $"(T6) after the refusal in a loaded match: line {slot} '{(slot >= 0 ? _notices.Label(slot).Text : "")}', sounds {_sfx.PlayCount(SfxEvent.Refused) - sounds}");
        await EndLive();
    }

    // ---- helpers ----

    // A Stop and a queued Build of `type` where CanPlace refuses exactly `want`; waits for that unit's entry and a frame; the line slot.
    private async Task<int> Refuse(EntityHandle w, int type, PlacementError want)
    {
        int anchor = AnchorWith(want, type, w, 5);
        int before = _notices.SoundsCued;
        int startTick = _sim.TickNumber;
        _sim.Enqueue(Command.Stop(0, w));
        _sim.Enqueue(Command.Build(0, w, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor), queued: true));
        ulong start = Time.GetTicksUsec();
        while (_notices.SoundsCued == before && Time.GetTicksUsec() - start < 5_000_000UL) await Frames(1);
        await Frames(1);
        int slot = LineOf(w);
        if (slot < 0) GD.Print($"    no line for {w} after {_sim.TickNumber - startTick} ticks ({want})");
        return slot;
    }

    private int AnchorWith(PlacementError want, int type, EntityHandle near, int away)
    {
        NavGrid g = W.NavGrid;
        NVector2 p = W.Units.Position[near.Index];
        int x0 = (int)(p.X / MapConstants.CellSize), y0 = (int)(p.Y / MapConstants.CellSize);
        for (int r = away; r < 40; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !g.InBounds(x0 + dx, y0 + dy)) continue;
                    int a = (y0 + dy) * g.Width + x0 + dx;
                    if (!W.CanPlace(0, type, a, out PlacementError e) && e == want) return a;
                }
        throw new InvalidOperationException($"no anchor refused {want} near {p}");
    }

    private int LineOf(EntityHandle unit)
    {
        for (int i = 0; i < _notices.Lines.Capacity; i++)
            if (_notices.Lines.Active[i] && _notices.Lines.Unit[i] == unit) return i;
        return -1;
    }

    private List<EntityHandle> Workers(int player)
    {
        var list = new List<EntityHandle>();
        UnitStore u = W.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == player && _data.Units[u.TypeId[i]].IsWorker) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    private void Focus(EntityHandle w)
    {
        NVector2 p = W.Units.Position[w.Index];
        _camera.SetFocus(p.X, p.Y);
    }

    private void StartLive(string[] args)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(args));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        Bind();
    }

    private void Bind()
    {
        _sim = _runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _notices = _match.GetNode<OrderNoticeView>("World3D/OrderNotices");
        _sfx = _match.GetNode<Sfx>("Sfx");
    }

    private async Task EndLive()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    private void StartMatch(params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        Bind();
        _sim.Tick();
        _sim.Tick();
    }

    private async Task EndMatch() => await EndLive();

    private int Spawn(int player, int type, NVector2 near, bool exact = false)
    {
        BuildingDef def = _data.Buildings[type];
        int anchor;
        if (exact)
        {
            if (!W.NavGrid.WorldToCell(near, out int cx, out int cy)) return -1;
            anchor = cy * W.NavGrid.Width + cx;
        }
        else anchor = OpenAnchor(def, near);
        if (anchor < 0) return -1;
        _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        _sim.Tick();
        _sim.Tick();
        int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    private int SpawnUnit(int player, int type, NVector2 at)
    {
        UnitStore u = W.Units;
        var alive = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) alive[i] = u.Alive[i];
        _sim.Enqueue(Command.SpawnUnit(player, type, at));
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && !alive[i]) return i;
        return -1;
    }

    private int OpenAnchor(BuildingDef def, NVector2 near)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int r = 0; r <= 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!g.WorldToCell(near + new NVector2(dx, dy) * MapConstants.CellSize, out int cx, out int cy)) continue;
                    if (cx < 3 || cy < 3 || cx + def.FootprintWidth >= g.Width - 3 || cy + def.FootprintHeight >= g.Height - 3) continue;
                    int a = cy * g.Width + cx;
                    if (!B.Fits(type, a)) continue;
                    int level = g.LevelAt(cx, cy);
                    bool ok = true;
                    for (int y = cy - 1; y <= cy + def.FootprintHeight && ok; y++)
                        for (int x = cx - 1; x <= cx + def.FootprintWidth && ok; x++)
                            if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                    if (ok) return a;
                }
        return -1;
    }

    private NVector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, _data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private bool NearOwnUnit(Vector2 p, float px)
    {
        UnitStore u = W.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0) continue;
            if (_sel.TryScreenPosition(i, out Vector2 s) && s.DistanceTo(p) < px) return true;
        }
        return false;
    }

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
