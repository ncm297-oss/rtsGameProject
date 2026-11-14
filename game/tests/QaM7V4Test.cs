using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M7-V4 (a building's data <c>scale</c> drawn as its box height). Forged data at the limits: the Watchtower 1x1 at 0.5,
/// the Depot 1x1 at 3.0, the Garrison Keep (4x4) at 3.0, the Barracks (3x3) at 0.5, the Billet (2x2) at 3.0.
/// <list type="bullet">
/// <item>Limits: every forged type on flat ground and on the most sloped open footprint beside a cliff / ramp: box = footprint by
/// 3 m x scale, base on the centre's ground, bar 0.6 m over the drawn top and over the highest footprint corner.</item>
/// <item>A 3.0 Keep site cancelled half-built (top at 9 m x progress), the slot then reused by a 0.5 building in the same
/// frame: the new box is the new type's height at full rise.</item>
/// <item>A 3.0 enemy Keep seen, left, then destroyed under fog: its ghost stays 9 m tall; a 3.0 enemy Billet site seen and left:
/// its remembered foundation is 15 % of 9 m, on the ground.</item>
/// <item>The placement ghost over blocked ground: red, its reason label over the drawn top of a 3.0 Keep, then lowered when
/// <c>Begin</c> switches to a 0.5 type.</item>
/// <item>A damaged 3.0 Keep's bar at the nearest and farthest zoom: on screen and above the drawn top on screen.</item>
/// <item>A 3.0 Keep behind a 0.5 Barracks: what a click on each visible pixel of the tall box selects (reported; the hit test
/// keeps the 3 m box by the Producer's decision).</item>
/// <item>No allocation over 20 syncs with the limit types, sites and ghosts shown.</item>
/// </list>
/// Headless: prints "QA M7-V4 TEST PASS" or FAIL lines. Windowed with <c>-- --shots &lt;dir&gt; --placeholder-art</c> it saves pictures.
/// </summary>
public partial class QaM7V4Test : Node
{
    private const string Tag = "QA M7-V4 TEST";
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // id, scale, footprint side (0 = unchanged)
    private static readonly (string Id, float Scale, int Side)[] Forged =
    {
        ("malazan_watchtower", 0.5f, 1), ("malazan_depot", 3.0f, 1), ("malazan_garrison_keep", 3.0f, 0),
        ("malazan_barracks", 0.5f, 0), ("malazan_billet", 3.0f, 0),
    };

    private readonly List<string> _failures = new();
    private string? _shots;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private GameData _data = null!;
    private int _keep, _tower, _depot, _barracks, _billet, _laborer;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m7v4-{OS.GetProcessId()}"));
        UnitViews.UseModels = false;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            _data = Forge(ProjectSettings.GlobalizePath("res://data"), root);
            _keep = _data.FindBuilding("malazan_garrison_keep");
            _tower = _data.FindBuilding("malazan_watchtower");
            _depot = _data.FindBuilding("malazan_depot");
            _barracks = _data.FindBuilding("malazan_barracks");
            _billet = _data.FindBuilding("malazan_billet");
            _laborer = _data.FindUnit("malazan_laborer");

            StartMatch();
            await Limits();
            await CancelledSite();
            await DestroyedUnderFog();
            await GhostOverBlocked();
            await BarsAtZoom();
            await TallBehindShort();
            await EndMatch();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private GameData Forge(string dataDir, string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        string path = Path.Combine(copy, "factions", "malazan", "buildings.json");
        JsonNode doc = JsonNode.Parse(File.ReadAllText(path))!;
        foreach ((string id, float scale, int side) in Forged)
        {
            JsonNode n = doc["buildings"]!.AsArray().First(b => (string?)b!["id"] == id)!;
            n["scale"] = (double)(decimal)scale;
            if (side > 0) n["footprint"] = new JsonObject { ["width"] = side, ["height"] = side };
            n["requires"] = new JsonArray();
        }
        File.WriteAllText(path, doc.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        return loaded.Data!;
    }

    private void StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--mute", "--resources", "high",
            "--faction", "1", "malazan", "--faction", "2", "malazan", "--zoom", CameraLimits.DefaultZoom.ToString(CultureInfo.InvariantCulture) }));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        typeof(Simulation).GetProperty("RefusedForTests", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_sim, new Action<Command, CommandRefusal, int>((c, why, d) => GD.Print($"  note: refused {c.Kind} p{c.Player} type {c.TypeId}: {why} {d}")));
        // One unit each, away from the rows, so neither player is defeated for owning nothing.
        _sim.Enqueue(Command.SpawnUnit(0, _laborer, West + new System.Numerics.Vector2(0f, -40f)));
        _sim.Enqueue(Command.SpawnUnit(1, _laborer, StartLayout.Block(G, 1, west: false, 1f)[0] + new System.Numerics.Vector2(0f, -40f)));
        _sim.Tick();
        _sim.Tick();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    private System.Numerics.Vector2 West => StartLayout.Block(G, 1, west: true, 1f)[0];

    // ---- Limits: flat and beside a cliff / ramp -------------------------------------------------------------------------
    private async Task Limits()
    {
        var x = West;
        int[] types = { _tower, _depot, _keep, _barracks, _billet };
        float off = -8f;
        foreach (int type in types)
        {
            int slot = Spawn(0, type, x + new System.Numerics.Vector2(off, -14f));
            off -= _data.Buildings[type].FootprintWidth * MapConstants.CellSize + 4f;
            if (!Check(slot >= 0, $"limits: {_data.Buildings[type].Key} not spawned flat")) continue;
            _views.Sync(W);
            BoxRow(slot, "flat");
        }
        foreach (int type in new[] { _tower, _depot, _keep })
        {
            int slot = SpawnAtLip(type, out float spread);
            if (!Check(slot >= 0, $"limits: {_data.Buildings[type].Key} no lip footprint")) continue;
            _views.Sync(W);
            GD.Print($"  lip {_data.Buildings[type].Key}: corner spread {spread:0.##} m");
            BoxRow(slot, "lip");
        }
        await Frames(1);
    }

    private void BoxRow(int slot, string what)
    {
        BuildingDef def = W.Data.Buildings[B.TypeId[slot]];
        Node3D root = _views.ViewOf(slot)!;
        MeshInstance3D box = _views.BoxOf(slot);
        var mesh = (BoxMesh)box.Mesh;
        System.Numerics.Vector2 c = Centre(slot);
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), want = 3f * def.Scale;
        float top = root.Position.Y + box.Position.Y + mesh.Size.Y * box.Scale.Y / 2f;
        float bottom = root.Position.Y + box.Position.Y - mesh.Size.Y * box.Scale.Y / 2f;
        float barY = root.Position.Y + root.GetNode<MeshInstance3D>("BarBack").Position.Y;
        float hiCorner = HighestCorner(def, B.Cell[slot]);
        GD.Print($"  {what} {def.Key} scale {def.Scale}: {mesh.Size.X}x{mesh.Size.Y * box.Scale.Y}x{mesh.Size.Z}, top {top - ground:0.###}, bar {barY - ground:0.###}, highest corner {hiCorner - ground:0.###}");
        Check(mesh.Size.X == def.FootprintWidth * 2f && mesh.Size.Z == def.FootprintHeight * 2f, $"{what} {def.Key}: box {mesh.Size} not the footprint");
        Check(Math.Abs(bottom - ground) < 1e-3f && Math.Abs(top - ground - want) < 1e-3f, $"{what} {def.Key}: bottom {bottom - ground}, top {top - ground}, want 0..{want}");
        Check(Math.Abs(barY - top - 0.6f) < 1e-3f, $"{what} {def.Key}: bar {barY - top} m over the drawn top, want 0.6");
        // Reported, not checked: beside a cliff the highest corner is the cliff's top edge (the box stands at its foot).
        if (barY - BuildingViews.BarThickness / 2f <= hiCorner)
            GD.Print($"    note: {what} {def.Key}: bar bottom {barY - BuildingViews.BarThickness / 2f - ground:0.###} m, under the highest footprint corner {hiCorner - ground:0.###} m");
    }

    private float HighestCorner(BuildingDef def, int anchor)
    {
        float x0 = anchor % G.Width * 2f, y0 = anchor / G.Width * 2f, x1 = x0 + def.FootprintWidth * 2f, y1 = y0 + def.FootprintHeight * 2f;
        float h = float.MinValue;
        foreach ((float x, float y) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) }) h = Math.Max(h, TerrainHeight.At(W.Heightmap, x, y));
        return h;
    }

    private int SpawnAtLip(int type, out float bestSlope)
    {
        BuildingDef hd = _data.Buildings[type];
        int best = -1;
        bestSlope = -1f;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            int x = cell % G.Width, y = cell / G.Width;
            if (x < 3 || y < 3 || x + hd.FootprintWidth + 3 >= G.Width || y + hd.FootprintHeight + 3 >= G.Height) continue;
            bool nearLip = false;
            for (int dy = -2; dy <= hd.FootprintHeight + 1 && !nearLip; dy++)
                for (int dx = -2; dx <= hd.FootprintWidth + 1 && !nearLip; dx++)
                    nearLip = (G.FlagsAt(x + dx, y + dy) & (NavFlags.Cliff | NavFlags.Ramp)) != 0;
            if (!nearLip || !B.Fits(type, cell)) continue;
            float x0 = x * 2f, y0 = y * 2f, x1 = x0 + hd.FootprintWidth * 2f, y1 = y0 + hd.FootprintHeight * 2f;
            float h0 = TerrainHeight.At(W.Heightmap, x0, y0), h1 = TerrainHeight.At(W.Heightmap, x1, y0), h2 = TerrainHeight.At(W.Heightmap, x0, y1), h3 = TerrainHeight.At(W.Heightmap, x1, y1);
            float slope = Math.Max(Math.Max(h0, h1), Math.Max(h2, h3)) - Math.Min(Math.Min(h0, h1), Math.Min(h2, h3));
            if (slope > bestSlope) (best, bestSlope) = (cell, slope);
        }
        if (best < 0) return -1;
        _sim.Enqueue(Command.SpawnBuilding(0, type, Rts.Sim.ViewApi.PlacementGhost.AnchorPoint(G, best)));
        _sim.Tick();
        _sim.Tick();
        int slot = B.SlotAt(best % G.Width, best / G.Width);
        return slot >= 0 && B.Cell[slot] == best ? slot : -1;
    }

    // ---- A site cancelled half-built; its slot reused in the same frame --------------------------------------------------
    private async Task CancelledSite()
    {
        System.Numerics.Vector2 near = West + new System.Numerics.Vector2(-10f, 16f);
        int anchor = OpenAnchor(_data.Buildings[_keep], near);
        if (!Check(anchor >= 0, "cancel: no anchor")) return;
        int site = BuildSite(0, _keep, anchor, out int worker);
        if (site < 0) return;
        int gen = B.Generation[site];
        float fill = 0f;
        for (int t = 0; t < 6000 && fill < 0.5f; t++)
        {
            _sim.Tick();
            BuildingBars.Of(B, W.Data.Buildings, site, out fill);
        }
        _views.Sync(W);
        MeshInstance3D box = _views.BoxOf(site);
        float top = box.Position.Y + ((BoxMesh)box.Mesh).Size.Y * box.Scale.Y / 2f;
        GD.Print($"  cancel: keep site at {fill:0.###}, top {top:0.###} m");
        Check(_views.ShownAsSite(site) && Math.Abs(top - 9f * Math.Max(BuildingViews.SiteMinHeight, fill)) < 1e-3f, $"cancel: half-built 3.0 keep top {top}, want {9f * fill}");
        // Cancel, then spawn a 0.5 building, both before the view syncs again.
        System.Numerics.Vector2 c = Centre(site);
        _sim.Enqueue(Command.Cancel(0, c));
        _sim.Tick();
        _sim.Tick();
        Check(!B.Alive[site] || B.Generation[site] != gen, "cancel: site still alive after Cancel");
        _sim.Enqueue(Command.SpawnBuilding(0, _barracks, Rts.Sim.ViewApi.PlacementGhost.AnchorPoint(G, anchor)));
        _sim.Tick();
        _sim.Tick();
        int reuse = B.SlotAt(anchor % G.Width, anchor / G.Width);
        GD.Print($"  cancel: reused slot {reuse} (site was {site})");
        _views.Sync(W);
        if (Check(reuse >= 0, "cancel: the 0.5 barracks did not spawn on the freed ground"))
        {
            Check(reuse == site, $"cancel: slot not reused ({reuse} vs {site}); the same-slot path is untested");
            BoxRow(reuse, "reused");
            Check(Math.Abs(_views.BoxOf(reuse).Scale.Y - 1f) < 1e-5f, $"cancel: reused box rise {_views.BoxOf(reuse).Scale.Y}, want 1");
            // And back: the 0.5 barracks destroyed, a 3.0 keep site in the slot (rise from 15 % of 9 m).
            DamageMethod.Invoke(B, new object[] { B.HandleOf(reuse), 100000 });
            _sim.Tick();
            int again = BuildSite(0, _keep, anchor, out _);
            if (again >= 0)
            {
                _views.Sync(W);
                MeshInstance3D b2 = _views.BoxOf(again);
                float t2 = b2.Position.Y + ((BoxMesh)b2.Mesh).Size.Y * b2.Scale.Y / 2f;
                BuildingBars.Of(B, W.Data.Buildings, again, out float f2);
                Check(Math.Abs(t2 - 9f * Math.Max(BuildingViews.SiteMinHeight, f2)) < 1e-3f, $"cancel: new keep site in slot {again}: top {t2}, want {9f * Math.Max(0.15f, f2)}");
            }
        }
        await Frames(1);
    }

    private int BuildSite(int player, int type, int anchor, out int worker)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, anchor);
        System.Numerics.Vector2 spot = c + new System.Numerics.Vector2(0f, def.FootprintHeight + 0.6f);
        if (!G.WorldToCell(spot, out int spx, out int spy) || !G.IsPassable(spx, spy)) spot = c - new System.Numerics.Vector2(0f, def.FootprintHeight + 0.6f);
        _sim.Enqueue(Command.SpawnUnit(player, _laborer, spot));
        _sim.Tick();
        _sim.Tick();
        worker = -1;
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == player && W.Units.TypeId[i] == _laborer && System.Numerics.Vector2.Distance(W.Units.Position[i], spot) < 1.5f) worker = i;
        if (!Check(worker >= 0, $"site {def.Key}: no laborer")) return -1;
        for (int t = 0; t < 4; t++) _sim.Tick(); // its sight explores the ground (else Unexplored)
        _sim.Enqueue(Command.Build(player, new EntityHandle(worker, W.Units.Generation[worker]), type, Rts.Sim.ViewApi.PlacementGhost.AnchorPoint(G, anchor)));
        int site = -1;
        for (int t = 0; t < 10 && site < 0; t++)
        {
            _sim.Tick();
            site = B.SlotAt(anchor % G.Width, anchor / G.Width);
        }
        Check(site >= 0 && B.UnderConstruction[site], $"site {def.Key}: none at {anchor} (gold {W.Gold[player]}, wood {W.Wood[player]})");
        return site >= 0 && B.UnderConstruction[site] ? site : -1;
    }

    // ---- Destroyed and remembered under fog ------------------------------------------------------------------------------
    private async Task DestroyedUnderFog()
    {
        System.Numerics.Vector2 far = West + new System.Numerics.Vector2(60f, -12f);
        int eKeep = Spawn(1, _keep, far);
        int billetAnchor = OpenAnchor(_data.Buildings[_billet], far + new System.Numerics.Vector2(14f, 0f));
        if (!Check(eKeep >= 0 && billetAnchor >= 0, "fog: enemy keep / billet anchor")) return;
        int eSite = BuildSite(1, _billet, billetAnchor, out int eWorker);
        if (eSite < 0) return;
        // The enemy worker leaves: the site stays a site (no one builds).
        _sim.Enqueue(Command.Stop(1, new EntityHandle(eWorker, W.Units.Generation[eWorker])));
        _sim.Enqueue(Command.Move(1, new EntityHandle(eWorker, W.Units.Generation[eWorker]), far + new System.Numerics.Vector2(0f, 30f)));
        System.Numerics.Vector2 between = (Centre(eKeep) + Centre(eSite)) / 2f + new System.Numerics.Vector2(0f, 9f);
        _sim.Enqueue(Command.SpawnUnit(0, _laborer, between));
        _sim.Tick();
        _sim.Tick();
        int scout = -1;
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 0 && System.Numerics.Vector2.Distance(W.Units.Position[i], between) < 1.5f) scout = i;
        if (!Check(scout >= 0, "fog: no scout")) return;
        _sim.Enqueue(Command.Move(0, new EntityHandle(scout, W.Units.Generation[scout]), Centre(eSite) + new System.Numerics.Vector2(0f, 4f)));
        for (int t = 0; t < 600 && !(W.Fog.CanSeeBuilding(0, eKeep) && W.Fog.CanSeeBuilding(0, eSite)); t++) _sim.Tick();
        Check(W.Fog.CanSeeBuilding(0, eKeep) && W.Fog.CanSeeBuilding(0, eSite), "fog: scout never saw the row");
        _views.Sync(W);
        _sim.Enqueue(Command.Move(0, new EntityHandle(scout, W.Units.Generation[scout]), West + new System.Numerics.Vector2(0f, 10f)));
        for (int t = 0; t < 1500 && !(_views.IsGhostShown(eKeep) && _views.IsGhostShown(eSite)); t++)
        {
            _sim.Tick();
            _views.Sync(W);
        }
        int keepGen = B.Generation[eKeep];
        DamageMethod.Invoke(B, new object[] { B.HandleOf(eKeep), 100000 });
        _sim.Tick();
        _sim.Tick();
        _views.Sync(W);
        Check(!B.Alive[eKeep] || B.Generation[eKeep] != keepGen, "fog: keep not destroyed");
        GhostRow(eKeep, _keep, site: false, "destroyed keep");
        GhostRow(eSite, _billet, site: true, "remembered billet site");
        // Allocation with the limit types, sites and ghosts on show.
        _views.Sync(W);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 20; f++) _views.Sync(W);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"alloc: 20 syncs allocated {bytes} bytes");
        if (_shots != null && DisplayServer.GetName() != "headless")
        {
            System.Numerics.Vector2 m = Centre(eSite) + new System.Numerics.Vector2(-7f, 0f);
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(m.X, m.Y);
            await Shot("qa-ghosts-under-fog");
        }
        await Frames(1);
    }

    private void GhostRow(int slot, int type, bool site, string what)
    {
        BuildingDef def = _data.Buildings[type];
        if (!Check(_views.IsGhostShown(slot), $"fog: {what} not drawn as a ghost")) return;
        Check(_views.GhostShownAsSite(slot) == site, $"fog: {what} shown as site {_views.GhostShownAsSite(slot)}, want {site}");
        MeshInstance3D g = _views.GhostBoxOf(slot)!;
        var mesh = (BoxMesh)g.Mesh;
        int cell = _views.GhostGeneration(slot) != 0 ? CellOfGhost(slot) : -1;
        System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), want = 3f * def.Scale * (site ? BuildingViews.SiteMinHeight : 1f);
        float top = g.Position.Y + mesh.Size.Y * g.Scale.Y / 2f, bottom = g.Position.Y - mesh.Size.Y * g.Scale.Y / 2f;
        GD.Print($"  ghost {what}: {mesh.Size.X}x{mesh.Size.Y * g.Scale.Y}x{mesh.Size.Z}, bottom {bottom - ground:0.###}, top {top - ground:0.###} (want {want:0.###})");
        Check(Math.Abs(bottom - ground) < 1e-3f && Math.Abs(top - ground - want) < 1e-3f, $"fog: {what} ghost from {bottom - ground} to {top - ground}, want 0..{want}");
    }

    private int CellOfGhost(int slot)
    {
        var field = typeof(BuildingViews).GetField("_ghostCell", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((int[])field.GetValue(_views)!)[slot];
    }

    // ---- Placement ghost over blocked ground --------------------------------------------------------------------------
    private async Task GhostOverBlocked()
    {
        var ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        int blocker = -1;
        for (int i = 0; i < B.Capacity && blocker < 0; i++) if (B.Alive[i] && B.Owner[i] == 0 && !B.UnderConstruction[i]) blocker = i;
        if (!Check(blocker >= 0, "ghost: no blocker")) return;
        System.Numerics.Vector2 c = Centre(blocker);
        _camera.SetFocus(c.X, c.Y);
        await Frames(1);
        Vector2 screen = _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y), c.Y));
        float lastBottom = 0f;
        foreach (int type in new[] { _keep, _tower, _depot, _keep })
        {
            ghost.Begin(type); // no End between: Begin switches type
            ghost.ScreenOverride = screen;
            await Frames(2);
            float h = 3f * _data.Buildings[type].Scale;
            var mesh = (BoxMesh)ghost.Box.Mesh;
            float px = ghost.ReasonLabel.PixelSize;
            float bottom = ghost.ReasonLabel.Position.Y - 0.5f * BuildGhost.ReasonFontSize * px;
            GD.Print($"  ghost over blocked {_data.Buildings[type].Key}: valid {ghost.Valid} ({ghost.Reason}), box h {mesh.Size.Y}, label bottom {bottom:0.###} m");
            Check(!ghost.Valid, $"ghost {type}: valid over a building");
            Check(Math.Abs(mesh.Size.Y - h) < 1e-4f && Math.Abs(ghost.Box.Position.Y - h / 2f) < 1e-4f, $"ghost {type}: box {mesh.Size.Y} at {ghost.Box.Position.Y}, want {h}");
            Check(ghost.ReasonLabel.Visible && bottom > h && bottom < h + 1.5f, $"ghost {type}: label bottom {bottom}, drawn top {h}");
            lastBottom = bottom;
        }
        ghost.ScreenOverride = null;
        ghost.End();
        _ = lastBottom;
    }

    // ---- Bars of a damaged 3.0 Keep at the nearest and farthest zoom ----------------------------------------------------
    private async Task BarsAtZoom()
    {
        int keep = -1;
        for (int i = 0; i < B.Capacity; i++)
            if (B.Alive[i] && B.Owner[i] == 0 && B.TypeId[i] == _keep && !B.UnderConstruction[i] && B.Cell[i] >= 0) { keep = i; break; }
        if (!Check(keep >= 0, "bars: no 3.0 keep")) return;
        DamageMethod.Invoke(B, new object[] { B.HandleOf(keep), _data.Buildings[_keep].Hp / 2 });
        _sim.Tick();
        System.Numerics.Vector2 c = Centre(keep);
        Vector2 view = GetViewport().GetVisibleRect().Size;
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            _views.Sync(W);
            BuildingBarKind bar = _views.ShownBar(keep, out float fill);
            MeshInstance3D f = _views.BarFillOf(keep);
            Vector3 barWorld = f.GlobalPosition;
            Vector2 barScreen = _camera.UnprojectPosition(barWorld);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
            Vector2 topScreen = _camera.UnprojectPosition(new Vector3(c.X, ground + 9f, c.Y));
            bool behind = _camera.IsPositionBehind(barWorld);
            GD.Print($"  bars @{zoom}: {bar} {fill:0.##} at y {barWorld.Y - ground:0.###} m, screen {barScreen}, drawn top centre screen {topScreen}, view {view}");
            Check(bar == BuildingBarKind.HitPoints && f.Visible, $"bars @{zoom}: {bar} visible {f.Visible}");
            // The shipped Teblor 1.25 (bar 4.35 m) must be on screen with its building centred, at every zoom; the 3.0 limit is reported (BUG-1381).
            Vector2 teblorBar = _camera.UnprojectPosition(new Vector3(c.X, ground + BuildingViews.BarHeight + 3f * 0.25f, c.Y));
            Check(teblorBar.Y >= 0 && teblorBar.Y <= view.Y, $"bars @{zoom}: a 1.25 building's bar off screen at {teblorBar}");
            bool on = !behind && barScreen.X >= 0 && barScreen.Y >= 0 && barScreen.X <= view.X && barScreen.Y <= view.Y;
            if (!on) GD.Print($"    note: bars @{zoom}: the 3.0 keep's bar is off screen at {barScreen} with the keep centred (BUG-1381)");
            Check(barScreen.Y < topScreen.Y, $"bars @{zoom}: bar at screen y {barScreen.Y} not over the drawn top {topScreen.Y}");
            if (_shots != null && DisplayServer.GetName() != "headless") await Shot($"qa-keep3-bar-zoom{zoom:0}");
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
    }

    // ---- A tall building behind a short one -----------------------------------------------------------------------------
    private async Task TallBehindShort()
    {
        // The camera looks along +/- z; put the short 0.5 Barracks on the camera side of a 3.0 Keep.
        _camera.SetZoom(CameraLimits.DefaultZoom);
        System.Numerics.Vector2 basePt = West + new System.Numerics.Vector2(-20f, 40f);
        int tall = Spawn(0, _keep, basePt);
        if (!Check(tall >= 0, "tall: keep")) return;
        System.Numerics.Vector2 tc = Centre(tall);
        _camera.SetFocus(tc.X, tc.Y);
        await Frames(1);
        float g0 = TerrainHeight.At(W.Heightmap, tc.X, tc.Y);
        bool cameraSouth = _camera.UnprojectPosition(new Vector3(tc.X, g0, tc.Y + 5f)).Y > _camera.UnprojectPosition(new Vector3(tc.X, g0, tc.Y)).Y;
        float dir = cameraSouth ? 1f : -1f;
        int shortB = Spawn(0, _barracks, tc + new System.Numerics.Vector2(-1f, dir * 8f));
        if (!Check(shortB >= 0, "tall: short barracks")) return;
        GD.Print($"  tall: keep centre {tc}, barracks centre {Centre(shortB)} (camera side {(cameraSouth ? "+z" : "-z")})");
        _views.Sync(W);
        await Frames(2);
        MeshInstance3D tb = _views.BoxOf(tall), sb = _views.BoxOf(shortB);
        Aabb tAabb = tb.GlobalTransform * tb.GetAabb(), sAabb = sb.GlobalTransform * sb.GetAabb();
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int k = 0; k < 16; k++)
        {
            Vector2 p = _camera.UnprojectPosition(k < 8 ? tAabb.GetEndpoint(k) : sAabb.GetEndpoint(k - 8));
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        int onTall = 0, tallPicksTall = 0, tallPicksShort = 0, tallPicksNone = 0, onShort = 0, shortPicksShort = 0, shortWrong = 0, airPicksShort = 0;
        for (float y = minY; y <= maxY; y += 2f)
            for (float x = minX; x <= maxX; x += 2f)
            {
                var px = new Vector2(x, y);
                Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
                float? tt = RayT(tAabb, o, d), ts = RayT(sAabb, o, d);
                int pick = _sel.PickBuilding(px);
                if (ts is float s && (tt is not float t0 || s <= t0))
                {
                    onShort++;
                    if (pick == shortB) shortPicksShort++; else shortWrong++;
                }
                else if (tt is float)
                {
                    onTall++;
                    if (pick == tall) tallPicksTall++;
                    else if (pick == shortB) tallPicksShort++;
                    else tallPicksNone++;
                }
                else if (pick == shortB) airPicksShort++;
            }
        GD.Print($"  tall behind short: {onTall} px on the drawn 9 m keep: {tallPicksTall} pick it, {tallPicksShort} pick the 1.5 m barracks in front, {tallPicksNone} nothing; " +
            $"{onShort} px on the drawn barracks: {shortPicksShort} pick it, {shortWrong} other; {airPicksShort} px on neither pick the barracks");
        // Ring: selecting the tall keep shows the ring under it, regardless of the short box in front.
        _sel.SelectBuilding(tall);
        await Frames(1);
        Check(_sel.SelectedBuilding == tall, "tall: keep not selectable");
        // Box-select over both never selects a building (unchanged rule) and drops the building.
        int n = _sel.BoxSelect(new Vector2(minX, minY), new Vector2(maxX, maxY), add: false);
        Check(_sel.SelectedBuilding < 0, $"tall: a box over the buildings left building {_sel.SelectedBuilding} selected");
        Check(shortWrong == 0, $"tall: {shortWrong} pixels on the drawn barracks pick something else");
        _record = (onTall, tallPicksShort, airPicksShort);
        if (_shots != null && DisplayServer.GetName() != "headless") await Shot("qa-tall-behind-short");
        _ = n;
    }

    private (int OnTall, int TallPicksShort, int AirPicksShort) _record;

    private static float? RayT(Aabb box, Vector3 o, Vector3 d)
    {
        float near = 0f, far = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float lo = box.Position[a], hi = box.End[a];
            if (Math.Abs(d[a]) < 1e-9f) { if (o[a] < lo || o[a] > hi) return null; continue; }
            float t1 = (lo - o[a]) / d[a], t2 = (hi - o[a]) / d[a];
            near = Math.Max(near, Math.Min(t1, t2));
            far = Math.Min(far, Math.Max(t1, t2));
            if (near > far) return null;
        }
        return near;
    }

    private int CountUnits()
    {
        int n = 0;
        for (int i = 0; i < W.Units.Capacity; i++) if (W.Units.Alive[i]) n++;
        return n;
    }

    // ---- helpers --------------------------------------------------------------------------------------------------------
    private int Spawn(int player, int type, System.Numerics.Vector2 near)
    {
        int anchor = OpenAnchor(_data.Buildings[type], near);
        if (anchor < 0) { GD.Print($"  note: no anchor for {type} near {near}"); return -1; }
        _sim.Enqueue(Command.SpawnBuilding(player, type, Rts.Sim.ViewApi.PlacementGhost.AnchorPoint(G, anchor)));
        _sim.Tick();
        _sim.Tick();
        int slot = B.SlotAt(anchor % G.Width, anchor / G.Width);
        if (slot < 0) GD.Print($"  note: spawn refused {type} p{player} anchor {anchor}: slot {slot}, tick {_sim.TickNumber}, buildings {CountB()}");
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    private int CountB() { int n = 0; for (int i = 0; i < B.Capacity; i++) if (B.Alive[i]) n++; return n; }

    private int OpenAnchor(BuildingDef def, System.Numerics.Vector2 near)
    {
        int type = W.Data.FindBuilding(def.Key);
        for (int r = 0; r <= 8; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    System.Numerics.Vector2 p = near + new System.Numerics.Vector2(dx, dy) * MapConstants.CellSize;
                    if (!G.WorldToCell(p, out int cx, out int cy)) continue;
                    int a = cy * G.Width + cx;
                    if (cx < 1 || cy < 1 || cx + def.FootprintWidth >= G.Width - 1 || cy + def.FootprintHeight >= G.Height - 1) continue;
                    if (!B.Fits(type, a) || !FitsWithMargin(def, cx, cy)) continue;
                    return a;
                }
        return -1;
    }

    private bool FitsWithMargin(BuildingDef def, int cx, int cy)
    {
        for (int y = cy - 1; y <= cy + def.FootprintHeight; y++)
            for (int x = cx - 1; x <= cx + def.FootprintWidth; x++)
                if (B.SlotAt(x, y) >= 0 || !G.IsPassable(x, y)) return false;
        return true;
    }

    private System.Numerics.Vector2 Centre(int slot) => StartBase.FootprintCenter(G, W.Data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private async Task Shot(string name)
    {
        Directory.CreateDirectory(_shots!);
        await Frames(4);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_shots!, name + ".png");
        Error e = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path} ({e})");
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
