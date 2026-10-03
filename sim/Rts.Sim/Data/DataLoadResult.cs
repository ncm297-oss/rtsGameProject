using System.Collections.Generic;

namespace Rts.Sim.Data;

/// <summary>Outcome of <see cref="DataLoader.LoadAll"/>: the data, or every error found (never both).</summary>
public sealed class DataLoadResult
{
    internal DataLoadResult(GameData? data, IReadOnlyList<DataError> errors)
    {
        Data = data;
        Errors = errors;
    }

    /// <summary>The loaded data; null when there is at least one error.</summary>
    public GameData? Data { get; }

    /// <summary>All problems found, in file order; empty on success.</summary>
    public IReadOnlyList<DataError> Errors { get; }

    /// <summary>True when the data loaded with no errors.</summary>
    public bool Ok => Data != null;
}
