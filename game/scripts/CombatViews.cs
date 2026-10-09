using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The first combat views (M4-V1): hp bars over hurt units, and corpse and rubble markers where things died.</summary>
/// <remarks>
/// <para><b>Hp bars.</b> Each frame the live units below their type's <c>hp</c> (<see cref="UnitHpBars.Collect"/>) get a
/// bar over the body at their interpolated position: a dark back and a fill of <c>Hp / max</c>, green through yellow to
/// red (<see cref="UnitHpBars.Color"/>). Two <see cref="MultiMesh"/>es (back, fill) hold one instance per bar, written
/// densely, so a dead or healed unit simply isn't listed the next frame. Above <see cref="BarFullZoom"/> the bars grow
/// with the zoom, as the cargo cube does (M3-V1), so they keep their screen size.</para>
/// <para><b>Markers.</b> Every <see cref="DeathEvent"/> (read between ticks: from <see cref="SimRunner.Ticked"/>, so a
/// frame of several ticks misses none, and from the frame loop for scenes that tick the sim themselves) adds a marker to a
/// <see cref="DeathMarkers"/> ring: a disc of the unit's radius in its owner's colour (<see cref="CorpseShade"/>) on a wider,
/// darker rim disc (<see cref="CorpseRimShade"/>, <see cref="CorpseRimScale"/>; M4-V2, BUG-0160: at 35 % both teams read
/// black) for <see cref="DeathMarkers.UnitLifetimeTicks"/>,
/// or a low grey box over a building's footprint for <see cref="DeathMarkers.BuildingLifetimeTicks"/>, both in game time
/// (sim ticks), at the death position on the terrain (a corpse at the highest ground under its rim,
/// <see cref="TerrainHeight.MaxUnder"/>, so it reads whole on a ramp: BUG-0190). The markers are two more <see cref="MultiMesh"/>es with one instance per
/// pool slot; an unused instance has a zero transform. A marker's transform is written only when it is added or removed,
/// so a steady frame writes nothing for them. The pool is fixed (<see cref="DeathMarkers.DefaultCapacity"/>): a death storm
/// replaces the oldest markers.</para>
/// <para><b>Fog</b> (M4-V4): with <see cref="Fog"/> set (before <see cref="Bind"/>), a unit the local player doesn't see gets no
/// bar, and the corpse and rubble materials are the fog's: a marker on unexplored ground is not drawn, one on explored
/// ground is darkened (the GPU reads the cell under each instance).</para>
/// Views hold no gameplay state: a bar is redrawn from the store each frame, a marker is a position and a timer.
/// Everything is made in <see cref="Bind"/>, so a steady frame allocates nothing.
/// </remarks>
public partial class CombatViews : Node3D
{
    /// <summary>Bar length, thickness and gap above the body top at zoom up to <see cref="BarFullZoom"/>, in meters.</summary>
    public const float BarLength = 1.2f, BarThickness = 0.16f, BarGap = 0.35f;

    /// <summary>Zoom (camera height, m) up to which bars keep their size; above it they grow in proportion (the cargo rule).</summary>
    public const float BarFullZoom = UnitViews.CargoFullZoom;

    /// <summary>Corpse disc height and rubble box height, in meters; rubble covers this share of the footprint.</summary>
    public const float CorpseHeight = 0.08f, RubbleHeight = 0.6f, RubbleShare = 0.95f;

    // Placeholder looks until the M6 art pass (M2-1 rule).
    private static readonly Color BarBackColor = new(0.08f, 0.08f, 0.08f);
    private static readonly Color RubbleColor = new(0.42f, 0.40f, 0.37f);
    /// <summary>A corpse's fill is its owner's colour at this share (M4-V2: 0.35 read black for both teams, BUG-0160).</summary>
    public const float CorpseShade = 0.85f;

    /// <summary>A corpse's rim is its owner's colour darkened to this share, so the disc's edge reads on any ground.</summary>
    public const float CorpseRimShade = 0.3f;

    /// <summary>The rim disc's radius over the corpse's, and its height (under the fill's <see cref="CorpseHeight"/>, so only the ring outside shows).</summary>
    public const float CorpseRimScale = 1.2f, CorpseRimHeight = 0.06f;

    /// <summary>The runner whose sim is shown each frame; null shows nothing (tests call <see cref="Sync"/> directly).</summary>
    public SimRunner? Runner
    {
        get => _runner;
        set
        {
            if (_runner != null) _runner.Ticked -= OnTicked;
            _runner = value;
            if (_runner != null) _runner.Ticked += OnTicked;
        }
    }

