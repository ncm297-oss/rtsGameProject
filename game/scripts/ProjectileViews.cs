using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Projectiles in flight and the marks where they land (M4-V3).</summary>
/// <remarks>
/// <para><b>Shots.</b> Two pooled <see cref="MultiMesh"/>es sized to <c>World.Projectiles.Capacity</c>: aimed shots (bolts,
/// arrows, magic bolts) as a short streak along their flight in a light shade of the owner's colour, lobs (Catapult stones,
/// Sapper sharpers) as a small dark stone. Every frame each live slot gets one instance, written densely, at
/// <c>PrevPosition → Position</c> interpolated by the render alpha (clamped: never extrapolated, BUG-0184: a re-led bolt
/// can step 1.8 m in a tick). An aimed shot leaves its shooter at <see cref="HeightFor"/>(the shooter type's <c>scale</c>) above the
/// terrain and closes on its victim's (M7-V2: a 0.5 unit shoots from 0.6 m, a 3.0 unit from 3.6 m, linear in the share of the
/// flight flown; at scale 1 both ends are <see cref="LaunchHeight"/> and the shot flies level, exactly as before); a lob rides the
/// ground (<see cref="StoneRide"/>) plus its arc, <see cref="ProjectileTracker.ArcHeight"/> from the launch point the
/// <see cref="ProjectileTracker"/> recorded.</para>
/// <para><b>Marks.</b> Every <see cref="ProjectileImpact"/> (read between ticks from <see cref="SimRunner.Ticked"/>, so a
/// frame of several ticks misses none, and from the frame loop for scenes that tick the sim themselves) adds a mark to an
/// <see cref="ImpactMarks"/> ring (<see cref="ImpactMarks.DefaultCapacity"/>): a flash for a hit, a dust puff for a miss,
/// a burst for a lob sized by the widest splash of the attacks that throw it; a flash sits at its victim's height
/// (<see cref="HeightFor"/> of the victim type's <c>scale</c>; a building's is <see cref="LaunchHeight"/>). One instance per pool slot; a mark grows and
/// fades over its life; a free slot has a zero transform.</para>
/// <para><b>Fog</b> (M4-V4): with <see cref="Fog"/> set, a shot is drawn only while its current cell (<c>Position</c>'s) is
/// visible to the local player, and a mark only while its landing cell is (<see cref="FogView.ShowsPoint"/>). Combat may fire
/// at a unit the screen still hides for up to 4 ticks (docs/03 "Vision, detection, fog", Known limits (1)), so the rule is
/// the shot's own cell, not its target's. A hidden mark still counts as drawn, so it expires on time.</para>
/// <para><b>Who shot at whom</b> (M7-VH1, BUG-1179): the tick a shot is first seen, its ends come from the read-only
/// <see cref="ShotEnds"/> accessor: the launch height is its attacker type's (a tower's: <see cref="LaunchHeight"/>), exact
/// even when the shooter died the tick it fired, and the strike height its victim's type (<see cref="ShotEnds.VictimUnitType"/>:
/// the live unit or its corpse; a building or an unknowable victim: <see cref="LaunchHeight"/>). Each end's height is kept per
/// slot, and a landing's flash takes the victim height of the dead slot whose last aim point it is (none: <see cref="LaunchHeight"/>).
/// The view queries no spatial hash.</para>
/// Views hold no gameplay state: shots are redrawn from the store each frame; the tracker and the marks are presentation.
/// Everything is made in <see cref="Bind"/>, so a steady frame allocates nothing.
/// </remarks>
public partial class ProjectileViews : Node3D
{
    /// <summary>Height (m) above the terrain an aimed shot leaves a scale-1 shooter and strikes a scale-1 victim (about the chest).</summary>
    public const float LaunchHeight = 1.2f;

    /// <summary>The launch and hit height (m) of a unit drawn at <paramref name="scale"/>: <see cref="LaunchHeight"/> x scale (M7-V2).</summary>
    public static float HeightFor(float scale) => LaunchHeight * scale;

