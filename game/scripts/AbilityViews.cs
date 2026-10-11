using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The ability views (M4-V6a, M4-V6b, M4-V6c, M4-V6d, M5-V1): the targeting rings while an ability is armed, the casts on the map, the status markers over units, the resolve flashes, the zone discs and the lost-cast cues.</summary>
/// <remarks>
/// <para><b>Targeting.</b> While <see cref="SelectionController.TargetAbility"/> is set, the ground under the cursor
/// (<see cref="SelectionController.AbilityPoint"/>) gets a ring of the ability's <c>radius</c> (where the effects would land)
/// and the selected unit that would cast there (<see cref="SelectionController.PickCaster"/>: the nearest ready one) a ring of
/// its <c>range</c>. Each ring is a <see cref="GroundDisc"/> band <see cref="RingWidth"/> wide whose outer radius is the def's
/// value, drawn on the terrain cells under it (M4-VH3, BUG-0430 / BUG-0462 item 1: on both sides of a cliff); its mesh is
/// rebuilt only when its centre moves. M4-V6d: a no-target (<see cref="AbilityKind.SelfAura"/>, and
/// <see cref="AbilityKind.Summon"/> since M4-V7: <see cref="AbilityCaster.IsNoTarget"/>) ability is never armed (its button
/// casts at once); while the cursor rests on its button the radius ring is drawn round the unit the press would cast with
/// (<see cref="SelectionController.SelfCaster"/>) and no range ring; an ability of radius 0 (a summon) draws no ring.</para>
/// <para><b>Casts.</b> Every unit the screen shows (the fog's shown list, M4-V4) standing in <see cref="UnitState.Casting"/>
/// gets a cast bar over its hp bar's place: the share of the def's <c>castTime</c> stood (<see cref="AbilityCaster.Progress"/>
/// of <c>UnitStore.CastTicks</c>). Every own unit with a cast in hand (<c>CastAbility</c> set: walking into range or standing)
/// gets a ring of the ability's radius on its <c>CastPoint</c>, so the player sees where the walking mage will cast. The bars
/// are two <see cref="MultiMesh"/>es with one instance per unit slot, written densely each frame; each cast-point ring is a
/// <see cref="GroundDisc"/> band from a <see cref="GroundDiscPool"/> keyed by the caster's slot (M5-V1, BUG-0520: on the
/// terrain on both sides of a cliff), rebuilt only when its cast point changes. M4-V6b (BUG-0342):
/// the bar is thicker, its back a dim violet rather than black and its fill never shorter than it is thick, so it reads as a
/// bar from the cast's first tick.</para>
/// <para><b>Statuses</b> (M4-V6b). Every unit the screen shows gets one small marker per active status
/// (<see cref="StatusMarkers.Collect"/>), side by side above its cast bar's place, in a colour by the status's
/// <see cref="StatusKind"/> (Burning flame, Slowed deep blue, Blinded dark violet since M4-V6c, Frenzied hot magenta since M4-V6d, any other kind pale). One coloured <see cref="MultiMesh"/>
/// sized for every slot's <see cref="StatusStore.PerUnit"/> entries, written densely each frame.</para>
/// <para><b>Resolve flashes</b> (M4-V6b). Every resolved cast in <c>World.AbilityEvents</c> (read from
/// <see cref="SimRunner.Ticked"/>, so a frame of several ticks misses none, and from the frame loop for scenes that tick the
/// sim themselves) adds a <see cref="ResolveFlashes"/> entry: a disc on the ground at the cast point that grows to the
/// ability's <c>radius</c> and fades over <see cref="ResolveFlashes.LifetimeTicks"/> (0.5 s) from its first frame, drawn
/// only when the point's cell was visible on that frame (<see cref="FogView.ShowsPoint"/>, decided once: M4-VH2, BUG-0371).
/// A cast start draws no flash. M5-V1 (BUG-0520): each drawn flash is a pooled <see cref="GroundDisc"/> meshed once at the
/// full radius on the terrain cells under it; it grows through <see cref="GroundDisc.SetScale"/>, not a rebuild.</para>
/// <para><b>Lost casts</b> (M5-V1). Every own cast the sim drops without a new order (an <see cref="AbilityEvent"/> with
/// <c>Dropped</c>: a walk-in stuck for 3 s, or the caster taken off its cast) adds a <see cref="LostCastCues"/> entry: a red
/// disc on the ground under the caster and the <c>ui.json</c> <c>hud.cast_lost</c> words over it, fading over
/// <see cref="LostCastCues.LifetimeTicks"/> (0.5 s), and one <see cref="SfxEvent.Refused"/> sound, whatever is selected. The
/// cast bar and cast-point ring end on their own (the sim cleared the cast). A cast the player cancels with a new order
/// records no event and gets no cue.</para>
/// <para><b>Zone discs</b> (M4-V6c). Every live zone the local player may see (<see cref="ZoneDiscs.Collect"/>: its own
/// always, another player's only where a cell within its radius plus 2 m is visible; every zone with <c>--no-fog</c>) is a
/// translucent sand-coloured disc of the zone's radius lying on the terrain (M4-VH3: a <see cref="GroundDisc"/> on the cells
/// it covers, so across a cliff it lies on the plateau and on the low ground, BUG-0430), with a rim in a light teal for an
/// own zone (BUG-0431) and in the owner's colour for an enemy's, fading over the zone's last second. One node per
/// <see cref="ZoneStore"/> slot, its mesh built once when its zone first shows; a node is hidden the frame its zone is freed
/// or the rule stops showing it.</para>
/// Views hold no gameplay state: everything is redrawn from the store and the selection each frame. Every node and pool is
/// made in <see cref="_Ready"/> and <see cref="Bind"/> (a ring's quad buffer grows once per larger radius; a disc pool grows
/// only past its high mark), so a steady frame allocates nothing.
/// </remarks>
public partial class AbilityViews : Node3D
{
    // Cached so per-frame and per-click lookups do not allocate a StringName from a string (BUG-0341).
    private static readonly StringName OrderQueue = "order_queue";

