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
/// "states": { "&lt;state&gt;": text }, "hud": { "&lt;label&gt;": text } }</c>. Command ids are <see cref="CommandIds"/>;
/// the placement, train and research keys are the snake_case names of every <see cref="PlacementError"/>,
/// <see cref="TrainError"/> and <see cref="ResearchError"/> member but <c>None</c> (<c>blocked</c>, <c>queue_full</c> ...),
/// the state keys those of every <see cref="UnitState"/> (M3-V3) plus <see cref="OrderedAttackKey"/> (M4-V2), the hud keys those of <see cref="HudText"/>; so a member
/// the sim adds later is a load error here until the file has its text. Each of the three reason sections must also
/// carry <see cref="ForwardKey"/> (<c>requires</c>), the reason M3-6's <c>requires</c> gating adds: a key with no enum
/// member yet is accepted (as is any extra key), so the file loads with or without that sim change. Loading is
/// fail-fast: a root that is not an object is one error (BUG-0110), every missing or empty key is one error naming it
/// ("ui.json: missing commands.stop.displayName"), and with any error there is no <see cref="UiText"/> (the match logs
/// them with <c>GD.PushError</c> and runs without the command card and the selection panel).
/// </remarks>
public sealed class UiText
{
    /// <summary>The <c>commands</c> key of each <see cref="CardCommand"/> (index = enum value); empty for the ones without their own text.</summary>
    public static readonly string[] CommandIds = { "", "move", "attack_move", "stop", "hold", "build_basic", "build_advanced", "cancel", "", "", "" };

    /// <summary>The reason key every reason section carries before the sim has its enum member (M3-6's <c>requires</c> gating).</summary>
    public const string ForwardKey = "requires";

    /// <summary>The <c>states</c> key of the panel's text for a unit chasing the target of an explicit Attack order (M4-V2; not a <see cref="UnitState"/>: a chaser is <c>Moving</c>).</summary>
    public const string OrderedAttackKey = "ordered_attack";

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

    private UiText()
    {
        _placement = Blank(Enum.GetValues<PlacementError>().Length);
        _train = Blank(Enum.GetValues<TrainError>().Length);
        _research = Blank(Enum.GetValues<ResearchError>().Length);
        _states = Blank(Enum.GetValues<UnitState>().Length);
        _hud = Blank(Enum.GetValues<HudText>().Length);
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

    /// <summary>The <c>requires</c> text of the <c>placement</c> section (<see cref="ForwardKey"/>), readable before the sim has the reason.</summary>
    public string ForwardPlacementText { get; private set; } = "";

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
            ui.BasicMenu = Menu(menus, "basic", errors);
            ui.AdvancedMenu = Menu(menus, "advanced", errors);
            ui.ForwardPlacementText = Section<PlacementError>(root, "placement", ui._placement, skipZero: true, forward: true, errors);
            ui.ForwardTrainText = Section<TrainError>(root, "train", ui._train, skipZero: true, forward: true, errors);
            ui.ForwardResearchText = Section<ResearchError>(root, "research", ui._research, skipZero: true, forward: true, errors);
            Section<UnitState>(root, "states", ui._states, skipZero: false, forward: false, errors);
            if (root.TryGetProperty("states", out JsonElement states))
                ui.OrderedAttackText = Text(states, OrderedAttackKey, $"states.{OrderedAttackKey}", errors);
            Section<HudText>(root, "hud", ui._hud, skipZero: false, forward: false, errors);
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

    private static string[] Blank(int n)
    {
        var a = new string[n];
        Array.Fill(a, "");
        return a;
    }

    private static BuildingSlot[] Menu(JsonElement menus, string name, List<string> errors)
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
