using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>
/// The selected units' queued orders (M6-V22): for every selected unit the local player owns, a thin line from the unit
/// through its order points (<see cref="OrderPath"/>: its current order's place, then each queue entry) and a small marker
/// at each queued point, coloured by order kind (<see cref="ColorOf"/>).
/// </summary>
/// <remarks>
/// Rebuilt from the sim every frame, never the source of truth. An entry with no place (Stop, Hold, a no-target ability)
/// is a ring round the previous point (M6-VH8, BUG-1470: a flat torus whose hole is wider than a marker, in its own
/// <see cref="MultiMesh"/> drawn before the markers, so the marker inside stays visible); every other ring at the same
/// point, from this unit or another selected one (M6-V23, BUG-1500), is the next ring out. Markers, rings and lines grow
/// with the camera's zoom past <see cref="MarkerFullZoom"/> and <see cref="LineFullZoom"/> (M6-VH8), so they still read at
/// the farthest zoom. An Attack entry is drawn on its
/// target only while the local player's fog shows it (a building's remembered ghost counts, as for
/// <see cref="TargetRing"/>): a hidden or dead target gets no marker and no line to it, and the path goes on from the last
/// drawn point. Lines, rings and markers are drawn over the terrain (no depth test), so a leg up a cliff stays visible.
/// Units sharing a queued point share its marker, and shared legs share their line (200 units on one path draw 8 markers,
/// not 1,600). Three <see cref="MultiMesh"/>es (lines, rings, markers, drawn in that order), one draw call each; buffers
/// grow while warming up and are reused, so a steady frame allocates nothing. The shown instances are also kept in managed
/// arrays for tests (the headless dummy renderer keeps no instance data). Holds view state only.
/// </remarks>
public partial class OrderPathView : Node3D
{
    /// <summary>The line's width (the rally line's), its thickness, and its lift above the ground, in meters.</summary>
    public const float LineWidth = 0.12f, LineThickness = 0.04f, LineLift = 0.15f;

    /// <summary>A marker's radius, its height and its lift above the ground, in meters.</summary>
    public const float MarkerRadius = 0.5f, MarkerHeight = 0.1f, MarkerLift = 0.18f;

    /// <summary>A no-place entry's ring (Stop, Hold, a no-target cast): its outer radius over <see cref="MarkerRadius"/>, and its lift.</summary>
    public const float NoPlaceScale = 1.8f, NoPlaceLift = 0.12f;

    /// <summary>
    /// A ring's hole over its outer radius (M6-VH8, BUG-1470): 0.7 x 0.9 m = 0.63 m, wider than the 0.5 m marker inside, so a
    /// ring never covers its marker. The next ring out at the same point is 1 / <see cref="RingInner"/> wider, so it starts
    /// where the one inside ends.
    /// </summary>
    public const float RingInner = 0.7f;

    /// <summary>Rings nest at most this many steps out at one point (a fourth ring there, from any selected unit, is drawn at the third's radius).</summary>
    public const int MaxRingLevel = 2;

    /// <summary>
    /// Zoom (camera height, m) up to which markers and rings keep their size; above it they grow in proportion (the cargo
    /// cube's and the hp bars' rule, M6-VH8): at the farthest zoom (60 m) a marker is 1 m in radius, 2 m wide.
    /// </summary>
    public const float MarkerFullZoom = UnitViews.CargoFullZoom;

    /// <summary>Zoom up to which the lines keep <see cref="LineWidth"/>; above it they widen in proportion, so a line is as wide on screen at 60 m as at 20 m.</summary>
    public const float LineFullZoom = CameraLimits.MinZoom;

    /// <summary>The render priorities of the lines, the rings and the markers: drawn in this order, each over the last (no depth test).</summary>
    public const int LinePriority = 1, RingPriority = 2, MarkerPriority = 3;

    /// <summary>The size factor at camera zoom <paramref name="zoom"/> for a size kept up to <paramref name="fullZoom"/>: 1, or zoom / fullZoom above it (1 for no zoom).</summary>
    public static float ZoomScale(float zoom, float fullZoom) => float.IsFinite(zoom) ? Math.Max(1f, zoom / fullZoom) : 1f;

    /// <summary>The outer radius (m, at size factor 1) of the ring <paramref name="level"/> steps out from its point (0: the first ring).</summary>
    public static float RingRadius(int level) => MarkerRadius * NoPlaceScale / MathF.Pow(RingInner, Math.Clamp(level, 0, MaxRingLevel));

