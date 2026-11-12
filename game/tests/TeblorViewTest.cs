using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M7-V3c (BUG-1260): every unit is drawn as wide as the room it takes (<see cref="UnitViews.DrawnRadius(UnitDef)"/> is the
/// collision radius; data <c>scale</c> is the height only), proven on the shipped data with the real Teblor (no forge).
/// <list type="bullet">
/// <item>Roster: every shipped type's capsule is <c>radius</c> wide and <c>(2 radius + 1) x scale</c> tall; every Malazan and
/// Whirlwind type (scale 1) is exactly the old <c>radius x scale</c> capsule. In a real match one of each type, selected: its
/// selection ring is 1.35x, its target ring 1.7x, its corpse disc 1x and the disc's rim 1.2x its radius, and the drawn radius the
/// refused-cast cue reads is the radius.</item>
/// <item>(a) The setup's added third row is set to Teblor (and the local row), and Start starts the 3-seat match.</item>
/// <item>(b) The local Teblor base at tick 30: no two of its Gatherers' drawn bodies overlap wherever the sim keeps them apart
/// (centre distance at least the sum of the sim radii). The same for the third seat's base.</item>
/// <item>(c) A Warrior beside a Heavy Infantry: widths 0.75 / 0.4, heights 3.75 m / 1.8 m (printed).</item>
/// <item>A group of three Warriors and five Gatherers moved together, selected: the same overlap rule.</item>
/// <item>(d) "Regenerating" under a real Warrior hurt by a Desert Archer's real hits, 6 s after its last hit.</item>
/// <item>(e) The War-dog's square in the Stables &amp; Kennels queue reads its initial and "×3", after Age II.</item>
/// <item>BUG-1260 item 2: in the normal match start no unit is ever drawn white (hit flash) or without its player's colour on
/// any of the first frames; a unit first seen below its maximum hp is (the M4-V2 flash, what QA's scratch scene showed).</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>teblor-1-setup</c>, <c>teblor-2-base</c>,
/// <c>teblor-3-warrior-beside-soldier</c>, <c>teblor-4-crowding</c> and <c>teblor-5-war-dog-queue</c>.
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/TeblorViewTest.tscn</c>; prints "TEBLOR VIEW TEST PASS" or FAIL lines.
/// </summary>
public partial class TeblorViewTest : Node
{
    private const string Teblor = "teblor";
    private const int BaseTick = 30;
    private const float Eps = 1e-3f;

    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private string _root = "";
    private string? _shots;
    private int _teblor;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://teblor-view-test-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] user = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < user.Length; i++)
                if (user[i] == "--shots") _shots = user[i + 1];
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _teblor = _data.FindFaction(Teblor);
            if (_teblor < 0) throw new InvalidOperationException("the shipped data has no teblor faction");
            MeshRows();
            await RosterInMatch();
            await SetupAndBase();
            await Staged();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"TEBLOR VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("TEBLOR VIEW TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- criterion 1 and 2: the capsule of every shipped type ----

    private void MeshRows()
    {
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 4);
        int old = 0, scaled = 0;
        for (int t = 0; t < _data.Units.Length; t++)
        {
            UnitDef def = _data.Units[t];
            CapsuleMesh? mesh = views.MeshOf(t);
            if (!Check(mesh != null, $"{def.Key}: no mesh")) continue;
            float wantH = (2f * def.Radius + 1f) * def.Scale;
            GD.Print($"  {def.Key}: radius {def.Radius.ToString(CultureInfo.InvariantCulture)} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: drawn {2f * mesh!.Radius:0.###} m wide, {mesh.Height:0.###} m tall");
            // Width is the collision radius alone; the scale is in the height only.
            Check(mesh.Radius == def.Radius && UnitViews.DrawnRadius(def) == def.Radius && Mathf.Abs(2f * mesh.Radius - 2f * def.Radius) < 1e-6f,
                $"{def.Key}: drawn radius {mesh.Radius} (DrawnRadius {UnitViews.DrawnRadius(def)}), want the collision radius {def.Radius}");
            Check(Mathf.Abs(mesh.Height - wantH) < 1e-5f && mesh.Height == UnitViews.DrawnHeight(def), $"{def.Key}: height {mesh.Height}, want {wantH}");
            if (def.Scale != 1f) scaled++;
            // Criterion 2: the two scale-1 factions draw exactly as before (the old formula, radius x scale by BodyHeight x scale).
            string faction = _data.Factions[def.Faction].Key;
            if (faction != "malazan" && faction != "whirlwind") continue;
            old++;
            Check(mesh.Radius == def.Radius * def.Scale && mesh.Height == UnitViews.BodyHeight(def.Radius) * def.Scale,
                $"{def.Key} ({faction}, scale {def.Scale}): capsule {mesh.Radius} x {mesh.Height}, the old formula's {def.Radius * def.Scale} x {UnitViews.BodyHeight(def.Radius) * def.Scale}");
        }
        Check(old > 0 && scaled > 0, $"{old} Malazan / Whirlwind types, {scaled} scaled types: the roster proves nothing");
        GD.Print($"roster: {_data.Units.Length} types, {scaled} scaled (width = radius, height x scale), {old} Malazan / Whirlwind types equal to the old formula");
        views.QueueFree();
    }

    // ---- criterion 1 in a real match: rings, target ring, corpse disc and rim, the cue's radius ----

    private async Task RosterInMatch()
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        try
        {
            match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute" }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            UnitStore u = w.Units;
            var sel = match.GetNode<SelectionController>("SelectionController");
            var rings = match.GetNode<SelectionRings>("World3D/SelectionRings");
            var target = match.GetNode<TargetRing>("World3D/TargetRing");
            var combat = match.GetNode<CombatViews>("World3D/CombatViews");
            match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
            Tick(sim, 2);

            System.Numerics.Vector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
            List<System.Numerics.Vector2> spots = Spots(w, mid, _data.Units.Length, 3);
            for (int t = 0; t < _data.Units.Length; t++) sim.Enqueue(Command.SpawnUnit(0, t, spots[t]));
            Tick(sim, 3);
            var slots = new int[_data.Units.Length];
            Array.Fill(slots, -1);
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Owner[i] == 0 && slots[u.TypeId[i]] < 0) slots[u.TypeId[i]] = i;
            if (!Check(slots.All(s => s >= 0), $"roster spawn incomplete: [{string.Join(", ", slots)}]")) return;

            sel.SelectOnly(Handle(u, slots[0]));
            for (int t = 1; t < slots.Length; t++) sel.Selection.Add(Handle(u, slots[t]));
            rings.Sync(w, 1f, sel.Selection.Items);
            Check(rings.ShownCount == slots.Length, $"{rings.ShownCount} rings for {slots.Length} selected");
            for (int k = 0; k < sel.Selection.Count && k < rings.ShownCount; k++)
            {
                int slot = sel.Selection.Items[k].Index;
                UnitDef def = _data.Units[u.TypeId[slot]];
                Check(Mathf.Abs(rings.ShownRadius(k) - def.Radius * SelectionRings.RadiusScale) < 1e-5f,
                    $"{def.Key}: selection ring {rings.ShownRadius(k)}, want {def.Radius * SelectionRings.RadiusScale} (1.35 x radius)");
                Check(UnitViews.DrawnRadius(w, slot) == def.Radius && u.Radius[slot] == def.Radius,
                    $"{def.Key}: drawn radius of the slot (the cue disc's) {UnitViews.DrawnRadius(w, slot)}, want {def.Radius}");
            }
            for (int t = 0; t < slots.Length; t++)
            {
                UnitDef def = _data.Units[t];
                target.Show(Handle(u, slots[t]), isBuilding: false);
                target.Sync(w, 1f, 0f);
                float r = target.Ring.Transform.Basis.X.Length();
                Check(target.Shown && Mathf.Abs(r - def.Radius * TargetRing.UnitScale) < 1e-4f,
                    $"{def.Key}: target ring {r} (shown {target.Shown}), want {def.Radius * TargetRing.UnitScale} (1.7 x radius)");
            }
            target.Mark.Clear();

            // One corpse per type, in a free row south of the units.
            var deaths = new DeathEvent[_data.Units.Length];
            List<System.Numerics.Vector2> graves = Spots(w, mid + new System.Numerics.Vector2(0f, 30f), _data.Units.Length, 3);
            for (int t = 0; t < deaths.Length; t++) deaths[t] = new DeathEvent(new EntityHandle(0, 0), false, t, 0, 1, graves[t]);
            combat.AddDeaths(w, deaths, sim.TickNumber);
            combat.Sync(w, 1f);
            await Frames(2);
            combat.Sync(w, 1f);
            int corpses = 0, rims = 0;
            for (int m = 0; m < combat.Markers.Capacity; m++)
            {
                if (combat.MarkerShown(m) != 1) continue;
                corpses++;
                UnitDef def = _data.Units[combat.Markers.Type[m]];
                float disc = combat.MarkerTransform(m).Basis.X.Length();
                Check(Mathf.Abs(disc - def.Radius) < 1e-5f, $"{def.Key}: corpse disc {disc}, want {def.Radius}");
                // The headless renderer keeps no multimesh transforms: the rim is read windowed only.
                if (DisplayServer.GetName() == "headless") continue;
                rims++;
                float rim = combat.CorpseRimMesh.GetInstanceTransform(m).Basis.X.Length();
                Check(Mathf.Abs(rim - def.Radius * CombatViews.CorpseRimScale) < 1e-5f, $"{def.Key}: corpse rim {rim}, want {def.Radius * CombatViews.CorpseRimScale}");
            }
            Check(corpses == deaths.Length, $"{corpses} corpses drawn for {deaths.Length} deaths");
            GD.Print($"roster in a match: {slots.Length} units, rings 1.35x / target 1.7x / corpse 1x the radius checked, {corpses} corpses, {rims} rims 1.2x (windowed only)");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(2);
        }
    }

    // ---- (a) the setup, (b) the base at the start, BUG-1260 item 2 in a normal start ----

    private async Task SetupAndBase()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = Path.Combine(_root, "settings.cfg");
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = 0.05; // one tick a frame
        AddChild(main);
        await Frames(2);
        try
        {
            if (!Check(main.Setup != null, "the shell built no setup screen")) return;
            main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = main.Setup!;
            setup.AddSlot();
            // (a) the third row set to Teblor: away from it first, so the pick is a real change.
            Pick(setup.FactionOptions[2], 0);
            Check(setup.FactionPick(2) == 0, "the third row could not pick Malazan");
            Pick(setup.FactionOptions[2], _teblor);
            Check(setup.SlotCount == 3 && setup.FactionPick(2) == _teblor && setup.FactionOptions[2].Visible
                && setup.FactionOptions[2].GetItemText(_teblor) == _data.Factions[_teblor].DisplayName,
                $"the third row: {setup.SlotCount} rows, faction {setup.FactionPick(2)} '{setup.FactionOptions[2].GetItemText(setup.FactionOptions[2].Selected)}'");
            await Shot("teblor-1-setup");
            Pick(setup.FactionOptions[0], _teblor);
            string joined = string.Join(" ", setup.MatchArgs() ?? Array.Empty<string>());
            Check(joined.Contains("--faction 1 teblor", StringComparison.Ordinal) && joined.Contains("--faction 3 teblor", StringComparison.Ordinal),
                $"Start's args: '{joined}'");
            setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            Match? match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, "Start started no match")) return;
            Simulation sim = match!.Simulation!;
            World w = sim.World;
            UnitStore u = w.Units;
            Check(w.Config.PlayerCount == 3 && w.FactionOf(0) == _teblor && w.FactionOf(2) == _teblor,
                $"seats: {w.Config.PlayerCount} players, factions {w.FactionOf(0)} / {w.FactionOf(1)} / {w.FactionOf(2)}");
            GD.Print($"  (a) started: '{match.StartLine}'");
            var views = match.GetNode<UnitViews>("World3D/UnitViews");
            var runner = match.GetNode<SimRunner>("SimRunner");
            var camera = match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;

            // BUG-1260 item 2: every drawn frame to tick 30, every shown unit in its player's colour and never flash-white.
            int frames = 0, looks = 0, white = 0, uncoloured = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < BaseTick && clock.Elapsed.TotalSeconds < 60)
            {
                await Frames(1); // before this frame's _Process: what the last frame synced and drew
                frames++;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!views.IsShown(i)) continue;
                    looks++;
                    MeshInstance3D view = views.ViewOf(i)!;
                    if (views.ShownLit(i) || view.MaterialOverlay == views.FlashMaterial) white++;
                    if (view.MaterialOverride != views.PlayerMaterial(u.Owner[i])) uncoloured++;
                }
            }
            runner.ProcessMode = ProcessModeEnum.Disabled; // hold the base still for the measure and the picture
            Check(frames > 0 && looks > 0 && white == 0 && uncoloured == 0,
                $"the start's first {frames} frames: {looks} unit looks, {white} flash-white, {uncoloured} not in their player's colour");
            GD.Print($"  BUG-1260 item 2, normal start: {frames} frames to tick {sim.TickNumber}, {looks} unit looks, {white} white, {uncoloured} without the player's colour");

            // (b) the Gatherers of both Teblor bases.
            views.Sync(w, 1f);
            foreach (int player in new[] { 0, 2 })
            {
                int[] gatherers = Enumerable.Range(0, u.Capacity)
                    .Where(i => u.Alive[i] && u.Owner[i] == player && _data.Units[u.TypeId[i]].IsWorker).ToArray();
                Check(gatherers.Length == _data.Rules.StartingWorkers && gatherers.All(i => _data.Units[u.TypeId[i]].Faction == _teblor),
                    $"player {player}: {gatherers.Length} Teblor workers, want {_data.Rules.StartingWorkers}");
                Apart(w, views, gatherers, $"tick {sim.TickNumber} player {player}'s Gatherers");
            }

            var sel = match.GetNode<SelectionController>("SelectionController");
            int hall = -1;
            for (int i = 0; i < w.Buildings.Capacity && hall < 0; i++)
                if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 0 && _data.Buildings[w.Buildings.TypeId[i]].Slot == BuildingSlot.TownHall) hall = i;
            if (Check(hall >= 0 && sel.SelectBuilding(hall), "no Longhouse to select"))
            {
                System.Numerics.Vector2 c = SelectionController.SiteCenter(w, hall);
                camera.SetFocus(c.X, c.Y + 2f);
            }
            await Shot("teblor-2-base");
            main.QuitToMenu();
            await Frames(2);
        }
        finally
        {
            if (GetTree().Paused) GetTree().Paused = false;
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (c), the group, (e), (d) and the white first frame, in one staged Teblor match ----

    private async Task Staged()
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        try
        {
            match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-fog", "--mute", "--faction", "1", Teblor }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            UnitStore u = w.Units;
            var sel = match.GetNode<SelectionController>("SelectionController");
            var views = match.GetNode<UnitViews>("World3D/UnitViews");
            var camera = match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;
            Tick(sim, 2);
            Check(w.FactionOf(0) == _teblor, $"player 0 plays {w.FactionOf(0)}, not Teblor");
            int warrior = _data.FindUnit("teblor_warrior"), gatherer = _data.FindUnit("teblor_gatherer"), heavy = _data.FindUnit("malazan_heavy_infantry");
            int archer = _data.FindUnit("whirlwind_desert_archer");
            if (!Check(warrior >= 0 && gatherer >= 0 && heavy >= 0 && archer >= 0, "a unit id is missing from the shipped data")) return;

            System.Numerics.Vector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
            // Open flat ground for each stage, so no cliff splits a group or hides a body.
            System.Numerics.Vector2 pairAt = FlatSpot(w, mid, 0, 20, 3);

            // (c) a Warrior beside a Heavy Infantry.
            EntityHandle wr = await SpawnOne(sim, 0, warrior, pairAt);
            EntityHandle hi = await SpawnOne(sim, 0, heavy, pairAt + new System.Numerics.Vector2(2.5f, 0f));
            if (!Check(wr != default && hi != default, "the pair did not spawn")) return;
            await Frames(1);
            var wm = (CapsuleMesh)views.ViewOf(wr.Index)!.Mesh;
            var hm = (CapsuleMesh)views.ViewOf(hi.Index)!.Mesh;
            float ratio = wm.Radius / hm.Radius;
            GD.Print($"  (c) Warrior {2f * wm.Radius:0.###} m wide x {wm.Height:0.###} m tall, Heavy Infantry {2f * hm.Radius:0.###} m x {hm.Height:0.###} m: width ratio {ratio:0.####} (0.75 / 0.4 = {0.75f / 0.4f:0.####}), height ratio {wm.Height / hm.Height:0.####}");
            Check(Mathf.Abs(ratio - 0.75f / 0.4f) < 1e-4f && Mathf.Abs(ratio - _data.Units[warrior].Radius / _data.Units[heavy].Radius) < 1e-6f,
                $"width ratio {ratio}, want 0.75 / 0.4");
            Check(Mathf.Abs(wm.Height - 3.75f) < 1e-4f && Mathf.Abs(hm.Height - 1.8f) < 1e-4f, $"heights {wm.Height} / {hm.Height}, want 3.75 / 1.8");
            camera.SetZoom(25f);
            camera.SetFocus(pairAt.X + 1.25f, pairAt.Y + 2f);
            await Shot("teblor-3-warrior-beside-soldier");

            // A group: three Warriors and five Gatherers sent to one point, so the sim packs them.
            System.Numerics.Vector2 groupAt = FlatSpot(w, pairAt, 9, 30, 5);
            var group = new List<EntityHandle>();
            for (int k = 0; k < 8; k++)
            {
                var off = new System.Numerics.Vector2(k % 4 * 3f - 4.5f, k / 4 * 3f - 1.5f);
                group.Add(await SpawnOne(sim, 0, k < 3 ? warrior : gatherer, groupAt + off));
            }
            if (!Check(group.All(h => h != default), "the group did not spawn")) return;
            foreach (EntityHandle h in group) sim.Enqueue(Command.Move(0, h, groupAt));
            Tick(sim, 120);
            sel.SelectOnly(group[0]);
            for (int k = 1; k < group.Count; k++) sel.Selection.Add(group[k]);
            foreach (EntityHandle h in group) u.Hp[h.Index] = Math.Max(1, w.MaxHp(h.Index) * 3 / 5); // bars for the picture
            views.Sync(w, 1f);
            Apart(w, views, group.Select(h => h.Index).ToArray(), "the group of 3 Warriors and 5 Gatherers");
            camera.SetZoom(25f);
            camera.SetFocus(groupAt.X, groupAt.Y + 2f);
            for (int f = 0; f < 20; f++) await Frames(1); // the hp drop's flash (0.15 s) is over before the picture
            await Shot("teblor-4-crowding");
            sel.Selection.Clear();

            // BUG-1260 item 2, the cause: a unit first seen below its maximum hp flashes white once (M4-V2, BUG-0160), as QA's
            // scratch scene hurt its Warriors before their first frame.
            System.Numerics.Vector2 lone = SameLevel(w, groupAt, 8, 12);
            sim.Enqueue(Command.SpawnUnit(0, warrior, lone));
            int before = Enumerable.Range(0, u.Capacity).Count(i => u.Alive[i]);
            Tick(sim, 2); // no frame in between: the views have not seen it
            int fresh = -1;
            for (int i = u.Capacity - 1; i >= 0 && fresh < 0; i--)
                if (u.Alive[i] && u.TypeId[i] == warrior && views.ViewOf(i) == null) fresh = i;
            if (Check(fresh >= 0 && Enumerable.Range(0, u.Capacity).Count(i => u.Alive[i]) == before + 1, "the hurt Warrior did not spawn"))
            {
                u.Hp[fresh] = w.MaxHp(fresh) / 2;
                views.Sync(w, 1f, 0.016f);
                bool lit = views.ShownLit(fresh) && views.ViewOf(fresh)!.MaterialOverlay == views.FlashMaterial;
                views.Sync(w, 1f, HitFlash.DefaultSeconds);
                bool after = views.ShownLit(fresh);
                GD.Print($"  BUG-1260 item 2, the cause: a Warrior first seen at half hp is drawn flash-white on its first frame: {lit}; {HitFlash.DefaultSeconds} s later: {after}");
                Check(lit && !after, $"a unit first seen hurt: flash on its first frame {lit}, after {HitFlash.DefaultSeconds} s {after}");
                u.Hp[fresh] = w.MaxHp(fresh);
                sim.Enqueue(Command.HoldPosition(0, Handle(u, fresh)));
                Tick(sim, 1);
            }

            await WarDogs(sim, sel, match);
            await Regen(sim, sel, match, warrior, archer, FlatSpot(w, groupAt, 22, 45, 6));
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(2);
        }
    }

    // (e) Stables & Kennels (and a War Lodge, Age II's second kind) spawned, Age II researched, a War-dog pack queued.
    private async Task WarDogs(Simulation sim, SelectionController sel, Match match)
    {
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int hall = -1;
        for (int i = 0; i < b.Capacity && hall < 0; i++)
            if (b.Alive[i] && b.Owner[i] == 0 && _data.Buildings[b.TypeId[i]].Slot == BuildingSlot.TownHall) hall = i;
        if (!Check(hall >= 0, "player 0 has no Longhouse")) return;
        int lodge = _data.FindBuilding("teblor_war_lodge"), stablesType = _data.FindBuilding("teblor_stables");
        int dog = _data.FindUnit("teblor_war_dog"), age = _data.FindTech("age_ii");
        var taken = new List<int> { b.Cell[hall] };
        int stables = -1;
        foreach (int type in new[] { lodge, stablesType })
        {
            int a = FreeAnchor(w, hall, type, taken);
            taken.Add(a);
            sim.Enqueue(Command.SpawnBuilding(0, type, w.NavGrid.CellCenter(a % w.NavGrid.Width, a / w.NavGrid.Width)));
            Tick(sim, 2);
            int s = b.SlotAt(a % w.NavGrid.Width, a / w.NavGrid.Width);
            Check(s >= 0 && b.TypeId[s] == type && !b.UnderConstruction[s], $"{_data.Buildings[type].Key} did not spawn finished");
            if (type == stablesType) stables = s;
        }
        if (stables < 0) return;
        SetMoney(w, 5000, 5000);
        sim.Enqueue(Command.Research(0, SelectionController.SiteCenter(w, hall), age));
        for (int t = 0; t < 1600 && !w.HasTech(0, age); t++) sim.Tick();
        if (!Check(w.HasTech(0, age), $"Age II not researched by tick {sim.TickNumber}")) return;
        sim.Enqueue(Command.Train(0, SelectionController.SiteCenter(w, stables), dog));
        Tick(sim, 2);
        Check(b.QueueCount[stables] == 1, $"the Stables queue holds {b.QueueCount[stables]} items after a War-dog Train");
        sel.SelectBuilding(stables);
        var strip = match.GetNode<ProductionQueueStrip>("Hud/QueueStrip");
        await Frames(2);
        strip.Sync();
        UnitDef dd = _data.Units[dog];
        string want = $"{dd.DisplayName.Substring(0, 1).ToUpperInvariant()}×{dd.SpawnCount}";
        Check(dd.SpawnCount == 3 && want.EndsWith("×3", StringComparison.Ordinal), $"the War-dog's spawnCount is {dd.SpawnCount}");
        Check(strip.ShownCount == 1 && strip.ShownType(0) == dog && strip.ItemLabel(0).Text == want && strip.ItemButton(0).Visible,
            $"the Stables strip: {strip.ShownCount} items, type {strip.ShownType(0)}, '{(strip.ShownCount > 0 ? strip.ItemLabel(0).Text : "")}' (want '{want}')");
        Check(strip.ItemButton(0).TooltipText == dd.DisplayName, $"the War-dog square's tooltip '{strip.ItemButton(0).TooltipText}'");
        GD.Print($"  (e) {_data.Buildings[stablesType].DisplayName} at tick {sim.TickNumber} (Age II): queue square '{strip.ItemLabel(0).Text}', tooltip '{strip.ItemButton(0).TooltipText}'");
        System.Numerics.Vector2 c = SelectionController.SiteCenter(w, stables);
        match.GetNode<RtsCamera>("RtsCamera").SetFocus(c.X, c.Y + 2f);
        await Shot("teblor-5-war-dog-queue");
    }

    // (d) A Warrior on hold, shot by a Desert Archer until 40 hp down; the archer walks away; the cue 6 s after the last hit.
    private async Task Regen(Simulation sim, SelectionController sel, Match match, int warrior, int archerType, System.Numerics.Vector2 at)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        var panel = match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        string cue = UiText.Shared!.Hud(HudText.Regenerating);
        FactionDef f = _data.Factions[_teblor];
        Check(f.RegenPerTick > 0 && f.RegenDelayTicks > 0, $"Teblor's regen: {f.RegenPerTick} a tick after {f.RegenDelayTicks} ticks");
        EntityHandle unit = await SpawnOne(sim, 0, warrior, at);
        System.Numerics.Vector2 shootFrom = SameLevel(w, at, 4, 6), away = SameLevel(w, at, 25, 35);
        EntityHandle archer = await SpawnOne(sim, 1, archerType, shootFrom);
        if (!Check(unit != default && archer != default, "the regen pair did not spawn")) return;
        sim.Enqueue(Command.HoldPosition(0, unit));
        sel.SelectOnly(unit);
        int max = w.MaxHp(unit.Index), start = sim.TickNumber;
        sim.Enqueue(Command.Attack(1, archer, unit, isBuilding: false));
        while (u.Hp[unit.Index] > max - 40 && sim.TickNumber < start + 1500) sim.Tick();
        if (!Check(u.Hp[unit.Index] <= max - 40 && u.Alive[unit.Index], $"the archer never hurt the Warrior (hp {u.Hp[unit.Index]} / {max}, tick {sim.TickNumber})")) return;
        sim.Enqueue(Command.Move(1, archer, away));
        int lastSeen = u.LastDamagedTick[unit.Index], shownAt = -1, shownEarly = 0;
        for (int t = 0; t < f.RegenDelayTicks + 200 && shownAt < 0; t++)
        {
            sim.Tick();
            panel.Sync();
            int last = u.LastDamagedTick[unit.Index];
            if (last != lastSeen) { lastSeen = last; continue; }
            if (panel.RegenLabel.Visible && sim.TickNumber - last < f.RegenDelayTicks) shownEarly++;
            if (panel.RegenLabel.Visible) shownAt = sim.TickNumber;
        }
        int lastHit = u.LastDamagedTick[unit.Index];
        Check(shownAt == lastHit + f.RegenDelayTicks && shownEarly == 0 && panel.RegenLabel.Text == cue && cue.Length > 0,
            $"the cue: first at next tick {shownAt} (last hit {lastHit}, want {lastHit + f.RegenDelayTicks}), {shownEarly} early, text '{panel.RegenLabel.Text}' (data '{cue}')");
        Check(u.Hp[unit.Index] < max, $"the Warrior is full ({u.Hp[unit.Index]} / {max}) when the cue shows");
        GD.Print($"  (d) Warrior hurt by the archer's real hits to {u.Hp[unit.Index]} / {max}; '{panel.RegenLabel.Text}' from next tick {shownAt} (last hit in tick {lastHit})");
        await Frames(2);
    }

    // ---- helpers ----

    // Every pair the sim keeps at least its two radii apart must be drawn apart: centre distance >= the two drawn radii.
    private void Apart(World w, UnitViews views, int[] slots, string what)
    {
        UnitStore u = w.Units;
        int kept = 0, closer = 0, overlaps = 0;
        float minGap = float.PositiveInfinity;
        for (int a = 0; a < slots.Length; a++)
            for (int c = a + 1; c < slots.Length; c++)
            {
                int i = slots[a], j = slots[c];
                float sim = System.Numerics.Vector2.Distance(u.Position[i], u.Position[j]);
                // A shown unit where its node stands; one the local player's fog hides (the AI seat's) where it would be drawn.
                Vector3 pi = Drawn(w, views, i), pj = Drawn(w, views, j);
                float drawn = new Vector2(pi.X - pj.X, pi.Z - pj.Z).Length();
                float ri = views.MeshOf(u.TypeId[i])!.Radius, rj = views.MeshOf(u.TypeId[j])!.Radius;
                if (sim < u.Radius[i] + u.Radius[j] - Eps)
                {
                    closer++;
                    GD.Print($"    {what}: slots {i} / {j} {sim:0.###} m apart, the sim lets them closer than {u.Radius[i] + u.Radius[j]:0.###} m");
                    continue;
                }
                kept++;
                minGap = Math.Min(minGap, drawn - ri - rj);
                if (drawn < ri + rj - Eps)
                {
                    overlaps++;
                    Check(false, $"{what}: slots {i} / {j} {drawn:0.###} m apart drawn {ri:0.###} + {rj:0.###} m wide: overlap {ri + rj - drawn:0.###} m");
                }
            }
        Check(kept > 0, $"{what}: no pair kept apart by the sim");
        int shown = slots.Count(views.IsShown);
        GD.Print($"  {what}: {slots.Length} units ({shown} shown, {slots.Length - shown} under fog), {kept} pairs kept apart by the sim, {overlaps} drawn overlapping (smallest gap {minGap:0.###} m), {closer} pairs the sim let closer");
    }

    private static Vector3 Drawn(World w, UnitViews views, int slot) =>
        views.IsShown(slot) ? views.ViewOf(slot)!.Position : UnitViews.GroundPoint(w, slot, 1f);

    private static EntityHandle Handle(UnitStore u, int slot) => new(slot, u.Generation[slot]);

    private static void Tick(Simulation sim, int n)
    {
        for (int i = 0; i < n; i++) sim.Tick();
    }

    // Spawns one unit (two ticks) and returns its handle, or default.
    private static Task<EntityHandle> SpawnOne(Simulation sim, int player, int type, System.Numerics.Vector2 at)
    {
        UnitStore u = sim.World.Units;
        var before = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) before[i] = u.Alive[i];
        sim.Enqueue(Command.SpawnUnit(player, type, at));
        Tick(sim, 2);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i] && u.Owner[i] == player && u.TypeId[i] == type) return Task.FromResult(new EntityHandle(i, u.Generation[i]));
        return Task.FromResult(default(EntityHandle));
    }

    // `count` passable cell centres on the level of `at`'s cell, `gap` cells apart, nearest first.
    private static List<System.Numerics.Vector2> Spots(World w, System.Numerics.Vector2 at, int count, int gap)
    {
        NavGrid g = w.NavGrid;
        g.WorldToCell(at, out int x0, out int y0);
        int level = g.LevelAt(x0, y0);
        var picked = new List<(int X, int Y)>();
        for (int r = 0; r < 60 && picked.Count < count; r++)
            for (int dy = -r; dy <= r && picked.Count < count; dy++)
                for (int dx = -r; dx <= r && picked.Count < count; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = x0 + dx, y = y0 + dy;
                    if (x < 1 || y < 1 || x >= g.Width - 1 || y >= g.Height - 1 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) continue;
                    if (picked.Any(p => Math.Abs(p.X - x) < gap && Math.Abs(p.Y - y) < gap)) continue;
                    picked.Add((x, y));
                }
        if (picked.Count < count) throw new InvalidOperationException($"only {picked.Count} of {count} spots near {at}");
        return picked.Select(p => g.CellCenter(p.X, p.Y)).ToList();
    }

    // A cell centre on the level of `at`'s cell, between `min` and `max` cells from it (the nearest ring first), whose square of
    // `half` cells round it is all passable and on that level.
    private static System.Numerics.Vector2 FlatSpot(World w, System.Numerics.Vector2 at, int min, int max, int half)
    {
        NavGrid g = w.NavGrid;
        g.WorldToCell(at, out int x0, out int y0);
        int level = g.LevelAt(x0, y0);
        for (int r = min; r <= max; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = x0 + dx, y = y0 + dy;
                    bool flat = true;
                    for (int qy = y - half; qy <= y + half && flat; qy++)
                        for (int qx = x - half; qx <= x + half && flat; qx++)
                            flat = qx >= 0 && qy >= 0 && qx < g.Width && qy < g.Height && g.IsPassable(qx, qy) && g.LevelAt(qx, qy) == level;
                    if (flat) return g.CellCenter(x, y);
                }
        throw new InvalidOperationException($"no flat {2 * half + 1}-cell square on level {level} {min}-{max} cells from {at}");
    }

    // A passable cell centre on the level of `at`'s cell, between `min` and `max` cells from it (the nearest ring first).
    private static System.Numerics.Vector2 SameLevel(World w, System.Numerics.Vector2 at, int min, int max)
    {
        NavGrid g = w.NavGrid;
        g.WorldToCell(at, out int x0, out int y0);
        int level = g.LevelAt(x0, y0);
        for (int r = min; r <= max; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = x0 + dx, y = y0 + dy;
                    if (g.IsPassable(x, y) && g.LevelAt(x, y) == level) return g.CellCenter(x, y);
                }
        throw new InvalidOperationException($"no cell on level {level} {min}-{max} cells from {at}");
    }

    // As QueuePackTest's: a free anchor 10-45 m from the hall, clear of the others and of units.
    private static int FreeAnchor(World w, int hall, int type, List<int> taken)
    {
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        BuildingDef def = w.Data.Buildings[type];
        System.Numerics.Vector2 near = SelectionController.SiteCenter(w, hall);
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < g.Width * g.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(g, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < 10f || d > 45f) continue;
            int x = cell % g.Width, y = cell / g.Width;
            if (taken.Any(t => Math.Abs(t % g.Width - x) < 8 && Math.Abs(t / g.Width - y) < 8)) continue;
            bool ok = w.CanPlace(0, type, cell, out PlacementError r);
            if (!ok && r != PlacementError.CannotAfford && !(r == PlacementError.Requires && w.Buildings.Fits(type, cell)) && !ExploredGround.IsUnexplored(r)) continue;
            bool empty = true;
            for (int i = 0; i < u.Capacity && empty; i++)
                if (u.Alive[i] && MathF.Abs(u.Position[i].X - c.X) < def.FootprintWidth + 3f && MathF.Abs(u.Position[i].Y - c.Y) < def.FootprintHeight + 3f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        if (best < 0) throw new InvalidOperationException($"no free anchor for {def.Key}");
        return best;
    }

    private static void SetMoney(World w, int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(w)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[0] = gold;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[0] = wood;
    }

    private static void Pick(OptionButton list, int index)
    {
        list.Select(index);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(_shots);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_shots, name + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok && _failures.Count < 200) _failures.Add(message);
        return ok;
    }
}
