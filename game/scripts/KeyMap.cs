using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Rts.Game;

/// <summary>
/// The rebindable HUD keys (M6-V8, docs/02 "Controls and camera"): one physical key per action of <see cref="Actions"/>
/// (the orders, Tab, the build menus, the control group digits and the command card's 15 grid keys), with today's keys
/// from <c>project.godot</c> as the defaults. Saved in <c>settings.cfg</c> <c>[keys]</c> by <see cref="GameSettings"/>;
/// <see cref="Apply"/> re-registers the Godot <see cref="InputMap"/> actions, which every key handler reads. View state only.
/// </summary>
/// <remarks>
/// Esc, the modifiers (Shift, Ctrl, Alt, Meta), the camera's arrow keys and the F10 / F12 keys stay fixed: binding one is
/// refused (<see cref="Reserved"/>), and so is anything that isn't one plain key (a modifier combo). Two actions may not share
/// a key when both are commands (orders, Tab, B / V, digits), both are grid keys, one is a control group digit or Tab and the
/// other any grid key (a build menu or a production card owns the whole grid while it is up, and a digit must recall its group
/// whatever is selected), or one is a command and the other a top-row grid key (Q W E R T: a unit card's ability row, which
/// sees keys before the orders). An order or build key and a lower grid key may share one, as A and the sixth grid key do by
/// default (docs/02 "Grid hotkeys").
/// </remarks>
public sealed class KeyMap
{
    /// <summary>Control groups and command card cells.</summary>
    public const int Groups = 9, Cards = 15;

    /// <summary>The input actions this map binds, in the settings tab's order.</summary>
    public static readonly string[] Actions = BuildActions();

    /// <summary>The index in <see cref="Actions"/> of the first <c>group_N</c> and of <c>card_0</c>.</summary>
    public static readonly int FirstGroup = Array.IndexOf(Actions, "group_1"), FirstCard = Array.IndexOf(Actions, "card_0");

    /// <summary>The index in <see cref="Actions"/> of <c>subgroup_next</c> (Tab).</summary>
    public static readonly int SubgroupNext = Array.IndexOf(Actions, "subgroup_next");

    /// <summary>Keys no action may take.</summary>
    public static readonly Key[] Reserved =
    {
        Key.Escape, Key.Shift, Key.Ctrl, Key.Alt, Key.Meta, Key.Left, Key.Right, Key.Up, Key.Down, Key.F10, Key.F12, Key.None,
    };

    /// <summary>What <see cref="TrySet"/> returns for a reserved key; a conflict returns the other action's index, a success <see cref="Ok"/>.</summary>
    public const int Ok = -1, IsReserved = -2;

    private static Key[]? _defaults;
    private readonly Key[] _keys;

    /// <summary>Moves on every <see cref="Apply"/>, so a view that shows key names (the command card) knows to refresh them.</summary>
    public static int Version { get; private set; }

    /// <summary>A map holding the defaults.</summary>
    public KeyMap() => _keys = (Key[])Defaults().Clone();

    /// <summary>The bound physical key of action <paramref name="i"/>.</summary>
    public Key this[int i] => _keys[i];

    /// <summary>The bound physical key of <paramref name="action"/>, or <see cref="Key.None"/> for an action not in the map.</summary>
    public Key Get(string action)
    {
        int i = Array.IndexOf(Actions, action);
        return i >= 0 ? _keys[i] : Key.None;
    }

    /// <summary>The project's key for each action (<c>project.godot</c>'s <c>input/&lt;action&gt;</c>, read once): the defaults.</summary>
    public static Key[] Defaults()
    {
        if (_defaults != null) return _defaults;
        var keys = new Key[Actions.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            Variant setting = ProjectSettings.GetSetting($"input/{Actions[i]}");
            if (setting.VariantType != Variant.Type.Dictionary) continue;
            foreach (Variant e in setting.AsGodotDictionary()["events"].AsGodotArray())
                if (e.As<InputEvent>() is InputEventKey k)
                {
                    keys[i] = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
                    break;
                }
        }
        return _defaults = keys;
    }

    /// <summary>A copy.</summary>
    public KeyMap Clone()
    {
        var copy = new KeyMap();
        _keys.CopyTo(copy._keys, 0);
        return copy;
    }

    /// <summary>True when every action has the same key as in <paramref name="other"/>.</summary>
    public bool SameAs(KeyMap other) => ((ReadOnlySpan<Key>)_keys).SequenceEqual(other._keys);

