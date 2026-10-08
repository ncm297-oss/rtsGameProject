using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Matches the "needs X" wording of player-facing descriptions and the pages' Requires cells against a <c>requires</c>
/// list (BUG-0090): text that says something is needed must have the entry, and an entry must be said.
/// </summary>
internal static class RequiresText
{
    /// <summary>A <c>requires</c> id as the player reads it: the tech's or the building's display name.</summary>
    public static string Name(GameData data, string id)
    {
        int t = data.FindTech(id);
        if (t >= 0) return data.Techs[t].DisplayName;
        int b = data.FindBuilding(id);
        Assert.True(b >= 0, $"requires '{id}' names no tech or building");
        return data.Buildings[b].DisplayName;
    }

    /// <summary>The Requires cell of a page table: the names joined with ", ", or "—" for none.</summary>
    public static string Cell(GameData data, IEnumerable<string> requires)
    {
        string[] names = requires.Select(r => Name(data, r)).ToArray();
        return names.Length == 0 ? "—" : string.Join(", ", names);
    }

    /// <summary>The words a description may use for a requirement other than "needs" (BUG-0132): none may appear.</summary>
    /// <remarks>
    /// <see cref="Needs"/> reads only "needs", so a description saying "requires Age II" or "after Age II" would escape
    /// the match against <c>requires</c>. Rather than parse every phrasing, shipped text keeps to the one word.
    /// </remarks>
    public static readonly Regex OtherWords =
        new(@"\b(require[sd]?|requirements?|after|unlocked by|once you have)\b", RegexOptions.IgnoreCase);

    /// <summary>The clause of an any-of rule: "Needs two kinds of building: infantry, ranged, mounted or upgrade".</summary>
    private const string KindsOf = " kinds of building: ";

    /// <summary>
    /// Every name a description says is needed: the text after each "needs" / "Needs" up to the end of its clause
    /// ('.' or ';'), split on ", " and " and ", without a leading article. "needs a Legion Barracks." gives
    /// "Legion Barracks"; "Needs Melee Weapons and Age II." gives both. An any-of clause ("needs two kinds of
    /// building: ...") is skipped here and read by <see cref="AnyOf"/>.
    /// </summary>
    public static string[] Needs(string description)
    {
        var names = new List<string>();
        int at = 0;
        while ((at = description.IndexOf("needs ", at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            at += "needs ".Length;
            int end = description.IndexOfAny(new[] { '.', ';' }, at);
            string clause = description[at..(end < 0 ? description.Length : end)];
            if (clause.Contains(KindsOf, StringComparison.Ordinal)) continue;
            foreach (string part in clause.Split(new[] { ", ", " and " }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                if (name.StartsWith("a ", StringComparison.Ordinal)) name = name[2..];
                else if (name.StartsWith("an ", StringComparison.Ordinal)) name = name[3..];
                names.Add(name);
            }
        }
        return names.ToArray();
    }

    /// <summary>
    /// The any-of clause of a description, "Needs two kinds of building: infantry, ranged, mounted or upgrade": the
    /// count word ("two") and the kinds, split on ", " and " or "; null when the description has none.
    /// </summary>
    public static (string Count, string[] Kinds)? AnyOf(string description)
    {
        int at = description.IndexOf(KindsOf, StringComparison.Ordinal);
        if (at < 0) return null;
        int start = description.LastIndexOf("eeds ", at, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{description}': an any-of clause must follow 'Needs'");
        string count = description[(start + "eeds ".Length)..at];
        int from = at + KindsOf.Length;
        int end = description.IndexOfAny(new[] { '.', ';' }, from);
        string list = description[from..(end < 0 ? description.Length : end)];
        return (count, list.Split(new[] { ", ", " or " }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToArray());
    }

    /// <summary>Asserts the description's "needs" names are exactly the names of <paramref name="requires"/>.</summary>
    public static void AssertMatches(GameData data, string key, string description, IEnumerable<string> requires)
    {
        string[] said = Needs(description).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] required = requires.Select(r => Name(data, r)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(said.SequenceEqual(required),
            $"{key} description needs: requires [{string.Join(", ", required)}] vs description [{string.Join(", ", said)}]");
    }
}
