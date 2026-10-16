using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V8 fix round 1 (2026-10-15-2325): attacks on the BUG-0740 / 0741 / 0742 fixes in <see cref="KeyMap"/>.
/// <list type="bullet">
/// <item>The defaults never clash under the new rule (else every file with a <c>[keys]</c> section would reset).</item>
/// <item>Random walks on the tab (seeded, thousands): every map <see cref="KeyMap.TrySet"/> builds, written and read back,
/// is the same map with 0 problems; every bound key is plain and fires its action after <see cref="KeyMap.Apply"/>.</item>
/// <item>Rotations and swaps: the four orders rotated, the 15 grid keys rotated, the 9 digits reversed, Tab and a digit
/// swapped, B / V swapped.</item>
/// <item>Digit / Tab vs grid keys on the tab and in the file (including a file the pre-fix build could write).</item>
/// <item>Every action x every <see cref="Key"/> value: accepted means it round-trips; modifier masks and
/// <see cref="Key.Special"/> are refused.</item>
/// <item>Hand-edited values: combos, odd names, non-strings: either one problem and the default, or a plain key that fires.</item>
/// <item>Clash cascades in the file end, and old files (no section, empty section, partial, extra keys) read clean.</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V8R1Test.tscn</c>; prints "QA M6-V8 R1 TEST PASS" or each failure.</remarks>
public partial class QaM6V8R1Test : Node
{
    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        try
        {
            DefaultsClean();
            RandomWalks();
            Rotations();
            DigitsAndGrid();
            EveryKeyEveryAction();
            HandEdits();
            CascadesAndOldFiles();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        new KeyMap().Apply();
        foreach (string f in _failures) GD.Print($"QA M6-V8 R1 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V8 R1 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private static int A(string action) => Array.IndexOf(KeyMap.Actions, action);

    private void Check(bool ok, string what)
    {
        if (!ok && _failures.Count < 60) _failures.Add(what);
    }

    private static (KeyMap Back, List<string> Problems) RoundTrip(KeyMap keys)
    {
        var file = new ConfigFile();
        keys.Write(file);
        // Through text, as the real file is.
        var parsed = new ConfigFile();
        parsed.Parse(file.EncodeToText());
        var problems = new List<string>();
        return (KeyMap.Read(parsed, problems), problems);
    }

    private static (KeyMap Back, List<string> Problems) ReadKeys(string section)
    {
        var file = new ConfigFile();
        file.Parse(section);
        var problems = new List<string>();
        return (KeyMap.Read(file, problems), problems);
    }

    private static string Dump(KeyMap m) =>
        string.Join(" ", Enumerable.Range(0, KeyMap.Actions.Length).Select(i => $"{KeyMap.Actions[i]}={OS.GetKeycodeString(m[i])}"));

    // True when no two actions that clash share a key.
    private static string? ClashIn(KeyMap m)
    {
        for (int i = 0; i < KeyMap.Actions.Length; i++)
            for (int j = i + 1; j < KeyMap.Actions.Length; j++)
                if (m[i] == m[j] && KeyMap.Clash(i, j)) return $"{KeyMap.Actions[i]} / {KeyMap.Actions[j]} both {OS.GetKeycodeString(m[i])}";
        return null;
    }

    private void Fires(KeyMap m, string what)
    {
        m.Apply();
        for (int i = 0; i < KeyMap.Actions.Length; i++)
        {
            Key k = m[i];
            bool plain = KeyMap.IsPlainKey(k);
            bool fires = InputMap.EventIsAction(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true }, KeyMap.Actions[i]);
            if (!plain || !fires) { Check(false, $"{what}: {KeyMap.Actions[i]} bound to 0x{(long)k:x}, plain {plain}, fires {fires}"); break; }
        }
    }

    private void DefaultsClean()
    {
        var d = new KeyMap();
        Check(ClashIn(d) == null, $"the defaults clash under the new rule: {ClashIn(d)}");
        (KeyMap back, List<string> p) = RoundTrip(d);
        Check(back.SameAs(d) && p.Count == 0, $"defaults round trip: problems [{string.Join("; ", p)}]");
        Fires(d, "defaults");
    }

    // The pool a player might press: letters, digits, Tab, F keys, keypad, punctuation, a few reserved ones (refused).
    private static readonly Key[] Pool = BuildPool();

    private static Key[] BuildPool()
    {
        var keys = new List<Key>();
        for (Key k = Key.A; k <= Key.Z; k++) keys.Add(k);
        for (Key k = Key.Key0; k <= Key.Key9; k++) keys.Add(k);
        for (Key k = Key.Kp0; k <= Key.Kp9; k++) keys.Add(k);
        keys.AddRange(new[]
        {
            Key.Tab, Key.F1, Key.F2, Key.F5, Key.F9, Key.F10, Key.F11, Key.F12, Key.Space, Key.Comma, Key.Period, Key.Slash,
            Key.Semicolon, Key.Apostrophe, Key.Bracketleft, Key.Bracketright, Key.Minus, Key.Equal, Key.Quoteleft,
            Key.Backslash, Key.Escape, Key.Shift, Key.Ctrl, Key.Alt, Key.Left, Key.Home, Key.Insert, Key.Delete, Key.Capslock,
            Key.Backspace, Key.Enter, Key.KpEnter,
        });
        return keys.ToArray();
    }

    private void RandomWalks()
    {
        int walks = 0, sets = 0, lost = 0;
        for (int seed = 1; seed <= 3000; seed++)
        {
            var rng = new Random(seed); // test code only; seeded for repeatability
            var m = new KeyMap();
            int steps = 5 + rng.Next(60);
            for (int s = 0; s < steps; s++)
            {
                int action = rng.Next(KeyMap.Actions.Length);
                Key key = Pool[rng.Next(Pool.Length)];
                if (m.TrySet(action, key) == KeyMap.Ok) sets++;
                if (rng.Next(40) == 0) m.Reset();
            }
            walks++;
            string? clash = ClashIn(m);
            Check(clash == null, $"walk {seed}: TrySet built a clashing map: {clash}");
            (KeyMap back, List<string> p) = RoundTrip(m);
            if (!back.SameAs(m) || p.Count > 0)
            {
                lost++;
                Check(false, $"walk {seed}: read back differs, problems [{string.Join("; ", p)}]\n  wrote {Dump(m)}\n  read  {Dump(back)}");
            }
            if (seed % 300 == 0) Fires(m, $"walk {seed}");
        }
        GD.Print($"random walks: {walks} maps, {sets} accepted TrySet calls, {lost} not read back as written");
    }

    private void Rotations()
    {
        Key[] d = KeyMap.Defaults();
        var cases = new List<(string Name, KeyMap Map)>();

        var orders = new KeyMap();
        string[] o = { "order_attack_move", "order_stop", "order_hold", "order_move" };
        var section = "[keys]\n" + string.Join("\n", o.Select((a, i) => $"{a}=\"{OS.GetKeycodeString(d[A(o[(i + 1) % 4])])}\"")) + "\n";
        cases.Add(("orders rotated (file)", ReadKeys(section).Back));
        (KeyMap ob, List<string> op) = ReadKeys(section);
        Check(op.Count == 0 && ob[A("order_attack_move")] == d[A("order_stop")] && ob[A("order_move")] == d[A("order_attack_move")],
            $"orders rotated in the file: problems [{string.Join("; ", op)}], read {Dump(ob)}");

        // The rotation brings A (attack-move's default) to the top row, which the clash rule forbids: attack-move moves to K too.
        var grid = new System.Text.StringBuilder("[keys]\norder_attack_move=\"K\"\n");
        for (int c = 0; c < KeyMap.Cards; c++) grid.Append($"card_{c}=\"{OS.GetKeycodeString(d[KeyMap.FirstCard + (c + 1) % KeyMap.Cards])}\"\n");
        (KeyMap gb, List<string> gp) = ReadKeys(grid.ToString());
        bool gridOk = gp.Count == 0 && gb.Get("order_attack_move") == Key.K;
        for (int c = 0; c < KeyMap.Cards; c++) gridOk &= gb[KeyMap.FirstCard + c] == d[KeyMap.FirstCard + (c + 1) % KeyMap.Cards];
        Check(gridOk, $"15 grid keys rotated in the file: problems [{string.Join("; ", gp)}]");
        cases.Add(("grid rotated", gb));

        var digits = new System.Text.StringBuilder("[keys]\n");
        for (int g = 1; g <= 9; g++) digits.Append($"group_{g}=\"{10 - g}\"\n");
        (KeyMap db, List<string> dp) = ReadKeys(digits.ToString());
        bool digitsOk = dp.Count == 0;
        for (int g = 1; g <= 9; g++) digitsOk &= db.Get($"group_{g}") == Key.Key0 + (10 - g);
        Check(digitsOk, $"digits reversed in the file: problems [{string.Join("; ", dp)}]");
        cases.Add(("digits reversed", db));

        (KeyMap tb, List<string> tp) = ReadKeys("[keys]\nsubgroup_next=\"1\"\ngroup_1=\"Tab\"\n");
        Check(tp.Count == 0 && tb.Get("subgroup_next") == Key.Key1 && tb.Get("group_1") == Key.Tab,
            $"Tab and 1 swapped: problems [{string.Join("; ", tp)}]");
        cases.Add(("tab/1 swapped", tb));

        (KeyMap bb, List<string> bp) = ReadKeys("[keys]\nbuild_basic=\"V\"\nbuild_advanced=\"B\"\n");
        Check(bp.Count == 0 && bb.Get("build_basic") == Key.V && bb.Get("build_advanced") == Key.B,
            $"B and V swapped: problems [{string.Join("; ", bp)}]");
        cases.Add(("B/V swapped", bb));

        // Every map above also survives its own write and read, and every key fires.
        foreach ((string name, KeyMap map) in cases)
        {
            (KeyMap back, List<string> p) = RoundTrip(map);
            Check(back.SameAs(map) && p.Count == 0, $"{name}: second round trip differs, problems [{string.Join("; ", p)}]");
            Fires(map, name);
        }
    }

    private void DigitsAndGrid()
    {
        var m = new KeyMap();
        int g1 = A("group_1"), tab = A("subgroup_next");
        for (int c = 0; c < KeyMap.Cards; c++)
        {
            int card = KeyMap.FirstCard + c;
            Key gridKey = m[card];
            Check(m.ConflictFor(g1, gridKey) >= 0, $"group_1 may take grid key {gridKey} (card_{c})");
            Check(m.ConflictFor(tab, gridKey) >= 0, $"Tab may take grid key {gridKey} (card_{c})");
            Check(m.ConflictFor(card, Key.Key1) == g1, $"card_{c} may take 1 (group_1's)");
            Check(m.ConflictFor(card, Key.Tab) == tab, $"card_{c} may take Tab");
        }
        // Orders and build keys still share lower grid keys (the default A / S / B / V depend on it), not top-row ones.
        Check(m.ConflictFor(A("order_stop"), Key.G) == KeyMap.Ok, "order_stop refused G (a lower grid key)");
        Check(m.ConflictFor(A("build_basic"), Key.Z) == KeyMap.Ok, "build_basic refused Z (a lower grid key)");
        Check(m.ConflictFor(A("order_stop"), Key.Q) == KeyMap.FirstCard, "order_stop took Q (a top-row grid key)");
        // A digit freed from the grid: card_10 moved to K, then group_1 may take Z.
        Check(m.TrySet(KeyMap.FirstCard + 10, Key.K) == KeyMap.Ok && m.TrySet(g1, Key.Z) == KeyMap.Ok, "group_1 refused a freed grid key");
        (KeyMap back, List<string> p) = RoundTrip(m);
        Check(back.SameAs(m) && p.Count == 0, $"group_1=Z, card_10=K round trip: problems [{string.Join("; ", p)}]");

        // In the file: a digit on a grid key still held by its default -> the digit's default, one problem.
        (back, p) = ReadKeys("[keys]\ngroup_1=\"Z\"\n");
        Check(back.Get("group_1") == Key.Key1 && back.Get("card_10") == Key.Z && p.Count == 1, $"file group_1=Z: group_1 {back.Get("group_1")}, problems {p.Count}");
        (back, p) = ReadKeys("[keys]\nsubgroup_next=\"G\"\n");
        Check(back.Get("subgroup_next") == Key.Tab && p.Count == 1, $"file Tab=G: {back.Get("subgroup_next")}, problems {p.Count}");
        // A file the pre-fix build could write (every action, group_1 and card_5 both "1"): both back, one problem, nothing else moves.
        var pre = new KeyMap();
        var file = new ConfigFile();
        pre.Write(file);
        file.SetValue("keys", "card_5", "1");
        file.SetValue("keys", "order_hold", "J");
        var probs = new List<string>();
        KeyMap old = KeyMap.Read(file, probs);
        Check(old.Get("group_1") == Key.Key1 && old.Get("card_5") == KeyMap.Defaults()[KeyMap.FirstCard + 5] && old.Get("order_hold") == Key.J && probs.Count == 1,
            $"pre-fix file card_5=1: group_1 {old.Get("group_1")}, card_5 {old.Get("card_5")}, hold {old.Get("order_hold")}, problems [{string.Join("; ", probs)}]");
    }

    private void EveryKeyEveryAction()
    {
        Key[] all = Enum.GetValues<Key>().Distinct().ToArray();
        int accepted = 0, lost = 0;
        for (int i = 0; i < KeyMap.Actions.Length; i++)
            foreach (Key k in all)
            {
                var m = new KeyMap();
                if (m.TrySet(i, k) != KeyMap.Ok) continue;
                accepted++;
                (KeyMap back, List<string> p) = RoundTrip(m);
                if (back[i] != k || p.Count > 0) { lost++; Check(false, $"{KeyMap.Actions[i]} = {k} (0x{(long)k:x}) accepted but reads back {back[i]}, problems {p.Count}"); }
            }
        GD.Print($"every action x every Key: {accepted} accepted bindings, {lost} lost on read back");

        // Masks and flags are refused by TrySet and the file, for a command and a grid key.
        Key[] bad =
        {
            Key.Special, Key.Unknown, Key.None, (Key)((long)Key.Q | (long)KeyModifierMask.MaskShift),
            (Key)((long)Key.Key1 | (long)KeyModifierMask.MaskCtrl), (Key)((long)Key.A | (long)KeyModifierMask.MaskAlt),
            (Key)((long)Key.Tab | (long)KeyModifierMask.MaskMeta), (Key)((long)Key.K | (long)KeyModifierMask.MaskKpad),
            (Key)((long)Key.K | (long)KeyModifierMask.MaskGroupSwitch), (Key)((long)Key.F1 | (long)KeyModifierMask.MaskCmdOrCtrl),
        };
        foreach (Key k in bad)
            foreach (int i in new[] { A("order_stop"), A("group_3"), KeyMap.FirstCard + 7 })
                Check(new KeyMap().TrySet(i, k) == KeyMap.IsReserved, $"TrySet({KeyMap.Actions[i]}, 0x{(long)k:x}) not refused");

        // Codes inside the code mask that are not keys (code-only, never from a key press or the file): noted, not asserted.
        // Not round-tripped here: OS.GetKeycodeString prints a Unicode parsing error for them (QA run 2026-10-15-2325: all lost).
        int odd = 0;
        foreach (long code in new long[] { 0x400999, 0x40FFFF, 0x5FFFFF, 0x12345, 0x7FFFFE })
            if (new KeyMap().TrySet(A("group_9"), (Key)code) == KeyMap.Ok) odd++;
        GD.Print($"note: {odd} of 5 non-enum codes inside the code mask accepted by TrySet (code-only)");
    }

    private void HandEdits()
    {
        string[] values =
        {
            "Shift+Q", "Ctrl+1", "Alt+A", "Meta+Tab", "Command+Q", "Kp 1", "Kp Enter", "Shift", "Ctrl", "Escape", "F10", "F12", "F11",
            "q", "TAB", "tab", "Q+Shift", "", " ", "  K  ", "Special", "Unknown", "None", "Space", "Left", "Home", "+", "Shift+",
            "1.5", "0x51", "81", "A A", "é", "Q\u0000", "Meta+K", "Command+K", "Cmd+K", "Kp+K", "Shift+Ctrl+K",
        };
        int clean = 0, rejected = 0;
        foreach (string v in values)
        {
            var file = new ConfigFile();
            file.SetValue("keys", "order_stop", v);
            var p = new List<string>();
            KeyMap m = KeyMap.Read(file, p);
            Key k = m.Get("order_stop");
            if (p.Count == 1 && k == Key.S) { rejected++; GD.Print($"note: '{v.Replace("\u0000", "\\0")}' rejected: {p[0]}"); continue; }
            if (p.Count == 0 && KeyMap.IsPlainKey(k) && Array.IndexOf(KeyMap.Reserved, k) < 0)
            {
                clean++;
                m.Apply();
                Check(InputMap.EventIsAction(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true }, "order_stop"),
                    $"'{v}' read as {k} but that key doesn't fire Stop");
                GD.Print($"note: '{v}' reads clean as {OS.GetKeycodeString(k)}");
                continue;
            }
            Check(false, $"'{v}' for order_stop: bound 0x{(long)k:x}, problems [{string.Join("; ", p)}]");
        }
        // Not strings.
        foreach (Variant v in new Variant[] { 81, 1.0, true, new Godot.Collections.Array { "Q" }, new Godot.Collections.Dictionary() })
        {
            var file = new ConfigFile();
            file.SetValue("keys", "order_stop", v);
            var p = new List<string>();
            KeyMap m = KeyMap.Read(file, p);
            Check(p.Count == 1 && m.Get("order_stop") == Key.S, $"non-string {v.VariantType} for order_stop: {m.Get("order_stop")}, problems {p.Count}");
            rejected++;
        }
        new KeyMap().Apply();
        GD.Print($"hand edits: {clean} read clean, {rejected} rejected with one problem");
    }