    /// <summary>An aimed shot's streak: length and thickness (m), larger than a real bolt so it reads at the default zoom.</summary>
    public const float StreakLength = 1.2f, StreakWidth = 0.12f;

    /// <summary>A lob's stone radius (m).</summary>
    public const float StoneRadius = 0.28f;

    /// <summary>How high (m) a lob's centre rides above the ground at launch and landing: under its radius, so it sits a little in the dirt.</summary>
    public const float StoneRide = 0.18f;

    /// <summary>Mark sizes (m): a flash's and a dust puff's radius at the start and end of their life.</summary>
    public const float FlashFrom = 0.25f, FlashTo = 0.55f, DustFrom = 0.2f, DustTo = 0.7f;

    /// <summary>A burst's final radius is this share of the widest splash among the attacks that throw the projectile (at least <see cref="BurstMin"/>).</summary>
    public const float BurstShare = 0.6f, BurstMin = 0.6f;

    /// <summary>How far an aimed streak's colour is lifted from its owner's colour toward white.</summary>
    public const float StreakLift = 0.55f;

    // Placeholder looks until the M6 art pass (M2-1 rule).
    private static readonly Color StoneColor = new(0.22f, 0.2f, 0.18f);
    private static readonly Color FlashColor = new(1f, 0.93f, 0.6f);
    private static readonly Color DustColor = new(0.6f, 0.53f, 0.42f);
    private static readonly Color BurstColor = new(1f, 0.55f, 0.15f);

    private SimRunner? _runner;
    private MultiMesh _aimed = null!, _lobs = null!, _marks = null!;
    private GameData _data = null!;
    private Color[] _streakColors = Array.Empty<Color>();
    private float[] _burstRadius = Array.Empty<float>();
    private int[] _drawnSlot = Array.Empty<int>();
    private Vector3[] _drawnAt = Array.Empty<Vector3>();
    private bool[] _drawnLob = Array.Empty<bool>();
    private int[] _added = Array.Empty<int>(), _removed = Array.Empty<int>();
    private Transform3D[] _markTransform = Array.Empty<Transform3D>();
    // M7-V2, per projectile slot: the shot's ends' heights, resolved the tick it was first seen, and what it was seen as.
    private float[] _scaleOf = Array.Empty<float>();
    private float[] _fromHeight = Array.Empty<float>(), _toHeight = Array.Empty<float>();
    private bool[] _known = Array.Empty<bool>();
    private System.Numerics.Vector2[] _knownLaunch = Array.Empty<System.Numerics.Vector2>(), _knownAim = Array.Empty<System.Numerics.Vector2>();
    private int[] _knownOwner = Array.Empty<int>(), _knownType = Array.Empty<int>();
    private EntityHandle[] _knownAttacker = Array.Empty<EntityHandle>(), _knownVictim = Array.Empty<EntityHandle>();
    private int[] _dead = Array.Empty<int>();
    private float[] _markHeight = Array.Empty<float>();
    private long _heightsTick = long.MinValue;
    private static readonly Transform3D Hidden = new(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Vector3.Zero);

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

    /// <summary>The fog whose visible cells the shots and marks are drawn in (M4-V4); null draws all of them.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>Shots in flight not drawn this frame because their cell is not visible.</summary>
    public int HiddenShots { get; private set; }

    /// <summary>Each slot's launch point and arc (view state).</summary>
    public ProjectileTracker Tracker { get; private set; } = null!;

    /// <summary>The landing marks' pool.</summary>
    public ImpactMarks Marks { get; private set; } = null!;

    /// <summary>Shots drawn this frame (aimed + lobs).</summary>
    public int Shown { get; private set; }

    /// <summary>Aimed streaks and lob stones drawn this frame.</summary>
    public int ShownAimed { get; private set; }

    /// <inheritdoc cref="ShownAimed"/>
    public int ShownLobs { get; private set; }