    /// <summary>Width of a targeting ring's band, and of a cast ring's band as a share of its radius, in meters.</summary>
    public const float RingWidth = 0.22f, CastRingShare = 0.08f;

    /// <summary>Height of the rings above the ground under them (the target ring sits at 0.09), in meters.</summary>
    public const float Lift = 0.12f;

    /// <summary>The cast bar's length and thickness at zoom up to <see cref="CombatViews.BarFullZoom"/>, and its height over the hp bar's place, in meters.</summary>
    public const float BarLength = 1.2f, BarThickness = 0.2f, BarAbove = 0.3f;

    /// <summary>A status marker's size, the gap between neighbours' centres, and its height over the hp bar's place, in meters (grown with the zoom as the bars are).</summary>
    public const float MarkerSize = 0.26f, MarkerSpacing = 0.34f, MarkerAbove = 0.62f;

    /// <summary>A resolve flash's disc starts at this share of the ability's radius and reaches the full radius at <see cref="FlashGrowShare"/> of its life.</summary>
    public const float FlashFrom = 0.5f, FlashGrowShare = 0.4f;

    /// <summary>A resolve flash's opacity at the start of its life (it fades to 0).</summary>
    public const float FlashAlpha = 0.75f;

    /// <summary>A zone disc's rim band (m) and its height above the ground under it (m; just under the flashes' and rings' <see cref="Lift"/>, so a flash inside a storm draws on top).</summary>
    public const float ZoneRimWidth = 0.35f, ZoneLift = 0.1f;

    /// <summary>A zone disc's rim is this many times as opaque as its fill (capped at 1), and fades with it.</summary>
    public const float RimBoost = 2.2f;

    // UI tints until the M6 art pass: the area reads fire-orange, the reach pale blue, the bar violet (magic).
    private static readonly Color RadiusColor = new(1f, 0.55f, 0.1f);
    private static readonly Color RangeColor = new(0.6f, 0.8f, 1f);
    private static readonly Color CastRingColor = new(1f, 0.4f, 0.05f);
    private static readonly Color CastRingFill = new(1f, 0.4f, 0.05f, 0f);
    // BUG-0342: a black back read as an empty dash; a dim violet back reads as the bar's empty part.
    private static readonly Color BarBackColor = new(0.28f, 0.2f, 0.38f);
    private static readonly Color BarFillColor = new(0.85f, 0.55f, 1f);

    /// <summary>The Burning (damage over time) marker's colour: flame.</summary>
    public static readonly Color DamageOverTimeColor = new(1f, 0.42f, 0.05f);

    /// <summary>The Slowed (slow) marker's colour: a deep saturated blue, so it reads on sand and pale bodies at zoom 30 (BUG-0371 a; was a pale blue-grey; a taste default until the M6 art pass).</summary>
    public static readonly Color SlowColor = new(0.08f, 0.3f, 0.95f);

    /// <summary>The Blinded (blind) marker's colour (M4-V6c): a dark violet, apart from Burning's orange and Slowed's blue on sand and on both factions' bodies at zoom 30 (a taste default until the M6 art pass).</summary>
    public static readonly Color BlindColor = new(0.5f, 0.06f, 0.62f);

    /// <summary>The Frenzied (frenzied) marker's colour (M4-V6d): a hot magenta, at least 0.4 apart in some channel from Burning's orange, Slowed's blue, Blinded's violet and the pale default, and off the Whirlwind's crimson accent (a taste default until the M6 art pass).</summary>
    public static readonly Color FrenzyColor = new(1f, 0.2f, 0.75f);

    /// <summary>A marker of any other status kind: pale.</summary>
    public static readonly Color OtherStatusColor = new(0.92f, 0.92f, 0.88f);

    /// <summary>A zone disc's fill (its alpha is the disc's, <see cref="ZoneDiscs.Alpha"/>): sand and dust.</summary>
    public static readonly Color ZoneFillColor = new(0.6f, 0.45f, 0.26f);

    /// <summary>An own zone's rim: a light teal, at least 0.4 apart in some channel from every faction's colour (an enemy zone's rim), so a mirror match's own and enemy storms read apart (BUG-0431; was a deeper sand within 0.064 of the Whirlwind's gold). A taste default until the M6 team-colour pass.</summary>
    public static readonly Color OwnZoneRimColor = new(0.2f, 0.8f, 0.9f);

    /// <summary>An enemy zone's rim when its owner has no colour (no player colours bound): red.</summary>
    public static readonly Color EnemyZoneRimColor = new(0.85f, 0.15f, 0.1f);

    /// <summary>The resolve flash's colour (its alpha fades).</summary>
    public static readonly Color FlashColor = new(1f, 0.78f, 0.35f);

    /// <summary>A resolve flash's rim band, as a share of its drawn radius, and how many times as opaque as the fill it is (a slightly stronger edge).</summary>
    public const float FlashRimShare = 0.12f, FlashRimBoost = 1.3f;

