using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// The "names only its own faction's things" rule with the faction's own names blanked out first (D-H9, docs/01 2026-11-15
/// (a)): "Edur Warrior" and "Warrior Lodge" contain the Teblor's "Warrior", and the units keep their names, so a foreign
/// name counts only where it stands outside every own name. A foreign name that merely contains an own one ("Heavy
/// Infantry" round an own "Infantry") still counts.
/// </summary>
internal static class OwnNames
{
    /// <summary>The unit and building display names of faction <paramref name="faction"/> in <paramref name="d"/>.</summary>
    public static List<string> Of(GameData d, int faction)
    {
        var names = new List<string>();
        foreach (UnitDef u in d.Units) if (u.Faction == faction) names.Add(u.DisplayName);
        foreach (BuildingDef b in d.Buildings) if (b.Faction == faction) names.Add(b.DisplayName);
        return names;
    }

    /// <summary>
    /// Whether <paramref name="text"/> says <paramref name="name"/> somewhere that is not inside one of
    /// <paramref name="own"/> (each occurrence of an own name blanks its characters).
    /// </summary>
    public static bool Names(string text, string name, IReadOnlyCollection<string> own)
    {
        if (name.Length == 0) return false;
        var blank = new bool[text.Length];
        foreach (string o in own)
        {
            if (o.Length == 0) continue;
            for (int at = text.IndexOf(o, StringComparison.Ordinal); at >= 0; at = text.IndexOf(o, at + 1, StringComparison.Ordinal))
                for (int k = at; k < at + o.Length; k++) blank[k] = true;
        }
        for (int at = text.IndexOf(name, StringComparison.Ordinal); at >= 0; at = text.IndexOf(name, at + 1, StringComparison.Ordinal))
            for (int k = at; k < at + name.Length; k++)
                if (!blank[k]) return true;
        return false;
    }
}