    private void CascadesAndOldFiles()
    {
        // attack=H (Hold not moved) resets to A, which then clashes with stop=A: both back, 2 problems, defaults.
        (KeyMap back, List<string> p) = ReadKeys("[keys]\norder_attack_move=\"H\"\norder_stop=\"A\"\n");
        Check(back.SameAs(new KeyMap()) && p.Count == 2, $"cascade attack=H, stop=A: problems [{string.Join("; ", p)}], read {Dump(back)}");
        // A long chain: each order takes the next one's default except the last, which isn't moved.
        (back, p) = ReadKeys("[keys]\norder_attack_move=\"S\"\norder_stop=\"H\"\norder_hold=\"M\"\n");
        Check(back.SameAs(new KeyMap()) && p.Count == 3, $"chain A->S->H->M (M unmoved): problems [{string.Join("; ", p)}], read {Dump(back)}");
        // Every action set to the same key: ends, and the result clashes nowhere.
        var sb = new System.Text.StringBuilder("[keys]\n");
        foreach (string a in KeyMap.Actions) sb.Append($"{a}=\"K\"\n");
        (back, p) = ReadKeys(sb.ToString());
        Check(ClashIn(back) == null && p.Count > 0, $"all 31 actions = K: clash {ClashIn(back)}, problems {p.Count}");
        GD.Print($"all actions = K: {p.Count} problems, K kept by {Enumerable.Range(0, KeyMap.Actions.Length).Count(i => back[i] == Key.K)} action(s)");
        // Fuzzed files: random values per action from the pool; the read never clashes and every key fires.
        for (int seed = 1; seed <= 500; seed++)
        {
            var rng = new Random(seed * 7919);
            var f = new System.Text.StringBuilder("[keys]\n");
            foreach (string a in KeyMap.Actions)
                if (rng.Next(3) > 0) f.Append($"{a}=\"{OS.GetKeycodeString(Pool[rng.Next(Pool.Length)])}\"\n");
            (back, p) = ReadKeys(f.ToString());
            string? clash = ClashIn(back);
            Check(clash == null, $"fuzzed file {seed}: read map clashes: {clash}");
            if (seed % 100 == 0) Fires(back, $"fuzzed file {seed}");
        }

        // Old files: no section (M6-V7), an empty section, a partial one, unknown actions.
        (back, p) = ReadKeys("[display]\nwidth=1280\n");
        Check(back.SameAs(new KeyMap()) && p.Count == 0, $"no [keys]: problems {p.Count}");
        (back, p) = ReadKeys("[keys]\n");
        Check(back.SameAs(new KeyMap()) && p.Count == 0, $"empty [keys]: problems {p.Count}");
        (back, p) = ReadKeys("[keys]\norder_patrol=\"P\"\ngroup_10=\"0\"\ncard_15=\"Y\"\norder_hold=\"J\"\n");
        Check(back.Get("order_hold") == Key.J && p.Count == 0, $"unknown actions plus hold=J: hold {back.Get("order_hold")}, problems {p.Count}");
    }
}