    /// <summary>The lost-cast cue's red (M5-V1; its alpha fades). A taste default until the M6 art pass.</summary>
    public static readonly Color CueColor = new(0.95f, 0.08f, 0.06f);

    /// <summary>The lost-cast cue's words' colour: a light red that reads over the ground (the placement refusal's tint).</summary>
    public static readonly Color CueTextColor = new(1f, 0.62f, 0.58f);

    /// <summary>A lost-cast cue's disc reaches this far past the caster's body (m), its fill opacity at the start, its rim band's share of its radius, and its height over the ground (m; over the rings and flashes).</summary>
    public const float CueMargin = 0.9f, CueFillAlpha = 0.55f, CueRimShare = 0.25f, CueLift = 0.14f;

    /// <summary>The lost-cast cue's words sit this far over the caster's cast bar's place (m, grown with the zoom as the bars are).</summary>
    public const float CueTextAbove = 1.1f;

    /// <summary>Disc nodes each pool makes up front (cast rings, flashes, cues); a pool doubles past it only when a frame needs more.</summary>
    public const int CastRingNodes = 16, FlashNodes = 16, CueNodes = 4;

    private static readonly Transform3D Hidden = new(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Vector3.Zero);

    private SimRunner? _runner;
    private SelectionController? _sel;
    private GameData _data = null!;
    private GroundDisc _range = null!, _radius = null!;
    private float _shownRadius, _shownRange;
    private MultiMesh _barBack = null!, _barFill = null!;
    private GroundDiscPool? _castRings, _flashDiscs, _cueDiscs;
    private Label3D[] _cueLabels = Array.Empty<Label3D>();
    private int[] _cueSlot = Array.Empty<int>(), _cueRemoved = Array.Empty<int>();
    private bool[] _cueShown = Array.Empty<bool>();
    private long _cuesSounded;
    private string _castLostText = "";
    private float[] _bodyTop = Array.Empty<float>();
    private int[] _barSlot = Array.Empty<int>(), _circleSlot = Array.Empty<int>();
    private float[] _barFillShown = Array.Empty<float>(), _barFillLength = Array.Empty<float>();
    private float _barScale = 1f;
    private MultiMesh _markers = null!;
    private StatusMark[] _marks = Array.Empty<StatusMark>();
    private Vector3[] _markAt = Array.Empty<Vector3>();
    private Color[] _statusColor = Array.Empty<Color>();
    private Transform3D[] _flashTransform = Array.Empty<Transform3D>();
    private int[] _removed = Array.Empty<int>();
    private GroundDisc[] _zones = Array.Empty<GroundDisc>();
    private bool[] _zoneDrawn = Array.Empty<bool>();
    private ZoneDisc[] _discs = Array.Empty<ZoneDisc>();
    private Vector3[] _discAt = Array.Empty<Vector3>();
    private Color[] _discFill = Array.Empty<Color>(), _discRim = Array.Empty<Color>(), _playerColor = Array.Empty<Color>();

    /// <summary>The camera whose zoom sizes the cast bars (the hp bar rule); null keeps the base size.</summary>
    public RtsCamera? Camera { get; set; }

    /// <summary>The fog whose hide rule applies to the cast bars; null shows every live caster's.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>True while the radius ring is drawn: an ability armed and the cursor on the map, or (M4-V6d) a no-target ability's button hovered with a caster ready.</summary>
    public bool TargetingShown => _radius.Visible;

    /// <summary>True while the radius ring is the hover ring of a no-target ability's button, drawn round its caster (M4-V6d); the range ring is hidden then.</summary>
    public bool SelfRingShown { get; private set; }

    /// <summary>The ability the targeting rings are drawn for, or -1.</summary>
    public int ShownAbility { get; private set; } = -1;

    /// <summary>The radius ring's drawn outer radius (m): the def's <c>radius</c>.</summary>
    public float ShownRadius => _shownRadius;

    /// <summary>The range ring's drawn outer radius (m): the def's <c>range</c>.</summary>
    public float ShownRange => _shownRange;

    /// <summary>The radius ring's centre (sim x, y in meters) as last drawn.</summary>
    public System.Numerics.Vector2 RadiusCenter { get; private set; }

    /// <summary>The unit slot the range ring is drawn round (the caster that would cast), or -1 when it is hidden; for a hovered no-target ability (M4-V6d) the caster the radius ring is drawn round.</summary>
    public int RangeCaster { get; private set; } = -1;

    /// <summary>The range ring node.</summary>
    public GroundDisc RangeRing => _range;

    /// <summary>The radius ring node.</summary>
    public GroundDisc RadiusRing => _radius;

    /// <summary>Cast bars drawn this frame.</summary>
    public int ShownBars { get; private set; }

    /// <summary>The unit slot under cast bar <paramref name="k"/>.</summary>
    public int BarSlot(int k) => _barSlot[k];

    /// <summary>The fill of cast bar <paramref name="k"/> as drawn (0 to 1).</summary>
    public float BarFill(int k) => _barFillShown[k];

    /// <summary>The drawn length (m) of cast bar <paramref name="k"/>'s fill: its share of the bar, but never under the bar's thickness (BUG-0342).</summary>
    public float BarFillLength(int k) => _barFillLength[k];

    /// <summary>Cast-point rings drawn this frame.</summary>
    public int ShownCircles { get; private set; }

    /// <summary>The caster slot of cast-point ring <paramref name="k"/>.</summary>
    public int CircleSlot(int k) => _circleSlot[k];

