using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Selection, control groups, subgroups and unit orders for the local player (docs/02 "Controls and camera").</summary>
/// <remarks>
/// Left click selects the nearest own unit under the cursor, a 4 px drag box-selects own units by
/// their projected centres; Shift (<c>select_add</c>) toggles a clicked unit or adds a box. A
/// double-click or Ctrl + click (<c>select_type</c>) selects every own on-screen unit of the
/// clicked type. Right click, A + click, S and H all go through <see cref="Order"/>, as does the
/// minimap. The geometry lives in the pure <see cref="ScreenPicker"/> and <see cref="GroundPicker"/>,
/// groups and subgroups in <see cref="ControlGroups"/> and <see cref="Rts.Sim.ViewApi.Subgroups"/>;
/// the sim is changed only through <see cref="Simulation.Enqueue"/>. A selection action that leaves
/// a changed, non-empty selection plays <see cref="SfxEvent.Select"/>; an order that enqueued
/// anything plays <see cref="SfxEvent.Command"/> (M2-6).
/// </remarks>
public partial class SelectionController : Node
{
    /// <summary>The human player's index until the match setup screen exists.</summary>
    public const int LocalPlayer = 0;

    private static readonly StringName[] GroupActions =
        { "group_1", "group_2", "group_3", "group_4", "group_5", "group_6", "group_7", "group_8", "group_9" };

    private SimRunner _runner = null!;
    private RtsCamera _camera = null!;
    private SelectionRings _rings = null!;
    private ColorRect _box = null!;
    private Sfx? _sfx;

    private Vector2 _press;
    private bool _pressing, _boxing, _doubleClick;
    private readonly int[] _issued = new int[(int)CommandKind.AttackMove + 1];

    // Per-slot projection scratch, filled on each click or box release.
    private System.Numerics.Vector2[] _screen = Array.Empty<System.Numerics.Vector2>();
    private float[] _radiusPx = Array.Empty<float>();
    private bool[] _candidate = Array.Empty<bool>();
    private int[] _picked = Array.Empty<int>();

    // The selection before the current selection action, to tell whether it changed (Select sound).
    private EntityHandle[] _before = Array.Empty<EntityHandle>();
    private int _beforeCount;

    /// <summary>The selected units.</summary>
    public SelectionSet Selection { get; private set; } = new(1);

    /// <summary>Control groups 1-9 (index 0-8).</summary>
    public ControlGroups Groups { get; private set; } = new(1);

    /// <summary>The selection's unit types and the active one (Tab).</summary>
    public Subgroups Subgroups { get; private set; } = new(1);

    /// <summary>True after A: the next left click on the map attack-moves the selection there.</summary>
    /// <remarks>Ends on that click, on Esc, S, H, any right-click (3D view or minimap), or when the selection becomes empty.</remarks>
    public bool Targeting { get; private set; }

    /// <summary>Orders dropped because the command queue was too full to take a whole order (logged).</summary>
    public int DroppedOrders { get; private set; }

    /// <summary>Commands of <paramref name="kind"/> enqueued by <see cref="Order"/> so far (one per unit).</summary>
    public int IssuedCount(CommandKind kind) => (uint)kind < (uint)_issued.Length ? _issued[(int)kind] : 0;

    public override void _Ready()
    {
        _box = GetNode<ColorRect>("BoxLayer/Box");
        _box.Visible = false;
    }

