using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Placeholder building views (M3-V1): one box per live building, with a progress bar over a site and a hit-point bar over a damaged building.</summary>
/// <remarks>
/// Polled each frame from <see cref="BuildingStore.Alive"/> / <see cref="BuildingStore.Generation"/> (the props
/// precedent; there are no sim events yet). A slot's nodes are created the first time it holds a building and hidden
/// while it is free, so a later building in the slot reuses them. The box is the footprint (cells x
/// <see cref="MapConstants.CellSize"/>) by <see cref="BoxHeight"/>, in the owner's faction colour; a construction site is
/// a dusty mauve (<see cref="SiteColor"/>) and rises with its progress. The bar's kind and fill come from the pure <see cref="BuildingBars"/>; its
/// transform is only written when they change. Meshes (one per building type, plus one bar) and materials (one per
/// player, site, and the bar colours) are made in <see cref="Bind"/>, so a steady frame allocates nothing. Views hold no
/// gameplay state: the per-slot values kept here only say what is already drawn.
/// Fog (M4-V4): with <see cref="Fog"/> set, an enemy building the local player doesn't see (<c>Fog.CanSeeBuilding</c>
/// false, through <see cref="FogView.BuildingShown"/>) is hidden like a freed slot, bar included, and placed afresh when it
/// comes into sight.
/// Ghosts (M4-V5): a slot the fog marks in <see cref="FogView.GhostShown"/> (an enemy building seen once and not seen now,
/// from <c>Fog.Ghosts</c>) also gets a ghost box: its remembered type's box at its remembered anchor, full height, in the
/// owner's colour darkened to <see cref="FogView.ExploredBrightness"/>, with no bar. Ghost boxes are their own per-slot
/// pool (a slot can show a new building and the old one's ghost at once until the next fog update), created the first time
/// the slot has a ghost and hidden when the sim drops the entry or the building is seen again. A remembered site draws
/// finished (BUG-0275 item 1: the entry keeps no progress).
/// </remarks>
public partial class BuildingViews : Node3D
{
    /// <summary>Height of a finished building's placeholder box, in meters.</summary>
    public const float BoxHeight = 3f;

    /// <summary>Share of <see cref="BoxHeight"/> a site's box has at no progress (it rises to the full height at completion).</summary>
    public const float SiteMinHeight = 0.15f;

    /// <summary>Bar centre height above the ground, in meters.</summary>
    public const float BarHeight = BoxHeight + 0.6f;

    /// <summary>Bar length as a share of the footprint's width, and its thickness in meters.</summary>
    public const float BarLengthShare = 0.9f, BarThickness = 0.3f;

    // Placeholder tints until the M6 art pass (M2-1 rule: hard-coded like the terrain's).
    /// <summary>
    /// A site's placeholder colour until the M6 art pass: a dusty mauve, hue about 305 degrees, far from both factions'
    /// building colours (Malazan slate #4B4F55 at about 214, Whirlwind ochre #C8892E at about 35). The old slate
    /// (0.42, 0.45, 0.50) was the Malazan hue, so a full-height Malazan site differed from a finished one only in
    /// lightness (BUG-0107).
    /// </summary>
    public static readonly Color SiteColor = new(0.64f, 0.48f, 0.62f);
    private static readonly Color BarBackColor = new(0.08f, 0.08f, 0.08f);
    private static readonly Color ProgressColor = new(0.95f, 0.85f, 0.35f);
    private static readonly Color HitPointColor = new(0.30f, 0.90f, 0.35f);

    /// <summary>The runner whose sim is shown each frame; null shows nothing (tests call <see cref="Sync"/> directly).</summary>
    public SimRunner? Runner { get; set; }

    /// <summary>The fog whose hide rule applies (M4-V4); null shows every live building.</summary>
    public FogOfWar? Fog { get; set; }

    private BoxMesh[] _meshes = Array.Empty<BoxMesh>();
    private BoxMesh _barMesh = null!;
    private StandardMaterial3D[] _playerMats = Array.Empty<StandardMaterial3D>();
    private StandardMaterial3D _siteMat = null!, _progressMat = null!, _hpMat = null!;