    /// <summary>The node drawing cast-point ring <paramref name="k"/> (M5-V1: a <see cref="GroundDisc"/> band, BUG-0520).</summary>
    public GroundDisc CastRing(int k) => _castRings!.NodeOf(_circleSlot[k])!;

    /// <summary>The drawn height of cast-point ring <paramref name="k"/> at ground point (x, z), NaN off its band (<see cref="GroundDisc.SurfaceAt"/>).</summary>
    public float CastRingSurfaceAt(int k, float x, float z) => CastRing(k).SurfaceAt(x, z);

    /// <summary>The cast-point rings' node pool.</summary>
    public GroundDiscPool CastRingPool => _castRings!;

    /// <summary>Status markers drawn this frame.</summary>
    public int ShownMarkers { get; private set; }

    /// <summary>Status marker <paramref name="k"/> as drawn: its unit, status and place in the unit's row.</summary>
    public StatusMark MarkerAt(int k) => _marks[k];

    /// <summary>Where status marker <paramref name="k"/> was drawn (its instance's centre).</summary>
    public Vector3 MarkerPosition(int k) => _markAt[k];

    /// <summary>The colour status <paramref name="status"/>'s markers are drawn in.</summary>
    public Color StatusColor(int status) => (uint)status < (uint)_statusColor.Length ? _statusColor[status] : OtherStatusColor;

    /// <summary>The resolve flashes' pool.</summary>
    public ResolveFlashes Flashes { get; private set; } = null!;

    /// <summary>Flash slot <paramref name="slot"/> as drawn: a basis scaled by its drawn radius at its node's centre (on the terrain + <see cref="Lift"/>), or a zero basis while unused or hidden by the fog.</summary>
    public Transform3D FlashTransform(int slot) => _flashTransform[slot];

    /// <summary>The node drawing flash slot <paramref name="slot"/> (M5-V1, BUG-0520), or null when it isn't drawn.</summary>
    public GroundDisc? FlashNode(int slot) => _flashDiscs?.NodeOf(slot);

    /// <summary>The drawn height of flash slot <paramref name="slot"/> at ground point (x, z), NaN where it draws nothing.</summary>
    public float FlashSurfaceAt(int slot, float x, float z) => FlashNode(slot)?.SurfaceAt(x, z) ?? float.NaN;

    /// <summary>The resolve flashes' node pool.</summary>
    public GroundDiscPool FlashPool => _flashDiscs!;

    /// <summary>The lost-cast cues' pool (M5-V1).</summary>
    public LostCastCues LostCasts { get; private set; } = null!;

    /// <summary>Lost-cast cues drawn this frame.</summary>
    public int ShownCues { get; private set; }

    /// <summary>The cue slot of drawn cue <paramref name="k"/>.</summary>
    public int CueSlot(int k) => _cueSlot[k];

    /// <summary>The disc of cue slot <paramref name="slot"/>, or null when it isn't drawn.</summary>
    public GroundDisc? CueNode(int slot) => _cueDiscs?.NodeOf(slot);

    /// <summary>The words over cue slot <paramref name="slot"/>.</summary>
    public Label3D CueLabel(int slot) => _cueLabels[slot];

    /// <summary>The lost-cast cues' disc pool.</summary>
    public GroundDiscPool CuePool => _cueDiscs!;

    /// <summary>Where the lost-cast sound goes (<see cref="SfxEvent.Refused"/>); null plays none.</summary>
    public Sfx? Sound { get; set; }

    /// <summary>The words over a lost cast's caster (<c>ui.json</c> <c>hud.cast_lost</c>); empty shows none (no <c>ui.json</c>, or no HUD).</summary>
    public string CastLostText
    {
        get => _castLostText;
        set
        {
            _castLostText = value ?? "";
            foreach (Label3D l in _cueLabels) l.Text = _castLostText;
        }
    }

    /// <summary>Flashes drawn (not hidden) by the last frame.</summary>
    public int ShownFlashes { get; private set; }

    /// <summary>The marker multimesh.</summary>
    public MultiMesh MarkerMesh => _markers;

    /// <summary>Zone disc nodes: one per <see cref="ZoneStore"/> slot (<see cref="ZoneStore.Capacity"/>), made in <see cref="Bind"/>.</summary>
    public int ZoneNodes => _zones.Length;

    /// <summary>Zone disc nodes visible now (equal to <see cref="ShownZones"/> after a <see cref="Sync"/>).</summary>
    public int ZoneNodesVisible
    {
        get
        {
            int n = 0;
            foreach (GroundDisc d in _zones) if (d.Visible) n++;
            return n;
        }
    }

    /// <summary>The node drawing zone slot <paramref name="slot"/>'s disc.</summary>
    public GroundDisc ZoneNode(int slot) => _zones[slot];

    /// <summary>Zone discs drawn this frame.</summary>
    public int ShownZones { get; private set; }

    /// <summary>Zone disc <paramref name="k"/> as drawn this frame (slots ascending).</summary>
    public ZoneDisc ZoneDiscAt(int k) => _discs[k];

    /// <summary>Where zone disc <paramref name="k"/> was drawn (its instance's centre).</summary>
    public Vector3 ZoneDiscPosition(int k) => _discAt[k];

    /// <summary>Zone disc <paramref name="k"/>'s fill colour as drawn (alpha: its opacity).</summary>
    public Color ZoneDiscFill(int k) => _discFill[k];

    /// <summary>Zone disc <paramref name="k"/>'s rim colour as drawn (alpha: the rim band's share of the radius).</summary>
    public Color ZoneDiscRim(int k) => _discRim[k];