    /// <summary>Every action back to its default.</summary>
    public void Reset() => Defaults().CopyTo(_keys, 0);

    /// <summary>True when actions <paramref name="a"/> and <paramref name="b"/> may not share a key (see the remarks).</summary>
    public static bool Clash(int a, int b)
    {
        if (a == b) return false;
        bool gridA = a >= FirstCard, gridB = b >= FirstCard;
        if (gridA == gridB) return true;
        int card = gridA ? a - FirstCard : b - FirstCard, command = gridA ? b : a;
        // BUG-0741: a digit or Tab must work whatever is selected, and a build menu or a production card owns the whole grid.
        if (command >= FirstGroup || command == SubgroupNext) return true;
        return card < CommandCard.Columns;
    }

    /// <summary>
    /// True when <paramref name="key"/> is one plain key: not <see cref="Key.None"/>, no modifier bits (a file's "Shift+Q"
    /// is Q with the Shift mask, which no key press matches once applied), not the bare <see cref="Key.Special"/> flag
    /// or <see cref="Key.Unknown"/> (BUG-0742), and a code whose Godot name reads back as the same key, so it survives the
    /// file (BUG-0744: a code inside the code mask that names no key, such as 0x400999, was accepted and lost on read).
    /// </summary>
    public static bool IsPlainKey(Key key)
    {
        long code = (long)key;
        if (key == Key.None || key == Key.Special || key == Key.Unknown || (code & ~(long)KeyModifierMask.CodeMask) != 0) return false;
        // Asked before naming it: OS.GetKeycodeString logs a "Unicode parsing error" for a code that is neither a named key
        // (the Special range) nor a character.
        bool named = (code & (long)Key.Special) != 0 ? Enum.IsDefined(key) : code <= 0x10FFFF && (code < 0xD800 || code > 0xDFFF);
        if (!named) return false;
        string name = OS.GetKeycodeString(key);
        return name.Length > 0 && OS.FindKeycodeFromString(name) == key;
    }

    /// <summary>The action <paramref name="key"/> would clash with if action <paramref name="i"/> took it, <see cref="IsReserved"/> (a fixed key or not a plain key), or <see cref="Ok"/>.</summary>
    public int ConflictFor(int i, Key key)
    {
        if (!IsPlainKey(key) || Array.IndexOf(Reserved, key) >= 0) return IsReserved;
        for (int j = 0; j < _keys.Length; j++)
            if (_keys[j] == key && Clash(i, j)) return j;
        return Ok;
    }

    /// <summary>Binds <paramref name="key"/> to action <paramref name="i"/> unless it is reserved or clashes; returns <see cref="Ok"/> when bound, else what <see cref="ConflictFor"/> says (nothing changes).</summary>
    public int TrySet(int i, Key key)
    {
        int conflict = ConflictFor(i, key);
        if (conflict == Ok) _keys[i] = key;
        return conflict;
    }

