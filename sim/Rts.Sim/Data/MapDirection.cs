namespace Rts.Sim.Data;

/// <summary>M6-S7: a compass side of a hand-made map's ramp (north is row 0, west column 0), the file's <c>up</c> value.</summary>
public enum MapDirection
{
    /// <summary>Toward row 0.</summary>
    North,

    /// <summary>Toward the last row.</summary>
    South,

    /// <summary>Toward the last column.</summary>
    East,

    /// <summary>Toward column 0.</summary>
    West,
}
