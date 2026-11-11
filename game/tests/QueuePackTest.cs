using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M7-V2: a pack's count in the production queue strip, on forged data (a copy of <c>res://data</c> under <c>user://</c> with
/// Heavy Infantry <c>spawnCount</c> 3 and mixed scales: Heavy Infantry 1.5, Crossbowman 0.5, Desert Archer 3.0), in a real
/// <c>Match</c>:
/// <list type="bullet">
/// <item>Every type's square text: the pack's initial plus "×3" ("H×3"), every other type its initial alone.</item>
/// <item>A queued pack at a Barracks shows "H×3"; a Laborer at the keep shows "L"; 60 steady frames rewrite nothing and 60
/// <see cref="ProductionQueueStrip.Sync"/> calls allocate 0 bytes.</item>
/// <item>A click on the started pack's square sends one <c>CancelTrain</c> for that item: the item's cost back once (a pack is paid
/// once) and the whole reservation (3 x pop 1 = 6 halves) released; an unstarted pack's click refunds it and moves no pop.</item>
/// <item>20 pack items queued at four Barracks, a 150 v 150 mixed-scale shooting brawl: 60 frames of the strip, the projectile
/// views' <c>CollectTick</c> and <c>Sync</c> allocate 0 bytes, and the shots in the air leave at their shooters' scaled heights.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QueuePackTest.tscn</c>; prints "QUEUE PACK TEST PASS" or
/// "QUEUE PACK TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>queue-pack.png</c> (a Barracks with two
/// packs queued: "H×3" twice).
/// </summary>
public partial class QueuePackTest : Node
{
    private const int Pack = 3;
    private const string PackId = "malazan_heavy_infantry";

    // The forged scales, by unit id and faction folder.
    private static readonly (string Faction, string Id, float Scale)[] Scales =
    {
        ("malazan", "malazan_heavy_infantry", 1.5f), ("malazan", "malazan_crossbowman", 0.5f), ("whirlwind", "whirlwind_desert_archer", 3.0f),
    };

    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private ProductionQueueStrip _strip = null!;
    private ProjectileViews _shots = null!;
    private int _pack, _laborer, _hall;
    private readonly List<int> _barracks = new();
    private string? _shotDir;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://queue-pack-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shotDir = args[i + 1];
            await Frame();
            _data = Forge(ProjectSettings.GlobalizePath("res://data"), root);
            _pack = _data.FindUnit(PackId);
            TextRows();
            StartMatch();
            await Frames();
            await SpawnBarracks();
            await ShowRows();
            await CancelRows();
            await BrawlRows();
            RemoveChild(_match);
            _match.QueueFree();
            await Frame();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QUEUE PACK TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QUEUE PACK TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // A copy of the data with the pack and the scales written into the faction files.
    private GameData Forge(string dataDir, string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        Directory.CreateDirectory(copy);
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        foreach (string faction in Scales.Select(s => s.Faction).Distinct())
        {
            string path = Path.Combine(copy, "factions", faction, "units.json");
            JsonNode units = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
            JsonArray list = units["units"]!.AsArray();
            foreach ((string f, string id, float scale) in Scales)
                if (f == faction) list.First(n => (string?)n!["id"] == id)!["scale"] = (double)(decimal)scale;
            if (faction == "malazan") list.First(n => (string?)n!["id"] == PackId)!["spawnCount"] = Pack;
            File.WriteAllText(path, units.ToJsonString());
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        GameData data = loaded.Data!;
        UnitDef pack = data.Units[data.FindUnit(PackId)];
        Check(pack.SpawnCount == Pack && pack.ItemHalfPop == Pack * pack.HalfPop, $"forged {PackId}: spawnCount {pack.SpawnCount}, item half-pop {pack.ItemHalfPop}");
        foreach ((_, string id, float scale) in Scales) Check(data.Units[data.FindUnit(id)].Scale == scale, $"forged {id}: scale not {scale}");
        return data;
    }

    // Criterion 1, the text: the pack's initial and "×3"; every other type its initial alone.
    private void TextRows()
    {
        foreach (UnitDef def in _data.Units)
        {
            string initial = def.DisplayName.Substring(0, 1).ToUpperInvariant();
            string want = def.SpawnCount > 1 ? $"{initial}×{def.SpawnCount}" : initial;
            Check(ProductionQueueStrip.SquareText(def) == want, $"{def.Key}: square text '{ProductionQueueStrip.SquareText(def)}', want '{want}'");
        }
        Check(ProductionQueueStrip.SquareText(_data.Units[_pack]) == "H×3", $"the pack reads '{ProductionQueueStrip.SquareText(_data.Units[_pack])}', want 'H×3'");
    }

    private void StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute", "--no-fog" }));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked by the test
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _strip = _match.GetNode<ProductionQueueStrip>("Hud/QueueStrip");
        _shots = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        Tick(2);
        _hall = HallSlot();
        _laborer = _data.Factions[W.FactionOf(0)].Units.First(t => _data.Units[t].Slot == UnitSlot.Worker);
    }