    /// <summary>The drawn height of zone disc <paramref name="k"/>'s surface at ground point (x, z), NaN where it draws nothing (<see cref="GroundDisc.SurfaceAt"/>).</summary>
    public float ZoneSurfaceAt(int k, float x, float z) => _zones[_discs[k].Slot].SurfaceAt(x, z);

    /// <summary>The drawn height of the radius ring (or with <paramref name="range"/> the range ring) at ground point (x, z), NaN off its band.</summary>
    public float RingSurfaceAt(bool range, float x, float z) => (range ? _range : _radius).SurfaceAt(x, z);

    public override void _Ready()
    {
        _range = Ring("RangeRing");
        _radius = Ring("RadiusRing");
    }

    /// <summary>Creates the bar, ring, marker, flash and zone disc pools; call once before the first <see cref="Sync"/>.</summary>
    /// <param name="playerRgb">Per player, its colour (0xRRGGBB) for an enemy zone's rim; null draws every enemy rim <see cref="EnemyZoneRimColor"/>.</param>
    /// <param name="zoneCapacity">The world's <see cref="ZoneStore.Capacity"/>.</param>
    public void Bind(GameData data, int unitCapacity, SimRunner? runner, SelectionController? selection, int flashCapacity = ResolveFlashes.DefaultCapacity,
        uint[]? playerRgb = null, int zoneCapacity = ZoneStore.DefaultCapacity)
    {
        _data = data;
        if (_runner != null) _runner.Ticked -= OnTicked;
        _runner = runner;
        if (_runner != null) _runner.Ticked += OnTicked;
        _sel = selection;
        _bodyTop = new float[data.Units.Length];
        for (int t = 0; t < _bodyTop.Length; t++) _bodyTop[t] = UnitViews.BodyHeight(data.Units[t].Radius);
        _barSlot = new int[unitCapacity];
        _circleSlot = new int[unitCapacity];
        _barFillShown = new float[unitCapacity];
        _barFillLength = new float[unitCapacity];
        _barBack = Multi("CastBack", new BoxMesh { Size = Vector3.One, Material = Flat(BarBackColor) }, unitCapacity);
        _barFill = Multi("CastFill", new BoxMesh { Size = Vector3.One, Material = Flat(BarFillColor) }, unitCapacity);
        _castRings?.Free();
        _castRings = new GroundDiscPool(this, "CastRing", unitCapacity, CastRingNodes);

        // Marker colours follow the status's kind (data), so a new status of a known kind needs no code.
        _statusColor = new Color[data.Statuses.Length];
        for (int s = 0; s < _statusColor.Length; s++)
            _statusColor[s] = data.Statuses[s].Kind switch
            {
                StatusKind.DamageOverTime => DamageOverTimeColor,
                StatusKind.Slow => SlowColor,
                StatusKind.Blind => BlindColor,
                StatusKind.Frenzied => FrenzyColor,
                _ => OtherStatusColor,
            };
        int markCapacity = unitCapacity * StatusStore.PerUnit;
        _marks = new StatusMark[markCapacity];
        _markAt = new Vector3[markCapacity];
        var colored = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _markers = Multi("StatusMarkers", new BoxMesh { Size = Vector3.One, Material = colored }, markCapacity, colors: true);

        Flashes = new ResolveFlashes(flashCapacity);
        _removed = new int[flashCapacity];
        _flashTransform = new Transform3D[flashCapacity];
        Array.Fill(_flashTransform, Hidden);
        _flashDiscs?.Free();
        _flashDiscs = new GroundDiscPool(this, "Flash", flashCapacity, FlashNodes);

        LostCasts = new LostCastCues();
        _cuesSounded = 0;
        _cueSlot = new int[LostCasts.Capacity];
        _cueRemoved = new int[LostCasts.Capacity];
        _cueShown = new bool[LostCasts.Capacity];
        _cueDiscs?.Free();
        _cueDiscs = new GroundDiscPool(this, "LostCast", LostCasts.Capacity, CueNodes);
        foreach (Label3D old in _cueLabels) old.QueueFree();
        _cueLabels = new Label3D[LostCasts.Capacity];
        for (int c = 0; c < _cueLabels.Length; c++)
        {
            _cueLabels[c] = new Label3D
            {
                Name = $"LostCastText{c}", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = BuildGhost.ReasonFontSize,
                OutlineSize = 12, Modulate = CueTextColor, Text = _castLostText, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_cueLabels[c]);
        }
        ShownCues = 0;

        _playerColor = new Color[playerRgb?.Length ?? 0];
        for (int p = 0; p < _playerColor.Length; p++) _playerColor[p] = UnitViews.ColorFromRgb(playerRgb![p]);
        int zones = Math.Max(1, zoneCapacity);
        _discs = new ZoneDisc[zones];
        _discAt = new Vector3[zones];
        _discFill = new Color[zones];
        _discRim = new Color[zones];
        foreach (GroundDisc old in _zones) old.QueueFree();
        _zones = new GroundDisc[zones];
        _zoneDrawn = new bool[zones];
        for (int z = 0; z < zones; z++)
        {
            _zones[z] = new GroundDisc { Name = $"ZoneDisc{z}" };
            AddChild(_zones[z]);
        }
        ShownZones = 0;
    }

    public override void _ExitTree()
    {
        if (_runner != null) _runner.Ticked -= OnTicked;
    }

    // Each tick the runner runs: take its resolves and drops while they are still in World.AbilityEvents.
    private void OnTicked(Simulation sim) => CollectTick(sim.World);