    /// <summary>The camera whose zoom sizes the bars; null keeps the base size.</summary>
    public RtsCamera? Camera { get; set; }

    /// <summary>The fog (M4-V4): its hide rule drops hidden units' bars, and its materials draw the markers; set before <see cref="Bind"/>. Null draws everything.</summary>
    public FogOfWar? Fog { get; set; }

    private SimRunner? _runner;
    private MultiMesh _back = null!, _fill = null!, _corpses = null!, _rims = null!, _rubble = null!;
    private GameData _data = null!;
    private Color[] _corpseColors = Array.Empty<Color>(), _rimColors = Array.Empty<Color>();
    private float[] _bodyTop = Array.Empty<float>();
    private int[] _hurt = Array.Empty<int>();
    private float[] _shownFill = Array.Empty<float>();
    private Color[] _shownColor = Array.Empty<Color>();
    // The transform written for each marker instance (the dummy renderer of headless runs keeps no instance data to read back).
    private Transform3D[] _markerTransform = Array.Empty<Transform3D>();
    private int[] _added = Array.Empty<int>(), _removed = Array.Empty<int>();
    // What each marker instance shows: 0 nothing, 1 a corpse, 2 rubble.
    private byte[] _markerShown = Array.Empty<byte>();
    private float _barScale = 1f;

    /// <summary>The marker pool (positions, kinds, expiry ticks).</summary>
    public DeathMarkers Markers { get; private set; } = null!;

    /// <summary>Bars drawn this frame.</summary>
    public int ShownBars { get; private set; }

    /// <summary>The unit slot under bar <paramref name="k"/> (k below <see cref="ShownBars"/>).</summary>
    public int BarSlot(int k) => _hurt[k];

    /// <summary>The fill of bar <paramref name="k"/> as drawn.</summary>
    public float BarFill(int k) => _shownFill[k];

    /// <summary>The colour of bar <paramref name="k"/> as drawn (the value written to its instance).</summary>
    public Color BarColor(int k) => _shownColor[k];

    /// <summary>The bar length drawn at the current zoom, in meters.</summary>
    public float BarLengthNow => BarLength * _barScale;

    /// <summary>What marker instance <paramref name="slot"/> shows: 0 nothing, 1 a corpse, 2 rubble.</summary>
    public int MarkerShown(int slot) => _markerShown[slot];

    /// <summary>The transform written to marker instance <paramref name="slot"/> (a zero basis while unused).</summary>
    public Transform3D MarkerTransform(int slot) => _markerTransform[slot];

    /// <summary>Marker instances whose transform was written so far (added or removed; nothing in a steady frame).</summary>
    public long MarkerWrites { get; private set; }

    /// <summary>The corpse multimesh (one instance per pool slot).</summary>
    public MultiMesh CorpseMesh => _corpses;

    /// <summary>The corpse rim multimesh (one instance per pool slot, drawn with each corpse).</summary>
    public MultiMesh CorpseRimMesh => _rims;

    /// <summary>The fill colour of player <paramref name="player"/>'s corpses (black for an unknown player).</summary>
    public Color CorpseColor(int player) => (uint)player < (uint)_corpseColors.Length ? _corpseColors[player] : Colors.Black;

    /// <summary>The rim colour of player <paramref name="player"/>'s corpses.</summary>
    public Color CorpseRimColor(int player) => (uint)player < (uint)_rimColors.Length ? _rimColors[player] : Colors.Black;

    /// <summary>The rubble multimesh (one instance per pool slot).</summary>
    public MultiMesh RubbleMesh => _rubble;

