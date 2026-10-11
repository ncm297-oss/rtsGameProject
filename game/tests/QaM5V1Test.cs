using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M5-V1 (session 2026-10-11-0314) on the real Match scene. (1) Twelve own mages, each ringed by eight holding enemy
/// Raiders, ordered to cast far off while another unit is selected, run through the live <see cref="SimRunner"/> (its
/// <c>Ticked</c> event and <see cref="AbilityViews"/>' own <c>_Process</c>, not a test-driven Sync): exactly 12 cues for 12
/// drops, the 12 discs at once, one Refused sound per frame that saw new drops, the hud.cast_lost words on all 12 (none at
/// all with <c>--no-hud</c>), the selection untouched, then nothing after the 0.5 s. (2) A second round driven by hand: 0 B
/// per Sync and no new cue / ring node. (3) <see cref="GroundDiscPool"/> churn: a frame of 16 keys followed by a frame of 16
/// other keys, and after that no growth. (4) <see cref="GroundDisc.SetScale"/> edges (NaN, 0, negative, above 1) and
/// <see cref="GroundDisc.SurfaceAt"/> at the drawn share.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM5V1Test.tscn</c>; prints "QA M5-V1 TEST PASS" and exits 0, or each failure and exits 1.</remarks>
public partial class QaM5V1Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private AbilityViews _views = null!;
    private SelectionController _sel = null!;
    private Sfx _sfx = null!;
    private int _drops;
    private string? _shots;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            int sa = Array.IndexOf(args, "--shots");
            if (sa >= 0 && sa + 1 < args.Length) _shots = args[sa + 1];
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await TwelveDrops(1, hud: true);
            await TwelveDrops(6, hud: false);
            await PoolChurn();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA M5-V1 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M5-V1 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task TwelveDrops(ulong seed, bool hud)
    {
        var extra = new List<string> { "--units", "0", "--no-bases", "--no-combat", "--no-fog" };
        if (!hud) extra.Add("--no-hud");
        StartMatch(seed, extra.ToArray());
        string tag = $"seed {seed} {(hud ? "hud" : "no-hud")}";
        int mageType = _data.FindUnit("malazan_cadre_mage"), raider = _data.FindUnit("whirlwind_raider"), laborer = _data.FindUnit("malazan_laborer");
        List<System.Numerics.Vector2> spots = OpenSpots(13);
        if (!Check(spots.Count == 13, $"{tag}: only {spots.Count} open spots")) { await EndMatch(); return; }
        for (int m = 0; m < 12; m++)
        {
            _sim.Enqueue(Command.SpawnUnit(0, mageType, spots[m]));
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.Tau / 8f;
                _sim.Enqueue(Command.SpawnUnit(1, raider, spots[m] + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a))));
            }
        }
        _sim.Enqueue(Command.SpawnUnit(0, laborer, spots[12]));
        _sim.Tick();
        _sim.Tick();
        var mages = new List<EntityHandle>();
        EntityHandle other = default;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            var h = new EntityHandle(i, U.Generation[i]);
            if (U.TypeId[i] == mageType) mages.Add(h);
            else if (U.TypeId[i] == laborer) other = h;
            else _sim.Enqueue(Command.HoldPosition(1, h));
        }
        _sim.Tick();
        if (!Check(mages.Count == 12 && other.Generation != 0, $"{tag}: staged {mages.Count} mages, other {other}")) { await EndMatch(); return; }
        _sel.Selection.Clear();
        _sel.Selection.Add(other);
        Check(hud ? _views.CastLostText == "Cast lost" : _views.CastLostText.Length == 0, $"{tag}: CastLostText '{_views.CastLostText}'");

        // (1) Round 1 through the live runner.
        System.Numerics.Vector2 far = G.CellCenter(G.Width / 2, G.Height / 2);
        foreach (EntityHandle m in mages) _sim.Enqueue(Command.UseAbility(0, m, 0, far));
        long cues0 = _views.LostCasts.Added;
        int refused0 = _sfx.PlayCount(SfxEvent.Refused);
        _drops = 0;
        int dropFrames = 0, maxShown = 0, maxLabels = 0, badText = 0, framesAfter = -1;
        _runner.Ticked += CountDrops;
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        ulong start = Time.GetTicksMsec();
        int seenDrops = 0;
        bool shot = false;
        RtsCamera camera = _match.GetNode<RtsCamera>("RtsCamera");
        camera.SetFocus(spots[0].X, spots[0].Y);
        while (Time.GetTicksMsec() - start < 12000)
        {
            await Frame();
            if (_drops != seenDrops) { dropFrames++; seenDrops = _drops; }
            maxShown = Math.Max(maxShown, _views.ShownCues);
            int labels = 0;
            for (int c = 0; c < _views.LostCasts.Capacity; c++)
            {
                Label3D l = _views.CueLabel(c);
                if (!l.Visible) continue;
                labels++;
                if (l.Text != "Cast lost") badText++;
            }
            maxLabels = Math.Max(maxLabels, labels);
            if (!shot && _views.ShownCues >= 4 && _shots != null && DisplayServer.GetName() != "headless")
            {
                shot = true;
                _runner.ProcessMode = ProcessModeEnum.Disabled;
                await Shot($"qa-m5v1-seed{seed}-{(hud ? "hud" : "nohud")}-cues");
                _runner.ProcessMode = ProcessModeEnum.Inherit;
            }
            if (_drops >= 12 && framesAfter < 0) framesAfter = 0;
            if (framesAfter >= 0 && ++framesAfter > 90) break;
        }
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _runner.Ticked -= CountDrops;
        int refused = _sfx.PlayCount(SfxEvent.Refused) - refused0;
        long cues = _views.LostCasts.Added - cues0;
        GD.Print($"{tag}: {_drops} drops over {dropFrames} frames, {cues} cues, {refused} Refused sounds, at most {maxShown} cues and {maxLabels} labels shown at once, cue pool {_views.CuePool.Nodes} nodes (grew {_views.CuePool.Grown}), now {_views.ShownCues} shown");
        Check(_drops == 12 && cues == 12, $"{tag}: {_drops} drops, {cues} cues; want 12 each");
        Check(refused >= 1 && refused <= dropFrames, $"{tag}: {refused} Refused sounds for {dropFrames} frames with new drops");
        Check(maxShown == 12, $"{tag}: at most {maxShown} cues drawn at once, want 12 (all dropped on one tick)");
        Check(hud ? maxLabels == 12 && badText == 0 : maxLabels == 0, $"{tag}: at most {maxLabels} labels, {badText} with the wrong text");
        Check(_views.ShownCues == 0 && _views.CuePool.InUse == 0, $"{tag}: {_views.ShownCues} cues / {_views.CuePool.InUse} discs left after the 0.5 s");
        Check(_sel.Selection.Count == 1 && _sel.Selection.Items[0] == other, $"{tag}: the selection changed");
        foreach (EntityHandle m in mages) Check(U.CastAbility[m.Index] < 0, $"{tag}: mage {m.Index} still holds its cast");

        // (2) Round 2 by hand: 0 B per Sync, no new node.
        int cueNodes = _views.CuePool.Nodes, ringNodes = _views.CastRingPool.Nodes;
        foreach (EntityHandle m in mages) _sim.Enqueue(Command.UseAbility(0, m, 0, far));
        long bytes = 0;
        int drops2 = 0, maxShown2 = 0, maxRings = 0;
        for (int f = 0; f < 200; f++)
        {
            if (f % 2 == 0)
            {
                _sim.Tick();
                drops2 += DropsNow(_sim);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            _views.Sync(W, f % 2 / 2f, Vector2.Zero, false);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            maxShown2 = Math.Max(maxShown2, _views.ShownCues);
            maxRings = Math.Max(maxRings, _views.ShownCircles);
        }
        GD.Print($"{tag}: round 2: {drops2} drops, {maxRings} rings, {maxShown2} cues at once, {bytes} B over 200 Syncs; nodes cue {cueNodes} -> {_views.CuePool.Nodes}, ring {ringNodes} -> {_views.CastRingPool.Nodes}");
        Check(drops2 == 12 && maxShown2 == 12 && maxRings == 12, $"{tag}: round 2: {drops2} drops, {maxShown2} cues, {maxRings} rings");
        Check(bytes == 0 && _views.CuePool.Nodes == cueNodes && _views.CastRingPool.Nodes == ringNodes, $"{tag}: round 2: {bytes} B, cue nodes {cueNodes} -> {_views.CuePool.Nodes}, ring nodes {ringNodes} -> {_views.CastRingPool.Nodes}");
        await EndMatch();
    }

    private void CountDrops(Simulation sim) => _drops += DropsNow(sim);

    private static int DropsNow(Simulation sim)
    {
        int n = 0;
        foreach (AbilityEvent e in sim.World.AbilityEvents) if (e.Dropped) n++;
        return n;
    }

    // (3) + (4): the pool on a flat-enough map, and the scale edges.
    private async Task PoolChurn()
    {
        StartMatch(1, "--units", "0", "--no-bases", "--no-combat", "--no-fog");
        var holder = new Node3D { Name = "QaPoolHolder" };
        AddChild(holder);
        var pool = new GroundDiscPool(holder, "QaPool", 64, 16);
        List<System.Numerics.Vector2> spots = OpenSpots(1);
        System.Numerics.Vector2 c = spots.Count > 0 ? spots[0] : G.CellCenter(G.Width / 2, G.Height / 2);
        void FrameOf(int from)
        {
            pool.Begin();
            for (int k = from; k < from + 16; k++)
            {
                GroundDisc d = pool.Take(k);
                d.Place(W.Heightmap, c + new System.Numerics.Vector2(k % 8, k / 8), 1f, 0f, 0.1f);
                d.Visible = true;
            }
            pool.End();
        }
        FrameOf(0);
        int afterFirst = pool.Nodes;
        FrameOf(16);
        int afterSwap = pool.Nodes;
        for (int r = 0; r < 10; r++) FrameOf(r % 2 == 0 ? 0 : 16);
        int afterChurn = pool.Nodes;
        int visible = 0;
        for (int n = 0; n < pool.Nodes; n++) if (pool.Node(n).Visible) visible++;
        GD.Print($"pool churn: 16 keys -> {afterFirst} nodes; 16 other keys next frame -> {afterSwap} nodes (grew {pool.Grown}); 10 more alternations -> {afterChurn}; {visible} visible, {pool.InUse} in use");
        Check(afterFirst == 16, $"pool: 16 keys made {afterFirst} nodes");
        Check(afterChurn == afterSwap, $"pool: alternating 16-key frames kept growing ({afterSwap} -> {afterChurn})");
        Check(visible == 16 && pool.InUse == 16, $"pool: {visible} visible, {pool.InUse} in use, want 16");
        pool.Begin();
        pool.End();
        visible = 0;
        for (int n = 0; n < pool.Nodes; n++) if (pool.Node(n).Visible) visible++;
        Check(visible == 0 && pool.InUse == 0, $"pool: an empty frame left {visible} visible, {pool.InUse} in use");

        // (4) SetScale edges.
        var disc = new GroundDisc { Name = "QaScaleDisc" };
        holder.AddChild(disc);
        disc.Place(W.Heightmap, c, 2f, 0f, 0.1f);
        disc.Visible = true;
        (float In, float Want)[] cases = { (float.NaN, 1f), (0f, 0.001f), (-3f, 0.001f), (5f, 1f), (0.5f, 0.5f), (float.PositiveInfinity, 1f) };
        foreach ((float input, float want) in cases)
        {
            disc.SetScale(input);
            Check(disc.DrawnShare == want, $"SetScale({input}) drew {disc.DrawnShare}, want {want}");
        }
        disc.SetScale(0.4f);
        bool outside = float.IsNaN(disc.SurfaceAt(c.X + 1f, c.Y)); // 0.5 of the radius, past 0.4
        disc.SetScale(0.6f);
        bool inside = !float.IsNaN(disc.SurfaceAt(c.X + 1f, c.Y));
        Check(outside && inside, $"SurfaceAt 1 m out on a 2 m disc: NaN at share 0.4 {outside}, drawn at 0.6 {inside}");
        pool.Free();
        holder.QueueFree();
        await EndMatch();
    }

    // Up to `count` cell centres whose cells within 2 are passable plain ground of one level, 10 m apart from each other.
    private List<System.Numerics.Vector2> OpenSpots(int count)
    {
        Heightmap hm = W.Heightmap;
        var spots = new List<System.Numerics.Vector2>();
        for (int y = G.Height / 4; y < 3 * G.Height / 4 && spots.Count < count; y += 2)
            for (int x = G.Width / 4; x < 3 * G.Width / 4 && spots.Count < count; x += 2)
            {
                bool ok = true;
                for (int dy = -3; dy <= 3 && ok; dy++)
                    for (int dx = -3; dx <= 3 && ok; dx++)
                        ok = G.IsPassable(x + dx, y + dy) && !hm.IsRamp(x + dx, y + dy) && hm.LevelAt(x + dx, y + dy) == hm.LevelAt(x, y);
                if (!ok) continue;
                System.Numerics.Vector2 p = G.CellCenter(x, y);
                bool clear = true;
                foreach (System.Numerics.Vector2 s in spots) if (System.Numerics.Vector2.Distance(s, p) < 10f) clear = false;
                for (int i = 0; i < U.Capacity && clear; i++) if (U.Alive[i] && System.Numerics.Vector2.Distance(U.Position[i], p) < 6f) clear = false;
                if (clear) spots.Add(p);
            }
        return spots;
    }

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--zoom", "30" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _sfx = _match.GetNode<Sfx>("Sfx");
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private async Task Shot(string name)
    {
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot: {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