    private Node3D?[] _roots = Array.Empty<Node3D?>();
    private MeshInstance3D[] _boxes = Array.Empty<MeshInstance3D>();
    private MeshInstance3D[] _backs = Array.Empty<MeshInstance3D>();
    private MeshInstance3D[] _fills = Array.Empty<MeshInstance3D>();
    // What each slot's nodes show now: the building's generation (0: hidden), site flag, box height share, bar.
    private int[] _shownGen = Array.Empty<int>();
    private bool[] _shownSite = Array.Empty<bool>();
    private float[] _shownRise = Array.Empty<float>();
    private BuildingBarKind[] _shownBar = Array.Empty<BuildingBarKind>();
    private float[] _shownFill = Array.Empty<float>();
    private float[] _barLength = Array.Empty<float>();
    private StandardMaterial3D[] _ghostMats = Array.Empty<StandardMaterial3D>();
    private MeshInstance3D?[] _ghostBoxes = Array.Empty<MeshInstance3D?>();
    // What each slot's ghost box shows now: the remembered generation (0: hidden) and anchor.
    private int[] _ghostGen = Array.Empty<int>();
    private int[] _ghostCell = Array.Empty<int>();

    /// <summary>View roots created so far (one per slot ever used).</summary>
    public int NodeCount { get; private set; }

    /// <summary>Bar transform writes so far (only when a bar's kind or fill changes).</summary>
    public int BarUpdates { get; private set; }