    /// <summary>Creates the meshes, materials and pools; call once before the first <see cref="Sync"/>.</summary>
    /// <param name="playerRgb">Per player, its colour (0xRRGGBB), darkened for its corpses.</param>
    public void Bind(GameData data, int unitCapacity, uint[] playerRgb, int markerCapacity = DeathMarkers.DefaultCapacity)
    {
        _data = data;
        _bodyTop = new float[data.Units.Length];
        for (int t = 0; t < _bodyTop.Length; t++) _bodyTop[t] = UnitViews.BodyHeight(data.Units[t].Radius);
        _corpseColors = new Color[playerRgb.Length];
        _rimColors = new Color[playerRgb.Length];
        for (int p = 0; p < playerRgb.Length; p++)
        {
            Color c = UnitViews.ColorFromRgb(playerRgb[p]);
            _corpseColors[p] = new Color(c.R * CorpseShade, c.G * CorpseShade, c.B * CorpseShade);
            _rimColors[p] = new Color(c.R * CorpseRimShade, c.G * CorpseRimShade, c.B * CorpseRimShade);
        }
        _hurt = new int[unitCapacity];
        _shownFill = new float[unitCapacity];
        _shownColor = new Color[unitCapacity];
        _back = Multi("HpBack", new BoxMesh { Size = Vector3.One, Material = Flat(BarBackColor, vertexColor: false) }, unitCapacity, colors: false);
        _fill = Multi("HpFill", new BoxMesh { Size = Vector3.One, Material = Flat(Colors.White, vertexColor: true) }, unitCapacity, colors: true);
        _back.VisibleInstanceCount = 0;
        _fill.VisibleInstanceCount = 0;

        Markers = new DeathMarkers(markerCapacity);
        _added = new int[Math.Max(unitCapacity, markerCapacity)];
        _removed = new int[markerCapacity];
        _markerShown = new byte[markerCapacity];
        _markerTransform = new Transform3D[markerCapacity];
        Material corpseMat = Fog != null ? Fog.MarkerMaterial()
            : new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var disc = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 16, Rings = 1, Material = corpseMat };
        _rims = Multi("CorpseRims", disc, markerCapacity, colors: true);
        _corpses = Multi("Corpses", disc, markerCapacity, colors: true);
        Material rubbleMat = Fog != null ? Fog.PropMaterial(RubbleColor, 1f) : new StandardMaterial3D { AlbedoColor = RubbleColor, Roughness = 1f };
        _rubble = Multi("Rubble", new BoxMesh { Size = Vector3.One, Material = rubbleMat }, markerCapacity, colors: false);
        var hidden = new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Vector3.Zero);
        for (int i = 0; i < markerCapacity; i++)
        {
            _corpses.SetInstanceTransform(i, hidden);
            _rims.SetInstanceTransform(i, hidden);
            _rubble.SetInstanceTransform(i, hidden);
        }
    }

    public override void _ExitTree() => Runner = null;

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha);
    }

    // Each tick the runner runs: take its deaths while they are still in World.Deaths.
    private void OnTicked(Simulation sim) => CollectDeaths(sim.World);

    /// <summary>Adds the markers for the deaths of the last tick run (once per tick; a later call for the same tick adds nothing).</summary>
    public void CollectDeaths(World world)
    {
        if (Markers == null) return;
        ReadOnlySpan<DeathEvent> deaths = world.Deaths;
        int n = Markers.Collect(deaths, world.TickNumber, _added);
        if (n <= _added.Length)
        {
            for (int i = 0; i < n; i++) ShowMarker(world, _added[i]);
            return;
        }
        // More deaths in one tick than the slot list holds (the ring wrapped): redraw every marker in use.
        DeathMarkers m = Markers;
        for (int slot = 0; slot < m.Capacity; slot++)
            if (m.Active[slot]) ShowMarker(world, slot);
    }

    /// <summary>Adds markers for <paramref name="deaths"/> as if seen at tick <paramref name="tick"/> (test hook for death storms the sim can't stage in one tick).</summary>
    public void AddDeaths(World world, ReadOnlySpan<DeathEvent> deaths, long tick)
    {
        for (int i = 0; i < deaths.Length; i++) ShowMarker(world, Markers.Add(deaths[i], tick));
    }

    /// <summary>One frame: deaths of the last tick, expired markers, and every hurt unit's bar. No allocation.</summary>
    public void Sync(World world, float alpha)
    {
        if (Markers == null) return;
        CollectDeaths(world);
        int gone = Markers.Expire(world.TickNumber, _removed);
        for (int k = 0; k < gone && k < _removed.Length; k++) HideMarker(_removed[k]);
        if (Camera != null) _barScale = Math.Max(1f, Camera.Zoom / BarFullZoom);
        SyncBars(world, alpha);
    }

    private void SyncBars(World world, float alpha)
    {
        UnitStore u = world.Units;
        FogView? fog = Fog?.Refreshed(world);
        // The hurt units the screen shows: through the fog's shown list, so a hidden unit gets no bar.
        int n = UnitHpBars.Collect(fog != null ? fog.UnitShown : u.Alive, u.TypeId, u.Hp, _data.Units, _hurt);
        float len = BarLength * _barScale, thick = BarThickness * _barScale;
        for (int k = 0; k < n; k++)
        {
            int i = _hurt[k];
            int type = u.TypeId[i];
            UnitHpBars.Shows(u.Hp[i], _data.Units[type].Hp, out float fill);
            _shownFill[k] = fill;
            Vector3 top = UnitViews.GroundPoint(world, i, alpha) + new Vector3(0f, _bodyTop[type] + BarGap * _barScale, 0f);
            _back.SetInstanceTransform(k, new Transform3D(Basis.FromScale(new Vector3(len, thick * 0.8f, thick * 0.8f)), top));
            float filled = Math.Max(len * fill, 1e-3f);
            _fill.SetInstanceTransform(k, new Transform3D(Basis.FromScale(new Vector3(filled, thick, thick)), top + new Vector3(-len / 2f + filled / 2f, 0f, 0f)));
            System.Numerics.Vector3 c = UnitHpBars.Color(fill);
            _shownColor[k] = new Color(c.X, c.Y, c.Z);
            _fill.SetInstanceColor(k, _shownColor[k]);
        }
        if (n != ShownBars)
        {
            _back.VisibleInstanceCount = n;
            _fill.VisibleInstanceCount = n;
            ShownBars = n;
        }
    }

    private void ShowMarker(World world, int slot)
    {
        bool building = Markers.IsBuilding[slot];
        if (_markerShown[slot] != 0) HideMarker(slot); // the ring replaced the oldest marker
        System.Numerics.Vector2 p = Markers.Position[slot];
        float y = TerrainHeight.At(world.Heightmap, p.X, p.Y);
        int type = Markers.Type[slot];
        if (building)
        {
            BuildingDef def = _data.Buildings[Math.Clamp(type, 0, _data.Buildings.Length - 1)];
            var size = new Vector3(def.FootprintWidth * MapConstants.CellSize * RubbleShare, RubbleHeight, def.FootprintHeight * MapConstants.CellSize * RubbleShare);
            _markerTransform[slot] = new Transform3D(Basis.FromScale(size), new Vector3(p.X, y + RubbleHeight / 2f, p.Y));
            _rubble.SetInstanceTransform(slot, _markerTransform[slot]);
            _markerShown[slot] = 2;
        }
        else
        {
            float r = _data.Units[Math.Clamp(type, 0, _data.Units.Length - 1)].Radius;
            // At the highest ground under the rim, so a disc on a ramp is not half buried (BUG-0190 item 1).
            y = TerrainHeight.MaxUnder(world.Heightmap, p.X, p.Y, r * CorpseRimScale);
            _markerTransform[slot] = new Transform3D(Basis.FromScale(new Vector3(r, CorpseHeight, r)), new Vector3(p.X, y + CorpseHeight / 2f, p.Y));
            _corpses.SetInstanceTransform(slot, _markerTransform[slot]);
            float rr = r * CorpseRimScale;
            _rims.SetInstanceTransform(slot, new Transform3D(Basis.FromScale(new Vector3(rr, CorpseRimHeight, rr)), new Vector3(p.X, y + CorpseRimHeight / 2f, p.Y)));
            int owner = Markers.Owner[slot];
            _corpses.SetInstanceColor(slot, CorpseColor(owner));
            _rims.SetInstanceColor(slot, CorpseRimColor(owner));
            _markerShown[slot] = 1;
        }
        MarkerWrites++;
    }

    private void HideMarker(int slot)
    {
        var hidden = new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Vector3.Zero);
        if (_markerShown[slot] == 1)
        {
            _corpses.SetInstanceTransform(slot, hidden);
            _rims.SetInstanceTransform(slot, hidden);
        }
        else if (_markerShown[slot] == 2) _rubble.SetInstanceTransform(slot, hidden);
        _markerShown[slot] = 0;
        _markerTransform[slot] = hidden;
        MarkerWrites++;
    }

    private MultiMesh Multi(string name, Mesh mesh, int count, bool colors)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = colors,
            Mesh = mesh,
            InstanceCount = count,
        };
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return mm;
    }

    private static StandardMaterial3D Flat(Color c, bool vertexColor) => new()
    {
        AlbedoColor = c,
        VertexColorUseAsAlbedo = vertexColor,
        VertexColorIsSrgb = vertexColor, // instance colours are written in sRGB, like the albedo colours
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
