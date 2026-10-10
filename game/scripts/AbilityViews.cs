using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The ability views (M4-V6a, M4-V6b): the targeting rings while an ability is armed, the casts on the map, the status markers over units and the resolve flashes.</summary>
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
/// rings are three <see cref="MultiMesh"/>es with one instance per unit slot, written densely each frame. M4-V6b (BUG-0342):
/// the bar is thicker, its back a dim violet rather than black and its fill never shorter than it is thick, so it reads as a
/// bar from the cast's first tick.</para>
/// <para><b>Statuses</b> (M4-V6b). Every unit the screen shows gets one small marker per active status
/// (<see cref="StatusMarkers.Collect"/>), side by side above its cast bar's place, in a colour by the status's
/// <see cref="StatusKind"/> (Burning flame, Slowed blue-grey, any other kind pale). One coloured <see cref="MultiMesh"/>
/// sized for every slot's <see cref="StatusStore.PerUnit"/> entries, written densely each frame.</para>
/// <para><b>Resolve flashes</b> (M4-V6b). Every resolved cast in <c>World.AbilityEvents</c> (read from
/// <see cref="SimRunner.Ticked"/>, so a frame of several ticks misses none, and from the frame loop for scenes that tick the
/// sim themselves) adds a <see cref="ResolveFlashes"/> entry: a flat disc on the ground at the cast point that grows to the
/// ability's <c>radius</c> and fades over <see cref="ResolveFlashes.LifetimeTicks"/> (0.5 s), drawn only while the point's
/// cell is visible (<see cref="FogView.ShowsPoint"/>). A cast start draws no flash.</para>
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
    public const float BarLength = 1.2f, BarThickness = 0.2f, BarAbove = 0.3f;

    /// <summary>A status marker's size, the gap between neighbours' centres, and its height over the hp bar's place, in meters (grown with the zoom as the bars are).</summary>
    public const float MarkerSize = 0.26f, MarkerSpacing = 0.34f, MarkerAbove = 0.62f;

    /// <summary>A resolve flash's disc starts at this share of the ability's radius and reaches the full radius at <see cref="FlashGrowShare"/> of its life.</summary>
    public const float FlashFrom = 0.5f, FlashGrowShare = 0.4f;

    /// <summary>A resolve flash's opacity at the start of its life (it fades to 0).</summary>
    public const float FlashAlpha = 0.75f;

    // UI tints until the M6 art pass: the area reads fire-orange, the reach pale blue, the bar violet (magic).
    private static readonly Color RadiusColor = new(1f, 0.55f, 0.1f);
    private static readonly Color RangeColor = new(0.6f, 0.8f, 1f);
    private static readonly Color CastRingColor = new(1f, 0.4f, 0.05f);
    // BUG-0342: a black back read as an empty dash; a dim violet back reads as the bar's empty part.
    private static readonly Color BarBackColor = new(0.28f, 0.2f, 0.38f);
    private static readonly Color BarFillColor = new(0.85f, 0.55f, 1f);

    /// <summary>The Burning (damage over time) marker's colour: flame.</summary>
    public static readonly Color DamageOverTimeColor = new(1f, 0.42f, 0.05f);

    /// <summary>The Slowed (slow) marker's colour: blue-grey.</summary>
    public static readonly Color SlowColor = new(0.55f, 0.66f, 0.82f);

    /// <summary>A marker of any other status kind (M4-V6c adds its own): pale.</summary>
    public static readonly Color OtherStatusColor = new(0.92f, 0.92f, 0.88f);

    /// <summary>The resolve flash's colour (its alpha fades).</summary>
    public static readonly Color FlashColor = new(1f, 0.78f, 0.35f);

    private static readonly Transform3D Hidden = new(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Vector3.Zero);

    private SimRunner? _runner;
    private SelectionController? _sel;
    private GameData _data = null!;
    private MeshInstance3D _range = null!, _radius = null!;
    private TorusMesh?[] _rangeMesh = Array.Empty<TorusMesh?>(), _radiusMesh = Array.Empty<TorusMesh?>();
    private MultiMesh _barBack = null!, _barFill = null!, _circles = null!;
    private float[] _bodyTop = Array.Empty<float>();
    private int[] _barSlot = Array.Empty<int>(), _circleSlot = Array.Empty<int>();
    private float[] _barFillShown = Array.Empty<float>(), _barFillLength = Array.Empty<float>();
    private float _barScale = 1f;
    private MultiMesh _markers = null!, _flashes = null!;
    private StatusMark[] _marks = Array.Empty<StatusMark>();
    private Vector3[] _markAt = Array.Empty<Vector3>();
    private Color[] _statusColor = Array.Empty<Color>();
    private Transform3D[] _flashTransform = Array.Empty<Transform3D>();
    private int[] _removed = Array.Empty<int>();

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

    /// <summary>The drawn length (m) of cast bar <paramref name="k"/>'s fill: its share of the bar, but never under the bar's thickness (BUG-0342).</summary>
    public float BarFillLength(int k) => _barFillLength[k];

    /// <summary>Cast-point rings drawn this frame.</summary>
    public int ShownCircles { get; private set; }

    /// <summary>The caster slot of cast-point ring <paramref name="k"/>.</summary>
    public int CircleSlot(int k) => _circleSlot[k];

    /// <summary>Status markers drawn this frame.</summary>
    public int ShownMarkers { get; private set; }

    /// <summary>Status marker <paramref name="k"/> as drawn: its unit, status and place in the unit's row.</summary>
    public StatusMark MarkerAt(int k) => _marks[k];

    /// <summary>Where status marker <paramref name="k"/> was drawn (its instance's centre).</summary>
    public Vector3 MarkerPosition(int k) => _markAt[k];

    /// <summary>The colour status <paramref name="status"/>'s markers are drawn in.</summary>
    public Color StatusColor(int status) => (uint)status < (uint)_statusColor.Length ? _statusColor[status] : OtherStatusColor;

    /// <summary>The resolve flashes' pool.</summary>
    public ResolveFlashes Flashes { get; private set; } = null!;

    /// <summary>The transform written to flash instance <paramref name="slot"/> (a zero basis while unused or hidden by the fog).</summary>
    public Transform3D FlashTransform(int slot) => _flashTransform[slot];

    /// <summary>Flashes drawn (not hidden) by the last frame.</summary>
    public int ShownFlashes { get; private set; }

    /// <summary>The marker and flash multimeshes.</summary>
    public MultiMesh MarkerMesh => _markers;

    /// <inheritdoc cref="MarkerMesh"/>
    public MultiMesh FlashMesh => _flashes;

    public override void _Ready()
    {
        _range = Ring("RangeRing");
        _radius = Ring("RadiusRing");
    }

    /// <summary>Creates the bar, ring, marker and flash pools; call once before the first <see cref="Sync"/>.</summary>
    public void Bind(GameData data, int unitCapacity, SimRunner? runner, SelectionController? selection, int flashCapacity = ResolveFlashes.DefaultCapacity)
    {
        _data = data;
        if (_runner != null) _runner.Ticked -= OnTicked;
        _runner = runner;
        if (_runner != null) _runner.Ticked += OnTicked;
        _sel = selection;
        _bodyTop = new float[data.Units.Length];
        for (int t = 0; t < _bodyTop.Length; t++) _bodyTop[t] = UnitViews.BodyHeight(data.Units[t].Radius);
        _rangeMesh = new TorusMesh?[data.Abilities.Length];
        _radiusMesh = new TorusMesh?[data.Abilities.Length];
        _barSlot = new int[unitCapacity];
        _circleSlot = new int[unitCapacity];
        _barFillShown = new float[unitCapacity];
        _barFillLength = new float[unitCapacity];
        _barBack = Multi("CastBack", new BoxMesh { Size = Vector3.One, Material = Flat(BarBackColor) }, unitCapacity);
        _barFill = Multi("CastFill", new BoxMesh { Size = Vector3.One, Material = Flat(BarFillColor) }, unitCapacity);
        var unitRing = new TorusMesh { InnerRadius = 1f - CastRingShare, OuterRadius = 1f, Rings = 48, RingSegments = 4, Material = Flat(CastRingColor) };
        _circles = Multi("CastRings", unitRing, unitCapacity);

        // Marker colours follow the status's kind (data), so a new status of a known kind needs no code.
        _statusColor = new Color[data.Statuses.Length];
        for (int s = 0; s < _statusColor.Length; s++)
            _statusColor[s] = data.Statuses[s].Kind switch
            {
                StatusKind.DamageOverTime => DamageOverTimeColor,
                StatusKind.Slow => SlowColor,
                _ => OtherStatusColor,
            };
        int markCapacity = unitCapacity * StatusStore.PerUnit;
        _marks = new StatusMark[markCapacity];
        _markAt = new Vector3[markCapacity];
        var colored = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _markers = Multi("StatusMarkers", new BoxMesh { Size = Vector3.One, Material = colored }, markCapacity, colors: true);

        Flashes = new ResolveFlashes(flashCapacity);
        _removed = new int[flashCapacity];
        _flashTransform = new Transform3D[flashCapacity];
        var flashMat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _flashes = Multi("ResolveFlashes", new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 32, Rings = 1, Material = flashMat }, flashCapacity, colors: true);
        _flashes.VisibleInstanceCount = -1;
        for (int i = 0; i < flashCapacity; i++)
        {
            _flashes.SetInstanceTransform(i, Hidden);
            _flashTransform[i] = Hidden;
        }
    }

    public override void _ExitTree()
    {
        if (_runner != null) _runner.Ticked -= OnTicked;
    }

    // Each tick the runner runs: take its resolves while they are still in World.AbilityEvents.
    private void OnTicked(Simulation sim) => CollectTick(sim.World);

    /// <summary>Adds the flashes of the last tick's resolves (once per tick; a later call for the same tick does nothing).</summary>
    public void CollectTick(World world) => Flashes?.Collect(world.AbilityEvents, world.TickNumber);

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha, GetViewport().GetMousePosition(), Input.IsActionPressed(OrderQueue));
    }

    /// <summary>One frame: the targeting rings for the cursor at <paramref name="mouse"/> (screen pixels; <paramref name="queued"/>: Shift held), then the cast bars and cast-point rings, the status markers and the resolve flashes. Allocation-free once each armed ability's meshes exist.</summary>
    public void Sync(World world, float alpha, Vector2 mouse, bool queued)
    {
        if (_barBack == null) return;
        SyncTargeting(world, alpha, mouse, queued);
        if (Camera != null) _barScale = Math.Max(1f, Camera.Zoom / CombatViews.BarFullZoom);
        FogView? fog = Fog?.Refreshed(world);
        SyncCasts(world, alpha, fog);
        SyncMarkers(world, alpha, fog);
        SyncFlashes(world, alpha, fog);
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

    private void SyncCasts(World world, float alpha, FogView? fog)
    {
        UnitStore u = world.Units;
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
                // Never thinner than a square nub, so the first tick shows violet (BUG-0342).
                float filled = Math.Min(len, Math.Max(len * fill, thick));
                _barFill.SetInstanceTransform(bars, new Transform3D(Basis.FromScale(new Vector3(filled, thick, thick)), top + new Vector3(-len / 2f + filled / 2f, 0f, 0f)));
                _barSlot[bars] = i;
                _barFillShown[bars] = fill;
                _barFillLength[bars] = filled;
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

    private void SyncMarkers(World world, float alpha, FogView? fog)
    {
        UnitStore u = world.Units;
        ReadOnlySpan<bool> shown = fog != null ? fog.UnitShown : u.Alive;
        int n = StatusMarkers.Collect(u.Statuses, shown, _statusColor.Length, _marks);
        float size = MarkerSize * _barScale, spacing = MarkerSpacing * _barScale;
        var basis = Basis.FromScale(new Vector3(size, size, size));
        for (int k = 0; k < n; k++)
        {
            StatusMark m = _marks[k];
            int type = u.TypeId[m.Unit];
            Vector3 at = UnitViews.GroundPoint(world, m.Unit, alpha)
                + new Vector3(StatusMarkers.RowOffset(m.Place, m.Row, spacing), _bodyTop[type] + (CombatViews.BarGap + MarkerAbove) * _barScale, 0f);
            _markers.SetInstanceTransform(k, new Transform3D(basis, at));
            _markers.SetInstanceColor(k, _statusColor[m.Status]);
            _markAt[k] = at;
        }
        if (n != ShownMarkers)
        {
            _markers.VisibleInstanceCount = n;
            ShownMarkers = n;
        }
    }

    private void SyncFlashes(World world, float alpha, FogView? fog)
    {
        ResolveFlashes f = Flashes;
        CollectTick(world);
        int gone = f.Expire(world.TickNumber, _removed);
        for (int k = 0; k < gone && k < _removed.Length; k++) HideFlash(_removed[k]);
        float a = float.IsNaN(alpha) ? 1f : Math.Clamp(alpha, 0f, 1f);
        ReadOnlySpan<bool> active = f.Active;
        int shown = 0;
        for (int i = 0; i < active.Length; i++)
        {
            if (!active[i]) continue;
            System.Numerics.Vector2 p = f.Point[i];
            if (fog != null && !fog.ShowsPoint(world.Fog, p))
            {
                if (_flashTransform[i] != Hidden) HideFlash(i);
                f.MarkDrawn(i); // its frame has passed: it expires on time, unseen
                continue;
            }
            float age = f.Age(i, world.TickNumber, a);
            int ability = f.Ability[i];
            float radius = (uint)ability < (uint)_data.Abilities.Length ? _data.Abilities[ability].Radius : 1f;
            float r = radius * (FlashFrom + (1f - FlashFrom) * Math.Min(1f, age / FlashGrowShare));
            _flashTransform[i] = new Transform3D(Basis.FromScale(new Vector3(r, 0.06f, r)), new Vector3(p.X, TerrainHeight.At(world.Heightmap, p.X, p.Y) + Lift, p.Y));
            _flashes.SetInstanceTransform(i, _flashTransform[i]);
            _flashes.SetInstanceColor(i, new Color(FlashColor.R, FlashColor.G, FlashColor.B, FlashAlpha * (1f - age)));
            f.MarkDrawn(i);
            shown++;
        }
        ShownFlashes = shown;
    }

    private void HideFlash(int slot)
    {
        _flashes.SetInstanceTransform(slot, Hidden);
        _flashTransform[slot] = Hidden;
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

    private MultiMesh Multi(string name, Mesh mesh, int count, bool colors = false)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh, InstanceCount = count, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return mm;
    }

    private static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
}
