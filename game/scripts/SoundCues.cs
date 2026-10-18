using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Turns what the sim did into sound events (M6-V12): the world sounds for what the local player sees on screen, and the own-production cues.</summary>
/// <remarks>
/// Reads the sim only, after every tick the runner runs (<see cref="SimRunner.Ticked"/>, so a frame of several ticks misses
/// none) and once a frame; plays through <see cref="Sfx"/>, at most once per event per frame (the per-event gap and the
/// world cap are Sfx's).
/// <list type="bullet">
/// <item><b>World events</b>, each only when its position is on screen (<see cref="ScreenCheck"/>) and visible in the local
/// fog: <see cref="SfxEvent.UnitDeath"/> / <see cref="SfxEvent.BuildingRazed"/> from <c>World.Deaths</c> (a summon's expiry is
/// silent); <see cref="SfxEvent.Shot"/> for a projectile launched (a slot its own <see cref="ProjectileTracker"/> starts);
/// <see cref="SfxEvent.Cast"/> for a resolved <c>World.AbilityEvents</c> entry at its point; <see cref="SfxEvent.MeleeHit"/>
/// once a frame for a unit the hit flash saw hit (<see cref="HitFlash.WasHit"/>), shown and on screen, with no projectile
/// impact landing within <see cref="ImpactNearMeters"/> of it since the last frame (an arrow's hit is the Shot's).</item>
/// <item><b>The local player's own</b>, always: <see cref="SfxEvent.BuildingPlaced"/> when a construction site of theirs
/// appears, <see cref="SfxEvent.BuildingDone"/> when one finishes, <see cref="SfxEvent.Ready"/> when a production queue of
/// theirs shrinks on a tick that also brings a new own unit or a new own tech (a cancel shrinks it with neither).</item>
/// </list>
/// Holds no gameplay state: last-seen generations and counts only.
/// </remarks>
public partial class SoundCues : Node
{
    /// <summary>A hit with a projectile impact this close (m) since the last frame is the shot's, not a melee blow.</summary>
    public const float ImpactNearMeters = 1.5f;

    private const int ImpactBuffer = 128;

    private SimRunner? _runner;
    private Sfx? _sfx;
    private RtsCamera? _camera;
    private FogOfWar? _fog;
    private UnitViews? _units;
    private int _player;
    private ProjectileTracker? _shots;
    private bool[] _wasTracked = Array.Empty<bool>();
    private System.Numerics.Vector2[] _lastLaunch = Array.Empty<System.Numerics.Vector2>();
    private int[] _unitGen = Array.Empty<int>(), _buildingGen = Array.Empty<int>(), _queue = Array.Empty<int>();
    private bool[] _site = Array.Empty<bool>();
    private int _techs;
    private readonly bool[] _pending = new bool[Enum.GetValues<SfxEvent>().Length];
    private readonly int[] _cued = new int[Enum.GetValues<SfxEvent>().Length];
    private readonly int[] _unseen = new int[Enum.GetValues<SfxEvent>().Length];
    private readonly System.Numerics.Vector2[] _impacts = new System.Numerics.Vector2[ImpactBuffer];
    private int _impactCount;

    /// <summary>Times <paramref name="e"/> was handed to <see cref="Sfx"/> (once a frame at most; Sfx may still drop it).</summary>
    public int Cued(SfxEvent e) => _cued[(int)e];

    /// <summary>World events of kind <paramref name="e"/> not cued because they were off screen or not visible.</summary>
    public int Unseen(SfxEvent e) => _unseen[(int)e];

    /// <summary>Connects the cues to the match: the sim's runner, the effects, the camera and the local fog (null: everything visible), the unit views' hit flash.</summary>
    public void Init(SimRunner runner, Sfx sfx, RtsCamera camera, FogOfWar? fog, UnitViews units, int player)
    {
        _sfx = sfx;
        _camera = camera;
        _fog = fog;
        _units = units;
        _player = player;
        World w = runner.Simulation!.World;
        _shots = new ProjectileTracker(w.Projectiles.Capacity);
        _wasTracked = new bool[w.Projectiles.Capacity];
        _lastLaunch = new System.Numerics.Vector2[w.Projectiles.Capacity];
        _unitGen = new int[w.Units.Capacity];
        _buildingGen = new int[w.Buildings.Capacity];
        _queue = new int[w.Buildings.Capacity];
        _site = new bool[w.Buildings.Capacity];
        Array.Fill(_unitGen, -1);
        Array.Fill(_buildingGen, -1);
        // What stands now is the starting state, not news.
        ScanOwn(w, out _, out _, out _, out _, out _);
        _techs = OwnTechs(w);
        if (_runner != null) _runner.Ticked -= OnTicked;
        _runner = runner;
        _runner.Ticked += OnTicked;
    }

