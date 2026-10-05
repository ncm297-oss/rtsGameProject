using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Mouse selection and right-click move orders for the local player (docs/02 "Controls and camera").</summary>
/// <remarks>
/// Left click selects the nearest own unit under the cursor, a 4 px drag box-selects own units by
/// their projected centres; Shift (<c>select_add</c>) toggles a clicked unit or adds a box. A plain
/// click on no own unit clears the selection. Right click (<c>command</c>) picks the ground and
/// enqueues one <see cref="Command.Move"/> per selected unit. The geometry lives in the pure
/// <see cref="ScreenPicker"/> and <see cref="GroundPicker"/>; the sim is changed only through
/// <see cref="Simulation.Enqueue"/>.
/// </remarks>
public partial class SelectionController : Node
{
    /// <summary>The human player's index until the match setup screen exists.</summary>
    public const int LocalPlayer = 0;

    private SimRunner _runner = null!;
    private RtsCamera _camera = null!;
    private SelectionRings _rings = null!;
    private ColorRect _box = null!;

    private Vector2 _press;
    private bool _pressing, _boxing;

    // Per-slot projection scratch, filled on each click or box release.
    private System.Numerics.Vector2[] _screen = Array.Empty<System.Numerics.Vector2>();
    private float[] _radiusPx = Array.Empty<float>();
    private bool[] _candidate = Array.Empty<bool>();
    private int[] _picked = Array.Empty<int>();

    /// <summary>The selected units.</summary>
    public SelectionSet Selection { get; private set; } = new(1);

    /// <summary>Move commands dropped because the command queue was too full to take a whole order (logged).</summary>
    public int DroppedOrders { get; private set; }

    public override void _Ready()
    {
        _box = GetNode<ColorRect>("BoxLayer/Box");
        _box.Visible = false;
    }

    /// <summary>Connects the controller to the match; call once after the sim exists.</summary>
    public void Init(SimRunner runner, RtsCamera camera, SelectionRings rings)
    {
        _runner = runner;
        _camera = camera;
        _rings = rings;
        int capacity = runner.Simulation!.World.Units.Capacity;
        Selection = new SelectionSet(capacity);
        _screen = new System.Numerics.Vector2[capacity];
        _radiusPx = new float[capacity];
        _candidate = new bool[capacity];
        _picked = new int[capacity];
        _rings.Init(capacity);
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is not Simulation sim) return;
        World world = sim.World;
        Selection.Prune(world.Units.Alive, world.Units.Generation);
        _rings.Sync(world, (float)_runner.Alpha, Selection.Items);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_runner?.Simulation == null) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.IsActionPressed("select"))
            {
                _pressing = true;
                _boxing = false;
                _press = mb.Position;
            }
            else if (mb.IsActionReleased("select") && _pressing)
            {
                _pressing = false;
                _box.Visible = false;
                FinishSelect(mb.Position);
            }
            else if (mb.IsActionPressed("command"))
            {
                IssueMove(mb.Position);
            }
        }
        else if (e is InputEventMouseMotion motion && _pressing)
        {
            if (!_boxing && ScreenPicker.IsDrag(ToNumerics(_press), ToNumerics(motion.Position))) _boxing = true;
            if (_boxing)
            {
                _box.Position = _press.Min(motion.Position);
                _box.Size = (motion.Position - _press).Abs();
                _box.Visible = true;
            }
        }
    }

    /// <summary>Screen position (pixels) of a unit's body centre as the picker sees it; false if behind the camera.</summary>
    public bool TryScreenPosition(int slot, out Vector2 screen)
    {
        World world = _runner.Simulation!.World;
        Vector3 p = BodyCentre(world, slot, (float)_runner.Alpha);
        screen = _camera.UnprojectPosition(p);
        return !_camera.IsPositionBehind(p);
    }

    private void FinishSelect(Vector2 release)
    {
        bool add = Input.IsActionPressed("select_add");
        Project();
        UnitStore u = _runner.Simulation!.World.Units;
        if (_boxing || ScreenPicker.IsDrag(ToNumerics(_press), ToNumerics(release)))
        {
            int n = ScreenPicker.PickBox(_screen, _candidate, ToNumerics(_press), ToNumerics(release), _picked);
            if (!add) Selection.Clear();
            for (int i = 0; i < n; i++) Selection.Add(new EntityHandle(_picked[i], u.Generation[_picked[i]]));
        }
        else
        {
            int slot = ScreenPicker.PickClick(_screen, _radiusPx, _candidate, ToNumerics(release));
            if (slot < 0)
            {
                // Shift + click on empty ground keeps the selection, as in most RTS games.
                if (!add) Selection.Clear();
                return;
            }
            var h = new EntityHandle(slot, u.Generation[slot]);
            if (add) Selection.Toggle(h);
            else
            {
                Selection.Clear();
                Selection.Add(h);
            }
        }
        _boxing = false;
    }

    // Fills the per-slot screen centre, pixel radius and candidate flag (live, own, in front of the camera).
    private void Project()
    {
        World world = _runner.Simulation!.World;
        UnitStore u = world.Units;
        float alpha = (float)_runner.Alpha;
        Transform3D cam = _camera.GlobalTransform;
        Vector3 forward = -cam.Basis.Z;
        float viewHeight = _camera.GetViewport().GetVisibleRect().Size.Y;
        float tanHalfFov = Mathf.Tan(Mathf.DegToRad(_camera.Fov) / 2f);
        for (int i = 0; i < u.Capacity; i++)
        {
            _candidate[i] = false;
            if (!u.Alive[i] || u.Owner[i] != LocalPlayer) continue;
            Vector3 p = BodyCentre(world, i, alpha);
            float depth = (p - cam.Origin).Dot(forward);
            if (depth <= _camera.Near || _camera.IsPositionBehind(p)) continue;
            _screen[i] = ToNumerics(_camera.UnprojectPosition(p));
            // Pixels per meter at that depth for a vertical-FOV perspective camera.
            _radiusPx[i] = u.Radius[i] * viewHeight / (2f * depth * tanHalfFov);
            _candidate[i] = true;
        }
    }

    private void IssueMove(Vector2 screen)
    {
        if (Selection.Count == 0) return;
        Simulation sim = _runner.Simulation!;
        Vector3 origin = _camera.ProjectRayOrigin(screen), dir = _camera.ProjectRayNormal(screen);
        if (!GroundPicker.TryPick(sim.World.Heightmap, new(origin.X, origin.Y, origin.Z), new(dir.X, dir.Y, dir.Z), out System.Numerics.Vector3 hit))
            return; // off the map: no order
        Selection.Prune(sim.World.Units.Alive, sim.World.Units.Generation);
        // The queue throws when full; never send half an order.
        if (sim.PendingCommandCount + Selection.Count > sim.World.Config.CommandCapacity)
        {
            DroppedOrders++;
            GD.PushWarning($"Move order for {Selection.Count} units dropped: command queue full.");
            return;
        }
        var target = new System.Numerics.Vector2(hit.X, hit.Z);
        foreach (EntityHandle h in Selection.Items)
            sim.Enqueue(Command.Move(LocalPlayer, h, target));
    }

    // The middle of the placeholder capsule: what the player sees and clicks.
    private static Vector3 BodyCentre(World world, int slot, float alpha) =>
        UnitViews.GroundPoint(world, slot, alpha) + new Vector3(0f, UnitViews.BodyHeight(world.Units.Radius[slot]) / 2f, 0f);

    private static System.Numerics.Vector2 ToNumerics(Vector2 v) => new(v.X, v.Y);
}