    /// <summary>Connects the controller to the match; call once after the sim exists.</summary>
    /// <param name="sfx">Where selection and order sounds go; null plays none.</param>
    public void Init(SimRunner runner, RtsCamera camera, SelectionRings rings, Sfx? sfx = null)
    {
        _runner = runner;
        _sfx = sfx;
        _camera = camera;
        _rings = rings;
        World world = runner.Simulation!.World;
        int capacity = world.Units.Capacity;
        Selection = new SelectionSet(capacity);
        Groups = new ControlGroups(capacity);
        Subgroups = new Subgroups(Math.Max(1, world.Data.Units.Length));
        _screen = new System.Numerics.Vector2[capacity];
        _radiusPx = new float[capacity];
        _candidate = new bool[capacity];
        _picked = new int[capacity];
        _before = new EntityHandle[capacity];
        _rings.Init(capacity);
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is not Simulation sim) return;
        World world = sim.World;
        UnitStore u = world.Units;
        Selection.Prune(u.Alive, u.Generation);
        Groups.Prune(u.Alive, u.Generation);
        // Nothing left to attack-move: the next click must be a normal selection click (BUG-0067).
        if (Selection.Count == 0) Targeting = false;
        // A death keeps the active subgroup while its type is still selected.
        Subgroups.Update(Selection.Items, u.TypeId, reset: false);
        _rings.Sync(world, (float)_runner.Alpha, Selection.Items);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_runner?.Simulation == null) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.IsActionPressed("select"))
            {
                // The selection may have died since the last _Process; then this is a normal click (BUG-0067).
                if (Targeting) PruneSelection();
                // While targeting, a left click is the order's point and never selects; off the map it does nothing.
                if (Targeting)
                {
                    if (IssueAt(CommandKind.AttackMove, mb.Position)) Targeting = false;
                    return;
                }
                _pressing = true;
                _boxing = false;
                _doubleClick = mb.DoubleClick;
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
                if (Targeting) CancelTargeting(); // right click cancels targeting and orders nothing
                else IssueAt(CommandKind.Move, mb.Position);
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
        else if (e is InputEventKey) HandleKey(e);
    }

    private void HandleKey(InputEvent e)
    {
        bool queued = Input.IsActionPressed("order_queue");
        if (e.IsActionPressed("order_cancel")) CancelTargeting();
        else if (e.IsActionPressed("order_attack_move")) Targeting = Selection.Count > 0;
        else if (e.IsActionPressed("order_stop") || e.IsActionPressed("order_hold"))
        {
            Targeting = false;
            Order(e.IsActionPressed("order_stop") ? CommandKind.Stop : CommandKind.HoldPosition, null, queued);
        }
        else if (e.IsActionPressed("subgroup_next")) Subgroups.Next();
        else
        {
            for (int g = 0; g < GroupActions.Length; g++)
            {
                if (!e.IsActionPressed(GroupActions[g])) continue;
                GroupKey(g);
                return;
            }
        }
    }

    /// <summary>Ends A-targeting without ordering anything (Esc, or a right-click on the 3D view or the minimap).</summary>
    public void CancelTargeting() => Targeting = false;

    // Ctrl + digit assigns, Shift + digit adds (Ctrl wins when both are held), a plain digit recalls;
    // a second quick recall of the same group centres the camera on it.
    private void GroupKey(int g)
    {
        UnitStore u = _runner.Simulation!.World.Units;
        Selection.Prune(u.Alive, u.Generation);
        if (Input.IsActionPressed("group_assign"))
        {
            if (Selection.Count > 0) Groups.Assign(g, Selection.Items); // an empty selection never wipes a group
        }
        else if (Input.IsActionPressed("group_add")) Groups.Add(g, Selection.Items);
        else
        {
            SnapshotSelection();
            if (Groups.Recall(g, Selection, u.Alive, u.Generation) == 0) return;
            SelectionChanged();
            PlaySelectIfChanged();
            if (Groups.Tap(g, Time.GetTicksMsec() / 1000.0) && Groups.TryMean(g, u.Position, u.Alive, u.Generation, out System.Numerics.Vector2 mean))
                _camera.SetFocus(mean.X, mean.Y);
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
        SnapshotSelection();
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
            }
            else if (_doubleClick || Input.IsActionPressed("select_type")) SelectType(u.TypeId[slot], add);
            else if (add) Selection.Toggle(new EntityHandle(slot, u.Generation[slot]));
            else
            {
                Selection.Clear();
                Selection.Add(new EntityHandle(slot, u.Generation[slot]));
            }
        }
        _boxing = false;
        SelectionChanged();
        PlaySelectIfChanged();
    }

    // Remembers the live selection before a selection action; leaves targeting alone (QaH1: a recall keeps A armed).
    private void SnapshotSelection()
    {
        UnitStore u = _runner.Simulation!.World.Units;
        Selection.Prune(u.Alive, u.Generation);
        Selection.Items.CopyTo(_before);
        _beforeCount = Selection.Count;
    }

    // Select sound when the action left a non-empty selection that differs (as a set) from the snapshot.
    private void PlaySelectIfChanged()
    {
        if (Selection.Count == 0) return;
        bool same = Selection.Count == _beforeCount;
        for (int i = 0; same && i < _beforeCount; i++) same = Selection.Contains(_before[i]);
        if (!same) _sfx?.Play(SfxEvent.Select);
    }

    // Every own on-screen unit of the type (Project() has run): a box over the whole viewport, filtered by type.
    private void SelectType(int type, bool add)
    {
        UnitStore u = _runner.Simulation!.World.Units;
        Vector2 view = _camera.GetViewport().GetVisibleRect().Size;
        int n = ScreenPicker.PickBox(_screen, _candidate, System.Numerics.Vector2.Zero, ToNumerics(view), _picked);
        if (!add) Selection.Clear();
        for (int i = 0; i < n; i++)
        {
            int s = _picked[i];
            if (u.TypeId[s] == type) Selection.Add(new EntityHandle(s, u.Generation[s]));
        }
    }

    // A selection the player made: the first subgroup becomes active.
    private void SelectionChanged()
    {
        UnitStore u = _runner.Simulation!.World.Units;
        PruneSelection();
        Subgroups.Update(Selection.Items, u.TypeId, reset: true);
    }

    // Drops dead units from the selection; targeting needs someone to order.
    private void PruneSelection()
    {
        UnitStore u = _runner.Simulation!.World.Units;
        Selection.Prune(u.Alive, u.Generation);
        if (Selection.Count == 0) Targeting = false;
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

    // Picks the ground under a screen point and orders the selection there (queued while order_queue is held); false if the ray missed the map.
    private bool IssueAt(CommandKind kind, Vector2 screen)
    {
        Simulation sim = _runner.Simulation!;
        Vector3 origin = _camera.ProjectRayOrigin(screen), dir = _camera.ProjectRayNormal(screen);
        if (!GroundPicker.TryPick(sim.World.Heightmap, new(origin.X, origin.Y, origin.Z), new(dir.X, dir.Y, dir.Z), out System.Numerics.Vector3 hit))
            return false;
        Order(kind, new Vector2(hit.X, hit.Z), Input.IsActionPressed("order_queue"));
        return true;
    }

    /// <summary>The one order path: enqueues one <paramref name="kind"/> command per selected live unit, or none if the whole order doesn't fit the command queue.</summary>
    /// <param name="kind">Move, AttackMove, Stop or HoldPosition.</param>
    /// <param name="point">Ground point (sim x, y in meters); required and finite for Move and AttackMove, ignored otherwise.</param>
    /// <param name="queued">Append to each unit's order queue (Shift) instead of replacing it.</param>
    public void Order(CommandKind kind, Vector2? point, bool queued)
    {
        if (_runner?.Simulation is not Simulation sim) return;
        if (kind is not (CommandKind.Move or CommandKind.AttackMove or CommandKind.Stop or CommandKind.HoldPosition))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a unit order");
        var target = System.Numerics.Vector2.Zero;
        if (kind is CommandKind.Move or CommandKind.AttackMove)
        {
            if (point is not Vector2 p || !float.IsFinite(p.X) || !float.IsFinite(p.Y)) return;
            target = new System.Numerics.Vector2(p.X, p.Y);
        }
        Selection.Prune(sim.World.Units.Alive, sim.World.Units.Generation);
        if (Selection.Count == 0) return;
        // The queue throws when full; never send half an order.
        if (sim.PendingCommandCount + Selection.Count > sim.World.Config.CommandCapacity)
        {
            DroppedOrders++;
            GD.PushWarning($"{kind} order for {Selection.Count} units dropped: command queue full.");
            return;
        }
        foreach (EntityHandle h in Selection.Items)
        {
            sim.Enqueue(kind switch
            {
                CommandKind.Move => Command.Move(LocalPlayer, h, target, queued),
                CommandKind.AttackMove => Command.AttackMove(LocalPlayer, h, target, queued),
                CommandKind.Stop => Command.Stop(LocalPlayer, h, queued),
                _ => Command.HoldPosition(LocalPlayer, h, queued),
            });
        }
        _issued[(int)kind] += Selection.Count;
        _sfx?.Play(SfxEvent.Command);
    }

    // The middle of the placeholder capsule: what the player sees and clicks.
    private static Vector3 BodyCentre(World world, int slot, float alpha) =>
        UnitViews.GroundPoint(world, slot, alpha) + new Vector3(0f, UnitViews.BodyHeight(world.Units.Radius[slot]) / 2f, 0f);

    private static System.Numerics.Vector2 ToNumerics(Vector2 v) => new(v.X, v.Y);
}
