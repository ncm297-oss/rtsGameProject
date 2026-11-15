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
/// is a wider marker at the previous point. An Attack entry is drawn on its target only while the local player's fog
/// shows it (a building's remembered ghost counts, as for <see cref="TargetRing"/>): a hidden or dead target gets no
/// marker and no line to it, and the path goes on from the last drawn point. Lines and markers are drawn over the
/// terrain (no depth test), so a leg up a cliff stays visible. Units sharing a queued point share its
/// marker, and shared legs share their line (200 units on one path draw 8 markers, not 1,600). Two
/// <see cref="MultiMesh"/>es (lines, markers), one draw call each; buffers grow while warming up and are reused, so a
/// steady frame allocates nothing. The shown instances are also kept in managed arrays for tests (the headless dummy
/// renderer keeps no instance data). Holds view state only.
/// </remarks>
public partial class OrderPathView : Node3D
{
    /// <summary>The line's width (the rally line's), its thickness, and its lift above the ground, in meters.</summary>
    public const float LineWidth = 0.12f, LineThickness = 0.04f, LineLift = 0.15f;

    /// <summary>A marker's radius, its height and its lift above the ground, in meters.</summary>
    public const float MarkerRadius = 0.5f, MarkerHeight = 0.1f, MarkerLift = 0.18f;

    /// <summary>A no-place marker (Stop, Hold, a no-target cast) is this much wider and sits lower, a ring round the point's own marker.</summary>
    public const float NoPlaceScale = 1.8f, NoPlaceLift = 0.12f;

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
    private MultiMeshInstance3D _linesNode = null!, _markersNode = null!;
    private MultiMesh _lines = null!, _markers = null!;
    private readonly OrderPoint[] _path = new OrderPoint[OrderPath.MaxPoints];
    private OrderPath? _reader;
    private World? _readerOf;
    private readonly Dictionary<PathKey, int> _markerAt = new(), _segmentAt = new();

    // Per marker / segment drawn this frame (index < count), pooled; the colour each instance was last given.
    private Vector3[] _mPos = new Vector3[StartCapacity];
    private CommandKind[] _mKind = new CommandKind[StartCapacity];
    private bool[] _mNoPlace = new bool[StartCapacity];
    private Color[] _mShown = new Color[StartCapacity];
    private Vector3[] _sFrom = new Vector3[StartCapacity], _sTo = new Vector3[StartCapacity];
    private CommandKind[] _sKind = new CommandKind[StartCapacity];
    private Color[] _sShown = new Color[StartCapacity];
    // What each instance's transform was last written from, so a frame rewrites only what moved (engine calls cost).
    private Vector3[] _mWritten = new Vector3[StartCapacity], _sWrittenFrom = new Vector3[StartCapacity], _sWrittenTo = new Vector3[StartCapacity];
    private bool[] _mWrittenRing = new bool[StartCapacity];

    /// <summary>The fog whose rule hides Attack targets (M4-V4); null shows every live target.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>The player whose selected units are drawn (the local seat).</summary>
    public int LocalPlayer { get; set; } = SelectionController.LocalPlayer;

    /// <summary>Markers drawn this frame.</summary>
    public int MarkerCount { get; private set; }

    /// <summary>Line segments drawn this frame.</summary>
    public int SegmentCount { get; private set; }

    /// <summary>Times a buffer grew (warm-up only).</summary>
    public int Grows { get; private set; }

    /// <summary>Marker <paramref name="k"/>'s world point (on the ground, before the lift).</summary>
    public Vector3 MarkerPoint(int k) => _mPos[k];

    /// <summary>Marker <paramref name="k"/>'s order kind.</summary>
    public CommandKind MarkerKind(int k) => _mKind[k];

    /// <summary>True when marker <paramref name="k"/> is an entry with no place of its own (drawn at the previous point).</summary>
    public bool MarkerNoPlace(int k) => _mNoPlace[k];

    /// <summary>Segment <paramref name="k"/>'s ends (on the ground, before the lift).</summary>
    public (Vector3 From, Vector3 To) Segment(int k) => (_sFrom[k], _sTo[k]);

    /// <summary>Segment <paramref name="k"/>'s order kind (the kind of the point it leads to).</summary>
    public CommandKind SegmentKind(int k) => _sKind[k];

    /// <summary>The line and marker nodes (one draw call each).</summary>
    public MultiMeshInstance3D LinesNode => _linesNode;

    /// <inheritdoc cref="LinesNode"/>
    public MultiMeshInstance3D MarkersNode => _markersNode;

