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
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M7-V2 (packs in the queue strip, shot heights by scale), on a forged copy of <c>res://data</c>: Heavy Infantry a
/// pack of 3 and the Crossbowman trained at the Barracks too (a single unit and a pack at one building), the Laborer a pack of
/// 2 (a pack under the keep's Age II tech), scales Heavy Infantry 1.5, Crossbowman 0.5, Desert Archer 3.0, Raider 0.75.
/// <list type="bullet">
/// <item>Strip: a full queue of MaxItems with a pack at the head, the middle and the last square; a pack cancelled from the
/// middle (the squares behind it shift, each changed square rewritten once, then nothing); a pack under a tech; every
/// state's steady Sync allocates 0 bytes and rewrites nothing; every square's label fits its 64 px square.</item>
/// <item>Shooter inference vs the store's internal ground truth (attacker type / victim): over a 150 v 150 mixed-scale brawl
/// plus a watchtower, the share of aimed shots whose drawn launch / strike height is not the true shooter's / victim's.</item>
/// <item>Determinism: the brawl run with the projectile views collecting and drawing every tick equals its headless replay
/// twin at every tick (the view's spatial queries change nothing), and a CollectTick + Sync leaves the state hash alone.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM7V2Test.tscn</c>; prints "QA M7V2 TEST PASS" or
/// "QA M7V2 TEST FAIL: ..." lines.
/// </summary>
public partial class QaM7V2Test : Node
{
    private static readonly (string Faction, string Id, float Scale)[] Scales =
    {
        ("malazan", "malazan_heavy_infantry", 1.5f), ("malazan", "malazan_crossbowman", 0.5f),
        ("whirlwind", "whirlwind_desert_archer", 3.0f), ("whirlwind", "whirlwind_raider", 0.75f),
        ("malazan", "malazan_cadre_mage", 1.5f), ("malazan", "malazan_catapult", 1.5f),
    };

    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo AttackerTypeP = typeof(ProjectileStore).GetProperty("AttackerType", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo AttackerIsBuildingP = typeof(ProjectileStore).GetProperty("AttackerIsBuilding", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo AttackerP = typeof(ProjectileStore).GetProperty("Attacker", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo VictimP = typeof(ProjectileStore).GetProperty("Victim", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo VictimIsBuildingP = typeof(ProjectileStore).GetProperty("VictimIsBuilding", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private ProductionQueueStrip _strip = null!;
    private ProjectileViews _shots = null!;
    private int _heavy, _crossbow, _laborer, _hall, _barracks, _range;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m7v2-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            _data = Forge(ProjectSettings.GlobalizePath("res://data"), root);
            _heavy = _data.FindUnit("malazan_heavy_infantry");
            _crossbow = _data.FindUnit("malazan_crossbowman");
            _laborer = _data.FindUnit("malazan_laborer");
            StartMatch();
            await Frames();
            SpawnHalls();
            await Frames();
            await StripPositions();
            await PackUnderTech();
            // The brawl in a fresh match: the strip rows set money by reflection, which a replay twin can't repeat.
            RemoveChild(_match);
            _match.QueueFree();
            await Frame();
            StartMatch();
            await Frames();
            await Brawl();
            RemoveChild(_match);
            _match.QueueFree();
            await Frame();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M7V2 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M7V2 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

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
            JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
            JsonArray list = units["units"]!.AsArray();
            foreach ((string f, string id, float scale) in Scales)
                if (f == faction) list.First(n => (string?)n!["id"] == id)!["scale"] = (double)(decimal)scale;
            if (faction == "malazan")
            {
                list.First(n => (string?)n!["id"] == "malazan_heavy_infantry")!["spawnCount"] = 3;
                list.First(n => (string?)n!["id"] == "malazan_laborer")!["spawnCount"] = 2;
                list.First(n => (string?)n!["id"] == "malazan_crossbowman")!["trainedAt"] = "malazan_barracks";
            }
            File.WriteAllText(path, units.ToJsonString());
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        return loaded.Data!;
    }

    private void StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "3", "--units", "0", "--mute", "--no-fog" }));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _strip = _match.GetNode<ProductionQueueStrip>("Hud/QueueStrip");
        _shots = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        Tick(2);
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) _hall = k;
    }

    private void SpawnHalls()
    {
        var taken = new List<int> { B.Cell[_hall] };
        int[] types =
        {
            StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.InfantryHall),
            StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.RangedHall),
        };
        var anchors = new List<int>();
        foreach (int type in types)
        {
            int a = FreeAnchor(type, taken);
            taken.Add(a);
            anchors.Add(a);
            _sim.Enqueue(Command.SpawnBuilding(0, type, G.CellCenter(a % G.Width, a / G.Width)));
        }
        Tick(2);
        _barracks = B.SlotAt(anchors[0] % G.Width, anchors[0] / G.Width);
        _range = B.SlotAt(anchors[1] % G.Width, anchors[1] / G.Width);
        Check(_barracks >= 0 && _range >= 0 && !B.UnderConstruction[_barracks], $"halls: barracks {_barracks}, range {_range}");
        SetMoney(20000, 20000);
    }

    private string Text(int type) => ProductionQueueStrip.SquareText(_data.Units[type]);

    // A full queue with the pack at head, middle and last; then the middle pack cancelled.
    private async Task StripPositions()
    {
        int b = _barracks;
        System.Numerics.Vector2 at = SelectionController.SiteCenter(W, b);
        _sel.SelectBuilding(b);
        int[] order = { _heavy, _crossbow, _heavy, _crossbow, _heavy };
        foreach (int t in order) _sim.Enqueue(Command.Train(0, at, t));
        Tick(2);
        await Frames();
        Check(Text(_heavy) == "H×3" && Text(_crossbow) == "C", $"texts: '{Text(_heavy)}' '{Text(_crossbow)}'");
        Check(B.QueueCount[b] == QueueStrip.MaxItems && _strip.ShownCount == QueueStrip.MaxItems, $"full queue: {B.QueueCount[b]} / strip {_strip.ShownCount}");
        for (int k = 0; k < order.Length; k++)
            Check(_strip.ItemLabel(k).Text == Text(order[k]) && _strip.ShownType(k) == order[k], $"full queue square {k}: '{_strip.ItemLabel(k).Text}' want '{Text(order[k])}'");
        Fits("full queue");
        Steady("full queue, packs at 0/2/4");

        // Cancel the middle pack (item 2): the squares behind it shift; squares 2 and 3 change, 4 hides.
        string[] before = Enumerable.Range(0, 5).Select(k => _strip.ItemLabel(k).Text).ToArray();
        _sim.Enqueue(Command.CancelTrain(0, at, 2));
        _strip.Sync();
        int u0 = _strip.Updates;
        float fill0 = _strip.ShownCount > 0 ? QueueStrip.HeadFill(B, b) : 0f;
        Tick(2);
        _strip.Sync();
        int rewrites = _strip.Updates - u0;
        bool barMoved = QueueStrip.HeadFill(B, b) != fill0;
        int[] after = { _heavy, _crossbow, _crossbow, _heavy };
        Check(B.QueueCount[b] == 4 && _strip.ShownCount == 4, $"after middle cancel: queue {B.QueueCount[b]}, strip {_strip.ShownCount}");
        for (int k = 0; k < after.Length; k++)
            Check(_strip.ItemLabel(k).Text == Text(after[k]), $"after middle cancel square {k}: '{_strip.ItemLabel(k).Text}' want '{Text(after[k])}'");
        Check(!_strip.ItemButton(4).Visible, "after middle cancel: square 4 still visible");
        int want = 2 + (barMoved ? 1 : 0);
        Check(rewrites == want, $"after middle cancel: {rewrites} rewrites, want {want} (squares 2 and 3{(barMoved ? " + bar" : "")})");
        _strip.Sync();
        Check(_strip.Updates - u0 == rewrites, $"after middle cancel: a second Sync rewrote {_strip.Updates - u0 - rewrites} more");
        GD.Print($"middle pack cancel: [{string.Join(" ", before)}] -> [{string.Join(" ", Enumerable.Range(0, 4).Select(k => _strip.ItemLabel(k).Text))}], {rewrites} rewrites (bar moved {barMoved})");
        Steady("after middle cancel");

        // Cancel the head pack (started): everything shifts left.
        _sim.Enqueue(Command.CancelTrain(0, at, 0));
        Tick(2);
        _strip.Sync();
        int[] after2 = { _crossbow, _crossbow, _heavy };
        for (int k = 0; k < after2.Length; k++)
            Check(_strip.ItemLabel(k).Text == Text(after2[k]), $"after head cancel square {k}: '{_strip.ItemLabel(k).Text}' want '{Text(after2[k])}'");
        Steady("after head cancel");
        // Last square cancelled via the strip's click.
        _strip.ItemButton(2).EmitSignal(BaseButton.SignalName.Pressed);
        Tick(2);
        _strip.Sync();
        Check(B.QueueCount[b] == 2 && _strip.ShownCount == 2 && _strip.ItemLabel(0).Text == "C" && _strip.ItemLabel(1).Text == "C",
            $"after last-pack click: queue {B.QueueCount[b]}, strip {_strip.ShownCount}");
        await ClearQueue(b);
        Check(B.ReservedHalfPop(b) == 0, $"barracks still reserves {B.ReservedHalfPop(b)} halves after clearing");
    }

    // A pack (the Laborer, spawnCount 2) queued behind the keep's Age II.
    private async Task PackUnderTech()
    {
        int h = _hall;
        System.Numerics.Vector2 at = SelectionController.SiteCenter(W, h);
        _sel.SelectBuilding(h);
        int age = _data.FindTech("age_ii");
        _sim.Enqueue(Command.Research(0, at, age));
        Tick(2);
        _sim.Enqueue(Command.Train(0, at, _laborer));
        _sim.Enqueue(Command.Train(0, at, _laborer));
        Tick(2);
        await Frames();
        bool techQueued = B.QueueCount[h] >= 1 && B.QueueIsTechAt(h, 0);
        Check(techQueued, $"keep: age_ii not queued at the head (queue {B.QueueCount[h]})");
        if (techQueued)
        {
            Check(_strip.ShownIsTech(0) && _strip.ItemLabel(0).Text == _data.Techs[age].DisplayName, $"tech square '{_strip.ItemLabel(0).Text}'");
            Check(B.QueueCount[h] == 3 && _strip.ItemLabel(1).Text == "L×2" && _strip.ItemLabel(2).Text == "L×2" && !_strip.ShownIsTech(1),
                $"packs under the tech: queue {B.QueueCount[h]}, '{_strip.ItemLabel(1).Text}' '{_strip.ItemLabel(2).Text}'");
            Fits("pack under tech");
            Steady("pack under tech");
            // Cancel the tech: the packs move up; square 0 goes from a tech (font 11) to a pack.
            _sim.Enqueue(Command.CancelTrain(0, at, 0));
            Tick(2);
            _strip.Sync();
            Check(_strip.ShownCount == 2 && !_strip.ShownIsTech(0) && _strip.ItemLabel(0).Text == "L×2" && _strip.ItemLabel(1).Text == "L×2",
                $"tech cancelled: strip {_strip.ShownCount}, '{_strip.ItemLabel(0).Text}' tech {_strip.ShownIsTech(0)}");
            Fits("tech cancelled");
        }
        await ClearQueue(h);
    }

    // Every visible square's text fits its square.
    private void Fits(string what)
    {
        for (int k = 0; k < _strip.ShownCount; k++)
        {
            Label l = _strip.ItemLabel(k);
            if (_strip.ShownIsTech(k)) continue; // tech names wrap/clip by design (M3-V3)
            float w = l.GetMinimumSize().X;
            Check(w <= _strip.ItemButton(k).Size.X, $"{what}: square {k} '{l.Text}' needs {w} px, the square is {_strip.ItemButton(k).Size.X}");
        }
    }

    private void Steady(string what)
    {
        _strip.Sync();
        int updates = _strip.Updates;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 60; f++) _strip.Sync();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"{what}: 60 steady Syncs allocated {bytes} bytes");
        Check(_strip.Updates == updates, $"{what}: 60 steady Syncs rewrote {_strip.Updates - updates}");
    }

    // A 150 v 150 mixed-scale brawl plus a player-0 watchtower: inference vs ground truth, hash twin, allocation.
    private async Task Brawl()
    {
        int tower = _data.FindBuilding("malazan_watchtower");
        StageBlocks(150, rows: 61);
        Tick(2);
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        if (tower >= 0)
        {
            for (int pl = 0; pl < 2; pl++)
            {
                int towerCell = -1;
                for (int r = 0; r < 20 && towerCell < 0; r++)
                {
                    int x = cx + (pl == 0 ? -4 : 4), y = cy + 31 + r;
                    if ((uint)y < (uint)G.Height && B.Fits(tower, y * G.Width + x)) towerCell = y * G.Width + x;
                }
                if (towerCell >= 0) _sim.Enqueue(Command.SpawnBuilding(pl, tower, G.CellCenter(towerCell % G.Width, towerCell / G.Width)));
            }
        }
        Tick(1);
        AttackMoveAll();

        var attackerType = (int[])AttackerTypeP.GetValue(W.Projectiles)!;
        var attackerIsBuilding = (bool[])AttackerIsBuildingP.GetValue(W.Projectiles)!;
        var victim = (EntityHandle[])VictimP.GetValue(W.Projectiles)!;
        var attacker = (EntityHandle[])AttackerP.GetValue(W.Projectiles)!;
        int deadShooterWrong = 0, lobs = 0, lobWrong = 0;
        float mageHeight = -1f;
        var victimIsBuilding = (bool[])VictimIsBuildingP.GetValue(W.Projectiles)!;
        ProjectileStore p = W.Projectiles;
        int seen = 0, fromWrong = 0, toWrong = 0, towerShots = 0, towerWrong = 0, hashMoved = 0;
        var fromBy = new Dictionary<string, int>();
        long bytes = 0;
        int measured = 0;
        var wasAlive = new bool[p.Capacity];
        var lastLaunch = new System.Numerics.Vector2[p.Capacity];
        var lastVictim = new EntityHandle[p.Capacity];
        int reuses = 0;
        for (int t = 0; t < 900; t++)
        {
            _sim.Tick();
            // Ground truth for each shot first fired this tick (PrevPosition == Position on the firing tick).
            ulong h0 = _sim.StateHash();
            long before = GC.GetAllocatedBytesForCurrentThread();
            _shots.CollectTick(W);
            _shots.Sync(W, 0.5f);
            long b = GC.GetAllocatedBytesForCurrentThread() - before;
            if (t >= 100 && t < 160) { bytes += b; measured++; }
            if (_sim.StateHash() != h0) hashMoved++;
            // Lobs (the 1.5 catapult's stones) ride the ground plus the arc, whatever the thrower's scale.
            for (int k = 0; k < _shots.Shown; k++)
            {
                if (!_shots.DrawnIsLob(k)) continue;
                Vector3 o = _shots.DrawnAt(k);
                var xz = new System.Numerics.Vector2(o.X, o.Z);
                float want = TerrainHeight.At(W.Heightmap, o.X, o.Z) + ProjectileViews.StoneRide + _shots.Tracker.ArcHeight(_shots.DrawnSlot(k), xz);
                lobs++;
                if (o.Y != want) lobWrong++;
            }
            for (int i = 0; i < p.Capacity; i++)
            {
                bool reused = p.Alive[i] && wasAlive[i] && _shots.Tracker.Launch[i] == lastLaunch[i] && victim[i] != lastVictim[i];
                bool fresh = p.Alive[i] && (!wasAlive[i] || _shots.Tracker.Launch[i] != lastLaunch[i] || reused);
                if (reused) reuses++;
                wasAlive[i] = p.Alive[i];
                if (p.Alive[i]) { lastLaunch[i] = _shots.Tracker.Launch[i]; lastVictim[i] = victim[i]; }
                if (!fresh || _shots.Tracker.IsLob[i]) continue;
                seen++;
                float wantFrom = attackerIsBuilding[i] ? ProjectileViews.LaunchHeight : ProjectileViews.HeightFor(_data.Units[attackerType[i]].Scale);
                float wantTo = ProjectileViews.LaunchHeight;
                EntityHandle v = victim[i];
                bool vKnown = victimIsBuilding[i] || U.IsAlive(v);
                if (!victimIsBuilding[i] && U.IsAlive(v)) wantTo = ProjectileViews.HeightFor(_data.Units[U.TypeId[v.Index]].Scale);
                bool fw = _shots.ShotFromHeight(i) != wantFrom, tw = vKnown && _shots.ShotToHeight(i) != wantTo;
                if (!attackerIsBuilding[i] && _data.Units[attackerType[i]].Key == "malazan_cadre_mage") mageHeight = _shots.ShotFromHeight(i);
                if (fw) fromWrong++;
                if (fw && !attackerIsBuilding[i] && !U.IsAlive(attacker[i])) deadShooterWrong++;
                if (tw) toWrong++;
                if (attackerIsBuilding[i]) { towerShots++; if (fw || tw) towerWrong++; }
                if (fw)
                {
                    string key = $"{(attackerIsBuilding[i] ? "building" : _data.Units[attackerType[i]].Key)} drawn {_shots.ShotFromHeight(i):0.##}";
                    fromBy[key] = fromBy.GetValueOrDefault(key) + 1;
                }
            }
            if (U.Count < 40) break;
        }
        GD.Print($"brawl inference: {seen} aimed shots seen on their firing tick; launch height wrong {fromWrong} ({deadShooterWrong} of them by a shooter that died the tick it fired), strike height wrong {toWrong}; " +
            $"tower shots {towerShots} ({towerWrong} wrong); same-slot same-launch new-victim refires {reuses}; hash moved by the view {hashMoved}; {measured} measured ticks {bytes} bytes");
        foreach (var kv in fromBy.OrderByDescending(k => k.Value).Take(8)) GD.Print($"  wrong launch: {kv.Key}: {kv.Value}");
        GD.Print($"lob draws {lobs} ({lobWrong} off ground + ride + arc); a 1.5 cadre mage's bolt left at {mageHeight} m (want {ProjectileViews.HeightFor(1.5f)})");
        Check(lobs > 0 && lobWrong == 0, $"{lobWrong} of {lobs} lob draws off ground + StoneRide + arc");
        Check(mageHeight == ProjectileViews.HeightFor(1.5f), $"the 1.5 cadre mage's bolt left at {mageHeight} m");
        Check(seen > 200, $"only {seen} aimed shots seen");
        Check(hashMoved == 0, $"CollectTick + Sync changed the state hash on {hashMoved} ticks");
        Check(bytes == 0, $"brawl: {measured} ticks of CollectTick + Sync allocated {bytes} bytes");
        Check((fromWrong - deadShooterWrong) * 100 <= seen, $"launch height wrong on {fromWrong} of {seen} shots (> 1%)");
        Check(toWrong * 100 <= seen * 5, $"strike height wrong on {toWrong} of {seen} shots (> 5%)");

        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"hash twin: {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"hash twin with views vs headless: {replay.Checkpoints.Length} checkpoints, ok {twin.Ok}");
        await Frames();
    }

    private static readonly string[][] BlockMix =
    {
        new[] { "malazan_crossbowman", "malazan_crossbowman", "malazan_heavy_infantry", "malazan_crossbowman", "malazan_cadre_mage", "malazan_catapult" },
        new[] { "whirlwind_desert_archer", "whirlwind_desert_archer", "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_priest" },
    };

    private void StageBlocks(int perSide, int rows)
    {
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        for (int pl = 0; pl < 2; pl++)
        {
            int dir = pl == 0 ? -1 : 1, placed = 0;
            for (int dx = 2; dx < G.Width && placed < perSide; dx++)
                for (int dy = 0; dy < rows && placed < perSide; dy++)
                {
                    int x = cx + dir * dx, y = cy + (dy % 2 == 0 ? dy / 2 : -(dy + 1) / 2);
                    if ((uint)x >= (uint)G.Width || (uint)y >= (uint)G.Height || !(field.CostAt(y * G.Width + x) < 200f)) continue;
                    _sim.Enqueue(Command.SpawnUnit(pl, _data.FindUnit(BlockMix[pl][placed % BlockMix[pl].Length]), G.CellCenter(x, y)));
                    placed++;
                }
        }
    }

    private void AttackMoveAll()
    {
        var c = new System.Numerics.Vector2[2];
        var n = new int[2];
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] < 2 && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker) { c[U.Owner[i]] += U.Position[i]; n[U.Owner[i]]++; }
        for (int k = 0; k < 2; k++) if (n[k] > 0) c[k] /= n[k];
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] < 2 && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker)
                _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), c[1 - U.Owner[i]]));
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

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++)
        {
            _sim.Tick();
            _shots.CollectTick(W);
        }
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