    /// <summary>Points closer than this (meters) share a marker; legs whose ends are this close share a line.</summary>
    public const float ShareDistance = 0.01f;

    // UI tints, not team colours. Attack is the Attack order's ring colour (TargetRing); a cast is the cast bar's (AbilityViews).
    /// <summary>A Move's colour.</summary>
    public static readonly Color MoveColor = new(0.35f, 1f, 0.45f);
    /// <summary>An AttackMove's colour.</summary>
    public static readonly Color AttackMoveColor = new(1f, 0.55f, 0.12f);
    /// <summary>A Gather's colour.</summary>
    public static readonly Color GatherColor = new(1f, 0.85f, 0.25f);
    /// <summary>A Build's colour.</summary>
    public static readonly Color BuildColor = new(0.3f, 0.65f, 1f);
    /// <summary>A Repair's colour.</summary>
    public static readonly Color RepairColor = new(0.3f, 0.95f, 0.95f);
    /// <summary>A Stop's colour.</summary>
    public static readonly Color StopColor = new(0.92f, 0.92f, 0.88f);
    /// <summary>A HoldPosition's colour.</summary>
    public static readonly Color HoldColor = new(1f, 0.95f, 0.55f);

    /// <summary>The colour of order kind <paramref name="kind"/>'s line and marker.</summary>
    public static Color ColorOf(CommandKind kind) => kind switch
    {
        CommandKind.Move => MoveColor,
        CommandKind.AttackMove => AttackMoveColor,
        CommandKind.Attack => TargetRing.RingColor,
        CommandKind.Gather => GatherColor,
        CommandKind.Build => BuildColor,
        CommandKind.Repair => RepairColor,
        CommandKind.UseAbility => AbilityViews.BarFillColor,
        CommandKind.Stop => StopColor,
        CommandKind.HoldPosition => HoldColor,
        _ => StopColor,
    };

    private const int StartCapacity = 64;
    private static readonly Color Unset = new(-1f, -1f, -1f, -1f);
    private static readonly Vector3 NoPoint = new(float.NaN, float.NaN, float.NaN);

    private SimRunner? _runner;
    private SelectionController? _sel;
    private MultiMeshInstance3D _linesNode = null!, _ringsNode = null!, _markersNode = null!;
    private MultiMesh _lines = null!, _rings = null!, _markers = null!;
    private TorusMesh _ringMesh = null!;
    private readonly OrderPoint[] _path = new OrderPoint[OrderPath.MaxPoints];
    private OrderPath? _reader;
    private World? _readerOf;
    private readonly Dictionary<PathKey, int> _markerAt = new(), _segmentAt = new();
    // Per point (1 cm) this frame: rings already drawn round it, over every selected unit (BUG-1500 item 1).
    private readonly Dictionary<PathKey, int> _ringsAt = new();

    // Per marker / segment drawn this frame (index < count), pooled.
    private Vector3[] _mPos = new Vector3[StartCapacity];
    private CommandKind[] _mKind = new CommandKind[StartCapacity];
    private bool[] _mNoPlace = new bool[StartCapacity];
    private int[] _mLevel = new int[StartCapacity];
    private Vector3[] _sFrom = new Vector3[StartCapacity], _sTo = new Vector3[StartCapacity];
    private CommandKind[] _sKind = new CommandKind[StartCapacity];
    private Color[] _sShown = new Color[StartCapacity];
    // Per disc / ring instance (its index in its own multimesh): what its transform was last written from, so a frame
    // rewrites only what moved (engine calls cost), and the colour it was last given.
    private Vector3[] _dWritten = new Vector3[StartCapacity], _rWritten = new Vector3[StartCapacity];
    private int[] _rWrittenLevel = new int[StartCapacity];
    private Color[] _dShown = new Color[StartCapacity], _rShown = new Color[StartCapacity];
    private Vector3[] _sWrittenFrom = new Vector3[StartCapacity], _sWrittenTo = new Vector3[StartCapacity];
    // The size factors the written transforms used (a zoom change rewrites them all).
    private float _writtenMarkerZoom = 1f, _writtenLineZoom = 1f;

