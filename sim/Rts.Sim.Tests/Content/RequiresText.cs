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

    /// <summary>
    /// Every name a description says is needed: the text after each "needs" / "Needs" up to the end of its clause
    /// ('.' or ';'), split on ", " and " and ", without a leading article. "needs a Legion Barracks." gives
    /// "Legion Barracks"; "Needs Melee Weapons and Age II." gives both.
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

    /// <summary>Asserts the description's "needs" names are exactly the names of <paramref name="requires"/>.</summary>
    public static void AssertMatches(GameData data, string key, string description, IEnumerable<string> requires)
    {
        string[] said = Needs(description).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] required = requires.Select(r => Name(data, r)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(said.SequenceEqual(required),
            $"{key}: description needs [{string.Join(", ", said)}] but requires is [{string.Join(", ", required)}]");
    }
}
