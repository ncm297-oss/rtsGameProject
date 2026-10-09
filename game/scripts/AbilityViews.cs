using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The ability views (M4-V6a): the targeting rings while an ability is armed, and the casts on the map.</summary>
/// <remarks>
/// <para><b>Targeting.</b> While <see cref="SelectionController.TargetAbility"/> is set, the ground under the cursor
/// (<see cref="SelectionController.AbilityPoint"/>) gets a ring of the ability's <c>radius</c> (where the effects would land)
/// and the selected unit that would cast there (<see cref="SelectionController.PickCaster"/>: the nearest ready one) a ring of
/// its <c>range</c>. Each ring is a flat torus whose outer radius is the def's value, one mesh per ability and ring made the
/// first time it is armed; a frame only moves the two nodes.</para>
/// <para><b>Casts.</b> Every unit the screen shows (the fog's shown list, M4-V4) standing in <see cref="UnitState.Casting"/>
/// gets a cast bar over its hp bar's place: the share of the def's <c>castTime</c> stood (<see cref="AbilityCaster.Progress"/>
/// of <c>UnitStore.CastTicks</c>). Every own unit with a cast in hand (<c>CastAbility</c> set: walking into range or standing)
/// gets a ring of the ability's radius on its <c>CastPoint</c>, so the player sees where the walking mage will cast. Bars and
/// rings are three <see cref="MultiMesh"/>es with one instance per unit slot, written densely each frame.</para>
/// Views hold no gameplay state: everything is redrawn from the store and the selection each frame. Everything but the
/// per-ability ring meshes is made in <see cref="Bind"/>, so a steady frame allocates nothing.
/// </remarks>
public partial class AbilityViews : Node3D
{
    // Cached so per-frame and per-click lookups do not allocate a StringName from a string (BUG-0341).
    private static readonly StringName OrderQueue = "order_queue";

    /// <summary>Width of a targeting ring's band, and of a cast ring's band as a share of its radius, in meters.</summary>
    public const float RingWidth = 0.22f, CastRingShare = 0.08f;

    /// <summary>Height of the rings above the ground at their centre (the target ring sits at 0.09), in meters.</summary>
    public const float Lift = 0.12f;

    /// <summary>The cast bar's length and thickness at zoom up to <see cref="CombatViews.BarFullZoom"/>, and its height over the hp bar's place, in meters.</summary>
    public const float BarLength = 1.2f, BarThickness = 0.12f, BarAbove = 0.25f;

    // UI tints until the M6 art pass: the area reads fire-orange, the reach pale blue, the bar violet (magic).
    private static readonly Color RadiusColor = new(1f, 0.55f, 0.1f);
    private static readonly Color RangeColor = new(0.6f, 0.8f, 1f);
    private static readonly Color CastRingColor = new(1f, 0.4f, 0.05f);
    private static readonly Color BarBackColor = new(0.08f, 0.08f, 0.08f);
    private static readonly Color BarFillColor = new(0.75f, 0.45f, 1f);

    private SimRunner? _runner;
    private SelectionController? _sel;
    private GameData _data = null!;
    private MeshInstance3D _range = null!, _radius = null!;
    private TorusMesh?[] _rangeMesh = Array.Empty<TorusMesh?>(), _radiusMesh = Array.Empty<TorusMesh?>();
    private MultiMesh _barBack = null!, _barFill = null!, _circles = null!;
    private float[] _bodyTop = Array.Empty<float>();
    private int[] _barSlot = Array.Empty<int>(), _circleSlot = Array.Empty<int>();
    private float[] _barFillShown = Array.Empty<float>();
    private float _barScale = 1f;

    /// <summary>The camera whose zoom sizes the cast bars (the hp bar rule); null keeps the base size.</summary>
    public RtsCamera? Camera { get; set; }

    /// <summary>The fog whose hide rule applies to the cast bars; null shows every live caster's.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>True while the two targeting rings' state is drawn (an ability armed and the cursor on the map).</summary>
    public bool TargetingShown => _radius.Visible;

    /// <summary>The ability the targeting rings are drawn for, or -1.</summary>
    public int ShownAbility { get; private set; } = -1;