    /// <summary>The fog whose rule hides Attack targets (M4-V4); null shows every live target.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>The camera whose zoom sizes the drawing (M6-VH8); null keeps the base sizes.</summary>
    public RtsCamera? Camera { get; set; }

    /// <summary>The player whose selected units are drawn (the local seat).</summary>
    public int LocalPlayer { get; set; } = SelectionController.LocalPlayer;

    /// <summary>Markers drawn this frame (discs and rings).</summary>
    public int MarkerCount { get; private set; }

    /// <summary>Line segments drawn this frame.</summary>
    public int SegmentCount { get; private set; }

    /// <summary>Times a buffer grew (warm-up only).</summary>
    public int Grows { get; private set; }

    /// <summary>Marker <paramref name="k"/>'s world point (on the ground, before the lift).</summary>
    public Vector3 MarkerPoint(int k) => _mPos[k];

    /// <summary>Marker <paramref name="k"/>'s order kind.</summary>
    public CommandKind MarkerKind(int k) => _mKind[k];

    /// <summary>True when marker <paramref name="k"/> is an entry with no place of its own (a ring round the previous point).</summary>
    public bool MarkerNoPlace(int k) => _mNoPlace[k];

    /// <summary>How many rings out from its point marker <paramref name="k"/> is drawn (0 for a disc or the first ring; M6-VH8).</summary>
    public int MarkerLevel(int k) => _mLevel[k];

    /// <summary>Marker <paramref name="k"/>'s drawn outer radius in meters this frame: the disc's, or its ring's, at the zoom.</summary>
    public float MarkerDrawnRadius(int k) => (_mNoPlace[k] ? RingRadius(_mLevel[k]) : MarkerRadius) * MarkerZoomScale;

    /// <summary>The size factor markers and rings were drawn at this frame (<see cref="ZoomScale"/> of the camera's zoom over <see cref="MarkerFullZoom"/>).</summary>
    public float MarkerZoomScale { get; private set; } = 1f;

    /// <summary>The size factor the lines were drawn at this frame (over <see cref="LineFullZoom"/>).</summary>
    public float LineZoomScale { get; private set; } = 1f;

    /// <summary>A line's drawn width in meters this frame.</summary>
    public float LineDrawnWidth => LineWidth * LineZoomScale;

    /// <summary>Segment <paramref name="k"/>'s ends (on the ground, before the lift).</summary>
    public (Vector3 From, Vector3 To) Segment(int k) => (_sFrom[k], _sTo[k]);

    /// <summary>Segment <paramref name="k"/>'s order kind (the kind of the point it leads to).</summary>
    public CommandKind SegmentKind(int k) => _sKind[k];

    /// <summary>The line, ring and marker nodes (one draw call each).</summary>
    public MultiMeshInstance3D LinesNode => _linesNode;

    /// <inheritdoc cref="LinesNode"/>
    public MultiMeshInstance3D RingsNode => _ringsNode;

    /// <inheritdoc cref="LinesNode"/>
    public MultiMeshInstance3D MarkersNode => _markersNode;

    /// <summary>The mesh every no-place entry's ring is drawn with: a torus of outer radius 1 and hole <see cref="RingInner"/>.</summary>
    public TorusMesh RingMesh => _ringMesh;