    /// <summary>The projectile slot of drawn shot <paramref name="k"/> (k below <see cref="Shown"/>).</summary>
    public int DrawnSlot(int k) => _drawnSlot[k];

    /// <summary>Where drawn shot <paramref name="k"/> is (its instance's origin).</summary>
    public Vector3 DrawnAt(int k) => _drawnAt[k];

    /// <summary>Whether drawn shot <paramref name="k"/> is a lob.</summary>
    public bool DrawnIsLob(int k) => _drawnLob[k];

    /// <summary>The transform written to mark instance <paramref name="slot"/> (a zero basis while unused).</summary>
    public Transform3D MarkTransform(int slot) => _markTransform[slot];

    /// <summary>The height (m) above the ground slot <paramref name="slot"/>'s aimed shot left at (its shooter's; <see cref="LaunchHeight"/> when unknown).</summary>
    public float ShotFromHeight(int slot) => (uint)slot < (uint)_fromHeight.Length ? _fromHeight[slot] : LaunchHeight;

    /// <summary>The height (m) above the ground slot <paramref name="slot"/>'s aimed shot strikes at (its victim's; <see cref="LaunchHeight"/> when unknown).</summary>
    public float ShotToHeight(int slot) => (uint)slot < (uint)_toHeight.Length ? _toHeight[slot] : LaunchHeight;

    /// <summary>The height (m) above the ground mark <paramref name="slot"/>'s flash is drawn at (its victim's).</summary>
    public float MarkFlashHeight(int slot) => (uint)slot < (uint)_markHeight.Length ? _markHeight[slot] : LaunchHeight;

    /// <summary>The burst's final radius (m) for projectile type <paramref name="type"/>.</summary>
    public float BurstRadius(int type) => (uint)type < (uint)_burstRadius.Length ? _burstRadius[type] : BurstMin;

    /// <summary>The aimed, lob and mark multimeshes.</summary>
    public MultiMesh AimedMesh => _aimed;

    /// <inheritdoc cref="AimedMesh"/>
    public MultiMesh LobMesh => _lobs;

    /// <inheritdoc cref="AimedMesh"/>
    public MultiMesh MarkMesh => _marks;