    // Four finished Barracks for player 0 (dev spawns) near the keep, and money.
    private async Task SpawnBarracks()
    {
        int type = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.InfantryHall);
        var taken = new List<int> { B.Cell[_hall] };
        var anchors = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            int a = FreeAnchor(type, taken);
            taken.Add(a);
            anchors.Add(a);
            _sim.Enqueue(Command.SpawnBuilding(0, type, G.CellCenter(a % G.Width, a / G.Width)));
        }
        Tick(2);
        foreach (int a in anchors) _barracks.Add(B.SlotAt(a % G.Width, a / G.Width));
        Check(_barracks.All(s => s >= 0 && !B.UnderConstruction[s] && B.Owner[s] == 0), $"spawned barracks: {string.Join(", ", _barracks)}");
        Check(_data.Units[_pack].TrainedAtTypeId == B.TypeId[_barracks[0]], $"{PackId} is not trained at the Barracks");
        SetMoney(5000, 5000);
        await Frames();
    }

    // Criterion 1, on screen: "H×3" at the Barracks, "L" at the keep; written once, a steady strip allocates nothing.
    private async Task ShowRows()
    {
        int b0 = _barracks[0];
        _sel.SelectBuilding(b0);
        _sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(W, b0), _pack));
        Tick(2);
        await Frames();
        Check(B.QueueCount[b0] == 1 && _strip.ShownCount == 1 && _strip.ShownType(0) == _pack && !_strip.ShownIsTech(0),
            $"one pack queued: queue {B.QueueCount[b0]}, strip {_strip.ShownCount} type {_strip.ShownType(0)}");
        Check(_strip.ItemLabel(0).Text == "H×3" && _strip.ItemButton(0).Visible, $"the pack's square reads '{_strip.ItemLabel(0).Text}'");
        Check(_strip.ItemButton(0).TooltipText == _data.Units[_pack].DisplayName, $"the pack's tooltip '{_strip.ItemButton(0).TooltipText}'");
        GD.Print($"pack square: '{_strip.ItemLabel(0).Text}' (tooltip '{_strip.ItemButton(0).TooltipText}')");
        await Steady("one pack");

        // A second pack: one more square, the head's text untouched.
        _sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(W, b0), _pack));
        Tick(2);
        await Frames();
        Check(_strip.ShownCount == 2 && _strip.ItemLabel(0).Text == "H×3" && _strip.ItemLabel(1).Text == "H×3",
            $"two packs: strip {_strip.ShownCount}, '{_strip.ItemLabel(0).Text}' '{_strip.ItemLabel(1).Text}'");
        await Steady("two packs");
        await Shot("queue-pack");

        // A single unit (the keep's Laborer) reads its initial alone.
        _sel.SelectBuilding(_hall);
        _sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(W, _hall), _laborer));
        Tick(2);
        await Frames();
        string initial = _data.Units[_laborer].DisplayName.Substring(0, 1).ToUpperInvariant();
        Check(_strip.ShownCount == 1 && _strip.ShownType(0) == _laborer && _strip.ItemLabel(0).Text == initial && !_strip.ItemLabel(0).Text.Contains('×'),
            $"one laborer: strip {_strip.ShownCount}, '{_strip.ItemLabel(0).Text}' (want '{initial}')");
        GD.Print($"single square: '{_strip.ItemLabel(0).Text}'");
        await Steady("one laborer");
        await ClearQueue(_hall);
        await ClearQueue(b0);
    }

    // 60 frames with nothing changing: the strip rewrites nothing, and 60 Syncs allocate 0 bytes.
    private async Task Steady(string what)
    {
        _strip.Sync();
        int updates = _strip.Updates;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 60; f++) _strip.Sync();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        for (int f = 0; f < 60; f++) await Frame();
        Check(bytes == 0, $"{what}: 60 steady Syncs allocated {bytes} bytes");
        Check(_strip.Updates == updates, $"{what}: 60 steady frames rewrote the strip {_strip.Updates - updates} times");
        GD.Print($"{what}: 60 steady frames, {bytes} bytes, {_strip.Updates - updates} rewrites");
    }

    // Criterion 2: a click on the pack's square cancels that item: its cost back once, the whole reservation released.
    private async Task CancelRows()
    {
        int b0 = _barracks[0];
        UnitDef def = _data.Units[_pack];
        _sel.SelectBuilding(b0);
        _sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(W, b0), _pack));
        Tick(4);
        await Frames();
        Check(B.QueueCount[b0] == 1 && B.Progress[b0] > 0 && B.ReservedHalfPop(b0) == Pack * def.HalfPop,
            $"started pack: queue {B.QueueCount[b0]}, progress {B.Progress[b0]}, reserved {B.ReservedHalfPop(b0)} halves (want {Pack * def.HalfPop})");
        int gold = W.Gold[0], wood = W.Wood[0], half = W.HalfPop[0];
        int start = PendingCount();
        _strip.ItemButton(0).EmitSignal(BaseButton.SignalName.Pressed);
        Command? sent = PendingCount() == start + 1 ? PendingAt(start) : null;
        Check(sent is { Kind: CommandKind.CancelTrain, TypeId: 0 } && sent.Value.Position == SelectionController.SiteCenter(W, b0),
            $"the pack's click: {PendingCount() - start} commands, {sent?.Kind} item {sent?.TypeId}");
        Tick(2);
        await Frames();
        Check(B.QueueCount[b0] == 0 && _strip.ShownCount == 0, $"after the cancel: queue {B.QueueCount[b0]}, strip {_strip.ShownCount}");
        Check(W.Gold[0] == gold + def.CostGold && W.Wood[0] == wood + def.CostWood,
            $"the cancel refunded {W.Gold[0] - gold} gold / {W.Wood[0] - wood} wood, want the item's {def.CostGold} / {def.CostWood} (once)");
        Check(W.HalfPop[0] == half - Pack * def.HalfPop && B.ReservedHalfPop(b0) == 0,
            $"the cancel moved half-pop {half} -> {W.HalfPop[0]} (want -{Pack * def.HalfPop}), reserved {B.ReservedHalfPop(b0)}");
        GD.Print($"started pack cancelled: +{W.Gold[0] - gold} gold +{W.Wood[0] - wood} wood, half-pop {half} -> {W.HalfPop[0]}");

        // An unstarted pack behind a started one: its click refunds it and moves no pop.
        _sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(W, b0), _pack));
        _sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(W, b0), _pack));
        Tick(4);
        await Frames();
        Check(B.QueueCount[b0] == 2 && _strip.ShownCount == 2 && B.ReservedHalfPop(b0) == Pack * def.HalfPop,
            $"two packs: queue {B.QueueCount[b0]}, strip {_strip.ShownCount}, reserved {B.ReservedHalfPop(b0)}");
        gold = W.Gold[0];
        wood = W.Wood[0];
        half = W.HalfPop[0];
        _strip.ItemButton(1).EmitSignal(BaseButton.SignalName.Pressed);
        Tick(2);
        await Frames();
        Check(B.QueueCount[b0] == 1 && _strip.ShownCount == 1 && W.Gold[0] == gold + def.CostGold && W.Wood[0] == wood + def.CostWood && W.HalfPop[0] == half,
            $"the queued pack's cancel: queue {B.QueueCount[b0]}, +{W.Gold[0] - gold} / +{W.Wood[0] - wood}, half-pop {half} -> {W.HalfPop[0]}");
        await ClearQueue(b0);
    }

    // Criterion 5: 20 pack items queued, a 150 v 150 mixed-scale brawl: the strip and the projectile views allocate nothing.
    private async Task BrawlRows()
    {
        SetMoney(5000, 5000);
        foreach (int b in _barracks)
            for (int i = 0; i < QueueStrip.MaxItems; i++) _sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(W, b), _pack));
        Tick(2);
        int queued = _barracks.Sum(b => B.QueueCount[b]);
        Check(queued == 20, $"{queued} pack items queued at four Barracks, want 20");
        _sel.SelectBuilding(_barracks[0]);
        StageBlocks(150, rows: 61);
        Tick(2);
        Check(U.Count >= 300, $"brawl: {U.Count} units");
        AttackMoveAll();
        int ticks = 0;
        while (W.Projectiles.Count < 20 && ticks < 2000)
        {
            Tick(1);
            ticks++;
        }
        Check(W.Projectiles.Count >= 20, $"brawl: {W.Projectiles.Count} shots in flight after {ticks} ticks");
        await Frames();
        Check(_strip.ShownCount == QueueStrip.MaxItems && Enumerable.Range(0, QueueStrip.MaxItems).All(k => _strip.ItemLabel(k).Text == "H×3"),
            $"brawl strip: {_strip.ShownCount} squares, '{_strip.ItemLabel(0).Text}'");
        long bytes = 0;
        int most = 0, small = 0, big = 0, flashes = 0;
        for (int f = 0; f < 60; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            _shots.CollectTick(W); // what SimRunner.Ticked does
            _shots.Sync(W, f % 3 / 3f);
            _strip.Sync();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            most = Math.Max(most, _shots.ShownAimed);
            for (int k = 0; k < _shots.Shown; k++)
            {
                if (_shots.DrawnIsLob(k)) continue;
                float from = _shots.ShotFromHeight(_shots.DrawnSlot(k));
                if (from == ProjectileViews.HeightFor(0.5f)) small++;
                if (from == ProjectileViews.HeightFor(3.0f)) big++;
            }
            for (int m = 0; m < _shots.Marks.Capacity; m++)
                if (_shots.Marks.Active[m] && _shots.Marks.Kind[m] == ImpactMarkKind.Flash && _shots.MarkFlashHeight(m) != ProjectileViews.LaunchHeight) flashes++;
        }
        GD.Print($"brawl 150 v 150 (mixed scales, 20 packs queued): up to {most} aimed shots drawn, {small} draws from 0.5 shooters, {big} from 3.0 ones, " +
            $"{flashes} scaled flash draws; 60 frames {bytes} bytes");
        Check(bytes == 0, $"brawl: 60 frames of the strip and the projectile views allocated {bytes} bytes");
        Check(most > 0 && small > 0 && big > 0 && flashes > 0, $"brawl: {most} aimed shots at most, {small} from 0.5 shooters, {big} from 3.0 shooters, {flashes} scaled flashes");
    }

    // ---- helpers ----

    // Shooter-heavy lines either side of the central cell, one unit a cell, as ProjectileViewTest's brawl.
    private static readonly string[][] BlockMix =
    {
        new[] { "malazan_crossbowman", "malazan_crossbowman", "malazan_heavy_infantry", "malazan_crossbowman", "malazan_cadre_mage" },
        new[] { "whirlwind_desert_archer", "whirlwind_desert_archer", "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_priest" },
    };

    private void StageBlocks(int perSide, int rows)
    {
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        for (int p = 0; p < 2; p++)
        {
            int dir = p == 0 ? -1 : 1, placed = 0;
            for (int dx = 2; dx < G.Width && placed < perSide; dx++)
                for (int dy = 0; dy < rows && placed < perSide; dy++)
                {
                    int x = cx + dir * dx, y = cy + (dy % 2 == 0 ? dy / 2 : -(dy + 1) / 2);
                    if ((uint)x >= (uint)G.Width || (uint)y >= (uint)G.Height || !(field.CostAt(y * G.Width + x) < 200f)) continue;
                    _sim.Enqueue(Command.SpawnUnit(p, _data.FindUnit(BlockMix[p][placed % BlockMix[p].Length]), G.CellCenter(x, y)));
                    placed++;
                }
            if (placed < perSide) throw new InvalidOperationException($"only {placed} block spots for player {p}");
        }
    }

    private void AttackMoveAll()
    {
        System.Numerics.Vector2[] c = { Centroid(0), Centroid(1) };
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker)
                _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), c[1 - U.Owner[i]]));
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker)
            {
                sum += U.Position[i];
                n++;
            }
        return n > 0 ? sum / n : System.Numerics.Vector2.Zero;
    }

    private async Task ClearQueue(int building)
    {
        for (int i = B.QueueCount[building] - 1; i >= 0; i--) _sim.Enqueue(Command.CancelTrain(0, SelectionController.SiteCenter(W, building), i));
        Tick(2);
        await Frames();
        Check(B.QueueCount[building] == 0, $"building {building} queue not cleared ({B.QueueCount[building]})");
    }

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[0] = gold;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[0] = wood;
    }

    private int PendingCount() => ((CommandQueue)CommandsField.GetValue(_sim)!).Count;

    private Command PendingAt(int i) => ((CommandQueue)CommandsField.GetValue(_sim)!)[i];

    private int HallSlot()
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException("player 0 has no hall");
    }

    // As ProductionHudTest's: a free anchor 10-45 m from the keep, clear of the others and of units.
    private int FreeAnchor(int type, List<int> taken)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 near = SelectionController.SiteCenter(W, _hall);
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < 10f || d > 45f) continue;
            int x = cell % G.Width, y = cell / G.Width;
            if (taken.Any(t => Math.Abs(t % G.Width - x) < 8 && Math.Abs(t / G.Width - y) < 8)) continue;
            bool ok = W.CanPlace(0, type, cell, out PlacementError r);
            if (!ok && r != PlacementError.CannotAfford && !(r == PlacementError.Requires && B.Fits(type, cell)) && !ExploredGround.IsUnexplored(r)) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 3f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 3f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        if (best < 0) throw new InvalidOperationException($"no free anchor for {def.Key}");
        return best;
    }

    // Windowed with --shots: the frame as drawn.
    private async Task Shot(string name)
    {
        if (_shotDir == null || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(_shotDir);
        await Frames();
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_shotDir, name + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
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
