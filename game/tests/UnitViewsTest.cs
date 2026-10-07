using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>M2-2: unit views interpolate without extrapolating, stand on the terrain, face the sim facing, reuse their nodes, and update 2,000 units without allocating; M2-7: facing blends PrevFacing to Facing the short way.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/UnitViewsTest.tscn</c>; prints
/// "UNITVIEWS TEST PASS" and exits 0, or prints each failure and exits 1. It drives its own
/// <see cref="Simulation"/> with <c>Tick</c> and calls <see cref="UnitViews.Sync"/> with chosen alphas.
/// </remarks>
public partial class UnitViewsTest : Node
{
    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"UNITVIEWS TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("UNITVIEWS TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void Run()
    {
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        GameData data = loaded.Data!;
        const int capacity = 2000;
        var sim = new Simulation(new SimConfig(7, 2, capacity, 4096) { Data = data });
        World world = sim.World;
        UnitStore u = world.Units;

        var views = new UnitViews();
        AddChild(views);
        views.Bind(data, capacity);
        int meshes = views.MeshCount, materials = views.MaterialCount;

        NVector2[] west = StartLayout.Block(world.NavGrid, 1000, true, 1f);
        NVector2[] east = StartLayout.Block(world.NavGrid, 1000, false, 1f);
        int walkerType = data.Factions[0].Units[1];
        sim.Enqueue(Command.SpawnUnit(0, walkerType, west[0]));
        sim.Tick();
        sim.Tick();
        var walker = new EntityHandle(0, u.Generation[0]);
        Check(u.IsAlive(walker), "the first spawn should take slot 0");
        sim.Enqueue(Command.Move(0, walker, east[0]));
        for (int i = 0; i < 100 && u.Position[0] == u.PrevPosition[0]; i++) sim.Tick();
        Check(u.Position[0] != u.PrevPosition[0], "walker never moved");

        // Interpolation: midpoint at 0.5, the previous tick at 0, never past the current tick.
        CheckPlaced(views, world, 0, 0.5f, NVector2.Lerp(u.PrevPosition[0], u.Position[0], 0.5f), "alpha 0.5 midpoint");
        CheckPlaced(views, world, 0, 0f, u.PrevPosition[0], "alpha 0");
        CheckPlaced(views, world, 0, 1.5f, u.Position[0], "alpha 1.5 clamps to the current tick");
        CheckPlaced(views, world, 0, -2f, u.PrevPosition[0], "alpha -2 clamps to the previous tick");
        CheckPlaced(views, world, 0, float.NaN, u.Position[0], "NaN alpha shows the current tick");

        // Facing: the node's forward (-Z) matches the sim facing and the walking direction.
        views.Sync(world, 0.5f);
        MeshInstance3D node0 = views.ViewOf(0)!;
        Vector3 forward = -node0.Basis.Z;
        float f = u.Facing[0];
        var want = new Vector3(Mathf.Cos(f), 0f, Mathf.Sin(f));
        Check(forward.DistanceTo(want) < 1e-3f, $"forward {forward}, sim facing {f} wants {want}");
        NVector2 step = NVector2.Normalize(u.Position[0] - u.PrevPosition[0]);
        Check(forward.X * step.X + forward.Z * step.Y > 0.9f, $"forward {forward} but the unit walked {step}");
        GD.Print($"facing check: sim {f:F3} rad, step ({step.X:F3}, {step.Y:F3}), node forward {forward}");
        // The walk above is along +x (facing 0), where a sign-flipped yaw gives the same answer (BUG-0053):
        // also face +y, -y and two diagonals. A flipped sign turns +y into -y and (1, 1) into (1, -1).
        foreach (float facing in new[] { Mathf.Pi / 2f, -Mathf.Pi / 2f, Mathf.Pi / 4f, -3f * Mathf.Pi / 4f })
        {
            u.Facing[0] = facing;
            u.PrevFacing[0] = facing; // a unit not turning this tick
            views.Sync(world, 0.5f);
            Vector3 fwd = -views.ViewOf(0)!.Basis.Z;
            var dir = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing)); // sim (x, y) is Godot (x, z)
            Check(fwd.DistanceTo(dir) < 1e-3f, $"facing {facing:F3}: node forward {fwd}, want {dir}");
        }
        FacingBlendRows(views, world);
        u.Facing[0] = f;
        u.PrevFacing[0] = f;

        // Pooling: a freed slot hides its node; a respawn into it (LIFO free list) reuses it.
        int nodes = views.NodeCount;
        u.Free(walker);
        views.Sync(world, 0.5f);
        Check(!node0.Visible, "dead slot's view still visible");
        int otherType = data.Factions[1].Units[0];
        sim.Enqueue(Command.SpawnUnit(1, otherType, east[0]));
        sim.Tick();
        sim.Tick();
        Check(u.Alive[0] && u.TypeId[0] == otherType, "respawn didn't reuse slot 0");
        views.Sync(world, 0.5f);
        Check(ReferenceEquals(views.ViewOf(0), node0), "respawned slot got a new node");
        Check(node0.Visible, "respawned slot's view hidden");
        Check(views.NodeCount == nodes, $"node count {nodes} -> {views.NodeCount} on respawn");
        CheckPlaced(views, world, 0, 0.5f, east[0], "respawned unit");

        // 2,000 units: the whole west block for player 0, the rest of the east block for player 1 (slot 0 holds east[0]).
        for (int i = 0; i < 1000; i++) sim.Enqueue(Command.SpawnUnit(0, data.Factions[0].Units[i % data.Factions[0].Units.Length], west[i]));
        for (int i = 1; i < 1000; i++) sim.Enqueue(Command.SpawnUnit(1, data.Factions[1].Units[i % data.Factions[1].Units.Length], east[i]));
        sim.Tick();
        sim.Tick();
        Check(u.Count == capacity, $"{u.Count} units alive, expected {capacity}");
        for (int i = 0; i < capacity; i++)
        {
            if (u.Alive[i] && u.Owner[i] == 0) sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), east[500]));
        }
        for (int i = 0; i < 5; i++) sim.Tick();

        views.Sync(world, 0.25f); // first use of every slot: creates the nodes
        var rings = new SelectionRings();
        AddChild(rings);
        rings.Init(capacity);
        var selection = new SelectionSet(capacity);
        for (int i = 0; i < capacity; i++) selection.Add(new EntityHandle(i, u.Generation[i]));
        rings.Sync(world, 0.25f, selection.Items);
        Check(rings.ShownCount == capacity, $"{rings.ShownCount} rings for {capacity} selected units");

        // Heights: every view stands on TerrainHeight.At of its interpolated point (1 mm).
        float worst = 0f;
        for (int i = 0; i < capacity; i++)
        {
            NVector2 p = NVector2.Lerp(u.PrevPosition[i], u.Position[i], 0.25f);
            float feet = views.ViewOf(i)!.Position.Y - UnitViews.BodyHeight(data.Units[u.TypeId[i]].Radius) / 2f;
            worst = Mathf.Max(worst, Mathf.Abs(feet - TerrainHeight.At(world.Heightmap, p.X, p.Y)));
        }
        Check(worst <= 1e-3f, $"worst height error {worst} m");

        // Allocation: the steady per-frame update of 2,000 views and 2,000 rings.
        views.Sync(world, 0.5f);
        rings.Sync(world, 0.5f, selection.Items);
        const int frames = 20;
        var sw = Stopwatch.StartNew();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < frames; k++) views.Sync(world, k / (float)frames);
        long viewBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        sw.Stop();
        double viewMs = sw.Elapsed.TotalMilliseconds / frames;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < frames; k++) rings.Sync(world, k / (float)frames, selection.Items);
        long ringBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GD.Print($"2000 views: {viewBytes} bytes over {frames} frames, {viewMs:F3} ms per frame; 2000 rings: {ringBytes} bytes; " +
            $"worst height error {worst * 1000:F4} mm; nodes {views.NodeCount}, meshes {views.MeshCount}, materials {views.MaterialCount}");
        Check(viewBytes == 0, $"view update allocated {viewBytes} bytes");
        Check(ringBytes == 0, $"ring update allocated {ringBytes} bytes");
        Check(views.NodeCount == capacity, $"{views.NodeCount} nodes for {capacity} slots");
        Check(views.MeshCount == meshes && views.MaterialCount == materials, "meshes or materials created after Bind");
        Check(views.MeshCount == data.Units.Length && views.MaterialCount == data.Factions.Length, "one mesh per type, one material per faction");
    }

    // M2-7: yaw follows lerp(PrevFacing, Facing) by alpha the short way round (wrap at ±π), never past the current tick.
    private void FacingBlendRows(UnitViews views, World world)
    {
        UnitStore u = world.Units;
        float pi = Mathf.Pi;
        Check(Mathf.Abs(UnitViews.BlendFacing(0f, pi / 2f, 0.5f) - pi / 4f) <= 1e-4f, $"0 -> π/2 at 0.5 gives {UnitViews.BlendFacing(0f, pi / 2f, 0.5f)}, want π/4");
        float across = UnitViews.BlendFacing(0.9f * pi, -0.9f * pi, 0.5f);
        Check(Mathf.Abs(Mathf.Abs(across) - pi) <= 1e-4f, $"0.9π -> -0.9π at 0.5 gives {across}, want ±π (through the wrap)");
        float quarter = UnitViews.BlendFacing(0.9f * pi, -0.9f * pi, 0.25f);
        Check(Mathf.Abs(quarter - 0.95f * pi) <= 1e-4f, $"0.9π -> -0.9π at 0.25 gives {quarter}, want 0.95π (the short way)");
        float back = UnitViews.BlendFacing(-0.9f * pi, 0.9f * pi, 0.5f);
        Check(Mathf.Abs(Mathf.Abs(back) - pi) <= 1e-4f, $"-0.9π -> 0.9π at 0.5 gives {back}, want ±π");
        // A half turn in one tick: deterministic, the same way every time.
        float half1 = UnitViews.BlendFacing(0f, pi, 0.5f), half2 = UnitViews.BlendFacing(0f, pi, 0.5f);
        Check(half1 == half2 && Mathf.Abs(Mathf.Abs(half1) - pi / 2f) <= 1e-4f, $"0 -> π at 0.5 gives {half1} / {half2}, want ±π/2 and the same twice");
        Check(Mathf.Abs(UnitViews.BlendFacing(0.3f, 1.2f, 0f) - 0.3f) <= 1e-6f && Mathf.Abs(UnitViews.BlendFacing(0.3f, 1.2f, 1f) - 1.2f) <= 1e-6f,
            "alpha 0 / 1 should give PrevFacing / Facing");
        Check(Mathf.Abs(UnitViews.BlendFacing(0.3f, 1.2f, 1.5f) - 1.2f) <= 1e-6f && Mathf.Abs(UnitViews.BlendFacing(0.3f, 1.2f, -1f) - 0.3f) <= 1e-6f
            && Mathf.Abs(UnitViews.BlendFacing(0.3f, 1.2f, float.NaN) - 1.2f) <= 1e-6f, "alpha outside [0, 1] or NaN should clamp like the position");

        // The node itself: PrevFacing 0, Facing π/2, alpha 0.5 faces π/4 on the ground; 0.9π -> -0.9π faces -x.
        (float prev, float cur, float want)[] rows = { (0f, pi / 2f, pi / 4f), (0.9f * pi, -0.9f * pi, pi) };
        foreach ((float prev, float cur, float want) in rows)
        {
            u.PrevFacing[0] = prev;
            u.Facing[0] = cur;
            views.Sync(world, 0.5f);
            Vector3 fwd = -views.ViewOf(0)!.Basis.Z;
            var dir = new Vector3(Mathf.Cos(want), 0f, Mathf.Sin(want));
            GD.Print($"facing blend {prev:F3} -> {cur:F3} at 0.5: node forward {fwd}, want {dir}");
            Check(fwd.DistanceTo(dir) < 1e-3f, $"facing blend {prev:F3} -> {cur:F3}: node forward {fwd}, want {dir}");
        }
    }

    private void CheckPlaced(UnitViews views, World world, int slot, float alpha, NVector2 expected, string what)
    {
        views.Sync(world, alpha);
        Vector3 p = views.ViewOf(slot)!.Position;
        float feet = p.Y - UnitViews.BodyHeight(world.Units.Radius[slot]) / 2f;
        float ground = TerrainHeight.At(world.Heightmap, expected.X, expected.Y);
        bool ok = Mathf.Abs(p.X - expected.X) < 1e-4f && Mathf.Abs(p.Z - expected.Y) < 1e-4f && Mathf.Abs(feet - ground) <= 1e-3f;
        GD.Print($"placement {what}: view {p}, expected ({expected.X}, {ground}, {expected.Y}) feet");
        Check(ok, $"{what}: view at {p}, expected x {expected.X}, z {expected.Y}, ground {ground}");
    }

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
