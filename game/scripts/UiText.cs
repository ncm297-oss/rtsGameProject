using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The view's own player-facing text and menu lists, from <c>game/data/common/ui.json</c> (M3-V2; CLAUDE.md rule 8).</summary>
/// <remarks>
/// View-only data: the sim's <c>DataLoader</c> never reads this file. Shape:
/// <c>{ "commands": { "&lt;id&gt;": { "displayName", "hotkeyHint" } }, "buildMenus": { "basic": [slot ids], "advanced": [slot ids] },
/// "placement": { "&lt;reason&gt;": text }, "train": { "&lt;reason&gt;": text }, "research": { "&lt;reason&gt;": text },
/// "states": { "&lt;state&gt;": text }, "hud": { "&lt;label&gt;": text }, "menu": { "&lt;key&gt;": text }, "setup": { "&lt;key&gt;": text },
/// "pause": { "&lt;key&gt;": text }, "settings": { "&lt;key&gt;": text }, "match_over": { "&lt;key&gt;": text }, "keys": { "&lt;key&gt;": text } }</c>. Command ids are <see cref="CommandIds"/>;
/// the placement, train and research keys are the snake_case names of every <see cref="PlacementError"/>,
/// <see cref="TrainError"/> and <see cref="ResearchError"/> member but <c>None</c> (<c>blocked</c>, <c>queue_full</c> ...),
/// the state keys those of every <see cref="UnitState"/> (M3-V3) plus <see cref="OrderedAttackKey"/> (M4-V2), the hud keys those of <see cref="HudText"/>,
/// the menu, setup and pause keys (the game shell, M6-V1) those of <see cref="MenuText"/>, <see cref="SetupText"/> and <see cref="PauseText"/>
/// (M6-V2: <c>setup.map_size_item</c> must also be a format naming <c>{0}</c> that formats to different text for different
/// sizes, else one error naming it, BUG-0602 item 1; M6-V6: the <c>settings</c> keys those of <see cref="SettingsText"/>, and
/// <c>settings.resolution_item</c> a format naming <c>{0}</c> (width) and <c>{1}</c> (height) under the same rule; M6-V4: the <c>match_over</c>
/// keys those of <see cref="MatchOverText"/>, <c>match_over.match_length</c> a format naming <c>{0}</c> and <c>match_over.clock</c> one naming
/// <c>{0}</c> (minutes) and <c>{1}</c> (seconds) under the same rule; M6-V8: the <c>keys</c> keys those of <see cref="KeysText"/>
/// (<c>group</c> and <c>reserved</c> formats naming <c>{0}</c>, <c>card</c> and <c>conflict</c> formats naming <c>{0}</c> and <c>{1}</c>),
/// and <c>hud.resource_left</c> a format naming <c>{0}</c> (the amount) and <c>{1}</c> (the resource), under the same rule; M6-V3: <c>setup</c> also carries the player colour table,
/// <c>colour_1</c> .. <c>colour_8</c>, each <c>{ "displayName", "rgb": "#RRGGBB" }</c>, <see cref="ColourCount"/>); so a member
/// the sim adds later is a load error here until the file has its text. Each of the three reason sections must also
/// carry <see cref="ForwardKey"/> (<c>requires</c>), the reason M3-6's <c>requires</c> gating adds: a key with no enum
/// member yet is accepted (as is any extra key), so the file loads with or without that sim change. The <c>placement</c>
/// section must also carry <see cref="UnexploredKey"/> (M4-V4), the text for M4-3b's "footprint not explored" refusal: read
/// the same way (<see cref="UnexploredPlacementText"/>), and through <see cref="PlacementText"/> once the sim has the member. Loading is
/// fail-fast: a root that is not an object is one error (BUG-0110), every missing or empty key is one error naming it
/// ("ui.json: missing commands.stop.displayName"), and with any error there is no <see cref="UiText"/> (the match logs
/// them with <c>GD.PushError</c> and runs without the command card and the selection panel).
/// </remarks>
public sealed class UiText
{
    /// <summary>The <c>commands</c> key of each <see cref="CardCommand"/> (index = enum value); empty for the ones without their own text.</summary>
    public static readonly string[] CommandIds = { "", "move", "attack_move", "stop", "hold", "build_basic", "build_advanced", "cancel", "", "", "", "" };

