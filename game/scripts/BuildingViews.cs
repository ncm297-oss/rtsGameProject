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
/// <see cref="MapConstants.CellSize"/>) by <see cref="DrawnHeight"/> (<see cref="BoxHeight"/> x the type's data <c>scale</c>, M7-V4:
/// the scale is the height only, the footprint stays the box's width and depth), in the owner's faction colour; a construction site is
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
/// the slot has a ghost and hidden when the sim drops the entry or the building is seen again. A remembered site
/// (<c>BuildingGhost.Site</c>, M6-V1) draws as a foundation: the site's box at its lowest (<see cref="SiteMinHeight"/>; the
/// entry keeps no progress) in the site colour darkened the same way.
/// Models (M6-V26): with <see cref="UseModels"/> on, a finished building whose type has a model file (<see cref="BuildingModels"/>,
/// found by the type's id) draws the model, fitted to its footprint, in its owner's team material, and its box is hidden; a
/// site keeps the rising box and switches to the model the frame it is shown finished. Its bar sits
/// <see cref="BarHeight"/> - <see cref="BoxHeight"/> over the model's highest point (a site's over its box, as before). A
/// remembered building of such a type (not a site) draws the same model darkened (<see cref="BuildingModels.GhostMaterial"/>)
/// on its own per-slot node in place of the ghost box. Every other type, and every type with models off, is a box as before.
/// </remarks>
public partial class BuildingViews : Node3D
{
    /// <summary>Height of a finished scale-1 building's placeholder box, in meters (a type's own is <see cref="DrawnHeight"/>).</summary>
    public const float BoxHeight = 3f;

    /// <summary>Share of its drawn height (<see cref="DrawnHeight"/>) a site's box has at no progress (it rises to the full height at completion).</summary>
    public const float SiteMinHeight = 0.15f;

    /// <summary>Bar centre height above the ground of a scale-1 building, in meters (a type's own is <see cref="BarHeightOf"/>).</summary>
    public const float BarHeight = BoxHeight + 0.6f;

    /// <summary>
    /// The drawn height of a finished building of type <paramref name="def"/>: <see cref="BoxHeight"/> x its data <c>scale</c>
    /// (M7-V4, the unit rule of M7-V3c: the scale is the height only; the box keeps the footprint's width and depth).
    /// </summary>
    public static float DrawnHeight(BuildingDef def) => BoxHeight * def.Scale;

    /// <summary>The bar centre's height above the ground for type <paramref name="def"/>: <see cref="BarHeight"/> - <see cref="BoxHeight"/> over its drawn top.</summary>
    public static float BarHeightOf(BuildingDef def) => BarHeight + (DrawnHeight(def) - BoxHeight);

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

    /// <summary>
    /// Whether <see cref="Bind"/> draws a finished building whose type has a model file as that model (M6-V26); on unless the
    /// command line holds <see cref="UnitViews.PlaceholderArtFlag"/>. A scene that pins a finished building's box sets it false
    /// before binding.
    /// </summary>
    public static bool UseModels { get; set; } = Array.IndexOf(OS.GetCmdlineUserArgs(), UnitViews.PlaceholderArtFlag) < 0;