    /// <summary>The radius ring's drawn outer radius (m): the def's <c>radius</c>.</summary>
    public float ShownRadius => _radius.Mesh is TorusMesh t ? t.OuterRadius : 0f;

    /// <summary>The range ring's drawn outer radius (m): the def's <c>range</c>.</summary>
    public float ShownRange => _range.Mesh is TorusMesh t ? t.OuterRadius : 0f;

    /// <summary>The radius ring's centre (sim x, y in meters) as last drawn.</summary>
    public System.Numerics.Vector2 RadiusCenter { get; private set; }

    /// <summary>The unit slot the range ring is drawn round (the caster that would cast), or -1 when it is hidden.</summary>
    public int RangeCaster { get; private set; } = -1;

    /// <summary>The range ring node.</summary>
    public MeshInstance3D RangeRing => _range;

    /// <summary>The radius ring node.</summary>
    public MeshInstance3D RadiusRing => _radius;

    /// <summary>Cast bars drawn this frame.</summary>
    public int ShownBars { get; private set; }

    /// <summary>The unit slot under cast bar <paramref name="k"/>.</summary>
    public int BarSlot(int k) => _barSlot[k];

    /// <summary>The fill of cast bar <paramref name="k"/> as drawn (0 to 1).</summary>
    public float BarFill(int k) => _barFillShown[k];

    /// <summary>Cast-point rings drawn this frame.</summary>
    public int ShownCircles { get; private set; }

    /// <summary>The caster slot of cast-point ring <paramref name="k"/>.</summary>
    public int CircleSlot(int k) => _circleSlot[k];

    public override void _Ready()
    {
        _range = Ring("RangeRing");
        _radius = Ring("RadiusRing");
    }

