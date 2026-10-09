using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Selection, control groups, subgroups and unit orders for the local player (docs/02 "Controls and camera").</summary>
/// <remarks>
/// Left click selects the nearest own unit under the cursor, a 4 px drag box-selects own units by
/// their projected centres; Shift (<c>select_add</c>) toggles a clicked unit or adds a box. A
/// double-click or Ctrl + click (<c>select_type</c>) selects every own on-screen unit of the
/// clicked type. Right click, A + click, S and H all go through <see cref="Order"/>, as does the
/// minimap; M + click is a plain Move (M3-V2). A right click on a live resource node with a worker
/// selected is a Gather for the workers and a Move for the rest (<see cref="ContextOrder"/>, M3-V1),
/// on an own damaged building a Repair and on an own site a joining Build (M3-V2); the minimap's
/// right click is a Move, or an Attack on a visible enemy unit's dot (M4-V4). A right click whose ray meets a building's drawn box first means that building (its
/// footprint centre), not the ground behind it (<see cref="ContextTarget"/>, BUG-0108). With an own finished building
/// selected a right click (3D view or minimap) sets its rally point, and one on the building itself clears it (M3-V3);
/// the production card's train, research and queue-cancel presses enqueue through <see cref="Produce"/> and
/// <see cref="CancelQueueItem"/>. A left click that hits no unit selects the own building whose box it
/// hits (<see cref="SelectedBuilding"/>, M3-V2: alone, units cleared; a box never selects one). The
/// command card (<see cref="Card"/>) sees keys and clicks first while its build menu or ghost is up. A right click or an
/// A + click whose ray meets an enemy unit's drawn body or an enemy building's box first (<see cref="EnemyAt"/>, M4-V2) is an
/// <c>Attack</c> on it for every selected unit (<see cref="AttackOrder"/>; Shift queues), marked by the red
/// <see cref="TargetRing"/>; an own unit or building in front hides what is behind it, as the player sees it. Under the fog
/// (M4-V4) only what the screen draws is under the cursor: an enemy the <see cref="Fog"/> hides is neither a target nor a
/// building to right-click, so a click there means the ground. The geometry lives in the pure <see cref="ScreenPicker"/> and <see cref="GroundPicker"/>,
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
    private static readonly int IssuedSlots = MaxKind() + 1;
    private readonly int[] _issued = new int[IssuedSlots];
    private CommandKind _target = CommandKind.Noop;

    // The selected building: slot and generation (view state only; -1 for none).
    private int _building = -1, _buildingGen;
    private int _shownOutline = -1, _shownOutlineGen;

    // Per unit type: in the worker slot (gathers on a right click on a node).
    private bool[] _isWorkerType = Array.Empty<bool>();

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

    /// <summary>True after A or M: the next left click on the map orders <see cref="TargetKind"/> there.</summary>
    /// <remarks>Ends on that click, on Esc, S, H, any right-click (3D view or minimap), or when the selection becomes empty.</remarks>
    public bool Targeting
    {
        get => _target != CommandKind.Noop;
        private set { if (!value) _target = CommandKind.Noop; }
    }

    /// <summary>The order the next targeting click gives: <see cref="CommandKind.AttackMove"/> (A), <see cref="CommandKind.Move"/> (M), or <see cref="CommandKind.Noop"/> when not targeting.</summary>
    public CommandKind TargetKind => _target;

    /// <summary>The command card that sees keys and clicks first (build menus, ghost); null without a HUD.</summary>
    public CommandCard? Card { get; set; }

    /// <summary>The Attack order's red ring (M4-V2); null draws none.</summary>
    public TargetRing? TargetRing { get; set; }

    /// <summary>The fog (M4-V4): a unit or building it hides is not under the cursor (no Attack, no context target); null sees everything.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>Draws the selected building's footprint outline; null draws none.</summary>
    public BuildingOutline? Outline { get; set; }

    /// <summary>The selected building's slot, or -1 (M3-V2). A view selection: a dead or reused slot reads -1 from the next read on.</summary>
    public int SelectedBuilding
    {
        get
        {
            if (_building < 0 || _runner?.Simulation is not Simulation sim) return -1;
            BuildingStore b = sim.World.Buildings;
            if (!b.Alive[_building] || b.Generation[_building] != _buildingGen) _building = -1;
            return _building;
        }
    }

    /// <summary>The selected building's slot when it is an own finished building (it has a production queue and a rally point), else -1 (M3-V3).</summary>
    public int SelectedFinishedBuilding
    {
        get
        {
            int slot = SelectedBuilding;
            if (slot < 0) return -1;
            BuildingStore b = _runner.Simulation!.World.Buildings;
            return !b.UnderConstruction[slot] && b.Owner[slot] == LocalPlayer ? slot : -1;
        }
    }

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
        _isWorkerType = new bool[world.Data.Units.Length];
        for (int t = 0; t < _isWorkerType.Length; t++) _isWorkerType[t] = world.Data.Units[t].Slot == UnitSlot.Worker;
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
        SyncOutline(world);
    }

    /// <summary>Clears a dead or reused building selection and keeps the footprint outline on the selected building; touches the outline only when the selection changes. Allocation-free.</summary>
    public void SyncOutline(World world)
    {
        int slot = SelectedBuilding;
        int gen = slot >= 0 ? world.Buildings.Generation[slot] : 0;
        if (slot == _shownOutline && gen == _shownOutlineGen) return;
        _shownOutline = slot;
        _shownOutlineGen = gen;
        if (slot >= 0) Outline?.ShowFor(world, slot);
        else Outline?.HideOutline();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_runner?.Simulation == null) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.IsActionPressed("select"))
            {
                // A placement ghost takes the click: a green one places, a red one does nothing; it never selects.
                if (Card != null && Card.GhostActive)
                {
                    Card.GhostClick(Input.IsActionPressed("order_queue"), mb.Position);
                    return;
                }
                // The selection may have died since the last _Process; then this is a normal click (BUG-0067).
                if (Targeting) PruneSelection();
                // While targeting, a left click is the order's point and never selects; off the map it does nothing.
                if (Targeting)
                {
                    AttackMoveClick(mb.Position);
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
                if (Card != null && Card.MenuOpen) Card.CloseMenu(); // closes a build menu (and its ghost), orders nothing
                else if (Targeting) CancelTargeting(); // right click cancels targeting and orders nothing
                else CommandAt(mb.Position, Input.IsActionPressed("order_queue"));
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
        // Build menus own the grid keys while open (docs/02 "Grid hotkeys"); B / V and a site's Cancel too.
        if (Card != null && Card.HandleKey(e)) return;
        bool queued = Input.IsActionPressed("order_queue");
        if (e.IsActionPressed("order_cancel")) CancelTargeting();
        else if (e.IsActionPressed("order_attack_move")) BeginAttackMove();
        else if (e.IsActionPressed("order_move")) BeginMove();
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

    /// <summary>What A does: arms attack-move targeting, only when something is selected.</summary>
    public void BeginAttackMove() => _target = Selection.Count > 0 ? CommandKind.AttackMove : CommandKind.Noop;

    /// <summary>What M does (M3-V2): arms Move targeting (a plain move ignoring enemies, today what a right click on ground does), only when something is selected.</summary>
    public void BeginMove() => _target = Selection.Count > 0 ? CommandKind.Move : CommandKind.Noop;

    /// <summary>What a left click at <paramref name="screen"/> does while targeting: gives the armed order (<see cref="TargetKind"/>: attack-move after A, move after M) at the ground there (queued while <c>order_queue</c> is held) and disarms; false (still armed) when not targeting or off the map. After A, a click on an enemy unit or building (<see cref="EnemyAt"/>) is an <c>Attack</c> on it instead (M4-V2).</summary>
    public bool AttackMoveClick(Vector2 screen)
    {
        if (!Targeting) return false;
        bool queued = Input.IsActionPressed("order_queue");
        if (_target == CommandKind.AttackMove && EnemyAt(screen, out EntityHandle enemy, out bool isBuilding))
        {
            AttackOrder(enemy, isBuilding, queued);
            Targeting = false;
            return true;
        }
        if (!OrderAt(_target, screen, queued)) return false;
        Targeting = false;
        return true;
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
        if (_boxing || ScreenPicker.IsDrag(ToNumerics(_press), ToNumerics(release)))
        {
            _boxing = false;
            BoxSelect(_press, release, add);
            return;
        }
        SnapshotSelection();
        Project();
        UnitStore u = _runner.Simulation!.World.Units;
        int slot = ScreenPicker.PickClick(_screen, _radiusPx, _candidate, ToNumerics(release));
        int building = slot < 0 ? PickBuilding(release) : -1;
        if (building >= 0)
        {
            // A building is selected alone (M3-V2), Shift or not.
            int was = SelectedBuilding;
            SelectBuilding(building);
            if (was != building) _sfx?.Play(SfxEvent.Select);
            return;
        }
        if (slot < 0)
        {
            // Shift + click on empty ground keeps the selection, as in most RTS games.
            if (!add)
            {
                Selection.Clear();
                ClearBuilding();
            }
        }
        else if (_doubleClick || Input.IsActionPressed("select_type")) SelectType(u.TypeId[slot], add);
        else if (add) Selection.Toggle(new EntityHandle(slot, u.Generation[slot]));
        else
        {
            Selection.Clear();
            Selection.Add(new EntityHandle(slot, u.Generation[slot]));
        }
        SelectionChanged();
        PlaySelectIfChanged();
    }

    /// <summary>What a box drag from <paramref name="from"/> to <paramref name="to"/> (screen pixels) does: selects the own units whose centres are inside, added to the selection when <paramref name="add"/> (Shift); returns the selection size.</summary>
    public int BoxSelect(Vector2 from, Vector2 to, bool add)
    {
        SnapshotSelection();
        Project();
        UnitStore u = _runner.Simulation!.World.Units;
        int n = ScreenPicker.PickBox(_screen, _candidate, ToNumerics(from), ToNumerics(to), _picked);
        // A box never selects a building; a plain one drops it like the units.
        if (!add)
        {
            Selection.Clear();
            ClearBuilding();
        }
        for (int i = 0; i < n; i++) Selection.Add(new EntityHandle(_picked[i], u.Generation[_picked[i]]));
        SelectionChanged();
        PlaySelectIfChanged();
        return Selection.Count;
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

    // A selection the player made: the first subgroup becomes active; selected units replace a selected building.
    private void SelectionChanged()
    {
        UnitStore u = _runner.Simulation!.World.Units;
        PruneSelection();
        Subgroups.Update(Selection.Items, u.TypeId, reset: true);
        if (Selection.Count > 0) ClearBuilding();
    }

    /// <summary>Selects own building slot <paramref name="slot"/> alone (units cleared, targeting ended); false (nothing changed) for a dead slot or another player's building.</summary>
    public bool SelectBuilding(int slot)
    {
        if (_runner?.Simulation is not Simulation sim) return false;
        BuildingStore b = sim.World.Buildings;
        if ((uint)slot >= (uint)b.Capacity || !b.Alive[slot] || b.Owner[slot] != LocalPlayer) return false;
        Selection.Clear();
        Subgroups.Update(Selection.Items, sim.World.Units.TypeId, reset: true);
        Targeting = false;
        _building = slot;
        _buildingGen = b.Generation[slot];
        return true;
    }

    /// <summary>Drops the building selection, if any.</summary>
    public void ClearBuilding() => _building = -1;

    /// <summary>The own building whose drawn box is under screen point <paramref name="screen"/>, or -1 (<see cref="BuildingPicker.PickRay"/>).</summary>
    public int PickBuilding(Vector2 screen)
    {
        World world = _runner.Simulation!.World;
        Vector3 o = _camera.ProjectRayOrigin(screen), d = _camera.ProjectRayNormal(screen);
        return BuildingPicker.PickRay(world.Buildings, world.Data.Buildings, world.NavGrid, world.Heightmap, LocalPlayer,
            new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), BuildingViews.BoxHeight, BuildingViews.SiteMinHeight);
    }

    /// <summary>Live selected units in the <c>worker</c> slot (prunes the selection first).</summary>
    public int SelectedWorkerCount()
    {
        if (_runner?.Simulation is not Simulation sim) return 0;
        UnitStore u = sim.World.Units;
        Selection.Prune(u.Alive, u.Generation);
        int n = 0;
        foreach (EntityHandle h in Selection.Items)
            if (_isWorkerType[u.TypeId[h.Index]]) n++;
        return n;
    }

    /// <summary>Live selected units in the <c>worker</c> slot, without pruning the selection (the card's per-frame read: a key handler must see the selection as the player left it).</summary>
    public int LiveWorkerCount()
    {
        if (_runner?.Simulation is not Simulation sim) return 0;
        UnitStore u = sim.World.Units;
        int n = 0;
        foreach (EntityHandle h in Selection.Items)
            if (u.Alive[h.Index] && u.Generation[h.Index] == h.Generation && _isWorkerType[u.TypeId[h.Index]]) n++;
        return n;
    }

    /// <summary>True when the active Tab subgroup is a <c>worker</c>-slot type (the card then offers the build menus).</summary>
    public bool ActiveSubgroupIsWorker =>
        Selection.Count > 0 && Subgroups.Count > 0 && (uint)Subgroups.ActiveType < (uint)_isWorkerType.Length && _isWorkerType[Subgroups.ActiveType];

    /// <summary>
    /// What a green placement click orders (M3-V2): one <c>Build</c> of <paramref name="typeId"/> anchored at
    /// <paramref name="anchor"/> per selected live worker (the first to apply places the site, the rest join it: docs/03
    /// M3-3), queued when <paramref name="queued"/>; one Command sound. Returns the Builds enqueued: 0 with no worker or
    /// when they don't all fit the command queue.
    /// </summary>
    public int OrderBuild(int typeId, int anchor, bool queued)
    {
        if (_runner?.Simulation is not Simulation sim) return 0;
        World world = sim.World;
        int workers = SelectedWorkerCount();
        if (workers == 0 || (uint)anchor >= (uint)(world.NavGrid.Width * world.NavGrid.Height)) return 0;
        if (sim.PendingCommandCount + workers > world.Config.CommandCapacity)
        {
            DroppedOrders++;
            GD.PushWarning($"Build order for {workers} workers dropped: command queue full.");
            return 0;
        }
        System.Numerics.Vector2 point = PlacementGhost.AnchorPoint(world.NavGrid, anchor);
        UnitStore u = world.Units;
        foreach (EntityHandle h in Selection.Items)
            if (_isWorkerType[u.TypeId[h.Index]]) sim.Enqueue(Command.Build(LocalPlayer, h, typeId, point, queued));
        _issued[(int)CommandKind.Build] += workers;
        _sfx?.Play(SfxEvent.Command);
        return workers;
    }

    // The site the last Cancel was sent for, and the tick it was sent between (BUG-0126 item 4).
    private int _cancelSlot = -1, _cancelGeneration, _cancelTick = -1;

    /// <summary>What the card's Cancel does (M3-V2): one <c>Cancel</c> at the selected own site's footprint centre; false (nothing enqueued) when no own site is selected, a Cancel for this site is already waiting for the next tick (a second press, BUG-0126), or the command queue is full.</summary>
    public bool CancelSelectedSite()
    {
        if (_runner?.Simulation is not Simulation sim) return false;
        int slot = SelectedBuilding;
        BuildingStore b = sim.World.Buildings;
        if (slot < 0 || !b.UnderConstruction[slot] || b.Owner[slot] != LocalPlayer) return false;
        if (sim.TickNumber == _cancelTick && slot == _cancelSlot && b.Generation[slot] == _cancelGeneration) return false;
        if (sim.PendingCommandCount + 1 > sim.World.Config.CommandCapacity)
        {
            DroppedOrders++;
            GD.PushWarning("Cancel dropped: command queue full.");
            return false;
        }
        sim.Enqueue(Command.Cancel(LocalPlayer, SiteCenter(sim.World, slot)));
        (_cancelSlot, _cancelGeneration, _cancelTick) = (slot, b.Generation[slot], sim.TickNumber);
        _issued[(int)CommandKind.Cancel]++;
        _sfx?.Play(SfxEvent.Command);
        return true;
    }

    /// <summary>The footprint centre (meters) of building slot <paramref name="slot"/>: where the card's Cancel points.</summary>
    public static System.Numerics.Vector2 SiteCenter(World world, int slot) =>
        StartBase.FootprintCenter(world.NavGrid, world.Data.Buildings[world.Buildings.TypeId[slot]], world.Buildings.Cell[slot]);

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

    /// <summary>What a right click (Move) or targeting click (AttackMove) at <paramref name="screen"/> orders: picks the ground there and calls <see cref="Order"/>; false if the ray missed the map.</summary>
    public bool OrderAt(CommandKind kind, Vector2 screen, bool queued)
    {
        Simulation sim = _runner.Simulation!;
        Vector3 origin = _camera.ProjectRayOrigin(screen), dir = _camera.ProjectRayNormal(screen);
        if (!GroundPicker.TryPick(sim.World.Heightmap, new(origin.X, origin.Y, origin.Z), new(dir.X, dir.Y, dir.Z), out System.Numerics.Vector3 hit))
            return false;
        Order(kind, new Vector2(hit.X, hit.Z), queued);
        return true;
    }

    /// <summary>
    /// What a right click at <paramref name="screen"/> on the 3D view orders: resolves the point (<see cref="ContextTarget"/>);
    /// with a building selected its rally order (<see cref="RallyOrder"/>, M3-V3: set there, cleared on the building
    /// itself, nothing for a site); otherwise <see cref="ContextOrder"/>. False if the click met neither a building nor the map.
    /// </summary>
    public bool CommandAt(Vector2 screen, bool queued)
    {
        // An enemy under the cursor is an Attack (M4-V2); a selected building keeps its rally rule.
        if (SelectedBuilding < 0 && EnemyAt(screen, out EntityHandle enemy, out bool isBuilding))
        {
            AttackOrder(enemy, isBuilding, queued);
            return true;
        }
        if (!ContextTarget(screen, out System.Numerics.Vector2 point, out int building)) return false;
        if (SelectedBuilding >= 0)
        {
            RallyOrder(point, building);
            return true;
        }
        ContextOrder(new Vector2(point.X, point.Y), queued);
        return true;
    }

    /// <summary>
    /// The enemy under screen point <paramref name="screen"/> (M4-V2): what the camera ray meets first among unit bodies
    /// (<see cref="UnitPicker.PickRay"/>), building boxes (<see cref="BuildingPicker.PickRay(BuildingStore, System.Collections.Immutable.ImmutableArray{BuildingDef}, Rts.Sim.Map.NavGrid, Rts.Sim.Map.Heightmap, int, System.Numerics.Vector3, System.Numerics.Vector3, float, float, out float)"/>)
    /// and resource props, when it is another player's live unit or building: its handle in <paramref name="target"/>,
    /// <paramref name="isBuilding"/> for a building. False when the first thing hit is own, a prop, or nothing (the click
    /// then means what it meant before). Allocation-free.
    /// </summary>
    public bool EnemyAt(Vector2 screen, out EntityHandle target, out bool isBuilding)
    {
        target = default;
        isBuilding = false;
        if (_runner?.Simulation is not Simulation sim) return false;
        World world = sim.World;
        Vector3 o = _camera.ProjectRayOrigin(screen), d = _camera.ProjectRayNormal(screen);
        System.Numerics.Vector3 so = new(o.X, o.Y, o.Z), sd = new(d.X, d.Y, d.Z);
        UnitStore u = world.Units;
        // Only what the screen draws can be clicked (M4-V4): the fog's shown lists stand in for Alive.
        FogView? fog = Fog?.Refreshed(world);
        ReadOnlySpan<bool> units = fog != null ? fog.UnitShown : u.Alive;
        int unit = UnitPicker.PickRay(units, u.PrevPosition, u.Position, u.Radius, world.Heightmap, (float)_runner.Alpha, so, sd, UnitViews.ExtraBodyHeight, out float unitT);
        int building = BuildingPicker.PickRay(world.Buildings, world.Data.Buildings, world.NavGrid, world.Heightmap, -1,
            so, sd, BuildingViews.BoxHeight, BuildingViews.SiteMinHeight, fog != null ? fog.BuildingShown : ReadOnlySpan<bool>.Empty, out float buildingT);
        ResourceStore r = world.Resources;
        ResourcePicker.PickRay(world.NavGrid, world.Data.Resources, r.Alive, r.TypeId, r.Cell, world.Heightmap, so, sd, PropsView.Shape, out float nodeT);
        return UnitPicker.ResolveEnemy(units, u.Owner, u.Generation, unit, unitT, world.Buildings, building, buildingT, nodeT, LocalPlayer, out target, out isBuilding);
    }

    /// <summary>
    /// The Attack order (M4-V2): one <c>Attack</c> on <paramref name="target"/> (a building handle when
    /// <paramref name="isBuilding"/>) per selected live unit, workers too (they obey an explicit Attack), queued when
    /// <paramref name="queued"/>; one Command sound and the red <see cref="TargetRing"/> on the target. Nothing with no
    /// selection, or when the whole order doesn't fit the command queue. The sim drops what it refuses (docs/03 "Implementation
    /// (M4-2a)"). Returns true if it enqueued.
    /// </summary>
    public bool AttackOrder(EntityHandle target, bool isBuilding, bool queued)
    {
        if (_runner?.Simulation is not Simulation sim) return false;
        Selection.Prune(sim.World.Units.Alive, sim.World.Units.Generation);
        if (Selection.Count == 0 || !RoomFor(sim, Selection.Count, "Attack")) return false;
        foreach (EntityHandle h in Selection.Items) sim.Enqueue(Command.Attack(LocalPlayer, h, target, isBuilding, queued));
        _issued[(int)CommandKind.Attack] += Selection.Count;
        _sfx?.Play(SfxEvent.Command);
        TargetRing?.Show(target, isBuilding);
        return true;
    }

    /// <summary>
    /// Where a right click at <paramref name="screen"/> points (BUG-0108): the building of any owner whose drawn box the
    /// camera ray meets first (<see cref="BuildingPicker.PickRay"/>; its slot in <paramref name="building"/>) gives its
    /// footprint centre, so the visible top of a box means the building and not the ground 2 m behind it; else the ground
    /// under the ray (<paramref name="building"/> -1). False when the ray meets neither. A resource node's drawn prop
    /// works the same way (M3-V3b; since M3-V4 <see cref="ResourcePicker.PickRay"/> tests <see cref="PropsView.Shape"/>, the
    /// trunk and cone or the mine's blocks, not the footprint's column, BUG-0125): when the ray meets a tree or a mine
    /// before any box, the point is the node's footprint centre, so a click on a canopy gathers that tree, and a click on
    /// open ground beside it is a Move. Terrain in front hides both.
    /// </summary>
    public bool ContextTarget(Vector2 screen, out System.Numerics.Vector2 point, out int building)
    {
        World world = _runner.Simulation!.World;
        Vector3 o = _camera.ProjectRayOrigin(screen), d = _camera.ProjectRayNormal(screen);
        System.Numerics.Vector3 so = new(o.X, o.Y, o.Z), sd = new(d.X, d.Y, d.Z);
        FogView? fog = Fog?.Refreshed(world);
        building = BuildingPicker.PickRay(world.Buildings, world.Data.Buildings, world.NavGrid, world.Heightmap, -1,
            so, sd, BuildingViews.BoxHeight, BuildingViews.SiteMinHeight, fog != null ? fog.BuildingShown : ReadOnlySpan<bool>.Empty, out float buildingT);
        ResourceStore r = world.Resources;
        int node = ResourcePicker.PickRay(world.NavGrid, world.Data.Resources, r.Alive, r.TypeId, r.Cell, world.Heightmap, so, sd,
            PropsView.Shape, out float nodeT);
        if (node >= 0 && nodeT < buildingT)
        {
            building = -1;
            ResourceDef def = world.Data.Resources[r.TypeId[node]];
            int w = world.NavGrid.Width, a = r.Cell[node];
            point = new System.Numerics.Vector2(a % w + def.FootprintWidth / 2f, a / w + def.FootprintHeight / 2f) * Rts.Sim.Map.MapConstants.CellSize;
            return true;
        }
        if (building >= 0)
        {
            point = SiteCenter(world, building);
            return true;
        }
        if (GroundPicker.TryPick(world.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit))
        {
            point = new System.Numerics.Vector2(hit.X, hit.Z);
            return true;
        }
        point = default;
        return false;
    }

    /// <summary>
    /// A right click's rally order while a building is selected (M3-V3): on the selected building itself
    /// (<paramref name="building"/>) one <c>ClearRally</c>; anywhere else (ground, a resource node, another building) one
    /// <c>SetRally</c> to <paramref name="point"/>; one Command sound. Nothing for a selected site, a non-finite point or a
    /// full command queue. Returns true if it enqueued.
    /// </summary>
    public bool RallyOrder(System.Numerics.Vector2 point, int building = -1)
    {
        if (_runner?.Simulation is not Simulation sim) return false;
        int slot = SelectedFinishedBuilding;
        if (slot < 0 || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return false;
        if (!RoomFor(sim, 1, "Rally")) return false;
        World world = sim.World;
        if (building == slot)
        {
            sim.Enqueue(Command.ClearRally(LocalPlayer, SiteCenter(world, slot)));
            _issued[(int)CommandKind.ClearRally]++;
        }
        else
        {
            sim.Enqueue(Command.SetRally(LocalPlayer, world.Buildings.Cell[slot], point));
            _issued[(int)CommandKind.SetRally]++;
        }
        _sfx?.Play(SfxEvent.Command);
        return true;
    }

    /// <summary>
    /// What a production card press does (M3-V3): one <c>Train</c> (or <c>Research</c> when <paramref name="isTech"/>) of
    /// <paramref name="typeId"/> at the selected own finished building's footprint centre, one Command sound; nothing (no
    /// sound) when <c>World.CanTrain</c> / <c>CanResearch</c> refuses now, nothing is selected, or the command queue is full.
    /// </summary>
    public bool Produce(int typeId, bool isTech)
    {
        if (_runner?.Simulation is not Simulation sim) return false;
        int slot = SelectedFinishedBuilding;
        if (slot < 0) return false;
        World world = sim.World;
        bool ok = isTech ? world.CanResearch(LocalPlayer, slot, typeId, out _) : world.CanTrain(LocalPlayer, slot, typeId, out _);
        if (!ok || !RoomFor(sim, 1, isTech ? "Research" : "Train")) return false;
        System.Numerics.Vector2 at = SiteCenter(world, slot);
        sim.Enqueue(isTech ? Command.Research(LocalPlayer, at, typeId) : Command.Train(LocalPlayer, at, typeId));
        _issued[(int)(isTech ? CommandKind.Research : CommandKind.Train)]++;
        _sfx?.Play(SfxEvent.Command);
        return true;
    }

    /// <summary>What a click on queue item <paramref name="item"/> of the selected own finished building does (M3-V3): one <c>CancelTrain</c> of it at the footprint centre, one Command sound; nothing past the queue's count.</summary>
    public bool CancelQueueItem(int item)
    {
        if (_runner?.Simulation is not Simulation sim) return false;
        int slot = SelectedFinishedBuilding;
        if (slot < 0 || (uint)item >= (uint)sim.World.Buildings.QueueCount[slot]) return false;
        if (!RoomFor(sim, 1, "Cancel")) return false;
        sim.Enqueue(Command.CancelTrain(LocalPlayer, SiteCenter(sim.World, slot), item));
        _issued[(int)CommandKind.CancelTrain]++;
        _sfx?.Play(SfxEvent.Command);
        return true;
    }

    /// <summary>What a portrait click in the selection panel does (M3-V3): selects that unit alone (if it is still alive), Select sound if that changed the selection.</summary>
    public bool SelectOnly(EntityHandle unit)
    {
        if (_runner?.Simulation is not Simulation sim) return false;
        UnitStore u = sim.World.Units;
        if ((uint)unit.Index >= (uint)u.Capacity || !u.Alive[unit.Index] || u.Generation[unit.Index] != unit.Generation) return false;
        SnapshotSelection();
        Selection.Clear();
        Selection.Add(unit);
        SelectionChanged();
        PlaySelectIfChanged();
        return true;
    }

    // The command queue throws when full: a whole order or nothing (one warning).
    private bool RoomFor(Simulation sim, int n, string what)
    {
        if (sim.PendingCommandCount + n <= sim.World.Config.CommandCapacity) return true;
        DroppedOrders++;
        GD.PushWarning($"{what} dropped: command queue full.");
        return false;
    }

    /// <summary>
    /// The right click's context command at a ground point (sim x, y in meters), with at least one <c>worker</c>-slot unit
    /// selected: on a live resource node's cell (<see cref="ResourcePicker"/>) a <c>Gather</c> of that point for every
    /// selected worker; on an own finished building below full hit points (<see cref="BuildingPicker"/>, M3-V2) a
    /// <c>Repair</c> of it; on an own site a <c>Build</c> of the site's type at its anchor (joins it: docs/03 M3-3); every
    /// other selected unit a <c>Move</c> there. Anywhere else, or with no worker, a plain <see cref="Order"/> Move. Shift
    /// queues them all. One Command sound; nothing if the whole order doesn't fit the command queue. The sim resolves the
    /// node or building at apply.
    /// </summary>
    public void ContextOrder(Vector2 point, bool queued)
    {
        if (_runner?.Simulation is not Simulation sim) return;
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return;
        var target = new System.Numerics.Vector2(point.X, point.Y);
        World world = sim.World;
        UnitStore u = world.Units;
        Selection.Prune(u.Alive, u.Generation);
        int workers = 0;
        foreach (EntityHandle h in Selection.Items)
            if (_isWorkerType[u.TypeId[h.Index]]) workers++;
        int site = -1;
        CommandKind work = workers == 0 ? CommandKind.Move : WorkAt(world, target, out site);
        if (work == CommandKind.Move)
        {
            Order(CommandKind.Move, point, queued);
            return;
        }
        if (sim.PendingCommandCount + Selection.Count > world.Config.CommandCapacity)
        {
            DroppedOrders++;
            GD.PushWarning($"{work} order for {Selection.Count} units dropped: command queue full.");
            return;
        }
        BuildingStore b = world.Buildings;
        int siteType = site >= 0 ? b.TypeId[site] : 0;
        System.Numerics.Vector2 anchorPoint = site >= 0 ? PlacementGhost.AnchorPoint(world.NavGrid, b.Cell[site]) : target;
        foreach (EntityHandle h in Selection.Items)
        {
            if (!_isWorkerType[u.TypeId[h.Index]]) sim.Enqueue(Command.Move(LocalPlayer, h, target, queued));
            else if (work == CommandKind.Gather) sim.Enqueue(Command.Gather(LocalPlayer, h, target, queued));
            else if (work == CommandKind.Repair) sim.Enqueue(Command.Repair(LocalPlayer, h, target, queued));
            else sim.Enqueue(Command.Build(LocalPlayer, h, siteType, anchorPoint, queued));
        }
        _issued[(int)work] += workers;
        _issued[(int)CommandKind.Move] += Selection.Count - workers;
        _sfx?.Play(SfxEvent.Command);
    }

    /// <summary>
    /// What selected workers do on a right click at a ground point: <see cref="CommandKind.Gather"/> on a live node,
    /// <see cref="CommandKind.Repair"/> on an own finished building below full hit points, <see cref="CommandKind.Build"/>
    /// on an own site (its slot in <paramref name="site"/>), else <see cref="CommandKind.Move"/>.
    /// </summary>
    public static CommandKind WorkAt(World world, System.Numerics.Vector2 point, out int site)
    {
        site = -1;
        if (NodeAt(world, point) >= 0) return CommandKind.Gather;
        BuildingStore b = world.Buildings;
        int slot = BuildingPicker.SlotAt(b, world.NavGrid, point);
        if (slot < 0 || b.Owner[slot] != LocalPlayer) return CommandKind.Move;
        if (b.UnderConstruction[slot])
        {
            site = slot;
            return CommandKind.Build;
        }
        return b.Hp[slot] < world.Data.Buildings[b.TypeId[slot]].Hp ? CommandKind.Repair : CommandKind.Move;
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

    /// <summary>The slot of the live resource node covering a ground point (sim x, y in meters), or -1 (<see cref="ResourcePicker"/>).</summary>
    public static int NodeAt(World world, System.Numerics.Vector2 point) =>
        ResourcePicker.NodeAtPoint(world.NavGrid, world.Data.Resources, world.Resources.Alive, world.Resources.TypeId, world.Resources.Cell, point);

    // The middle of the placeholder capsule: what the player sees and clicks.
    private static Vector3 BodyCentre(World world, int slot, float alpha) =>
        UnitViews.GroundPoint(world, slot, alpha) + new Vector3(0f, UnitViews.BodyHeight(world.Units.Radius[slot]) / 2f, 0f);

    private static System.Numerics.Vector2 ToNumerics(Vector2 v) => new(v.X, v.Y);

    private static int MaxKind()
    {
        int max = 0;
        foreach (CommandKind k in Enum.GetValues<CommandKind>()) max = Math.Max(max, (int)k);
        return max;
    }
}