    /// <summary>Creates the shared meshes and materials and sizes the slot pool; call once before the first <see cref="Sync"/>.</summary>
    /// <param name="playerRgb">Per player, the colour (0xRRGGBB) of its finished buildings.</param>
    public void Bind(GameData data, int capacity, uint[] playerRgb)
    {
        _meshes = new BoxMesh[data.Buildings.Length];
        for (int t = 0; t < _meshes.Length; t++)
        {
            BuildingDef def = data.Buildings[t];
            _meshes[t] = new BoxMesh { Size = new Vector3(def.FootprintWidth * MapConstants.CellSize, BoxHeight, def.FootprintHeight * MapConstants.CellSize) };
        }
        _playerMats = new StandardMaterial3D[playerRgb.Length];
        for (int p = 0; p < playerRgb.Length; p++)
            _playerMats[p] = new StandardMaterial3D { AlbedoColor = UnitViews.ColorFromRgb(playerRgb[p]), Roughness = 0.85f };
        _ghostMats = new StandardMaterial3D[playerRgb.Length];
        for (int p = 0; p < playerRgb.Length; p++)
        {
            Color c = UnitViews.ColorFromRgb(playerRgb[p]);
            float k = FogView.ExploredBrightness;
            _ghostMats[p] = new StandardMaterial3D { AlbedoColor = new Color(c.R * k, c.G * k, c.B * k), Roughness = 0.95f };
        }
        _siteMat = new StandardMaterial3D { AlbedoColor = SiteColor, Roughness = 0.95f };
        _barMesh = new BoxMesh { Size = Vector3.One };
        var back = new StandardMaterial3D { AlbedoColor = BarBackColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _barMesh.Material = back;
        _progressMat = new StandardMaterial3D { AlbedoColor = ProgressColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _hpMat = new StandardMaterial3D { AlbedoColor = HitPointColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

        _roots = new Node3D?[capacity];
        _boxes = new MeshInstance3D[capacity];
        _backs = new MeshInstance3D[capacity];
        _fills = new MeshInstance3D[capacity];
        _shownGen = new int[capacity];
        _shownSite = new bool[capacity];
        _shownRise = new float[capacity];
        _shownBar = new BuildingBarKind[capacity];
        _shownFill = new float[capacity];
        _barLength = new float[capacity];
        _ghostBoxes = new MeshInstance3D?[capacity];
        _ghostGen = new int[capacity];
        _ghostCell = new int[capacity];
    }

    public override void _Process(double delta)
    {
        if (Runner?.Simulation is Simulation sim) Sync(sim.World);
    }

    /// <summary>Shows every live building and hides freed slots. No allocation once each slot has its nodes.</summary>
    public void Sync(World world)
    {
        BuildingStore b = world.Buildings;
        FogView? fog = Fog?.Refreshed(world);
        int n = Math.Min(b.Capacity, _roots.Length);
        for (int i = 0; i < n; i++)
        {
            // A slot gets its nodes when it first holds a live building, seen or not (as the unit views).
            if (b.Alive[i] && _roots[i] == null) CreateView(i).Visible = false;
            if (!b.Alive[i] || (fog != null && !fog.ShowsBuilding(i)))
            {
                if (_shownGen[i] != 0)
                {
                    _roots[i]!.Visible = false;
                    _shownGen[i] = 0;
                }
                continue;
            }
            if (_shownGen[i] != b.Generation[i]) Place(world, i);
            bool site = b.UnderConstruction[i];
            BuildingBarKind bar = BuildingBars.Of(b, world.Data.Buildings, i, out float fill);
            float rise = site ? Math.Max(SiteMinHeight, fill) : 1f;
            if (site != _shownSite[i])
            {
                _boxes[i].MaterialOverride = site ? _siteMat : _playerMats[Math.Clamp(b.Owner[i], 0, _playerMats.Length - 1)];
                _shownSite[i] = site;
            }
            if (rise != _shownRise[i])
            {
                // The box stands on the ground and grows upward.
                _boxes[i].Transform = new Transform3D(Basis.FromScale(new Vector3(1f, rise, 1f)), new Vector3(0f, BoxHeight * rise / 2f, 0f));
                _shownRise[i] = rise;
            }
            if (bar != _shownBar[i] || fill != _shownFill[i]) ShowBar(i, bar, fill);
        }
        SyncGhosts(world, fog);
    }

    /// <summary>Ghost boxes drawn now.</summary>
    public int GhostsShown { get; private set; }

    /// <summary>Ghost box nodes created so far (one per slot that ever had a ghost).</summary>
    public int GhostNodeCount { get; private set; }

    /// <summary>True while slot <paramref name="slot"/>'s ghost box is drawn.</summary>
    public bool IsGhostShown(int slot) => (uint)slot < (uint)_ghostGen.Length && _ghostGen[slot] != 0;

    /// <summary>The remembered generation slot <paramref name="slot"/>'s ghost box shows (0 when none).</summary>
    public int GhostGeneration(int slot) => (uint)slot < (uint)_ghostGen.Length ? _ghostGen[slot] : 0;

    /// <summary>The ghost box of slot <paramref name="slot"/>, or null if the slot never had a ghost.</summary>
    public MeshInstance3D? GhostBoxOf(int slot) => (uint)slot < (uint)_ghostBoxes.Length ? _ghostBoxes[slot] : null;

    /// <summary>The darkened material of player <paramref name="player"/>'s ghosts.</summary>
    public StandardMaterial3D GhostMaterial(int player) => _ghostMats[player];

    // The fog's ghost list onto the ghost pool: placed when an entry appears or changes, hidden when it goes.
    private void SyncGhosts(World world, FogView? fog)
    {
        int n = Math.Min(world.Buildings.Capacity, _ghostBoxes.Length), count = 0;
        for (int i = 0; i < n; i++)
        {
            if (fog == null || !fog.ShowsGhost(i))
            {
                if (_ghostGen[i] != 0)
                {
                    _ghostBoxes[i]!.Visible = false;
                    _ghostGen[i] = 0;
                }
                continue;
            }
            count++;
            Rts.Sim.Vision.BuildingGhost g = fog.Ghosts[i];
            if (_ghostGen[i] == g.Generation && _ghostCell[i] == g.Cell) continue;
            MeshInstance3D box = _ghostBoxes[i] ?? CreateGhost(i);
            BuildingDef def = world.Data.Buildings[g.TypeId];
            System.Numerics.Vector2 c = StartBase.FootprintCenter(world.NavGrid, def, g.Cell);
            box.Position = new Vector3(c.X, TerrainHeight.At(world.Heightmap, c.X, c.Y) + BoxHeight / 2f, c.Y);
            box.Mesh = _meshes[g.TypeId];
            box.MaterialOverride = _ghostMats[Math.Clamp(g.Owner, 0, _ghostMats.Length - 1)];
            box.Visible = true;
            _ghostGen[i] = g.Generation;
            _ghostCell[i] = g.Cell;
        }
        GhostsShown = count;
    }

    private MeshInstance3D CreateGhost(int slot)
    {
        var box = new MeshInstance3D { Name = $"Ghost{slot}" };
        AddChild(box);
        _ghostBoxes[slot] = box;
        GhostNodeCount++;
        return box;
    }

    /// <summary>The root node of building slot <paramref name="slot"/>, or null if the slot has never held a building.</summary>
    public Node3D? ViewOf(int slot) => (uint)slot < (uint)_roots.Length ? _roots[slot] : null;

    /// <summary>The box of slot <paramref name="slot"/> (exists once <see cref="ViewOf"/> does).</summary>
    public MeshInstance3D BoxOf(int slot) => _boxes[slot];

    /// <summary>The bar fill node of slot <paramref name="slot"/> (exists once <see cref="ViewOf"/> does).</summary>
    public MeshInstance3D BarFillOf(int slot) => _fills[slot];

    /// <summary>True while slot <paramref name="slot"/>'s view is shown (a live building the fog doesn't hide).</summary>
    public bool IsShown(int slot) => (uint)slot < (uint)_shownGen.Length && _shownGen[slot] != 0;

    /// <summary>The bar slot <paramref name="slot"/> shows, and its fill in [0, 1].</summary>
    public BuildingBarKind ShownBar(int slot, out float fill)
    {
        fill = _shownFill[slot];
        return _shownBar[slot];
    }

    /// <summary>True if slot <paramref name="slot"/> is drawn as a site (the site colour, rising).</summary>
    public bool ShownAsSite(int slot) => _shownSite[slot];

    /// <summary>The material for player <paramref name="player"/>'s finished buildings.</summary>
    public StandardMaterial3D PlayerMaterial(int player) => _playerMats[player];

    /// <summary>The material of construction sites.</summary>
    public StandardMaterial3D SiteMaterial => _siteMat;

    // A building (new, or new in a reused slot): mesh, position on its footprint centre, and fresh shown values.
    private void Place(World world, int i)
    {
        BuildingStore b = world.Buildings;
        Node3D root = _roots[i] ?? CreateView(i);
        BuildingDef def = world.Data.Buildings[b.TypeId[i]];
        NavGrid g = world.NavGrid;
        System.Numerics.Vector2 c = StartBase.FootprintCenter(g, def, b.Cell[i]);
        root.Position = new Vector3(c.X, TerrainHeight.At(world.Heightmap, c.X, c.Y), c.Y);
        _boxes[i].Mesh = _meshes[b.TypeId[i]];
        _barLength[i] = def.FootprintWidth * MapConstants.CellSize * BarLengthShare;
        _backs[i].Transform = new Transform3D(Basis.FromScale(new Vector3(_barLength[i], BarThickness * 0.8f, BarThickness * 0.8f)), new Vector3(0f, BarHeight, 0f));
        bool site = b.UnderConstruction[i];
        _boxes[i].MaterialOverride = site ? _siteMat : _playerMats[Math.Clamp(b.Owner[i], 0, _playerMats.Length - 1)];
        _shownSite[i] = site;
        _shownRise[i] = -1f; // forces the box transform
        _shownBar[i] = BuildingBarKind.None;
        _shownFill[i] = 0f;
        _backs[i].Visible = false;
        _fills[i].Visible = false;
        root.Visible = true;
        _shownGen[i] = b.Generation[i];
    }

    // The bar grows from its left end; hidden for None.
    private void ShowBar(int i, BuildingBarKind bar, float fill)
    {
        _shownBar[i] = bar;
        _shownFill[i] = fill;
        BarUpdates++;
        bool on = bar != BuildingBarKind.None;
        _backs[i].Visible = on;
        _fills[i].Visible = on && fill > 0f;
        if (!on) return;
        float len = _barLength[i], filled = len * fill;
        _fills[i].MaterialOverride = bar == BuildingBarKind.Progress ? _progressMat : _hpMat;
        _fills[i].Transform = new Transform3D(Basis.FromScale(new Vector3(Math.Max(filled, 1e-3f), BarThickness, BarThickness)),
            new Vector3(-len / 2f + filled / 2f, BarHeight, 0f));
    }

    private Node3D CreateView(int slot)
    {
        var root = new Node3D { Name = $"Building{slot}" };
        var box = new MeshInstance3D { Name = "Box" };
        var back = new MeshInstance3D { Name = "BarBack", Mesh = _barMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        var fill = new MeshInstance3D { Name = "BarFill", Mesh = _barMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        root.AddChild(box);
        root.AddChild(back);
        root.AddChild(fill);
        AddChild(root);
        _roots[slot] = root;
        _boxes[slot] = box;
        _backs[slot] = back;
        _fills[slot] = fill;
        NodeCount++;
        return root;
    }
}
