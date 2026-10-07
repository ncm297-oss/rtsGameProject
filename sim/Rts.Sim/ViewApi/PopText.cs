using System.Globalization;

namespace Rts.Sim.ViewApi;

/// <summary>The resource bar's population numbers (M3-V3): half-pop as the player reads it ("5", "5.5") and whether the cap is reached. Pure.</summary>
/// <remarks>The sim counts population in half-pop (<c>World.HalfPop</c> / <c>HalfPopCap</c>, docs/03 M3-4); the bar shows <c>n / 2</c>.</remarks>
public static class PopText
{
    /// <summary><paramref name="halfPop"/> / 2 in invariant digits: whole numbers without a decimal point, an odd count with ".5" ("11" -> "5.5", "-3" -> "-1.5").</summary>
    /// <remarks>Allocates the string: call it only when the number shown changes.</remarks>
    public static string Format(int halfPop)
    {
        long h = halfPop; // int.MinValue has no positive int
        bool negative = h < 0;
        if (negative) h = -h;
        string whole = (h / 2).ToString(CultureInfo.InvariantCulture);
        string text = h % 2 == 0 ? whole : whole + ".5";
        return negative ? "-" + text : text;
    }

    /// <summary>True when no more population fits: <paramref name="halfPop"/> at or over <paramref name="halfPopCap"/> (the bar turns red).</summary>
    public static bool AtCap(int halfPop, int halfPopCap) => halfPop >= halfPopCap;
}