    public override void _Ready()
    {
        // Drawn in render-priority order (no depth test): lines, then rings, then markers on top. One multimesh for discs and
        // rings drew a Stop's disc over the marker it surrounds (BUG-1470).
        _lines = NewMulti(new BoxMesh { Size = Vector3.One }, LinePriority);
        _ringMesh = new TorusMesh { InnerRadius = RingInner, OuterRadius = 1f, Rings = 24, RingSegments = 6 };
        _rings = NewMulti(_ringMesh, RingPriority);
        _markers = NewMulti(new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 16, Rings = 1 }, MarkerPriority);
        _linesNode = new MultiMeshInstance3D { Name = "Lines", Multimesh = _lines, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _ringsNode = new MultiMeshInstance3D { Name = "Rings", Multimesh = _rings, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _markersNode = new MultiMeshInstance3D { Name = "Markers", Multimesh = _markers, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_linesNode);
        AddChild(_ringsNode);
        AddChild(_markersNode);
        Array.Fill(_dShown, Unset);
        Array.Fill(_rShown, Unset);
        Array.Fill(_sShown, Unset);
        Array.Fill(_dWritten, NoPoint);
        Array.Fill(_rWritten, NoPoint);
        Array.Fill(_sWrittenFrom, NoPoint);
    }

    private static MultiMesh NewMulti(PrimitiveMesh mesh, int priority)
    {
        // Drawn over the terrain: a straight leg up or down a cliff would otherwise vanish into the slope (waypoints read
        // as an overlay, as in other RTS games).
        mesh.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White,
            NoDepthTest = true, RenderPriority = priority,
        };
        return new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh,
            InstanceCount = StartCapacity, VisibleInstanceCount = 0,
        };
    }

    /// <summary>A reader of <paramref name="world"/>'s unit orders (<see cref="OrderPath"/>), over the unit store's own arrays: build once per world.</summary>
    public static OrderPath NewReader(World world)
    {
        UnitStore u = world.Units;
        return new OrderPath(world.Data, world.NavGrid, world.Buildings,
            u.Alive, u.Generation, u.TypeId, u.Position, u.State, u.Goal, u.Mode, u.AnchorPosition, u.Target, u.TargetIsBuilding,
            u.CastAbility, u.CastPoint, u.BuildTarget, u.GatherNode, u.GatherSite, u.QueueCount, u.QueueKind, u.QueuePosition, u.QueueTypeId);
    }

    /// <summary>Connects the drawing to the match; call once after the sim exists.</summary>
    public void Init(SimRunner runner, SelectionController selection)
    {
        _runner = runner;
        _sel = selection;
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha);
    }

    /// <summary>Rebuilds the drawing for this frame from the selection and the sim at render blend <paramref name="alpha"/>. Allocation-free once warm.</summary>
    public void Sync(World world, float alpha)
    {
        MarkerCount = 0;
        SegmentCount = 0;
        _markerAt.Clear();
        _segmentAt.Clear();
        _ringsAt.Clear();
        if (_sel != null)
        {
            FogView? fog = Fog?.Refreshed(world);
            UnitStore u = world.Units;
            if (!ReferenceEquals(_readerOf, world))
            {
                _reader = NewReader(world); // once per match (a load or a replay brings a new world)
                _readerOf = world;
            }
            foreach (EntityHandle h in _sel.Selection.Items)
            {
                if (!u.IsAlive(h) || u.Owner[h.Index] != LocalPlayer) continue;
                int n = _reader!.Fill(h, _path);
                if (n == 0) continue;
                Vector3 prev = UnitViews.GroundPoint(world, h.Index, alpha);
                int run = 0; // the first entry after the last drawn point: the no-place entries from here ring prev
                for (int k = 0; k < n; k++)
                {
                    ref OrderPoint p = ref _path[k];
                    if (!p.HasPlace)
                    {
                        if (!p.IsCurrent) AddRing(p.Kind, prev, Occurrence(run, k));
                        continue;
                    }
                    if (!Resolve(world, fog, in p, alpha, out Vector3 at)) continue;
                    AddSegment(prev, at, p.Kind);
                    if (!p.IsCurrent) AddMarker(new PathKey(Q(at.X), Q(at.Z), 0, 0, (int)p.Kind * 2), p.Kind, at, noPlace: false, 0);
                    prev = at;
                    run = k + 1;
                }
            }
        }
        Write();
    }

    // Where point p is drawn; false for an Attack whose target is dead or hidden from the local player (no leak).
    private static bool Resolve(World world, FogView? fog, in OrderPoint p, float alpha, out Vector3 at)
    {
        at = default;
        if (p.Kind != CommandKind.Attack)
        {
            at = Ground(world, p.Point);
            return true;
        }
        if (p.Target.Generation == 0) return false; // no target: nothing to draw (never at the origin)
        int slot = p.Target.Index;
        if (p.TargetIsBuilding)
        {
            if (p.TargetLive && (fog == null || fog.ShowsBuilding(slot)))
            {
                at = Ground(world, p.Point);
                return true;
            }
            // A building the fog draws as a ghost: on its remembered footprint, also once it is gone (the player can't know).
            if (fog != null && fog.ShowsGhost(slot) && fog.Ghosts[slot].Generation == p.Target.Generation)
            {
                at = Ground(world, SelectionController.GhostCenter(world, fog, slot));
                return true;
            }
            return false;
        }
        if (!p.TargetLive || (fog != null && !fog.ShowsUnit(slot))) return false;
        at = UnitViews.GroundPoint(world, slot, alpha);
        return true;
    }

    private static Vector3 Ground(World world, System.Numerics.Vector2 p) => new(p.X, TerrainHeight.At(world.Heightmap, p.X, p.Y), p.Y);

    private static int Q(float v) => (int)MathF.Round(v / ShareDistance);

    // How many queued no-place entries of entry k's kind come before it in this unit's run [run, k) round one point: its
    // Stop is the first Stop there, a second Stop the second, and so on.
    private int Occurrence(int run, int k)
    {
        int seen = 0;
        for (int j = run; j < k; j++)
            if (!_path[j].HasPlace && !_path[j].IsCurrent && _path[j].Kind == _path[k].Kind) seen++;
        return seen;
    }

    // A no-place entry's ring round `at` (BUG-1500 item 1): rings are told apart per point by (kind, how many of that kind
    // the unit has there before it), so units with the same end orders share their rings, while another unit's other kind
    // at the point is a new ring. Each new ring at a point takes the next level out, counted per point across every
    // selected unit (one unit's Stop and another's Hold nest), up to MaxRingLevel.
    private void AddRing(CommandKind kind, Vector3 at, int occurrence)
    {
        int qx = Q(at.X), qz = Q(at.Z);
        var key = new PathKey(qx, qz, occurrence, 1, (int)kind * 2 + 1);
        if (_markerAt.ContainsKey(key)) return;
        var point = new PathKey(qx, qz, 0, 0, -1);
        _ringsAt.TryGetValue(point, out int used);
        _ringsAt[point] = used + 1;
        AddMarker(key, kind, at, noPlace: true, Math.Min(used, MaxRingLevel));
    }

    private void AddMarker(PathKey key, CommandKind kind, Vector3 at, bool noPlace, int level)
    {
        int k = MarkerCount;
        if (!_markerAt.TryAdd(key, k)) return;
        if (k >= _mPos.Length) GrowMarkers();
        _mPos[k] = at;
        _mKind[k] = kind;
        _mNoPlace[k] = noPlace;
        _mLevel[k] = level;
        MarkerCount = k + 1;
    }

    private void AddSegment(Vector3 from, Vector3 to, CommandKind kind)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        if (dx * dx + dz * dz < 0.0025f) return; // under 5 cm: nothing to draw (and no direction to look along)
        var key = new PathKey(Q(from.X), Q(from.Z), Q(to.X), Q(to.Z), (int)kind);
        int k = SegmentCount;
        if (!_segmentAt.TryAdd(key, k)) return;
        if (k >= _sFrom.Length) GrowSegments();
        _sFrom[k] = from;
        _sTo[k] = to;
        _sKind[k] = kind;
        SegmentCount = k + 1;
    }

    private void GrowMarkers()
    {
        int size = _mPos.Length * 2;
        Array.Resize(ref _mPos, size);
        Array.Resize(ref _mKind, size);
        Array.Resize(ref _mNoPlace, size);
        Array.Resize(ref _mLevel, size);
        // Both instance buffers hold one instance per marker slot; a new buffer holds no colours or transforms.
        _dShown = new Color[size];
        _rShown = new Color[size];
        Array.Fill(_dShown, Unset);
        Array.Fill(_rShown, Unset);
        _dWritten = new Vector3[size];
        _rWritten = new Vector3[size];
        Array.Fill(_dWritten, NoPoint);
        Array.Fill(_rWritten, NoPoint);
        _rWrittenLevel = new int[size];
        _markers.InstanceCount = size;
        _rings.InstanceCount = size;
        Grows++;
    }

    private void GrowSegments()
    {
        int size = _sFrom.Length * 2;
        Array.Resize(ref _sFrom, size);
        Array.Resize(ref _sTo, size);
        Array.Resize(ref _sKind, size);
        _sShown = new Color[size];
        Array.Fill(_sShown, Unset);
        _sWrittenFrom = new Vector3[size];
        _sWrittenTo = new Vector3[size];
        Array.Fill(_sWrittenFrom, NoPoint);
        _lines.InstanceCount = size;
        Grows++;
    }

    // Writes this frame's instances: transforms only for what moved (units and targets move) or when the zoom changed the
    // sizes, colours only when they change. Discs go to the marker multimesh, no-place entries to the ring multimesh.
    private void Write()
    {
        float zoom = Camera?.Zoom ?? float.NaN;
        float zm = ZoomScale(zoom, MarkerFullZoom), zl = ZoomScale(zoom, LineFullZoom);
        MarkerZoomScale = zm;
        LineZoomScale = zl;
        if (zm != _writtenMarkerZoom)
        {
            Array.Fill(_dWritten, NoPoint);
            Array.Fill(_rWritten, NoPoint);
            _writtenMarkerZoom = zm;
        }
        if (zl != _writtenLineZoom)
        {
            Array.Fill(_sWrittenFrom, NoPoint);
            _writtenLineZoom = zl;
        }
        int discs = 0, rings = 0;
        for (int k = 0; k < MarkerCount; k++)
        {
            Color c = ColorOf(_mKind[k]);
            if (_mNoPlace[k])
            {
                int r = rings++;
                int level = _mLevel[k];
                if (_rWritten[r] != _mPos[k] || _rWrittenLevel[r] != level)
                {
                    float radius = RingRadius(level) * zm, height = MarkerHeight * zm;
                    // The torus's tube is (1 - RingInner) tall at scale 1: flattened to the marker's height.
                    Vector3 at = _mPos[k] + new Vector3(0f, NoPlaceLift + height / 2f, 0f);
                    _rings.SetInstanceTransform(r, new Transform3D(Basis.FromScale(new Vector3(radius, height / (1f - RingInner), radius)), at));
                    _rWritten[r] = _mPos[k];
                    _rWrittenLevel[r] = level;
                }
                if (_rShown[r] != c)
                {
                    _rings.SetInstanceColor(r, c);
                    _rShown[r] = c;
                }
                continue;
            }
            int d = discs++;
            if (_dWritten[d] != _mPos[k])
            {
                float radius = MarkerRadius * zm, height = MarkerHeight * zm;
                Vector3 at = _mPos[k] + new Vector3(0f, MarkerLift + height / 2f, 0f);
                _markers.SetInstanceTransform(d, new Transform3D(Basis.FromScale(new Vector3(radius, height, radius)), at));
                _dWritten[d] = _mPos[k];
            }
            if (_dShown[d] != c)
            {
                _markers.SetInstanceColor(d, c);
                _dShown[d] = c;
            }
        }
        _markers.VisibleInstanceCount = discs;
        _rings.VisibleInstanceCount = rings;
        var lift = new Vector3(0f, LineLift, 0f);
        for (int k = 0; k < SegmentCount; k++)
        {
            if (_sWrittenFrom[k] != _sFrom[k] || _sWrittenTo[k] != _sTo[k])
            {
                Vector3 a = _sFrom[k] + lift, b = _sTo[k] + lift;
                Vector3 dir = b - a;
                float length = dir.Length();
                // The box's local Z runs along the leg, scaled to its length (as the rally line).
                Basis basis = Basis.LookingAt(dir / length, Vector3.Up) * Basis.FromScale(new Vector3(LineWidth * zl, LineThickness * zl, length));
                _lines.SetInstanceTransform(k, new Transform3D(basis, (a + b) / 2f));
                _sWrittenFrom[k] = _sFrom[k];
                _sWrittenTo[k] = _sTo[k];
            }
            Color c = ColorOf(_sKind[k]);
            if (_sShown[k] != c)
            {
                _lines.SetInstanceColor(k, c);
                _sShown[k] = c;
            }
        }
        _lines.VisibleInstanceCount = SegmentCount;
    }

    // A disc's (x, z, 0, 0, kind code), a ring's (x, z, occurrence, 1, kind code), a ring point's (x, z, 0, 0, -1) or a
    // leg's (x1, z1, x2, z2, kind) in 1 cm steps.
    private readonly struct PathKey : IEquatable<PathKey>
    {
        private readonly int _a, _b, _c, _d, _e;

        public PathKey(int a, int b, int c, int d, int e) => (_a, _b, _c, _d, _e) = (a, b, c, d, e);

        public bool Equals(PathKey o) => _a == o._a && _b == o._b && _c == o._c && _d == o._d && _e == o._e;

        public override bool Equals(object? obj) => obj is PathKey o && Equals(o);

        public override int GetHashCode() => unchecked(((((_a * 397) ^ _b) * 397 ^ _c) * 397 ^ _d) * 397 ^ _e);
    }
}