    /// <summary>The reason key every reason section carries before the sim has its enum member (M3-6's <c>requires</c> gating).</summary>
    public const string ForwardKey = "requires";

    /// <summary>The <c>placement</c> key of the refusal M4-3b adds for a footprint the player hasn't explored (M4-V4: the view's key, added before the sim's member).</summary>
    public const string UnexploredKey = "unexplored";

    /// <summary>The <c>states</c> key of the panel's text for a unit chasing the target of an explicit Attack order (M4-V2; not a <see cref="UnitState"/>: a chaser is <c>Moving</c>).</summary>
    public const string OrderedAttackKey = "ordered_attack";

    /// <summary>Colours in the player colour table (<c>setup.colour_1</c> .. <c>colour_8</c>; M6-V3).</summary>
    public const int ColourCount = 8;

    /// <summary>Largest number of entries a build menu can show (the 5 x 3 grid).</summary>
    public const int MaxMenuEntries = 15;

    private static UiText? _shared;
    private static bool _sharedTried;

    private readonly string[] _names = new string[CommandIds.Length];
    private readonly string[] _hints = new string[CommandIds.Length];
    private readonly string[] _placement;
    private readonly string[] _train;
    private readonly string[] _research;
    private readonly string[] _states;
    private readonly string[] _hud;
    private readonly string[] _menu;
    private readonly string[] _setup;
    private readonly string[] _pause;
    private readonly string[] _settings;
    private readonly string[] _matchOver;
    private readonly string[] _keys;
    private readonly string[] _load;
    private readonly string[] _colourNames = Blank(ColourCount);
    private readonly uint[] _colourRgb = new uint[ColourCount];

    private UiText()
    {
        _placement = Blank(Enum.GetValues<PlacementError>().Length);
        _train = Blank(Enum.GetValues<TrainError>().Length);
        _research = Blank(Enum.GetValues<ResearchError>().Length);
        _states = Blank(Enum.GetValues<UnitState>().Length);
        _hud = Blank(Enum.GetValues<HudText>().Length);
        _menu = Blank(Enum.GetValues<MenuText>().Length);
        _setup = Blank(Enum.GetValues<SetupText>().Length);
        _pause = Blank(Enum.GetValues<PauseText>().Length);
        _settings = Blank(Enum.GetValues<SettingsText>().Length);
        _matchOver = Blank(Enum.GetValues<MatchOverText>().Length);
        _keys = Blank(Enum.GetValues<KeysText>().Length);
        _load = Blank(Enum.GetValues<LoadText>().Length);
        Array.Fill(_names, "");
        Array.Fill(_hints, "");
    }

    /// <summary>The basic (Age I) build menu's slots, in order.</summary>
    public BuildingSlot[] BasicMenu { get; private set; } = Array.Empty<BuildingSlot>();

    /// <summary>The advanced (Age II) build menu's slots, in order.</summary>
    public BuildingSlot[] AdvancedMenu { get; private set; } = Array.Empty<BuildingSlot>();

    /// <summary>The label of a command button; empty for <see cref="CardCommand.None"/> and <see cref="CardCommand.Place"/>.</summary>
    public string CommandName(CardCommand c) => (uint)c < (uint)_names.Length ? _names[(int)c] : "";

    /// <summary>The hotkey hint shown on a command button.</summary>
    public string CommandHint(CardCommand c) => (uint)c < (uint)_hints.Length ? _hints[(int)c] : "";

    /// <summary>The short text the placement ghost shows for a refusal; empty for <see cref="PlacementError.None"/>.</summary>
    public string PlacementText(PlacementError reason) => (uint)reason < (uint)_placement.Length ? _placement[(int)reason] : "";

    /// <summary>The text a greyed train button shows for <paramref name="reason"/> (<c>ui.json</c> <c>train</c>); empty for <see cref="TrainError.None"/>.</summary>
    public string TrainText(TrainError reason) => (uint)reason < (uint)_train.Length ? _train[(int)reason] : "";

    /// <summary>The text a greyed research button shows for <paramref name="reason"/> (<c>ui.json</c> <c>research</c>); empty for <see cref="ResearchError.None"/>.</summary>
    public string ResearchText(ResearchError reason) => (uint)reason < (uint)_research.Length ? _research[(int)reason] : "";

    /// <summary>A unit state in plain words (<c>ui.json</c> <c>states</c>), for the selection panel.</summary>
    public string StateText(UnitState state) => (uint)state < (uint)_states.Length ? _states[(int)state] : "";