    /// <summary>Creates the bar and ring pools; call once before the first <see cref="Sync"/>.</summary>
    public void Bind(GameData data, int unitCapacity, SimRunner? runner, SelectionController? selection)
    {
        _data = data;
        _runner = runner;
        _sel = selection;
        _bodyTop = new float[data.Units.Length];
        for (int t = 0; t < _bodyTop.Length; t++) _bodyTop[t] = UnitViews.BodyHeight(data.Units[t].Radius);
        _rangeMesh = new TorusMesh?[data.Abilities.Length];
        _radiusMesh = new TorusMesh?[data.Abilities.Length];
        _barSlot = new int[unitCapacity];
        _circleSlot = new int[unitCapacity];
        _barFillShown = new float[unitCapacity];
        _barBack = Multi("CastBack", new BoxMesh { Size = Vector3.One, Material = Flat(BarBackColor) }, unitCapacity);
        _barFill = Multi("CastFill", new BoxMesh { Size = Vector3.One, Material = Flat(BarFillColor) }, unitCapacity);
        var unitRing = new TorusMesh { InnerRadius = 1f - CastRingShare, OuterRadius = 1f, Rings = 48, RingSegments = 4, Material = Flat(CastRingColor) };
        _circles = Multi("CastRings", unitRing, unitCapacity);
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha, GetViewport().GetMousePosition(), Input.IsActionPressed(OrderQueue));
    }

    /// <summary>One frame: the targeting rings for the cursor at <paramref name="mouse"/> (screen pixels; <paramref name="queued"/>: Shift held), then the cast bars and cast-point rings. Allocation-free once each armed ability's meshes exist.</summary>
    public void Sync(World world, float alpha, Vector2 mouse, bool queued)
    {
        if (_barBack == null) return;
        SyncTargeting(world, alpha, mouse, queued);
        if (Camera != null) _barScale = Math.Max(1f, Camera.Zoom / CombatViews.BarFullZoom);
        SyncCasts(world, alpha);
    }

    private void SyncTargeting(World world, float alpha, Vector2 mouse, bool queued)
    {
        int ability = _sel?.TargetAbility ?? -1;
        if (ability < 0 || !_sel!.AbilityPoint(mouse, out System.Numerics.Vector2 point))
        {
            HideTargeting();
            return;
        }
        AbilityDef def = _data.Abilities[ability];
        if (ShownAbility != ability)
        {
            ShownAbility = ability;
            _radius.Mesh = _radiusMesh[ability] ??= RingMesh(def.Radius, RadiusColor);
            _range.Mesh = _rangeMesh[ability] ??= RingMesh(def.Range, RangeColor);
        }
        RadiusCenter = point;
        _radius.Position = new Vector3(point.X, TerrainHeight.At(world.Heightmap, point.X, point.Y) + Lift, point.Y);
        if (!_radius.Visible) _radius.Visible = true;
        int caster = _sel.PickCaster(ability, point, queued, out _);
        RangeCaster = caster;
        if (caster < 0)
        {
            if (_range.Visible) _range.Visible = false;
            return;
        }
        _range.Position = UnitViews.GroundPoint(world, caster, alpha) + new Vector3(0f, Lift, 0f);
        if (!_range.Visible) _range.Visible = true;
    }

    private void HideTargeting()
    {
        RangeCaster = -1;
        if (_radius.Visible) _radius.Visible = false;
        if (_range.Visible) _range.Visible = false;
    }

    private void SyncCasts(World world, float alpha)
    {
        UnitStore u = world.Units;
        FogView? fog = Fog?.Refreshed(world);
        ReadOnlySpan<bool> shown = fog != null ? fog.UnitShown : u.Alive;
        float len = BarLength * _barScale, thick = BarThickness * _barScale;
        int bars = 0, circles = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.CastAbility[i] < 0) continue;
            int type = u.TypeId[i];
            var abilities = _data.Units[type].Abilities;
            int k = u.CastAbility[i];
            if ((uint)k >= (uint)abilities.Length) continue;
            AbilityDef def = _data.Abilities[abilities[k]];
            if (u.State[i] == UnitState.Casting && i < shown.Length && shown[i])
            {
                float fill = AbilityCaster.Progress(u.CastTicks[i], def.CastTicks);
                Vector3 top = UnitViews.GroundPoint(world, i, alpha)
                    + new Vector3(0f, _bodyTop[type] + (CombatViews.BarGap + BarAbove) * _barScale, 0f);
                _barBack.SetInstanceTransform(bars, new Transform3D(Basis.FromScale(new Vector3(len, thick * 0.8f, thick * 0.8f)), top));
                float filled = Math.Max(len * fill, 1e-3f);
                _barFill.SetInstanceTransform(bars, new Transform3D(Basis.FromScale(new Vector3(filled, thick, thick)), top + new Vector3(-len / 2f + filled / 2f, 0f, 0f)));
                _barSlot[bars] = i;
                _barFillShown[bars] = fill;
                bars++;
            }
            if (u.Owner[i] == SelectionController.LocalPlayer)
            {
                System.Numerics.Vector2 p = u.CastPoint[i];
                float r = def.Radius;
                var at = new Vector3(p.X, TerrainHeight.At(world.Heightmap, p.X, p.Y) + Lift, p.Y);
                _circles.SetInstanceTransform(circles, new Transform3D(Basis.FromScale(new Vector3(r, 0.3f, r)), at));
                _circleSlot[circles] = i;
                circles++;
            }
        }
        if (bars != ShownBars)
        {
            _barBack.VisibleInstanceCount = bars;
            _barFill.VisibleInstanceCount = bars;
            ShownBars = bars;
        }
        if (circles != ShownCircles)
        {
            _circles.VisibleInstanceCount = circles;
            ShownCircles = circles;
        }
    }

    // A flat ring whose outer edge is `radius` m from its centre.
    private static TorusMesh RingMesh(float radius, Color c) => new()
    {
        InnerRadius = Math.Max(0.01f, radius - RingWidth),
        OuterRadius = radius,
        Rings = Math.Clamp((int)(radius * 8f), 32, 160),
        RingSegments = 4,
        Material = Flat(c),
    };

    private MeshInstance3D Ring(string name)
    {
        // Flattened in Y like the selection ring, so it reads as painted on the ground.
        var node = new MeshInstance3D { Name = name, Scale = new Vector3(1f, 0.3f, 1f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(node);
        return node;
    }

    private MultiMesh Multi(string name, Mesh mesh, int count)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = count, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return mm;
    }

    private static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
}