    /// <summary>Re-registers each action's <see cref="InputMap"/> events as its one physical key (no modifiers, so Ctrl + digit still matches a group key).</summary>
    public void Apply()
    {
        for (int i = 0; i < Actions.Length; i++)
        {
            StringName action = Actions[i];
            if (!InputMap.HasAction(action)) InputMap.AddAction(action);
            InputMap.ActionEraseEvents(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = _keys[i] });
        }
        Version++;
    }

    /// <summary>Writes the <c>[keys]</c> section: each action's key by its Godot name (<c>"A"</c>, <c>"Tab"</c>, <c>"1"</c>).</summary>
    public void Write(ConfigFile file)
    {
        for (int i = 0; i < Actions.Length; i++) file.SetValue("keys", Actions[i], OS.GetKeycodeString(_keys[i]));
    }

    /// <summary>
    /// The <c>[keys]</c> section of <paramref name="file"/>: a missing section or key is the default (a file from before
    /// M6-V8 reads clean); a value that isn't one plain key name (junk, a number, a reserved key, a modifier combo such as
    /// "Shift+Q" or "Meta+K": the trimmed value must equal the key's Godot name, any case) is one problem in
    /// <paramref name="problems"/> and the default. Extra keys are ignored. The keys read are
    /// then checked as a whole map, never against the defaults of actions the file also moves (BUG-0740: any map the
    /// Keybinds tab makes reads back as written): see <see cref="ResolveClashes"/>.
    /// </summary>
    public static KeyMap Read(ConfigFile file, List<string> problems)
    {
        var map = new KeyMap();
        if (!file.HasSection("keys")) return map;
        var fromFile = new bool[Actions.Length];
        for (int i = 0; i < Actions.Length; i++)
        {
            if (!file.HasSectionKey("keys", Actions[i])) continue;
            Variant v = file.GetValue("keys", Actions[i]);
            string text = v.VariantType == Variant.Type.String ? v.AsString().Trim() : "";
            Key key = text.Length > 0 ? OS.FindKeycodeFromString(text) : Key.None;
            // BUG-0744: Godot drops a prefix it doesn't read as a modifier ("Meta+K", "Cmd+K", "Kp+K" all give K), so the
            // value must be the key's own name (any case) or it is a combo the file can't hold.
            if (!IsPlainKey(key) || Array.IndexOf(Reserved, key) >= 0 || !string.Equals(OS.GetKeycodeString(key), text, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"keys.{Actions[i]} must be the name of one key that isn't reserved, not {v}");
                continue;
            }
            map._keys[i] = key;
            fromFile[i] = true;
        }
        map.ResolveClashes(fromFile, problems);
        return map;
    }

    // Two actions that clash after the read: a key from the file that clashes with a default goes back to its own default
    // (one problem); two keys from the file that clash both go back (one problem). Resetting can make a new clash with
    // another key from the file, so it repeats; it ends, as each pass puts at least one key back and the defaults never
    // clash with each other (if they somehow did, the whole map is the defaults).
    private void ResolveClashes(bool[] fromFile, List<string> problems)
    {
        Key[] defaults = Defaults();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < _keys.Length; i++)
                for (int j = i + 1; j < _keys.Length; j++)
                {
                    if (_keys[i] != _keys[j] || !Clash(i, j)) continue;
                    string name = OS.GetKeycodeString(_keys[i]);
                    if (fromFile[i] && fromFile[j]) problems.Add($"keys.{Actions[i]} and keys.{Actions[j]} are both \"{name}\"; both are their defaults");
                    else if (fromFile[i]) problems.Add($"keys.{Actions[i]} = \"{name}\" is already keys.{Actions[j]}");
                    else if (fromFile[j]) problems.Add($"keys.{Actions[j]} = \"{name}\" is already keys.{Actions[i]}");
                    else
                    {
                        problems.Add($"keys.{Actions[i]} and keys.{Actions[j]} share a key; all keys are the defaults");
                        Reset();
                        return;
                    }
                    if (fromFile[i]) (_keys[i], fromFile[i]) = (defaults[i], false);
                    if (fromFile[j]) (_keys[j], fromFile[j]) = (defaults[j], false);
                    changed = true;
                }
        }
    }

    /// <summary>The key as the keyboard labels it (the physical key under the current layout; its US name headless, where there is no layout).</summary>
    public static string KeyName(Key physical)
    {
        if (physical == Key.None) return "";
        Key code = DisplayServer.GetName() != "headless" ? DisplayServer.KeyboardGetKeycodeFromPhysical(physical) : physical;
        return OS.GetKeycodeString(code == Key.None ? physical : code);
    }

    /// <summary>The name of action <paramref name="i"/> on the Keybinds tab, from <c>ui.json</c> <c>keys</c>.</summary>
    public static string ActionName(UiText ui, int i)
    {
        if (i >= FirstCard) return ui.KeysFormat(KeysText.Card, (i - FirstCard) / CommandCard.Columns + 1, (i - FirstCard) % CommandCard.Columns + 1);
        if (i >= FirstGroup) return ui.KeysFormat(KeysText.Group, i - FirstGroup + 1);
        return Enum.TryParse(Actions[i].Replace("_", ""), ignoreCase: true, out KeysText k) ? ui.Keys(k) : Actions[i];
    }

    private static string[] BuildActions()
    {
        var list = new List<string> { "order_attack_move", "order_stop", "order_hold", "order_move", "subgroup_next", "build_basic", "build_advanced" };
        for (int g = 1; g <= Groups; g++) list.Add(string.Create(CultureInfo.InvariantCulture, $"group_{g}"));
        for (int c = 0; c < Cards; c++) list.Add(string.Create(CultureInfo.InvariantCulture, $"card_{c}"));
        return list.ToArray();
    }
}
