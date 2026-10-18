using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-VH2 (2026-10-16-0847): attacks on the view hardening.
/// <list type="bullet">
/// <item>BUG-0744 item 1: hostile <c>[keys]</c> values (QA's 44 hand edits of <c>QaM6V8R1Test</c> plus the Meta / Command /
/// Cmd / Kp / Win / Super / Option families in any case and with spaces) inside a whole, real settings file whose other
/// actions are moved off their defaults: a combo is refused with exactly one problem and the default; a plain key name in any
/// case with spaces round it reads as that key; every other action, section and value read as written ("nothing lost");
/// the file saved again reads clean.</item>
/// <item>BUG-0744 item 2: every code from 0 to 0x10FFFF and every <see cref="Key"/> value: what <see cref="KeyMap.IsPlainKey"/>
/// takes survives Write + Read through text on an action that may hold it, with 0 problems.</item>
/// <item>BUG-0684 item 2: <see cref="SettingsScreen"/> under an override both ways (on over off, off over on): shown, greyed,
/// Picks keeps the file's; a later screen without the override is live again.</item>
/// <item>BUG-0684 item 4: <see cref="GroundDiscPool"/> under seeded random frames (ascending and shuffled keys): the map key
/// &lt;-&gt; node stays a bijection, a reclaim takes a lower untaken key's node when there is one, else the highest
/// untaken key's (BUG-0662's rule).</item>
/// <item>BUG-0743 item 3 at 2x: an enemy base's workers mine while our scout leaves; the "Last seen" amount equals the sim's
/// amount at the last tick the mine was in sight, and the panel shows it.</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH2Test.tscn</c>; prints "QA M6-VH2 TEST PASS" or each failure.
/// Files live under a per-process <c>user://qa-m6vh2-&lt;pid&gt;/</c> folder, removed at the end.</remarks>
public partial class QaM6VH2Test : Node
{
    private readonly List<string> _failures = new();
    private readonly string _dir = $"user://qa-m6vh2-{OS.GetProcessId()}";

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_dir));
            HostileKeys();
            EveryCode();
            VsyncScreen();
            DiscPoolFuzz();
            await LastSeenAt2x();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        new KeyMap().Apply();
        try { Directory.Delete(ProjectSettings.GlobalizePath(_dir), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-VH2 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-VH2 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private bool Check(bool ok, string what)
    {
        if (!ok && _failures.Count < 60) _failures.Add(what);
        return ok;
    }

    private static int A(string action) => Array.IndexOf(KeyMap.Actions, action);

    // ------------------------------------------------------------------ BUG-0744 item 1

    private void HostileKeys()
    {
        // The other actions moved off their defaults (none of them onto S, Stop's default).
        var moved = new KeyMap();
        foreach ((string a, Key k) in new[] { ("order_hold", Key.J), ("order_move", Key.Kp5), ("group_9", Key.Kp9), ("build_basic", Key.F3), ("subgroup_next", Key.Quoteleft) })
            Check(moved.TrySet(A(a), k) == KeyMap.Ok, $"setup: {a} could not take {k}");
        var baseline = new GameSettings { Width = 1600, Height = 900, Vsync = false, Quality = SettingsQuality.High, MasterVolume = 37, MusicVolume = 12, SfxVolume = 88, EdgePan = 1.75f, ConfineCursor = false, Keys = moved };
        string path = $"{_dir}/hostile.cfg";
        Check(baseline.Save(path), "setup: could not save");
        string[] lines = File.ReadAllLines(ProjectSettings.GlobalizePath(path));
        int stopLine = Array.FindIndex(lines, l => l.StartsWith("order_stop=", StringComparison.Ordinal));
        if (!Check(stopLine >= 0, "setup: no order_stop line in the saved file")) return;

        // (value, expected): null = refused (one problem, S); a key = reads clean as that key.
        var cases = new List<(string Value, Key? Want)>();
        // QaM6V8R1Test's hand edits (the strings), with the outcome the BUG-0744 rule implies.
        foreach (string v in new[] { "Shift+Q", "Ctrl+1", "Alt+A", "Meta+Tab", "Command+Q", "Shift", "Ctrl", "Escape", "F10", "F12", "Q+Shift", "", " ",
                     "Special", "Unknown", "None", "+", "Shift+", "1.5", "0x51", "81", "A A", "é", "Meta+K", "Command+K", "Cmd+K", "Kp+K", "Shift+Ctrl+K" })
            cases.Add((v, null));
        foreach ((string v, Key k) in new[] { ("Kp 1", Key.Kp1), ("Kp Enter", Key.KpEnter), ("F11", Key.F11), ("TAB", Key.Tab), ("tab", Key.Tab), ("  K  ", Key.K), ("Insert", Key.Insert), ("Home", Key.Home) })
            cases.Add((v, k));
        // "Left" is reserved (the camera's arrow keys); since M6-V12 so is Space (jump to the last alert), in any case.
        foreach (string v in new[] { "Left", "Space", "sPaCe", "SPACE" }) cases.Add((v, null));
        // The Meta family in every case and spacing; prefixes Godot doesn't read as modifiers; combos on digits / named keys.
        foreach (string v in new[] { "META+K", "meta+k", "mEtA+K", "Meta+K ", " Meta+K", "Meta +K", "Meta+ K", "COMMAND+K", "command+k", "Command+K  ", "CMD+K", "cmd+k",
                     "KP+K", "kp+k", "Kp+K ", "Kp+1", "Win+K", "Windows+K", "Super+K", "Option+K", "Opt+K", "AltGr+K", "Fn+K", "Hyper+K", "Meta+1", "Meta+Space",
                     "Meta+Kp 1", "Meta+F1", "K+", "K+K", "+K", "++K", "Shift + K", "Meta", "Command", "Windows", "Super", "Meta+", "Ctrl+Meta+K", "Kp", "Kp ",
                     "Kp1", "Kp  1", "\tK\t+", "K\u00a0+K", "Ｋ", "K\u200b" })
            cases.Add((v, null));
        foreach ((string v, Key k) in new[] { ("k", Key.K), ("K ", Key.K), (" K", Key.K), ("\tK", Key.K), ("K\t", Key.K), ("\u00a0K\u00a0", Key.K), ("hOmE", Key.Home), ("HOME", Key.Home),
                     ("kp 1", Key.Kp1), ("KP 1", Key.Kp1), ("f1", Key.F1), ("F1 ", Key.F1), ("Tab ", Key.Tab), ("kp enter", Key.KpEnter), ("SLASH", Key.Slash), ("y", Key.Y), ("BACKSLASH", Key.Backslash) })
            cases.Add((v, k));

        int refused = 0, clean = 0;
        foreach ((string value, Key? want) in cases)
        {
            string[] edited = (string[])lines.Clone();
            edited[stopLine] = "order_stop=" + GD.VarToStr(value);
            File.WriteAllLines(ProjectSettings.GlobalizePath(path), edited);
            var problems = new List<string>();
            GameSettings read = GameSettings.Read(path, problems, out bool found);
            string shown = value.Replace("\t", "\\t").Replace("\u00a0", "\\u00a0").Replace("\u200b", "\\u200b");
            Key expectKey = want ?? Key.S;
            int expectProblems = want == null ? 1 : 0;
            Check(found && problems.Count == expectProblems && read.Keys.Get("order_stop") == expectKey,
                $"'{shown}' for order_stop: read {OS.GetKeycodeString(read.Keys.Get("order_stop"))}, {problems.Count} problems [{string.Join("; ", problems)}], want {OS.GetKeycodeString(expectKey)} with {expectProblems}");
            // Nothing lost: every other action and every other field as written.
            for (int i = 0; i < KeyMap.Actions.Length; i++)
                if (i != A("order_stop") && read.Keys[i] != moved[i])
                {
                    Check(false, $"'{shown}': {KeyMap.Actions[i]} read {OS.GetKeycodeString(read.Keys[i])}, written {OS.GetKeycodeString(moved[i])}");
                    break;
                }
            Check(read.Width == 1600 && read.Height == 900 && !read.Vsync && read.Quality == SettingsQuality.High && read.MasterVolume == 37 && read.MusicVolume == 12
                && read.SfxVolume == 88 && read.EdgePan == 1.75f && !read.ConfineCursor, $"'{shown}': another field read differently");
            // Saved again (the game saves its settings on the next commit): clean, the same map.
            string again = $"{_dir}/again.cfg";
            read.Save(again);
            var p2 = new List<string>();
            GameSettings back = GameSettings.Read(again, p2, out _);
            Check(p2.Count == 0 && back.Keys.SameAs(read.Keys), $"'{shown}': saved again, {p2.Count} problems [{string.Join("; ", p2)}]");
            if (want == null) refused++; else clean++;
        }
        GD.Print($"hostile keys: {cases.Count} values in a whole file, {refused} refused with one problem, {clean} read clean; other actions and fields kept");
    }

    // ------------------------------------------------------------------ BUG-0744 item 2

    private void EveryCode()
    {
        var watch = Stopwatch.StartNew();
        int plain = 0, lost = 0;
        var codes = new List<long>();
        for (long c = 0; c <= 0x10FFFF; c++) codes.Add(c);
        foreach (Key k in Enum.GetValues<Key>()) codes.Add((long)k);
        codes.AddRange(new long[] { 0x400999, 0x40FFFF, 0x5FFFFF, 0x7FFFFF, 0x800000, 0x1000000 | (long)Key.K, 0x2000000 | (long)Key.A });
        var fresh = new KeyMap();
        foreach (long c in codes)
        {
            var k = (Key)c;
            if (!KeyMap.IsPlainKey(k)) continue;
            plain++;
            // An action that may hold it: the first whose ConflictFor is Ok on the defaults.
            int action = -1;
            for (int i = 0; i < KeyMap.Actions.Length && action < 0; i++) if (fresh.ConflictFor(i, k) == KeyMap.Ok) action = i;
            if (action < 0)
            {
                // Taken by a default everywhere it may go, or reserved: its name must still read back as the key.
                if (Array.IndexOf(KeyMap.Reserved, k) < 0 && OS.FindKeycodeFromString(OS.GetKeycodeString(k)) != k) { lost++; Check(false, $"0x{c:x} plain but its name doesn't read back"); }
                continue;
            }
            var m = new KeyMap();
            m.TrySet(action, k);
            var file = new ConfigFile();
            m.Write(file);
            var parsed = new ConfigFile();
            parsed.Parse(file.EncodeToText());
            var problems = new List<string>();
            KeyMap back = KeyMap.Read(parsed, problems);
            if (!back.SameAs(m) || problems.Count != 0)
            {
                lost++;
                Check(false, $"0x{c:x} ('{OS.GetKeycodeString(k)}') on {KeyMap.Actions[action]}: read back {OS.GetKeycodeString(back[action])}, problems [{string.Join("; ", problems)}]");
            }
        }
        GD.Print($"every code: {codes.Count} codes, {plain} plain keys, {lost} lost through the file, {watch.ElapsedMilliseconds} ms");
        Check(plain > 100 && lost == 0, $"every code: {plain} plain, {lost} lost");
    }

    // ------------------------------------------------------------------ BUG-0684 item 2

    private void VsyncScreen()
    {
        UiText ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
        var screen = new SettingsScreen { Name = "QaSettingsScreen" };
        AddChild(screen);
        screen.Build(ui);
        var size = new Vector2I(1152, 648);
        // --vsync on over a file saying off.
        screen.ShowSettings(new GameSettings { Vsync = false }, size, true);
        Check(screen.VsyncOption.Selected == 0 && screen.VsyncOption.Disabled && !screen.Picks().Vsync,
            $"on over off: shown {screen.VsyncOption.Selected} (want 0), disabled {screen.VsyncOption.Disabled}, picks {screen.Picks().Vsync} (want false)");
        // A pick forced on the greyed option (a script, a focus quirk) still never reaches Picks.
        screen.VsyncOption.Select(1);
        Check(!screen.Picks().Vsync, "on over off: selecting Off on the greyed field changed Picks");
        screen.VsyncOption.Select(0);
        Check(!screen.Picks().Vsync, "on over off: selecting On on the greyed field changed Picks");
        // --vsync off over on.
        screen.ShowSettings(new GameSettings { Vsync = true }, size, false);
        Check(screen.VsyncOption.Selected == 1 && screen.VsyncOption.Disabled && screen.Picks().Vsync,
            $"off over on: shown {screen.VsyncOption.Selected} (want 1), disabled {screen.VsyncOption.Disabled}, picks {screen.Picks().Vsync} (want true)");
        // No override (the game without the flag): live again, Picks follows the field.
        screen.ShowSettings(new GameSettings { Vsync = true }, size);
        Check(!screen.VsyncOption.Disabled && screen.VsyncOption.Selected == 0 && screen.Picks().Vsync, "no override after an override: still greyed or off");
        screen.VsyncOption.Select(1);
        Check(!screen.Picks().Vsync, "no override: selecting Off did not reach Picks");
        // Override equal to the file: still greyed, the file's value.
        screen.ShowSettings(new GameSettings { Vsync = true }, size, true);
        Check(screen.VsyncOption.Disabled && screen.VsyncOption.Selected == 0 && screen.Picks().Vsync, "override equal to the file: not greyed or wrong value");
        RemoveChild(screen);
        screen.Free();
    }

    // ------------------------------------------------------------------ BUG-0684 item 4

    private void DiscPoolFuzz()
    {
        var parent = new Node { Name = "QaDiscs" };
        AddChild(parent);
        int frames = 0, reclaims = 0;
        for (int seed = 1; seed <= 40; seed++)
        {
            var rng = new Random(seed * 104729);
            int keys = 8 + rng.Next(200);
            var pool = new GroundDiscPool(parent, $"d{seed}_", keys, 1 + rng.Next(8));
            bool shuffled = seed % 4 == 0;
            for (int f = 0; f < 60; f++)
            {
                frames++;
                // Last frame's holders, before this frame.
                var held = new Dictionary<int, GroundDisc>();
                for (int k = 0; k < keys; k++) if (pool.NodeOf(k) is GroundDisc d) held[k] = d;
                var take = Enumerable.Range(0, keys).Where(_ => rng.Next(100) < 30).ToList();
                if (shuffled) take = take.OrderBy(_ => rng.Next()).ToList();
                var taken = new HashSet<int>();
                var nodesThisFrame = new Dictionary<GroundDisc, int>();
                pool.Begin();
                foreach (int k in take)
                {
                    // What BUG-0662's rule allows if k has no node and none is free: an untaken holder below k first, else the highest untaken holder.
                    var candidates = held.Keys.Where(h => !taken.Contains(h) && pool.NodeOf(h) == held[h]).ToList();
                    bool hadNode = pool.NodeOf(k) != null;
                    int nodesBefore = pool.Nodes, inUseBefore = pool.InUse;
                    GroundDisc got = pool.Take(k);
                    taken.Add(k);
                    if (!Check(!nodesThisFrame.ContainsKey(got), $"seed {seed} frame {f}: key {k} got a node key {(nodesThisFrame.TryGetValue(got, out int o) ? o : -1)} took this frame")) return;
                    nodesThisFrame[got] = k;
                    if (hadNode) { Check(held.TryGetValue(k, out GroundDisc? was) && was == got, $"seed {seed} frame {f}: key {k} lost its own node"); continue; }
                    // Reclaimed from someone?
                    int from = held.Where(kv => kv.Value == got && kv.Key != k).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
                    if (from >= 0 && pool.Nodes == nodesBefore && candidates.Contains(from))
                    {
                        reclaims++;
                        var lower = candidates.Where(h => h < k).ToList();
                        if (lower.Count > 0) Check(from < k, $"seed {seed} frame {f}: key {k} reclaimed key {from}'s node with lower untaken {string.Join(",", lower)}");
                        else Check(from == candidates.Max(), $"seed {seed} frame {f}: key {k} reclaimed key {from}'s node, not the highest untaken {candidates.Max()}");
                        held.Remove(from);
                    }
                    else if (from >= 0 && !candidates.Contains(from))
                        Check(false, $"seed {seed} frame {f}: key {k} took key {from}'s node, which was not reclaimable");
                }
                pool.End();
                // After End: exactly the taken keys hold nodes, all distinct.
                var seen = new HashSet<GroundDisc>();
                for (int k = 0; k < keys; k++)
                {
                    GroundDisc? d = pool.NodeOf(k);
                    if (taken.Contains(k) != (d != null)) { Check(false, $"seed {seed} frame {f}: key {k} taken {taken.Contains(k)} but node {(d != null)}"); break; }
                    if (d != null && !seen.Add(d)) { Check(false, $"seed {seed} frame {f}: two keys share a node"); break; }
                }
                Check(pool.InUse == taken.Count && pool.Nodes <= keys, $"seed {seed} frame {f}: in use {pool.InUse}, taken {taken.Count}, nodes {pool.Nodes} of {keys} keys");
            }
        }
        GD.Print($"disc pool: {frames} frames over 40 seeds, {reclaims} reclaims checked against BUG-0662's rule");
        RemoveChild(parent);
        parent.Free();
    }

    // ------------------------------------------------------------------ BUG-0743 item 3

    private async Task LastSeenAt2x()
    {
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!Check(loaded.Ok, "data failed to load")) return;
        GameData data = loaded.Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        match.Start(data, LaunchOptions.Parse(new[] { "--seed", "1", "--mute", "--units", "0" }));
        runner.ProcessMode = ProcessModeEnum.Disabled;
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        Rts.Sim.Map.NavGrid g = w.NavGrid;
        ResourceStore r = w.Resources;
        SeenResources seen = match.GetNode<FogOfWar>("World3D/FogOfWar").SeenResources!;

        // The start bases are staged by commands: let them land.
        for (int t = 0; t < 3; t++) { sim.Tick(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        // The enemy hall and its nearest gold mine.
        int hall1 = -1;
        for (int b = 0; b < w.Buildings.Capacity; b++) if (w.Buildings.Alive[b] && w.Buildings.Owner[b] == 1) { hall1 = b; break; }
        if (!Check(hall1 >= 0, "no enemy building")) return;
        var hallAt = new System.Numerics.Vector2(w.Buildings.Cell[hall1] % g.Width, w.Buildings.Cell[hall1] / g.Width) * Rts.Sim.Map.MapConstants.CellSize;
        int mine = -1;
        float best = float.MaxValue;
        for (int s = 0; s < r.Capacity; s++)
        {
            if (!r.Alive[s] || data.Resources[r.TypeId[s]].Resource != ResourceKind.Gold) continue;
            var at = new System.Numerics.Vector2(r.Cell[s] % g.Width, r.Cell[s] / g.Width) * Rts.Sim.Map.MapConstants.CellSize;
            float d = System.Numerics.Vector2.Distance(at, hallAt);
            if (d < best) (best, mine) = (d, s);
        }
        if (!Check(mine >= 0, "no gold mine")) return;
        ResourceDef def = data.Resources[r.TypeId[mine]];
        int anchor = r.Cell[mine];
        var centre = new System.Numerics.Vector2((anchor % g.Width + def.FootprintWidth / 2f) * Rts.Sim.Map.MapConstants.CellSize,
            (anchor / g.Width + def.FootprintHeight / 2f) * Rts.Sim.Map.MapConstants.CellSize);
        // Every enemy worker on the mine.
        int workers = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1 && data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker)
            {
                sim.Enqueue(Command.Gather(1, new EntityHandle(i, w.Units.Generation[i]), centre));
                workers++;
            }
        // Our scout: a fresh worker beside the mine (a dev spawn), on the nearest passable cell.
        int standCell = Rts.Sim.Pathfinding.FlowField.NearestPassable(g, (anchor / g.Width) * g.Width + anchor % g.Width - 2);
        sim.Enqueue(Command.SpawnUnit(0, data.FindUnit("malazan_laborer"), g.CellCenter(standCell % g.Width, standCell / g.Width)));
        for (int t = 0; t < 120; t++) { sim.Tick(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        int scout = -1;
        for (int i = 0; i < w.Units.Capacity; i++) if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && System.Numerics.Vector2.Distance(w.Units.Position[i], centre) < 12f) scout = i;
        if (!Check(scout >= 0 && InSight(w, anchor, def), $"scout {scout}, mine in sight {InSight(w, anchor, def)}, enemy workers {workers}")) return;

        // 2x, two ticks a frame from the runner's own loop; the scout walks home.
        int hall0 = -1;
        for (int b = 0; b < w.Buildings.Capacity; b++) if (w.Buildings.Alive[b] && w.Buildings.Owner[b] == 0) { hall0 = b; break; }
        var home = new System.Numerics.Vector2(w.Buildings.Cell[hall0] % g.Width, w.Buildings.Cell[hall0] / g.Width) * Rts.Sim.Map.MapConstants.CellSize;
        sim.Enqueue(Command.Move(0, new EntityHandle(scout, w.Units.Generation[scout]), home));
        int lastSeenTick = -1, lastSeenAmount = -1, changesInSight = 0, prev = r.Remaining[mine], gone = 0, ticks = 0, unseenAfter = 0;
        bool out_ = false;
        int behindInSight = 0;
        void OnTick(Simulation s)
        {
            ticks++;
            bool inSight = InSight(w, anchor, def);
            if (inSight)
            {
                if (r.Remaining[mine] != prev) changesInSight++;
                if (seen.Amount[mine] != r.Remaining[mine]) behindInSight++;
                (lastSeenTick, lastSeenAmount) = (s.TickNumber, r.Remaining[mine]);
                if (out_) out_ = false;
            }
            else if (lastSeenTick >= 0)
            {
                out_ = true;
                unseenAfter++;
                if (seen.Amount[mine] != lastSeenAmount) gone++;
            }
            prev = r.Remaining[mine];
        }
        runner.Ticked += OnTick;
        runner.GameSpeed = 2.0;
        runner.FixedFrameDelta = 0.05;
        runner.ProcessMode = ProcessModeEnum.Inherit;
        for (int f = 0; f < 600 && unseenAfter < 200; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        runner.ProcessMode = ProcessModeEnum.Disabled;
        runner.Ticked -= OnTick;
        int amountNow = r.Remaining[mine];
        GD.Print($"2x last seen: {ticks} ticks, {workers} enemy workers, {changesInSight} amount changes in sight, last in sight at tick {lastSeenTick} with {lastSeenAmount}, " +
            $"{unseenAfter} unseen ticks after (sim now {amountNow}), last-seen amount off on {gone} of them");
        Check(behindInSight == 0, $"2x: the last-seen amount was behind the sim on {behindInSight} ticks in sight");
        Check(lastSeenTick >= 0 && unseenAfter > 0 && changesInSight > 0 && gone == 0 && seen.Amount[mine] == lastSeenAmount,
            $"2x last seen: in sight until tick {lastSeenTick} ({lastSeenAmount}), the store says {seen.Amount[mine]}, off on {gone} of {unseenAfter} unseen ticks, {changesInSight} changes in sight");
        Check(amountNow < lastSeenAmount, $"2x last seen: the enemy mined nothing out of sight ({lastSeenAmount} -> {amountNow}); the test proves less");
        RemoveChild(match);
        match.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static bool InSight(World w, int anchor, ResourceDef def)
    {
        int width = w.NavGrid.Width;
        for (int y = 0; y < def.FootprintHeight; y++)
            for (int x = 0; x < def.FootprintWidth; x++)
                if (w.Fog.IsVisible(0, anchor + y * width + x)) return true;
        return false;
    }
}