    /// <summary>Adds the flashes of the last tick's resolves and the cues of its own dropped casts (once per tick; a later call for the same tick does nothing).</summary>
    public void CollectTick(World world)
    {
        Flashes?.Collect(world.AbilityEvents, world.TickNumber);
        LostCasts?.Collect(world.AbilityEvents, world.TickNumber, SelectionController.LocalPlayer);
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha, GetViewport().GetMousePosition(), Input.IsActionPressed(OrderQueue));
    }

    /// <summary>One frame: the targeting rings for the cursor at <paramref name="mouse"/> (screen pixels; <paramref name="queued"/>: Shift held), then the cast bars and cast-point rings, the status markers, the resolve flashes, the zone discs and the lost-cast cues. Allocation-free once each armed ability's meshes exist and the disc pools have reached their high marks.</summary>
    public void Sync(World world, float alpha, Vector2 mouse, bool queued)
    {
        if (_barBack == null) return;
        SyncTargeting(world, alpha, mouse, queued);
        if (Camera != null) _barScale = Math.Max(1f, Camera.Zoom / CombatViews.BarFullZoom);
        FogView? fog = Fog?.Refreshed(world);
        SyncCasts(world, alpha, fog);
        SyncMarkers(world, alpha, fog);
        SyncFlashes(world, alpha, fog);
        SyncZones(world, fog);
        SyncCues(world, alpha);
    }

    private void SyncTargeting(World world, float alpha, Vector2 mouse, bool queued)
    {
        int ability = _sel?.TargetAbility ?? -1;
        if (ability < 0)
        {
            SyncSelfRing(world, alpha, queued);
            return;
        }
        SelfRingShown = false;
        if (!_sel!.AbilityPoint(mouse, out System.Numerics.Vector2 point))
        {
            HideTargeting();
            return;
        }
        AbilityDef def = _data.Abilities[ability];
        ShowMeshes(ability, def);
        RadiusCenter = point;
        PlaceRing(_radius, world, point, def.Radius);
        if (!_radius.Visible) _radius.Visible = true;
        int caster = _sel.PickCaster(ability, point, queued, out _);
        RangeCaster = caster;
        if (caster < 0)
        {
            if (_range.Visible) _range.Visible = false;
            return;
        }
        Vector3 casterAt = UnitViews.GroundPoint(world, caster, alpha);
        PlaceRing(_range, world, new System.Numerics.Vector2(casterAt.X, casterAt.Z), def.Range);
        if (!_range.Visible) _range.Visible = true;
    }

    private void ShowMeshes(int ability, AbilityDef def)
    {
        if (ShownAbility == ability) return;
        ShownAbility = ability;
        _shownRadius = def.Radius;
        // A no-target ability has no range (0): it never draws a range ring.
        _shownRange = def.Range > 0f ? def.Range : 0f;
    }

    // M4-V6d: while the cursor rests on a no-target (selfAura or summon) ability's button, its radius ring is drawn round the
    // unit the press would cast with (SelfCaster: the nearest ready caster rule at the selection's lead); nothing when none is
    // ready or the radius is 0 (a summon has nothing to ring).
    private void SyncSelfRing(World world, float alpha, bool queued)
    {
        int ability = _sel?.Card?.HoveredAbility ?? -1;
        int caster = -1;
        if (ability >= 0 && AbilityCaster.IsNoTarget(_data.Abilities[ability].Kind) && _data.Abilities[ability].Radius > 0f) caster = _sel!.SelfCaster(ability, queued, out _);
        if (caster < 0)
        {
            SelfRingShown = false;
            HideTargeting();
            return;
        }
        ShowMeshes(ability, _data.Abilities[ability]);
        Vector3 at = UnitViews.GroundPoint(world, caster, alpha);
        RadiusCenter = new System.Numerics.Vector2(at.X, at.Z);
        PlaceRing(_radius, world, RadiusCenter, _shownRadius);
        if (!_radius.Visible) _radius.Visible = true;
        if (_range.Visible) _range.Visible = false;
        RangeCaster = caster;
        SelfRingShown = true;
    }

    private void HideTargeting()
    {
        RangeCaster = -1;
        if (_radius.Visible) _radius.Visible = false;
        if (_range.Visible) _range.Visible = false;
    }

    private void SyncCasts(World world, float alpha, FogView? fog)
    {
        UnitStore u = world.Units;
        ReadOnlySpan<bool> shown = fog != null ? fog.UnitShown : u.Alive;
        float len = BarLength * _barScale, thick = BarThickness * _barScale;
        int bars = 0, circles = 0;
        _castRings!.Begin();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.CastAbility[i] < 0) continue;
            int type = u.TypeId[i];
            var abilities = _data.Units[type].Abilities;
            int k = u.CastAbility[i];
            if ((uint)k >= (uint)abilities.Length) continue;
            AbilityDef def = _data.Abilities[abilities[k]];
            if (u.State[i] == UnitState.Casting && i < shown.Length && shown[i])
            {
                float fill = AbilityCaster.Progress(u.CastTicks[i], def.CastTicks);
                Vector3 top = UnitViews.GroundPoint(world, i, alpha)
                    + new Vector3(0f, _bodyTop[type] + (CombatViews.BarGap + BarAbove) * _barScale, 0f);
                _barBack.SetInstanceTransform(bars, new Transform3D(Basis.FromScale(new Vector3(len, thick * 0.8f, thick * 0.8f)), top));
                // Never thinner than a square nub, so the first tick shows violet (BUG-0342).
                float filled = Math.Min(len, Math.Max(len * fill, thick));
                _barFill.SetInstanceTransform(bars, new Transform3D(Basis.FromScale(new Vector3(filled, thick, thick)), top + new Vector3(-len / 2f + filled / 2f, 0f, 0f)));
                _barSlot[bars] = i;
                _barFillShown[bars] = fill;
                _barFillLength[bars] = filled;
                bars++;
            }
            // A radius-0 ability (a summon) has no area to ring.
            if (u.Owner[i] == SelectionController.LocalPlayer && def.Radius > 0f)
            {
                // BUG-0520: a band on the terrain cells under it, so across a cliff it lies on both levels; its node is
                // the caster's for as long as it holds the cast, so the mesh is rebuilt only when the cast point moves.
                float r = def.Radius;
                GroundDisc ring = _castRings.Take(i);
                ring.Place(world.Heightmap, u.CastPoint[i], r, r * (1f - CastRingShare), Lift);
                ring.SetColors(CastRingFill, CastRingColor, CastRingShare);
                if (!ring.Visible) ring.Visible = true;
                _circleSlot[circles] = i;
                circles++;
            }
        }
        _castRings.End();
        if (bars != ShownBars)
        {
            _barBack.VisibleInstanceCount = bars;
            _barFill.VisibleInstanceCount = bars;
            ShownBars = bars;
        }
        ShownCircles = circles;
    }

    private void SyncMarkers(World world, float alpha, FogView? fog)
    {
        UnitStore u = world.Units;
        ReadOnlySpan<bool> shown = fog != null ? fog.UnitShown : u.Alive;
        int n = StatusMarkers.Collect(u.Statuses, shown, _statusColor.Length, _marks);
        float size = MarkerSize * _barScale, spacing = MarkerSpacing * _barScale;
        var basis = Basis.FromScale(new Vector3(size, size, size));
        for (int k = 0; k < n; k++)
        {
            StatusMark m = _marks[k];
            int type = u.TypeId[m.Unit];
            Vector3 at = UnitViews.GroundPoint(world, m.Unit, alpha)
                + new Vector3(StatusMarkers.RowOffset(m.Place, m.Row, spacing), _bodyTop[type] + (CombatViews.BarGap + MarkerAbove) * _barScale, 0f);
            _markers.SetInstanceTransform(k, new Transform3D(basis, at));
            _markers.SetInstanceColor(k, _statusColor[m.Status]);
            _markAt[k] = at;
        }
        if (n != ShownMarkers)
        {
            _markers.VisibleInstanceCount = n;
            ShownMarkers = n;
        }
    }

    private void SyncFlashes(World world, float alpha, FogView? fog)
    {
        ResolveFlashes f = Flashes;
        CollectTick(world);
        int gone = f.Expire(world.TickNumber, _removed);
        for (int k = 0; k < gone && k < _removed.Length; k++) HideFlash(_removed[k]);
        float a = float.IsNaN(alpha) ? 1f : Math.Clamp(alpha, 0f, 1f);
        ReadOnlySpan<bool> active = f.Active;
        int shown = 0;
        _flashDiscs!.Begin();
        for (int i = 0; i < active.Length; i++)
        {
            if (!active[i]) continue;
            System.Numerics.Vector2 p = f.Point[i];
            // The first frame decides once whether it shows (BUG-0371 b): a resolve the fog hid then never appears mid-fade.
            if (!f.Drawn[i]) f.MarkDrawn(i, world.TickNumber, fog == null || fog.ShowsPoint(world.Fog, p));
            if (!f.Shown[i])
            {
                _flashTransform[i] = Hidden;
                continue; // it expires on time, unseen (its node, if any, goes back to the pool at End)
            }
            float age = f.Age(i, world.TickNumber, a);
            int ability = f.Ability[i];
            float radius = (uint)ability < (uint)_data.Abilities.Length ? _data.Abilities[ability].Radius : 1f;
            float share = FlashFrom + (1f - FlashFrom) * Math.Min(1f, age / FlashGrowShare);
            float r = radius * share;
            float y = TerrainHeight.At(world.Heightmap, p.X, p.Y) + Lift;
            if (radius > 0f)
            {
                // BUG-0520: meshed once at the full radius on the cells under it (both levels of a cliff); it grows in the shader.
                GroundDisc disc = _flashDiscs.Take(i);
                disc.Place(world.Heightmap, p, radius, 0f, Lift);
                disc.SetScale(share);
                var fill = new Color(FlashColor.R, FlashColor.G, FlashColor.B, FlashAlpha * (1f - age));
                disc.SetColors(fill, new Color(fill.R, fill.G, fill.B, Math.Min(1f, fill.A * FlashRimBoost)), FlashRimShare);
                if (!disc.Visible) disc.Visible = true;
                y = disc.Position.Y;
            }
            _flashTransform[i] = new Transform3D(Basis.FromScale(new Vector3(r, 0.06f, r)), new Vector3(p.X, y, p.Y));
            shown++;
        }
        _flashDiscs.End();
        ShownFlashes = shown;
    }

    // M5-V1: the lost-cast cues. A new drop plays the refused sound once (the frame that first sees it); each live cue is a
    // red disc under its caster, fading over its 0.5 s, with the hud.cast_lost words over it. A cue whose caster died or
    // whose slot was reused draws nothing and expires on time.
    private void SyncCues(World world, float alpha)
    {
        LostCastCues c = LostCasts;
        if (c == null) return;
        if (c.Added != _cuesSounded)
        {
            _cuesSounded = c.Added;
            Sound?.Play(SfxEvent.Refused);
        }
        c.Expire(world.TickNumber, _cueRemoved);
        UnitStore u = world.Units;
        float a = float.IsNaN(alpha) ? 1f : Math.Clamp(alpha, 0f, 1f);
        float pixel = Camera != null ? BuildGhost.ReasonPixelPerZoom * Camera.Zoom : 0.01f;
        Array.Clear(_cueShown);
        _cueDiscs!.Begin();
        int shown = 0;
        ReadOnlySpan<bool> active = c.Active;
        for (int i = 0; i < active.Length; i++)
        {
            if (!active[i]) continue;
            if (!c.Drawn[i]) c.MarkDrawn(i, world.TickNumber);
            EntityHandle h = c.Caster[i];
            if ((uint)h.Index >= (uint)u.Capacity || !u.Alive[h.Index] || u.Generation[h.Index] != h.Generation) continue;
            float fade = 1f - c.Age(i, world.TickNumber, a);
            Vector3 at = UnitViews.GroundPoint(world, h.Index, alpha);
            GroundDisc disc = _cueDiscs.Take(i);
            disc.Place(world.Heightmap, new System.Numerics.Vector2(at.X, at.Z), u.Radius[h.Index] + CueMargin, 0f, CueLift);
            disc.SetColors(new Color(CueColor.R, CueColor.G, CueColor.B, CueFillAlpha * fade), new Color(CueColor.R, CueColor.G, CueColor.B, fade), CueRimShare);
            if (!disc.Visible) disc.Visible = true;
            if (_castLostText.Length > 0)
            {
                Label3D label = _cueLabels[i];
                if (label.PixelSize != pixel) label.PixelSize = pixel;
                int type = u.TypeId[h.Index];
                label.Position = at + new Vector3(0f, _bodyTop[type] + (CombatViews.BarGap + BarAbove) * _barScale + CueTextAbove * _barScale, 0f);
                label.Modulate = new Color(CueTextColor.R, CueTextColor.G, CueTextColor.B, fade);
                if (!label.Visible) label.Visible = true;
                _cueShown[i] = true;
            }
            _cueSlot[shown++] = i;
        }
        _cueDiscs.End();
        for (int i = 0; i < _cueLabels.Length; i++)
            if (!_cueShown[i] && _cueLabels[i].Visible) _cueLabels[i].Visible = false;
        ShownCues = shown;
    }

    private void SyncZones(World world, FogView? fog)
    {
        // The rule is ZoneDiscs' (fog bytes only); this only draws what it returns.
        int player = fog?.Player ?? SelectionController.LocalPlayer;
        int n = ZoneDiscs.Collect(world.Zones, world.Fog, player, fog != null && fog.Enabled, _discs);
        ReadOnlySpan<int> owner = world.Zones.Owner;
        // One node per zone slot, so a zone's mesh is built once (its centre and radius never change) however the list shifts.
        Array.Clear(_zoneDrawn);
        for (int k = 0; k < n; k++)
        {
            ZoneDisc d = _discs[k];
            int who = owner[d.Slot];
            Color rim = d.Own ? OwnZoneRimColor : (uint)who < (uint)_playerColor.Length ? _playerColor[who] : EnemyZoneRimColor;
            var fill = new Color(ZoneFillColor.R, ZoneFillColor.G, ZoneFillColor.B, d.Alpha);
            rim.A = Math.Min(0.5f, ZoneRimWidth / Math.Max(0.01f, d.Radius));
            GroundDisc node = _zones[d.Slot];
            node.Place(world.Heightmap, d.Center, d.Radius, 0f, ZoneLift);
            // The rim is RimBoost x as opaque as the fill, and fades with it.
            node.SetColors(fill, new Color(rim.R, rim.G, rim.B, Math.Min(1f, d.Alpha * RimBoost)), rim.A);
            if (!node.Visible) node.Visible = true;
            _zoneDrawn[d.Slot] = true;
            _discAt[k] = node.Position;
            _discFill[k] = fill;
            _discRim[k] = rim;
        }
        for (int z = 0; z < _zones.Length; z++)
            if (!_zoneDrawn[z] && _zones[z].Visible) _zones[z].Visible = false;
        ShownZones = n;
    }

    private void HideFlash(int slot) => _flashTransform[slot] = Hidden;

    // A ring on the ground whose outer edge is `radius` m from its centre, RingWidth wide (BUG-0430: on the terrain on both
    // sides of a cliff, as the zone discs).
    private void PlaceRing(GroundDisc ring, World world, System.Numerics.Vector2 center, float radius)
    {
        ring.Place(world.Heightmap, center, radius, radius - RingWidth, Lift);
        ring.SetColors(_ringFill[ring == _range ? 1 : 0], _ringRim[ring == _range ? 1 : 0], radius > 0f ? Math.Min(1f, RingWidth / radius) : 1f);
    }

    private GroundDisc Ring(string name)
    {
        var node = new GroundDisc { Name = name };
        AddChild(node);
        return node;
    }

    // The rings' colours: [0] the radius ring, [1] the range ring; a clear fill and an opaque rim band.
    private static readonly Color[] _ringRim = { RadiusColor, RangeColor };
    private static readonly Color[] _ringFill = { new(RadiusColor.R, RadiusColor.G, RadiusColor.B, 0f), new(RangeColor.R, RangeColor.G, RangeColor.B, 0f) };

    private MultiMesh Multi(string name, Mesh mesh, int count, bool colors = false)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh, InstanceCount = count, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return mm;
    }

    private static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
}