    public override void _Ready()
    {
        _lines = NewMulti(new BoxMesh { Size = Vector3.One });
        _markers = NewMulti(new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 16, Rings = 1 });
        _linesNode = new MultiMeshInstance3D { Name = "Lines", Multimesh = _lines, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _markersNode = new MultiMeshInstance3D { Name = "Markers", Multimesh = _markers, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_linesNode);
        AddChild(_markersNode);
        Array.Fill(_mShown, Unset);
        Array.Fill(_sShown, Unset);
        Array.Fill(_mWritten, NoPoint);
        Array.Fill(_sWrittenFrom, NoPoint);
    }

    private static MultiMesh NewMulti(PrimitiveMesh mesh)
    {
        // Drawn over the terrain: a straight leg up or down a cliff would otherwise vanish into the slope (waypoints read
        // as an overlay, as in other RTS games).
        mesh.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White,
            NoDepthTest = true, RenderPriority = 1,
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
                for (int k = 0; k < n; k++)
                {
                    ref OrderPoint p = ref _path[k];
                    if (!p.HasPlace)
                    {
                        if (!p.IsCurrent) AddMarker(p.Kind, prev, noPlace: true);
                        continue;
                    }
                    if (!Resolve(world, fog, in p, alpha, out Vector3 at)) continue;
                    AddSegment(prev, at, p.Kind);
                    if (!p.IsCurrent) AddMarker(p.Kind, at, noPlace: false);
                    prev = at;
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

    private void AddMarker(CommandKind kind, Vector3 at, bool noPlace)
    {
        var key = new PathKey(Q(at.X), Q(at.Z), 0, 0, (int)kind * 2 + (noPlace ? 1 : 0));
        int k = MarkerCount;
        if (!_markerAt.TryAdd(key, k)) return;
        if (k >= _mPos.Length) GrowMarkers();
        _mPos[k] = at;
        _mKind[k] = kind;
        _mNoPlace[k] = noPlace;
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
        _mShown = new Color[size];
        Array.Fill(_mShown, Unset); // a new instance buffer holds no colours or transforms
        _mWritten = new Vector3[size];
        Array.Fill(_mWritten, NoPoint);
        _mWrittenRing = new bool[size];
        _markers.InstanceCount = size;
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

    // Writes this frame's instances: transforms every frame (units and targets move), colours only when they change.
    private void Write()
    {
        for (int k = 0; k < MarkerCount; k++)
        {
            bool ring = _mNoPlace[k];
            if (_mWritten[k] != _mPos[k] || _mWrittenRing[k] != ring)
            {
                float r = MarkerRadius * (ring ? NoPlaceScale : 1f);
                Vector3 at = _mPos[k] + new Vector3(0f, (ring ? NoPlaceLift : MarkerLift) + MarkerHeight / 2f, 0f);
                _markers.SetInstanceTransform(k, new Transform3D(Basis.FromScale(new Vector3(r, MarkerHeight, r)), at));
                _mWritten[k] = _mPos[k];
                _mWrittenRing[k] = ring;
            }
            Color c = ColorOf(_mKind[k]);
            if (_mShown[k] != c)
            {
                _markers.SetInstanceColor(k, c);
                _mShown[k] = c;
            }
        }
        _markers.VisibleInstanceCount = MarkerCount;
        var lift = new Vector3(0f, LineLift, 0f);
        for (int k = 0; k < SegmentCount; k++)
        {
            if (_sWrittenFrom[k] != _sFrom[k] || _sWrittenTo[k] != _sTo[k])
            {
                Vector3 a = _sFrom[k] + lift, b = _sTo[k] + lift;
                Vector3 dir = b - a;
                float length = dir.Length();
                // The box's local Z runs along the leg, scaled to its length (as the rally line).
                Basis basis = Basis.LookingAt(dir / length, Vector3.Up) * Basis.FromScale(new Vector3(LineWidth, LineThickness, length));
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

    // A marker's (x, z, kind) or a leg's (x1, z1, x2, z2, kind) in 1 cm steps.
    private readonly struct PathKey : IEquatable<PathKey>
    {
        private readonly int _a, _b, _c, _d, _e;

        public PathKey(int a, int b, int c, int d, int e) => (_a, _b, _c, _d, _e) = (a, b, c, d, e);

        public bool Equals(PathKey o) => _a == o._a && _b == o._b && _c == o._c && _d == o._d && _e == o._e;

        public override bool Equals(object? obj) => obj is PathKey o && Equals(o);

        public override int GetHashCode() => unchecked(((((_a * 397) ^ _b) * 397 ^ _c) * 397 ^ _d) * 397 ^ _e);
    }
}
