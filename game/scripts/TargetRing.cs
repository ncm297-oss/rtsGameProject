using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The Attack order's feedback (M4-V2): a red ring on the ground round the unit or building the player just ordered an attack on, for <see cref="TargetMark.DefaultSeconds"/> of view time.</summary>
/// <remarks>
/// One pooled torus node, made in <see cref="_Ready"/> and reused by every order (no cursor art before M6). The mark itself
/// (target and time left) is a <see cref="TargetMark"/>; each frame the ring follows a unit target's interpolated position
/// (radius <see cref="UnitScale"/> x its radius, outside the green selection ring) or sits round a building's footprint,
/// and hides when the time is up or the target is dead or recycled. Moving a node allocates nothing, so a frame is 0 bytes.
/// With <see cref="Fog"/> set (M4-V4) the ring is not drawn while the fog hides its target (its time keeps running).
/// Holds view state only.
/// </remarks>
public partial class TargetRing : Node3D
{
    /// <summary>Ring radius as a multiple of a unit target's radius (the selection ring is 1.35).</summary>
    public const float UnitScale = 1.7f;

    /// <summary>Ring radius as a multiple of half a building footprint's longer side.</summary>
    public const float BuildingScale = 1.15f;

    /// <summary>Height above the ground (above the selection ring's), in meters.</summary>
    public const float Lift = 0.09f;

    // UI tint, not team colour: "attack" reads red.
    private static readonly Color RingColor = new(1f, 0.15f, 0.12f);

    private SimRunner? _runner;
    private MeshInstance3D _ring = null!;

    /// <summary>The fog whose hide rule applies (M4-V4); null draws the ring on any live target.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>The mark: target and time left.</summary>
    public TargetMark Mark { get; } = new();

    /// <summary>True while the ring is drawn.</summary>
    public bool Shown => _ring.Visible;

    /// <summary>The ring node (one for the whole match).</summary>
    public MeshInstance3D Ring => _ring;

    /// <summary>Ring nodes made (always 1 after <see cref="_Ready"/>: the pool).</summary>
    public int NodesMade { get; private set; }

    public override void _Ready()
    {
        var mesh = new TorusMesh { InnerRadius = 0.74f, OuterRadius = 1f, Rings = 28, RingSegments = 4 };
        mesh.Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = RingColor };
        _ring = new MeshInstance3D { Name = "Ring", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_ring);
        NodesMade++;
    }

    /// <summary>Connects the ring to the match; call once after the sim exists.</summary>
    public void Init(SimRunner runner) => _runner = runner;

    /// <summary>Starts the ring on <paramref name="target"/> (an Attack order was just given).</summary>
    public void Show(EntityHandle target, bool isBuilding) => Mark.Mark(target, isBuilding);

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha, (float)delta);
    }

    /// <summary>One frame: counts the mark's time down by <paramref name="delta"/> seconds and places or hides the ring. Allocation-free.</summary>
    public void Sync(World world, float alpha, float delta)
    {
        Mark.Update(delta);
        if (Mark.Active && !TargetAlive(world)) Mark.Clear();
        FogView? fog = Fog?.Refreshed(world);
        bool hidden = Mark.Active && fog != null && !(Mark.IsBuilding ? fog.ShowsBuilding(Mark.Target.Index) : fog.ShowsUnit(Mark.Target.Index));
        if (!Mark.Active || hidden)
        {
            if (_ring.Visible) _ring.Visible = false;
            return;
        }
        Vector3 at;
        float radius;
        int slot = Mark.Target.Index;
        if (Mark.IsBuilding)
        {
            BuildingDef def = world.Data.Buildings[world.Buildings.TypeId[slot]];
            System.Numerics.Vector2 c = StartBase.FootprintCenter(world.NavGrid, def, world.Buildings.Cell[slot]);
            at = new Vector3(c.X, TerrainHeight.At(world.Heightmap, c.X, c.Y), c.Y);
            radius = Math.Max(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize / 2f * BuildingScale;
        }
        else
        {
            at = UnitViews.GroundPoint(world, slot, alpha);
            radius = world.Units.Radius[slot] * UnitScale;
        }
        // Flattened in Y, as the selection ring, so it reads as painted on the ground.
        _ring.Transform = new Transform3D(Basis.FromScale(new Vector3(radius, 0.15f, radius)), at + new Vector3(0f, Lift, 0f));
        if (!_ring.Visible) _ring.Visible = true;
    }

    private bool TargetAlive(World world)
    {
        EntityHandle t = Mark.Target;
        if (Mark.IsBuilding)
        {
            BuildingStore b = world.Buildings;
            return (uint)t.Index < (uint)b.Capacity && b.Alive[t.Index] && b.Generation[t.Index] == t.Generation;
        }
        return world.Units.IsAlive(t);
    }
}