    /// <summary>Creates the meshes, materials, tracker and mark pool; call once before the first <see cref="Sync"/>.</summary>
    /// <param name="playerRgb">Per player, its colour (0xRRGGBB), lifted for its aimed shots.</param>
    public void Bind(GameData data, int projectileCapacity, uint[] playerRgb, int markCapacity = ImpactMarks.DefaultCapacity)
    {
        _data = data;
        _streakColors = new Color[playerRgb.Length];
        for (int p = 0; p < playerRgb.Length; p++) _streakColors[p] = UnitViews.ColorFromRgb(playerRgb[p]).Lerp(Colors.White, StreakLift);
        // A burst is sized by the splash of the attacks that throw that projectile (data), not by a C# table.
        _burstRadius = new float[data.Projectiles.Length];
        foreach (UnitDef u in data.Units)
        {
            int p = u.Attack.ProjectileTypeId;
            if ((uint)p < (uint)_burstRadius.Length) _burstRadius[p] = Math.Max(_burstRadius[p], u.Attack.Splash * BurstShare);
        }
        for (int p = 0; p < _burstRadius.Length; p++) _burstRadius[p] = Math.Max(_burstRadius[p], BurstMin);

        Tracker = new ProjectileTracker(projectileCapacity);
        _scaleOf = new float[data.Units.Length];
        for (int t = 0; t < _scaleOf.Length; t++) _scaleOf[t] = data.Units[t].Scale;
        _fromHeight = new float[projectileCapacity];
        _toHeight = new float[projectileCapacity];
        Array.Fill(_fromHeight, LaunchHeight);
        Array.Fill(_toHeight, LaunchHeight);
        _known = new bool[projectileCapacity];
        _knownLaunch = new System.Numerics.Vector2[projectileCapacity];
        _knownAim = new System.Numerics.Vector2[projectileCapacity];
        _knownOwner = new int[projectileCapacity];
        _knownType = new int[projectileCapacity];
        _knownAttacker = new EntityHandle[projectileCapacity];
        _knownVictim = new EntityHandle[projectileCapacity];
        _dead = new int[projectileCapacity];
        _heightsTick = long.MinValue;
        _drawnSlot = new int[projectileCapacity];
        _drawnAt = new Vector3[projectileCapacity];
        _drawnLob = new bool[projectileCapacity];
        var streakMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _aimed = Multi("AimedShots", new BoxMesh { Size = new Vector3(StreakWidth, StreakWidth, StreakLength), Material = streakMat }, projectileCapacity, colors: true);
        _lobs = Multi("LobShots", new SphereMesh { Radius = StoneRadius, Height = 2f * StoneRadius, RadialSegments = 8, Rings = 4, Material = new StandardMaterial3D { AlbedoColor = StoneColor, Roughness = 1f } }, projectileCapacity, colors: false);
        _aimed.VisibleInstanceCount = 0;
        _lobs.VisibleInstanceCount = 0;

        Marks = new ImpactMarks(markCapacity);
        _added = new int[Math.Max(projectileCapacity, markCapacity)];
        _removed = new int[markCapacity];
        _markTransform = new Transform3D[markCapacity];
        _markHeight = new float[markCapacity];
        Array.Fill(_markHeight, LaunchHeight);
        var markMat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _marks = Multi("ImpactMarks", new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6, Material = markMat }, markCapacity, colors: true);
        for (int i = 0; i < markCapacity; i++)
        {
            _marks.SetInstanceTransform(i, Hidden);
            _markTransform[i] = Hidden;
        }
    }

    public override void _ExitTree() => Runner = null;

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha);
    }

    // Each tick the runner runs: see the store's slots and take the landings while they are still in World.Impacts.
    private void OnTicked(Simulation sim) => CollectTick(sim.World);

    /// <summary>Brings the tracker up to the last tick and adds the marks for its landings (once per tick; a later call for the same tick does nothing).</summary>
    public void CollectTick(World world)
    {
        if (Tracker == null) return;
        bool fresh = world.TickNumber != _heightsTick;
        _heightsTick = world.TickNumber;
        int dead = fresh ? DropDead(world.Projectiles) : 0;
        Tracker.Observe(world.Projectiles, _data.Projectiles.AsSpan(), world.TickNumber);
        if (fresh) ObserveHeights(world);
        // New marks need no write here: Sync redraws every active mark each frame (they grow and fade).
        int added = Marks.Collect(world.Impacts, _data.Projectiles.AsSpan(), world.TickNumber, _added);
        if (added > 0) FlashHeights(world, Math.Min(added, _added.Length), dead);
    }

    // The slots this view knew last tick whose shot is gone now (landed or lost) go to _dead, for the landings' flashes.
    private int DropDead(ProjectileStore store)
    {
        ReadOnlySpan<bool> alive = store.Alive;
        int n = 0;
        for (int i = 0; i < alive.Length && i < _known.Length; i++)
        {
            if (!_known[i] || alive[i]) continue;
            _known[i] = false;
            _dead[n++] = i;
        }
        return n;
    }

    // Each live shot first seen this tick gets its ends' heights (see the remarks); every live shot's aim point is kept. A slot
    // holds a new shot when its shooter, victim, launch, owner or type differ from what was seen there (a skipping observer).
    private void ObserveHeights(World world)
    {
        ProjectileStore s = world.Projectiles;
        ReadOnlySpan<bool> alive = s.Alive;
        ReadOnlySpan<System.Numerics.Vector2> target = s.Target;
        ReadOnlySpan<int> owner = s.Owner, type = s.ProjectileTypeId;
        ReadOnlySpan<System.Numerics.Vector2> launch = Tracker.Launch;
        for (int i = 0; i < alive.Length && i < _known.Length; i++)
        {
            if (!alive[i]) continue;
            ShotEnds ends = ShotEnds.Of(s, i);
            if (!_known[i] || launch[i] != _knownLaunch[i] || owner[i] != _knownOwner[i] || type[i] != _knownType[i]
                || ends.Attacker != _knownAttacker[i] || ends.Victim != _knownVictim[i])
            {
                _known[i] = true;
                _knownLaunch[i] = launch[i];
                _knownOwner[i] = owner[i];
                _knownType[i] = type[i];
                _knownAttacker[i] = ends.Attacker;
                _knownVictim[i] = ends.Victim;
                int shooter = ends.ShooterUnitType, victim = ends.VictimUnitType(world.Units.Alive, world.Units.Generation, world.Units.TypeId);
                _fromHeight[i] = shooter >= 0 ? HeightFor(ScaleOf(shooter)) : LaunchHeight;
                _toHeight[i] = victim >= 0 ? HeightFor(ScaleOf(victim)) : LaunchHeight;
            }
            _knownAim[i] = target[i];
        }
    }

    private float ScaleOf(int type) => (uint)type < (uint)_scaleOf.Length ? _scaleOf[type] : 1f;

    // A landing's flash sits at its shot's victim height: the dead slot of the same owner and type whose last aim point the
    // landing is (an aimed shot is not steered on its landing tick, so the two are equal); a landing no known slot matches
    // (only an observer that skipped the shot's whole flight) sits at LaunchHeight.
    private void FlashHeights(World world, int added, int dead)
    {
        ReadOnlySpan<ProjectileImpact> impacts = world.Impacts;
        for (int k = 0; k < added && k < impacts.Length; k++)
        {
            int mark = _added[k];
            if ((uint)mark >= (uint)_markHeight.Length) continue;
            ProjectileImpact im = impacts[k];
            float h = LaunchHeight;
            for (int d = 0; d < dead; d++)
            {
                int slot = _dead[d];
                if (slot < 0 || _knownAim[slot] != im.Position || _knownOwner[slot] != im.Owner || _knownType[slot] != im.ProjectileTypeId) continue;
                h = _toHeight[slot];
                _dead[d] = -1; // one landing per shot
                break;
            }
            _markHeight[mark] = h;
        }
    }

    /// <summary>One frame: the last tick's landings, expired marks, every live shot and every mark. No allocation.</summary>
    public void Sync(World world, float alpha)
    {
        if (Tracker == null) return;
        CollectTick(world);
        float a = float.IsNaN(alpha) ? 1f : Math.Clamp(alpha, 0f, 1f);
        int gone = Marks.Expire(world.TickNumber, _removed);
        for (int k = 0; k < gone && k < _removed.Length; k++) HideMark(_removed[k]);
        if (gone > _removed.Length)
            for (int i = 0; i < Marks.Capacity; i++) if (!Marks.Active[i]) HideMark(i);
        FogView? fog = Fog?.Refreshed(world);
        SyncShots(world, a, fog);
        SyncMarks(world, a, fog);
    }

    private void SyncShots(World world, float a, FogView? fog)
    {
        ProjectileStore s = world.Projectiles;
        ReadOnlySpan<bool> alive = s.Alive;
        ReadOnlySpan<System.Numerics.Vector2> pos = s.Position, prev = s.PrevPosition, target = s.Target;
        ReadOnlySpan<int> owner = s.Owner;
        int nAimed = 0, nLobs = 0, shown = 0, hidden = 0;
        for (int i = 0; i < alive.Length; i++)
        {
            if (!alive[i]) continue;
            if (fog != null && !fog.ShowsPoint(world.Fog, pos[i]))
            {
                hidden++;
                continue;
            }
            System.Numerics.Vector2 at = System.Numerics.Vector2.Lerp(prev[i], pos[i], a);
            float ground = TerrainHeight.At(world.Heightmap, at.X, at.Y);
            bool lob = Tracker.IsLob[i];
            Vector3 origin;
            if (lob)
            {
                origin = new Vector3(at.X, ground + StoneRide + Tracker.ArcHeight(i, at), at.Y);
                _lobs.SetInstanceTransform(nLobs++, new Transform3D(Basis.Identity, origin));
            }
            else
            {
                System.Numerics.Vector2 d = pos[i] - prev[i];
                if (d.LengthSquared() < 1e-8f) d = target[i] - pos[i];
                float yaw = d.LengthSquared() < 1e-8f ? 0f : MathF.Atan2(d.X, d.Y);
                origin = new Vector3(at.X, ground + AimedHeight(i, at), at.Y);
                _aimed.SetInstanceTransform(nAimed, new Transform3D(new Basis(Vector3.Up, yaw), origin));
                int o = owner[i];
                _aimed.SetInstanceColor(nAimed, (uint)o < (uint)_streakColors.Length ? _streakColors[o] : Colors.White);
                nAimed++;
            }
            _drawnSlot[shown] = i;
            _drawnAt[shown] = origin;
            _drawnLob[shown] = lob;
            shown++;
        }
        if (nAimed != ShownAimed) _aimed.VisibleInstanceCount = nAimed;
        if (nLobs != ShownLobs) _lobs.VisibleInstanceCount = nLobs;
        ShownAimed = nAimed;
        ShownLobs = nLobs;
        Shown = shown;
        HiddenShots = hidden;
    }

    private void SyncMarks(World world, float a, FogView? fog)
    {
        ImpactMarks m = Marks;
        ReadOnlySpan<bool> active = m.Active;
        for (int i = 0; i < active.Length; i++)
        {
            if (!active[i]) continue;
            if (fog != null && !fog.ShowsPoint(world.Fog, m.Position[i]))
            {
                if (_markTransform[i] != Hidden) HideMark(i);
                m.MarkDrawn(i); // its frame has passed: it expires on time, unseen
                continue;
            }
            float age = m.Age(i, world.TickNumber, a);
            ImpactMarkKind kind = m.Kind[i];
            float from, to;
            Color c;
            switch (kind)
            {
                case ImpactMarkKind.Flash: from = FlashFrom; to = FlashTo; c = FlashColor; break;
                case ImpactMarkKind.Dust: from = DustFrom; to = DustTo; c = DustColor; break;
                default:
                    to = BurstRadius(m.ProjectileType[i]);
                    from = to * 0.3f;
                    c = BurstColor;
                    break;
            }
            float r = from + (to - from) * age;
            System.Numerics.Vector2 p = m.Position[i];
            float ground = TerrainHeight.At(world.Heightmap, p.X, p.Y);
            // A flash sits where an aimed shot flies; dust and bursts hug the ground (flattened).
            float y = kind == ImpactMarkKind.Flash ? ground + _markHeight[i] : ground + r * 0.25f;
            float flat = kind == ImpactMarkKind.Flash ? 1f : 0.5f;
            _markTransform[i] = new Transform3D(Basis.FromScale(new Vector3(r, r * flat, r)), new Vector3(p.X, y, p.Y));
            _marks.SetInstanceTransform(i, _markTransform[i]);
            _marks.SetInstanceColor(i, new Color(c.R, c.G, c.B, 0.85f * (1f - age)));
            m.MarkDrawn(i);
        }
    }

    // An aimed shot's height above the ground at `at`: its shooter's at launch to its victim's at the aim point, linear in the
    // share flown; level (exactly the one height) when both ends match, as in every scale-1 fight.
    private float AimedHeight(int slot, System.Numerics.Vector2 at)
    {
        float from = _fromHeight[slot], to = _toHeight[slot];
        if (from == to) return from;
        float length = Tracker.Length[slot];
        float t = length > 0f ? System.Numerics.Vector2.Distance(at, Tracker.Launch[slot]) / length : 1f;
        t = t > 0f ? MathF.Min(t, 1f) : 0f; // NaN to 0
        return from + (to - from) * t;
    }

    private void HideMark(int slot)
    {
        _marks.SetInstanceTransform(slot, Hidden);
        _markTransform[slot] = Hidden;
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
}