    /// <summary>The selection panel's state text for a unit chasing an ordered Attack's target (<c>ui.json</c> <c>states.ordered_attack</c>, M4-V2).</summary>
    public string OrderedAttackText { get; private set; } = "";

    /// <summary>A HUD label (<c>ui.json</c> <c>hud</c>): "Pop", the selection panel's stat names, "Needs".</summary>
    public string Hud(HudText label) => (uint)label < (uint)_hud.Length ? _hud[(int)label] : "";

    /// <summary>A main menu text (<c>ui.json</c> <c>menu</c>, M6-V1).</summary>
    public string Menu(MenuText key) => (uint)key < (uint)_menu.Length ? _menu[(int)key] : "";

    /// <summary>A skirmish setup text (<c>ui.json</c> <c>setup</c>, M6-V1).</summary>
    public string Setup(SetupText key) => (uint)key < (uint)_setup.Length ? _setup[(int)key] : "";

    /// <summary>One map size list item: <c>setup.map_size_item</c> with <paramref name="size"/> (cells a side) for its <c>{0}</c> (M6-V2).</summary>
    public string MapSizeItem(int size) => string.Format(CultureInfo.InvariantCulture, Setup(SetupText.MapSizeItem), size);

    /// <summary>The <c>setup</c> key of player colour <paramref name="index"/> (0-based): <c>colour_1</c> .. <c>colour_8</c>.</summary>
    public static string ColourKey(int index) => $"colour_{index + 1}";

    /// <summary>Player colour <paramref name="index"/>'s name (<c>setup.colour_N.displayName</c>, M6-V3); empty out of range.</summary>
    public string ColourName(int index) => (uint)index < ColourCount ? _colourNames[index] : "";

    /// <summary>Player colour <paramref name="index"/> as 0xRRGGBB (<c>setup.colour_N.rgb</c>, M6-V3); 0 out of range.</summary>
    public uint ColourRgb(int index) => (uint)index < ColourCount ? _colourRgb[index] : 0u;

    /// <summary>A pause menu text (<c>ui.json</c> <c>pause</c>, M6-V1).</summary>
    public string Pause(PauseText key) => (uint)key < (uint)_pause.Length ? _pause[(int)key] : "";

    /// <summary>A settings screen text (<c>ui.json</c> <c>settings</c>, M6-V6).</summary>
    public string Settings(SettingsText key) => (uint)key < (uint)_settings.Length ? _settings[(int)key] : "";

    /// <summary>One resolution list item: <c>settings.resolution_item</c> with the width for <c>{0}</c> and the height for <c>{1}</c> (M6-V6).</summary>
    public string ResolutionItem(int width, int height) => string.Format(CultureInfo.InvariantCulture, Settings(SettingsText.ResolutionItem), width, height);

    /// <summary>A match-over banner or stats screen text (<c>ui.json</c> <c>match_over</c>, M6-V4).</summary>
    public string MatchOver(MatchOverText key) => (uint)key < (uint)_matchOver.Length ? _matchOver[(int)key] : "";

    /// <summary>A Keybinds tab text (<c>ui.json</c> <c>keys</c>, M6-V8).</summary>
    public string Keys(KeysText key) => (uint)key < (uint)_keys.Length ? _keys[(int)key] : "";

    /// <summary>A Load Game screen or replay playback text (<c>ui.json</c> <c>load</c>, M6-V9).</summary>
    public string LoadGame(LoadText key) => (uint)key < (uint)_load.Length ? _load[(int)key] : "";

    /// <summary>A Load Game date cell: <c>load.date_item</c> with <paramref name="when"/> for its <c>{0}</c>.</summary>
    public string ReplayDate(DateTime when) => string.Format(CultureInfo.InvariantCulture, LoadGame(LoadText.DateItem), when);

    /// <summary>One seat of a Load Game players cell: <c>load.seat</c> with the faction's name for <c>{0}</c> and the seat's kind for <c>{1}</c>.</summary>
    public string ReplaySeat(string faction, string kind) => string.Format(CultureInfo.InvariantCulture, LoadGame(LoadText.Seat), faction, kind);