    public override void _ExitTree()
    {
        if (_runner != null) _runner.Ticked -= OnTicked;
        _runner = null;
    }

    private void OnTicked(Simulation sim)
    {
        World w = sim.World;
        FogView? fog = _fog?.Refreshed(w);
        foreach (DeathEvent d in w.Deaths)
            if (!d.Expired) Cue(d.IsBuilding ? SfxEvent.BuildingRazed : SfxEvent.UnitDeath, d.Position, w, fog);
        if (_shots == null) return;
        _shots.Observe(w.Projectiles, w.Data.Projectiles.AsSpan(), w.TickNumber);
        ReadOnlySpan<bool> tracked = _shots.Tracked;
        ReadOnlySpan<System.Numerics.Vector2> launch = _shots.Launch;
        for (int i = 0; i < tracked.Length; i++)
        {
            if (tracked[i] && (!_wasTracked[i] || launch[i] != _lastLaunch[i])) Cue(SfxEvent.Shot, launch[i], w, fog);
            _wasTracked[i] = tracked[i];
            _lastLaunch[i] = launch[i];
        }
        foreach (ProjectileImpact im in w.Impacts)
            if (im.Hit && _impactCount < _impacts.Length) _impacts[_impactCount++] = im.Position;
        foreach (AbilityEvent a in w.AbilityEvents)
            if (a.Resolved && !a.Dropped) Cue(SfxEvent.Cast, a.Point, w, fog);
        ScanOwn(w, out bool placed, out bool done, out bool queueFell, out bool newUnit, out _);
        int techs = OwnTechs(w);
        bool newTech = techs > _techs;
        _techs = techs;
        if (placed) _pending[(int)SfxEvent.BuildingPlaced] = true;
        if (done) _pending[(int)SfxEvent.BuildingDone] = true;
        if (queueFell && (newUnit || newTech)) _pending[(int)SfxEvent.Ready] = true;
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is not Simulation sim || _sfx == null || _units == null) return;
        World w = sim.World;
        FogView? fog = _fog?.Refreshed(w);
        HitFlash flash = _units.Flash;
        UnitStore u = w.Units;
        int n = Math.Min(flash.Capacity, u.Capacity);
        for (int i = 0; i < n; i++)
        {
            if (!flash.WasHit(i) || !u.Alive[i] || (fog != null && !fog.ShowsUnit(i)) || NearImpact(u.Position[i])) continue;
            if (!ScreenCheck.OnScreen(_camera!, w.Heightmap, u.Position[i])) continue;
            _pending[(int)SfxEvent.MeleeHit] = true;
            break;
        }
        _impactCount = 0;
        for (int e = 0; e < _pending.Length; e++)
        {
            if (!_pending[e]) continue;
            _pending[e] = false;
            _cued[e]++;
            _sfx.Play((SfxEvent)e);
        }
    }

    private bool NearImpact(System.Numerics.Vector2 at)
    {
        for (int k = 0; k < _impactCount; k++)
            if (System.Numerics.Vector2.DistanceSquared(_impacts[k], at) <= ImpactNearMeters * ImpactNearMeters) return true;
        return false;
    }

    // A world event: pending for this frame if its point is visible and on screen, else counted unseen.
    private void Cue(SfxEvent e, System.Numerics.Vector2 at, World w, FogView? fog)
    {
        if (_pending[(int)e]) return;
        if ((fog == null || fog.ShowsPoint(w.Fog, at)) && _camera != null && ScreenCheck.OnScreen(_camera, w.Heightmap, at)) _pending[(int)e] = true;
        else _unseen[(int)e]++;
    }

    // The local player's buildings and units against the last tick: a new site, a finished one, a shorter queue, a new unit.
    private void ScanOwn(World w, out bool placed, out bool done, out bool queueFell, out bool newUnit, out bool newBuilding)
    {
        placed = done = queueFell = newUnit = newBuilding = false;
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
        {
            if (!b.Alive[i] || b.Owner[i] != _player)
            {
                _buildingGen[i] = -1;
                continue;
            }
            bool site = b.UnderConstruction[i];
            if (b.Generation[i] != _buildingGen[i])
            {
                _buildingGen[i] = b.Generation[i];
                newBuilding = true;
                if (site) placed = true;
            }
            else
            {
                if (_site[i] && !site) done = true;
                if (b.QueueCount[i] < _queue[i]) queueFell = true;
            }
            _site[i] = site;
            _queue[i] = b.QueueCount[i];
        }
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != _player)
            {
                _unitGen[i] = -1;
                continue;
            }
            if (u.Generation[i] != _unitGen[i])
            {
                _unitGen[i] = u.Generation[i];
                newUnit = true;
            }
        }
    }

    private int OwnTechs(World w)
    {
        int n = 0;
        for (int t = 0; t < w.Data.Techs.Length; t++)
            if (w.HasTech(_player, t)) n++;
        return n;
    }
}
