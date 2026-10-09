using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;

namespace Rts.Game.Tests;

/// <summary>
/// QA attacks on M4-V4 (session 2026-10-09-0125) on the real Match scene. (1) Seed 11 (a third seed beside the dev's 1 and 6):
/// 900 ticks of attack-moving armies, one frame each, the texture's bytes equal to the fog's after every update, every
/// unit and building view equal to CanSeeUnit / CanSeeBuilding, the minimap layer equal to the texture. (2) A minimap
/// right-click (<see cref="Minimap.CommandAt"/>) on the spot of an enemy whose dot was shown and who died in the tick that
/// just ran: never an Attack on the dead handle; every Attack it records names a live unit. (3) 300 steady frames at
/// <c>--units 1000</c> (2,000 units, 4x the 500-unit budget): 0 bytes in the fog-reading views
/// and the minimap, uploads at most one per 4 ticks (+1). (4) Hash twins.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaFogViewTest.tscn</c>; prints
/// "QA FOG VIEW TEST PASS" and exits 0, or each failure and exits 1.</remarks>
public partial class QaFogViewTest : Node
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
            await ThirdSeed(11);
            await DeadDotClicks(1);
            await Steady(6, 1000);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"QA FOG VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA FOG VIEW TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
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
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private void AttackMoveArmies()
    {
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? east : west));
    }

    // ---- 1: a third seed, every frame ----

    private async Task ThirdSeed(ulong seed)
    {
        StartMatch(seed, "--units", "40");
        _sim.Tick();
        _sim.Tick();
        AttackMoveArmies();
        int lastVersion = -1, updates = 0, unitBad = 0, buildingBad = 0, textureBad = 0, miniBad = 0, shownEnemy = 0, hiddenEnemy = 0;
        int start = W.TickNumber, uploads0 = _fog.Uploads;
        for (int t = 0; t < 900; t++)
        {
            _sim.Tick();
            await Frame();
            int v = W.Fog.Version(0);
            if (_fog.View!.TextureVersion != v) textureBad++;
            if (v != lastVersion)
            {
                lastVersion = v;
                updates++;
                byte[] img = _fog.Image.GetData();
                if (!W.Fog.Visibility(0).SequenceEqual(img)) textureBad++;
                byte[] layer = _mini.FogImage.GetData();
                for (int c = 0; c < img.Length; c++)
                    if (layer[c * 4 + 3] != FogView.MinimapAlpha(img[c])) { miniBad++; break; }
            }
            for (int i = 0; i < U.Capacity; i++)
            {
                if (_units.IsShown(i) != W.Fog.CanSeeUnit(0, i)) unitBad++;
                if (U.Alive[i] && U.Owner[i] != 0) { if (_units.IsShown(i)) shownEnemy++; else hiddenEnemy++; }
            }
            for (int i = 0; i < W.Buildings.Capacity; i++)
                if (_buildings.IsShown(i) != W.Fog.CanSeeBuilding(0, i)) buildingBad++;
        }
        int ticks = W.TickNumber - start, uploads = _fog.Uploads - uploads0;
        GD.Print($"seed {seed}: {ticks} ticks, {updates} updates checked, {uploads} uploads; enemy unit-frames shown {shownEnemy} hidden {hiddenEnemy}; " +
            $"mismatches texture {textureBad} minimap {miniBad} units {unitBad} buildings {buildingBad}");
        Check(textureBad == 0 && miniBad == 0 && unitBad == 0 && buildingBad == 0, $"seed {seed}: mismatches texture {textureBad} minimap {miniBad} units {unitBad} buildings {buildingBad}");
        Check(uploads <= ticks / VisionConstants.UpdateInterval + 1, $"seed {seed}: {uploads} uploads over {ticks} ticks");
        Check(shownEnemy > 0 && hiddenEnemy > 0, $"seed {seed}: enemies shown {shownEnemy} hidden {hiddenEnemy}");
        Twin(seed);
        await EndMatch();
    }

    // ---- 2: a minimap right-click on the dot of an enemy that died this tick ----

    private async Task DeadDotClicks(ulong seed)
    {
        StartMatch(seed, "--units", "40", "--zoom", "60");
        _sim.Tick();
        _sim.Tick();
        // One own worker (by the hall, out of the fight) carries the clicks' orders, so the brawl goes on.
        int worker = -1;
        for (int i = 0; i < U.Capacity && worker < 0; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot == UnitSlot.Worker) worker = i;
        if (!Check(worker >= 0, "dead dot: no own worker")) { await EndMatch(); return; }
        AttackMoveArmies();
        var lastPos = new System.Numerics.Vector2[U.Capacity];
        var lastShown = new bool[U.Capacity];
        int clicks = 0, attacks = 0, moves = 0, badAttacks = 0;
        for (int t = 0; t < 1500 && clicks < 40; t++)
        {
            _sim.Tick();
            await Frame();
            for (int e = 0; e < W.Deaths.Length; e++)
            {
                DeathEvent d = W.Deaths[e]; // no span local across the awaits
                int slot = d.Victim.Index;
                if (d.IsBuilding || d.VictimOwner == 0 || !lastShown[slot]) continue;
                _sel.Selection.Clear();
                _sel.Selection.Add(new EntityHandle(worker, U.Generation[worker]));
                int before = _runner.Recorder!.CommandCount;
                CommandKind k = _mini.CommandAt(lastPos[slot], queued: false);
                clicks++;
                if (k == CommandKind.Attack) attacks++; else if (k == CommandKind.Move) moves++;
                _sim.Tick(); // the click's command is recorded when it applies
                for (int c = before; c < _runner.Recorder.CommandCount; c++)
                {
                    Command cmd = _runner.Recorder.CommandAt(c);
                    if (cmd.Kind != CommandKind.Attack || cmd.Player != 0) continue;
                    if (cmd.Target.Index == d.Victim.Index && cmd.Target.Generation == d.Victim.Generation) badAttacks++;
                }
                break;
            }
            for (int i = 0; i < U.Capacity; i++)
            {
                lastPos[i] = U.Position[i];
                lastShown[i] = _fog.View!.ShowsUnit(i);
            }
        }
        GD.Print($"dead dot (seed {seed}): {clicks} clicks on a just-dead enemy's dot: {attacks} Attacks (on another shown enemy there), {moves} Moves, {badAttacks} Attacks on the dead");
        Check(clicks > 0, "dead dot: no shown enemy died");
        Check(badAttacks == 0, $"dead dot: {badAttacks} Attacks on a unit that died that tick");
        Twin(seed);
        await EndMatch();
    }

    // ---- 3: steady frames at 2,000 units ----

    private async Task Steady(ulong seed, int perPlayer)
    {
        StartMatch(seed, "--units", perPlayer.ToString(CultureInfo.InvariantCulture));
        _sim.Tick();
        _sim.Tick();
        int spawned = U.Count;
        AttackMoveArmies();
        for (int t = 0; t < 240; t++)
        {
            _sim.Tick();
            SyncAll(0.5f);
        }
        long bytes = 0;
        int uploads0 = _fog.Uploads, ticks0 = W.TickNumber, bad = 0;
        for (int f = 0; f < 300; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            SyncAll(f % 3 / 3f);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            for (int i = 0; i < U.Capacity; i++) if (_units.IsShown(i) != W.Fog.CanSeeUnit(0, i)) bad++;
        }
        int uploads = _fog.Uploads - uploads0, ticks = W.TickNumber - ticks0;
        GD.Print($"steady (seed {seed}, {spawned} spawned, {U.Count} units): 300 frames, {ticks} ticks, {uploads} uploads, {bytes} bytes, {bad} view/CanSeeUnit disagreements");
        Check(spawned >= 2 * perPlayer * 9 / 10 && U.Count >= perPlayer, $"steady: {spawned} spawned, {U.Count} left");
        Check(bytes == 0, $"steady: {bytes} bytes over 300 frames at {U.Count} units");
        Check(bad == 0, $"steady: {bad} disagreements");
        Check(uploads > 0 && uploads <= ticks / VisionConstants.UpdateInterval + 1, $"steady: {uploads} uploads over {ticks} ticks");
        Twin(seed);
        await EndMatch();
    }

    private void SyncAll(float alpha)
    {
        _fog.Sync(W);
        _units.Sync(W, alpha, 0.016f);
        _buildings.Sync(W);
        _combat.Sync(W, alpha);
        _shots.Sync(W, alpha);
        _ring.Sync(W, alpha, 0.016f);
        _mini.SyncFog(W);
        if (W.TickNumber % Minimap.RefreshTicks == 0) _mini.Refresh(_sim);
    }

    private void Twin(ulong seed)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok, $"seed {seed}: twin {twin.Error} at tick {twin.Tick}");
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints, ok {twin.Ok}");
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker) { sum += U.Position[i]; n++; }
        return n > 0 ? sum / n : System.Numerics.Vector2.Zero;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