    /// <summary>The Load Game note for <paramref name="count"/> unreadable files: <c>load.skipped</c> with the count for its <c>{0}</c>.</summary>
    public string ReplaysSkipped(int count) => string.Format(CultureInfo.InvariantCulture, LoadGame(LoadText.Skipped), count);

    /// <summary>A <c>keys</c> format (<see cref="KeysText.Group"/>, <see cref="KeysText.Card"/>, <see cref="KeysText.Conflict"/>, <see cref="KeysText.Reserved"/>) filled with <paramref name="args"/>.</summary>
    public string KeysFormat(KeysText key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Keys(key), args);

    /// <summary>The selection panel's amount line for a resource node (M6-V8): <c>hud.resource_left</c> with <paramref name="amount"/> for <c>{0}</c> and the resource's name for <c>{1}</c>.</summary>
    public string ResourceLeft(int amount, string resource) => string.Format(CultureInfo.InvariantCulture, Hud(HudText.ResourceLeft), amount, resource);

    /// <summary>A match time as <c>match_over.clock</c> (whole minutes for <c>{0}</c>, the seconds past them for <c>{1}</c>) from a tick count (50 ms each); negative counts read as 0.</summary>
    public string Clock(int ticks)
    {
        int seconds = Math.Max(0, ticks) / Rts.Sim.SimConstants.TicksPerSecond;
        return string.Format(CultureInfo.InvariantCulture, MatchOver(MatchOverText.Clock), seconds / 60, seconds % 60);
    }

    /// <summary>The stats screen's length line: <c>match_over.match_length</c> with <see cref="Clock"/> of <paramref name="ticks"/> for its <c>{0}</c>.</summary>
    public string MatchLength(int ticks) => string.Format(CultureInfo.InvariantCulture, MatchOver(MatchOverText.MatchLength), Clock(ticks));

    /// <summary>The <c>requires</c> text of the <c>placement</c> section (<see cref="ForwardKey"/>), readable before the sim has the reason.</summary>
    public string ForwardPlacementText { get; private set; } = "";

    /// <summary>The <c>unexplored</c> text of the <c>placement</c> section (<see cref="UnexploredKey"/>), readable before the sim has the reason.</summary>
    public string UnexploredPlacementText { get; private set; } = "";

    /// <summary>The <c>requires</c> text of the <c>train</c> section (<see cref="ForwardKey"/>).</summary>
    public string ForwardTrainText { get; private set; } = "";

    /// <summary>The <c>requires</c> text of the <c>research</c> section (<see cref="ForwardKey"/>).</summary>
    public string ForwardResearchText { get; private set; } = "";

    /// <summary>The <c>placement</c> key of a reason: its enum name in snake_case (<c>UnitInTheWay</c> -> <c>unit_in_the_way</c>).</summary>
    public static string PlacementKey(PlacementError reason) => SnakeCase(reason.ToString());

    /// <summary>The snake_case key of any enum member (<c>QueueFull</c> -> <c>queue_full</c>): how <c>ui.json</c> names reasons, states and HUD labels.</summary>
    public static string Key<T>(T value) where T : struct, Enum => SnakeCase(value.ToString());

    /// <summary>Path of the shipped file.</summary>
    public static string DefaultPath => ProjectSettings.GlobalizePath("res://data/common/ui.json");

    /// <summary>The shipped file, loaded on first use; null (each error logged once with <c>GD.PushError</c>) if it is missing or incomplete.</summary>
    public static UiText? Shared
    {
        get
        {
            if (_sharedTried) return _shared;
            _sharedTried = true;
            var errors = new List<string>();
            _shared = Load(DefaultPath, errors);
            foreach (string e in errors) GD.PushError(e);
            return _shared;
        }
    }