    /// <summary>The models of the last <see cref="Bind"/>, or null when <see cref="UseModels"/> was off.</summary>
    public BuildingModels? Models { get; private set; }

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
    // The drawn height of each slot's type (its finished box) and its bar's centre height, set when the building is placed.
    private float[] _boxHeight = Array.Empty<float>();
    private float[] _barY = Array.Empty<float>();
    private StandardMaterial3D[] _ghostMats = Array.Empty<StandardMaterial3D>();
    private MeshInstance3D?[] _ghostBoxes = Array.Empty<MeshInstance3D?>();
    // What each slot's ghost box shows now: the remembered generation (0: hidden) and anchor.
    private int[] _ghostGen = Array.Empty<int>();
    private int[] _ghostCell = Array.Empty<int>();
    private bool[] _ghostSite = Array.Empty<bool>();
    private StandardMaterial3D _ghostSiteMat = null!;
    // M6-V26: each slot's model node (made the first time the slot shows a finished model type) and whether it is drawn now;
    // the same for each slot's remembered model.
    private MeshInstance3D?[] _models = Array.Empty<MeshInstance3D?>();
    private bool[] _shownModel = Array.Empty<bool>();
    private MeshInstance3D?[] _ghostModels = Array.Empty<MeshInstance3D?>();
    private bool[] _ghostModel = Array.Empty<bool>();

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
            _meshes[t] = new BoxMesh { Size = new Vector3(def.FootprintWidth * MapConstants.CellSize, DrawnHeight(def), def.FootprintHeight * MapConstants.CellSize) };
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
        float e = FogView.ExploredBrightness;
        _ghostSiteMat = new StandardMaterial3D { AlbedoColor = new Color(SiteColor.R * e, SiteColor.G * e, SiteColor.B * e), Roughness = 0.95f };
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
        _boxHeight = new float[capacity];
        _barY = new float[capacity];
        _ghostBoxes = new MeshInstance3D?[capacity];
        _ghostGen = new int[capacity];
        _ghostCell = new int[capacity];
        _ghostSite = new bool[capacity];
        Models = UseModels ? new BuildingModels(data, playerRgb) : null;
        _models = new MeshInstance3D?[capacity];
        _shownModel = new bool[capacity];
        _ghostModels = new MeshInstance3D?[capacity];
        _ghostModel = new bool[capacity];
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
            if (site != _shownSite[i]) SetLook(world, i, site);
            if (rise != _shownRise[i])
            {
                // The box stands on the ground and grows upward.
                _boxes[i].Transform = new Transform3D(Basis.FromScale(new Vector3(1f, rise, 1f)), new Vector3(0f, _boxHeight[i] * rise / 2f, 0f));
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

    /// <summary>True while slot <paramref name="slot"/>'s ghost is drawn as a remembered site (a foundation, M6-V1).</summary>
    public bool GhostShownAsSite(int slot) => IsGhostShown(slot) && _ghostSite[slot];

    /// <summary>The darkened site material of remembered sites (any owner: sites are drawn in the site colour).</summary>
    public StandardMaterial3D GhostSiteMaterial => _ghostSiteMat;

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
                    if (_ghostModels[i] is MeshInstance3D gm) gm.Visible = false;
                    _ghostModel[i] = false;
                    _ghostGen[i] = 0;
                }
                continue;
            }
            count++;
            Rts.Sim.Vision.BuildingGhost g = fog.Ghosts[i];
            if (_ghostGen[i] == g.Generation && _ghostCell[i] == g.Cell && _ghostSite[i] == g.Site) continue;
            MeshInstance3D box = _ghostBoxes[i] ?? CreateGhost(i);
            BuildingDef def = world.Data.Buildings[g.TypeId];
            System.Numerics.Vector2 c = StartBase.FootprintCenter(world.NavGrid, def, g.Cell);
            float ground = TerrainHeight.At(world.Heightmap, c.X, c.Y);
            int owner = Math.Clamp(g.Owner, 0, _ghostMats.Length - 1);
            // M6-V26: a remembered finished building of a model type is its model, darkened; a remembered site stays a foundation box.
            bool model = !g.Site && Models != null && Models.Has(g.TypeId);
            if (model)
            {
                MeshInstance3D m = _ghostModels[i] ?? CreateGhostModel(i);
                m.Mesh = Models!.MeshOf(g.TypeId);
                m.Transform = new Transform3D(Basis.Identity, new Vector3(c.X, ground, c.Y)) * Models.Fit(g.TypeId);
                m.MaterialOverride = Models.GhostMaterial(g.TypeId, owner);
                m.Visible = true;
            }
            else if (_ghostModels[i] is MeshInstance3D old) old.Visible = false;
            _ghostModel[i] = model;
            // A remembered site stands as a foundation, the box at its lowest, growing upward from the ground as a live site's.
            float rise = g.Site ? SiteMinHeight : 1f;
            box.Transform = new Transform3D(Basis.FromScale(new Vector3(1f, rise, 1f)),
                new Vector3(c.X, ground + DrawnHeight(def) * rise / 2f, c.Y));
            box.Mesh = _meshes[g.TypeId];
            box.MaterialOverride = g.Site ? _ghostSiteMat : _ghostMats[owner];
            box.Visible = !model;
            _ghostGen[i] = g.Generation;
            _ghostCell[i] = g.Cell;
            _ghostSite[i] = g.Site;
        }
        GhostsShown = count;
    }

    private MeshInstance3D CreateGhostModel(int slot)
    {
        var m = new MeshInstance3D { Name = $"GhostModel{slot}" };
        AddChild(m);
        _ghostModels[slot] = m;
        return m;
    }

    private MeshInstance3D CreateGhost(int slot)
    {
        var box = new MeshInstance3D { Name = $"Ghost{slot}" };
        AddChild(box);
        _ghostBoxes[slot] = box;
        GhostNodeCount++;
        return box;
    }

    /// <summary>True while slot <paramref name="slot"/>'s ghost is drawn as its type's model, darkened (M6-V26).</summary>
    public bool GhostShownAsModel(int slot) => IsGhostShown(slot) && _ghostModel[slot];

    /// <summary>The remembered model node of slot <paramref name="slot"/>, or null if the slot never had one (M6-V26).</summary>
    public MeshInstance3D? GhostModelOf(int slot) => (uint)slot < (uint)_ghostModels.Length ? _ghostModels[slot] : null;

    /// <summary>True while slot <paramref name="slot"/> is drawn as its type's model (a finished building of a model type, M6-V26).</summary>
    public bool ShownAsModel(int slot) => IsShown(slot) && _shownModel[slot];

    /// <summary>The model node of slot <paramref name="slot"/>, or null if the slot never showed a model (M6-V26).</summary>
    public MeshInstance3D? ModelOf(int slot) => (uint)slot < (uint)_models.Length ? _models[slot] : null;

    /// <summary>The height of slot <paramref name="slot"/>'s bar centre above its ground, in meters, as drawn now.</summary>
    public float BarY(int slot) => _barY[slot];

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
        _boxHeight[i] = DrawnHeight(def);
        _barY[i] = float.NaN; // SetLook places the bar
        SetLook(world, i, b.UnderConstruction[i]);
        _shownRise[i] = -1f; // forces the box transform
        _shownBar[i] = BuildingBarKind.None;
        _shownFill[i] = 0f;
        _backs[i].Visible = false;
        _fills[i].Visible = false;
        root.Visible = true;
        _shownGen[i] = b.Generation[i];
    }

    // Site or finished: the box's material, the model shown or not (M6-V26), and the bar over what is drawn (a site's box,
    // or the model's top). A bar moved is shown again on the next check.
    private void SetLook(World world, int i, bool site)
    {
        BuildingStore b = world.Buildings;
        int type = b.TypeId[i], owner = Math.Clamp(b.Owner[i], 0, _playerMats.Length - 1);
        bool model = !site && Models != null && Models.Has(type);
        _boxes[i].MaterialOverride = site ? _siteMat : _playerMats[owner];
        _boxes[i].Visible = !model;
        if (model)
        {
            MeshInstance3D m = _models[i] ?? CreateModel(i);
            m.Mesh = Models!.MeshOf(type);
            m.Transform = Models.Fit(type);
            m.MaterialOverride = Models.TeamMaterial(type, owner);
            m.Visible = true;
        }
        else if (_models[i] is MeshInstance3D old) old.Visible = false;
        _shownModel[i] = model;
        _shownSite[i] = site;
        float barY = model ? Models!.Top(type) + (BarHeight - BoxHeight) : BarHeightOf(world.Data.Buildings[type]);
        if (barY != _barY[i])
        {
            _barY[i] = barY;
            _backs[i].Transform = new Transform3D(Basis.FromScale(new Vector3(_barLength[i], BarThickness * 0.8f, BarThickness * 0.8f)), new Vector3(0f, barY, 0f));
            _shownFill[i] = -1f; // the fill follows on this frame's bar check
        }
    }

    private MeshInstance3D CreateModel(int slot)
    {
        var m = new MeshInstance3D { Name = "Model" };
        _roots[slot]!.AddChild(m);
        _models[slot] = m;
        return m;
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
            new Vector3(-len / 2f + filled / 2f, _barY[i], 0f));
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
