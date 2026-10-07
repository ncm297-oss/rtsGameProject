using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The view's own player-facing text and menu lists, from <c>game/data/common/ui.json</c> (M3-V2; CLAUDE.md rule 8).</summary>
/// <remarks>
/// View-only data: the sim's <c>DataLoader</c> never reads this file. Shape:
/// <c>{ "commands": { "&lt;id&gt;": { "displayName", "hotkeyHint" } }, "buildMenus": { "basic": [slot ids], "advanced": [slot ids] },
/// "placement": { "&lt;reason&gt;": text } }</c>. Command ids are <see cref="CommandIds"/>; placement keys are the snake_case
/// names of every <see cref="PlacementError"/> but <c>None</c> (<c>blocked</c>, <c>seals_ground</c> ...), so a reason
/// the sim adds later is a load error here until the file has its text. Loading is fail-fast: every missing or empty key
/// is one error naming it ("ui.json: missing commands.stop.displayName"), and with any error there is no
/// <see cref="UiText"/> (the match logs them with <c>GD.PushError</c> and runs without the command card).
/// </remarks>
public sealed class UiText
{
    /// <summary>The <c>commands</c> key of each <see cref="CardCommand"/> (index = enum value); empty for the ones without their own text.</summary>
    public static readonly string[] CommandIds = { "", "move", "attack_move", "stop", "hold", "build_basic", "build_advanced", "cancel", "" };

    /// <summary>Largest number of entries a build menu can show (the 5 x 3 grid).</summary>
    public const int MaxMenuEntries = 15;

    private static UiText? _shared;
    private static bool _sharedTried;

    private readonly string[] _names = new string[CommandIds.Length];
    private readonly string[] _hints = new string[CommandIds.Length];
    private readonly string[] _placement;

    private UiText()
    {
        _placement = new string[Enum.GetValues<PlacementError>().Length];
        Array.Fill(_placement, "");
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

    /// <summary>The <c>placement</c> key of a reason: its enum name in snake_case (<c>UnitInTheWay</c> -> <c>unit_in_the_way</c>).</summary>
    public static string PlacementKey(PlacementError reason) => SnakeCase(reason.ToString());

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
            JsonElement placement = Object(root, "placement", "placement", errors);
            foreach (PlacementError reason in Enum.GetValues<PlacementError>())
            {
                if (reason == PlacementError.None) continue;
                string key = PlacementKey(reason);
                ui._placement[(int)reason] = Text(placement, key, $"placement.{key}", errors);
            }
        }
        return errors.Count == before ? ui : null;
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