    /// <summary>Reads and checks a <c>ui.json</c>; null with <paramref name="errors"/> filled when it can't be read or misses a key.</summary>
    public static UiText? Load(string path, List<string> errors)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"ui.json: cannot read {path}: {ex.Message}");
            return null;
        }
        return Parse(json, errors);
    }

    /// <summary>Parses and checks <c>ui.json</c> text; null with <paramref name="errors"/> filled on any problem.</summary>
    public static UiText? Parse(string json, List<string> errors)
    {
        int before = errors.Count;
        var ui = new UiText();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            errors.Add($"ui.json: not valid JSON: {ex.Message}");
            return null;
        }
        using (doc)
        {
            JsonElement root = doc.RootElement;
            // BUG-0110: every check below reports a missing key only inside an object, so a root of another kind passed silently.
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"ui.json: the root must be an object, not {root.ValueKind}");
                return null;
            }
            JsonElement commands = Object(root, "commands", "commands", errors);
            for (int c = 0; c < CommandIds.Length; c++)
            {
                if (CommandIds[c].Length == 0) continue;
                string at = $"commands.{CommandIds[c]}";
                JsonElement entry = Object(commands, CommandIds[c], at, errors);
                ui._names[c] = Text(entry, "displayName", $"{at}.displayName", errors);
                ui._hints[c] = Text(entry, "hotkeyHint", $"{at}.hotkeyHint", errors);
            }
            JsonElement menus = Object(root, "buildMenus", "buildMenus", errors);
            ui.BasicMenu = MenuList(menus, "basic", errors);
            ui.AdvancedMenu = MenuList(menus, "advanced", errors);
            ui.ForwardPlacementText = Section<PlacementError>(root, "placement", ui._placement, skipZero: true, forward: true, errors);
            ui.UnexploredPlacementText = Forward<PlacementError>(root, "placement", ui._placement, UnexploredKey, errors);
            ui.ForwardTrainText = Section<TrainError>(root, "train", ui._train, skipZero: true, forward: true, errors);
            ui.ForwardResearchText = Section<ResearchError>(root, "research", ui._research, skipZero: true, forward: true, errors);
            Section<UnitState>(root, "states", ui._states, skipZero: false, forward: false, errors);
            if (root.TryGetProperty("states", out JsonElement states))
                ui.OrderedAttackText = Text(states, OrderedAttackKey, $"states.{OrderedAttackKey}", errors);
            Section<HudText>(root, "hud", ui._hud, skipZero: false, forward: false, errors);
            TwoArgs(ui._hud[(int)HudText.ResourceLeft], $"hud.{Key(HudText.ResourceLeft)}", "the amount", "the resource", errors);
            Section<MenuText>(root, "menu", ui._menu, skipZero: false, forward: false, errors);
            Section<SetupText>(root, "setup", ui._setup, skipZero: false, forward: false, errors);
            string sizeItem = ui._setup[(int)SetupText.MapSizeItem];
            if (sizeItem.Length > 0 && !IsSizeFormat(sizeItem))
                errors.Add($"ui.json: setup.{Key(SetupText.MapSizeItem)} must be a format with {{0}} (the map side) and no other placeholder: '{sizeItem}'");
            ReadColours(root, ui, errors);
            Section<PauseText>(root, "pause", ui._pause, skipZero: false, forward: false, errors);
            Section<SettingsText>(root, "settings", ui._settings, skipZero: false, forward: false, errors);
            string resolutionItem = ui._settings[(int)SettingsText.ResolutionItem];
            if (resolutionItem.Length > 0 && !IsResolutionFormat(resolutionItem))
                errors.Add($"ui.json: settings.{Key(SettingsText.ResolutionItem)} must be a format with {{0}} (the width) and {{1}} (the height) and no other placeholder: '{resolutionItem}'");
            Section<MatchOverText>(root, "match_over", ui._matchOver, skipZero: false, forward: false, errors);
            Section<KeysText>(root, "keys", ui._keys, skipZero: false, forward: false, errors);
            // M6-V9: the Load Game screen and the playback banners; three formats.
            Section<LoadText>(root, "load", ui._load, skipZero: false, forward: false, errors);
            string dateItem = ui._load[(int)LoadText.DateItem];
            if (dateItem.Length > 0 && !IsDateFormat(dateItem))
                errors.Add($"ui.json: load.{Key(LoadText.DateItem)} must be a format with {{0}} (the date) and no other placeholder: '{dateItem}'");
            TwoArgs(ui._load[(int)LoadText.Seat], $"load.{Key(LoadText.Seat)}", "the faction", "the seat", errors);
            string skipped = ui._load[(int)LoadText.Skipped];
            if (skipped.Length > 0 && !IsSizeFormat(skipped))
                errors.Add($"ui.json: load.{Key(LoadText.Skipped)} must be a format with {{0}} (the count) and no other placeholder: '{skipped}'");
            foreach (KeysText k in new[] { KeysText.Group, KeysText.Reserved })
            {
                string f = ui._keys[(int)k];
                if (f.Length > 0 && !IsSizeFormat(f))
                    errors.Add($"ui.json: keys.{Key(k)} must be a format with {{0}} and no other placeholder: '{f}'");
            }
            TwoArgs(ui._keys[(int)KeysText.Card], $"keys.{Key(KeysText.Card)}", "the row", "the column", errors);
            TwoArgs(ui._keys[(int)KeysText.Conflict], $"keys.{Key(KeysText.Conflict)}", "the key", "the other action", errors);
            string length = ui._matchOver[(int)MatchOverText.MatchLength];
            if (length.Length > 0 && !IsTextFormat(length))
                errors.Add($"ui.json: match_over.{Key(MatchOverText.MatchLength)} must be a format with {{0}} (the clock) and no other placeholder: '{length}'");
            string clock = ui._matchOver[(int)MatchOverText.Clock];
            if (clock.Length > 0 && !IsResolutionFormat(clock))
                errors.Add($"ui.json: match_over.{Key(MatchOverText.Clock)} must be a format with {{0}} (the minutes) and {{1}} (the seconds) and no other placeholder: '{clock}'");
        }
        return errors.Count == before ? ui : null;
    }

    // Reads one text per enum member (its snake_case key; member 0 skipped for the reason enums, whose 0 is None) into
    // `into`; with `forward` the section must also carry ForwardKey, whose text is returned. Extra keys are accepted.
    private static string Section<T>(JsonElement root, string name, string[] into, bool skipZero, bool forward, List<string> errors) where T : struct, Enum
    {
        JsonElement section = Object(root, name, name, errors);
        foreach (T member in Enum.GetValues<T>())
        {
            int i = Convert.ToInt32(member, CultureInfo.InvariantCulture);
            if (skipZero && i == 0) continue;
            string key = Key(member);
            into[i] = Text(section, key, $"{name}.{key}", errors);
        }
        if (!forward) return "";
        // Once the sim has the member, the loop above already read (and reported) the key: never twice.
        foreach (T member in Enum.GetValues<T>())
            if (Key(member) == ForwardKey) return into[Convert.ToInt32(member, CultureInfo.InvariantCulture)];
        return Text(section, ForwardKey, $"{name}.{ForwardKey}", errors);
    }

    // A required key of a reason section that may not have its enum member yet: the member's text once it exists (already
    // read and reported by Section), else the key read here. Nothing when the section itself is missing (reported once).
    private static string Forward<T>(JsonElement root, string name, string[] read, string key, List<string> errors) where T : struct, Enum
    {
        foreach (T member in Enum.GetValues<T>())
            if (Key(member) == key) return read[Convert.ToInt32(member, CultureInfo.InvariantCulture)];
        return root.TryGetProperty(name, out JsonElement section) ? Text(section, key, $"{name}.{key}", errors) : "";
    }

    // The player colour table (M6-V3): setup.colour_1 .. colour_8, each { "displayName", "rgb": "#RRGGBB" }, no two with the
    // same rgb (the table tells players apart; BUG-0631 item 1). Nothing when the setup section itself is missing (reported
    // once by Section).
    private static void ReadColours(JsonElement root, UiText ui, List<string> errors)
    {
        if (!root.TryGetProperty("setup", out JsonElement setup) || setup.ValueKind != JsonValueKind.Object) return;
        var parsed = new bool[ColourCount];
        for (int i = 0; i < ColourCount; i++)
        {
            string at = $"setup.{ColourKey(i)}";
            JsonElement entry = Object(setup, ColourKey(i), at, errors);
            if (entry.ValueKind != JsonValueKind.Object) continue;
            ui._colourNames[i] = Text(entry, "displayName", $"{at}.displayName", errors);
            string rgb = Text(entry, "rgb", $"{at}.rgb", errors);
            if (rgb.Length == 0) continue;
            if (TryRgb(rgb, out uint value))
            {
                ui._colourRgb[i] = value;
                parsed[i] = true;
            }
            else errors.Add($"ui.json: {at}.rgb must be a colour '#RRGGBB': '{rgb}'");
        }
        // One error per equal pair, naming both keys; the first earlier entry only, so three equal entries give two errors.
        for (int j = 1; j < ColourCount; j++)
            for (int i = 0; i < j; i++)
                if (parsed[i] && parsed[j] && ui._colourRgb[i] == ui._colourRgb[j])
                {
                    errors.Add($"ui.json: setup.{ColourKey(i)}.rgb and setup.{ColourKey(j)}.rgb are the same colour (#{ui._colourRgb[j]:X6}, as the data writes it): players must look different");
                    break;
                }
    }

    // "#RRGGBB" (hex digits, either case) as 0xRRGGBB, the data's palette form.
    private static bool TryRgb(string text, out uint rgb)
    {
        rgb = 0;
        if (text.Length != 7 || text[0] != '#') return false;
        foreach (char c in text.AsSpan(1))
            if (!char.IsAsciiHexDigit(c)) return false;
        return uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }

    // A map size item format: it names {0}, formats (a stray "{1}" or "{" would throw at Build), and names the size: two
    // sizes must format differently (BUG-0602 item 1: an escaped "{{0}}" contains "{0}" but every item would read "{0}").
    private static bool IsSizeFormat(string format)
    {
        if (!format.Contains("{0}", StringComparison.Ordinal)) return false;
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, 128) != string.Format(CultureInfo.InvariantCulture, format, 256);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // A resolution item format (M6-V6): it formats, and names both the width and the height (changing either changes the text).
    private static bool IsResolutionFormat(string format)
    {
        try
        {
            string a = string.Format(CultureInfo.InvariantCulture, format, 1280, 720);
            return a != string.Format(CultureInfo.InvariantCulture, format, 1920, 720) && a != string.Format(CultureInfo.InvariantCulture, format, 1280, 1080);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // M6-V8: a read text that must be a format naming {0} and {1} (and no other placeholder); one error naming the key otherwise.
    private static void TwoArgs(string format, string at, string first, string second, List<string> errors)
    {
        if (format.Length > 0 && !IsResolutionFormat(format))
            errors.Add($"ui.json: {at} must be a format with {{0}} ({first}) and {{1}} ({second}) and no other placeholder: '{format}'");
    }

    // A format with one text placeholder (M6-V4: the match length line): it formats, and names it (two texts differ).
    private static bool IsTextFormat(string format)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, "1:00") != string.Format(CultureInfo.InvariantCulture, format, "2:00");
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // A date cell format (M6-V9): it formats, and names the date (two dates a day and an hour apart format differently).
    private static bool IsDateFormat(string format)
    {
        try
        {
            var a = new DateTime(2026, 10, 16, 20, 36, 0);
            string text = string.Format(CultureInfo.InvariantCulture, format, a);
            return text != string.Format(CultureInfo.InvariantCulture, format, a.AddDays(1).AddHours(1));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string[] Blank(int n)
    {
        var a = new string[n];
        Array.Fill(a, "");
        return a;
    }

    private static BuildingSlot[] MenuList(JsonElement menus, string name, List<string> errors)
    {
        string at = $"buildMenus.{name}";
        if (menus.ValueKind != JsonValueKind.Object || !menus.TryGetProperty(name, out JsonElement list) || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() == 0)
        {
            if (menus.ValueKind == JsonValueKind.Object) errors.Add($"ui.json: missing {at} (a non-empty list of building slot ids)");
            return Array.Empty<BuildingSlot>();
        }
        if (list.GetArrayLength() > MaxMenuEntries)
        {
            errors.Add($"ui.json: {at} has {list.GetArrayLength()} entries, the card holds {MaxMenuEntries}");
            return Array.Empty<BuildingSlot>();
        }
        var slots = new BuildingSlot[list.GetArrayLength()];
        int i = 0;
        foreach (JsonElement e in list.EnumerateArray())
        {
            string? id = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (id == null || !BuildMenu.TryParseSlot(id, out slots[i])) errors.Add($"ui.json: {at}[{i}] is not a building slot id: {e}");
            i++;
        }
        return slots;
    }

    private static JsonElement Object(JsonElement parent, string key, string at, List<string> errors)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Object) return v;
        // A missing parent was already reported; its children would only repeat it.
        if (parent.ValueKind == JsonValueKind.Object) errors.Add($"ui.json: missing {at} (an object)");
        return default;
    }

    private static string Text(JsonElement parent, string key, string at, List<string> errors)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            && v.GetString() is { Length: > 0 } s)
            return s;
        if (parent.ValueKind == JsonValueKind.Object) errors.Add($"ui.json: missing {at} (a non-empty string)");
        return "";
    }

    private static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            if (char.IsUpper(ch))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
