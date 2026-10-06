namespace Rts.Sim.Data;

/// <summary>One problem found while loading game data: which file, where in it, and what is wrong.</summary>
/// <remarks>Developer-facing only (never shown to players), so the message may be a C# literal.</remarks>
public sealed class DataError
{
    /// <summary>Creates an error record.</summary>
    public DataError(string file, string path, string message)
    {
        File = file;
        Path = path;
        Message = message;
    }

    /// <summary>File relative to the data directory, with forward slashes (e.g. <c>factions/malazan/units.json</c>).</summary>
    public string File { get; }

    /// <summary>Field inside the file (e.g. <c>units[2].attack.type</c>); empty for whole-file problems.</summary>
    public string Path { get; }

    /// <summary>What is wrong.</summary>
    public string Message { get; }

    /// <summary>"file: path: message", leaving out an empty file or path (and its separator).</summary>
    public override string ToString()
    {
        string where = File.Length == 0 ? Path : Path.Length == 0 ? File : $"{File}: {Path}";
        return where.Length == 0 ? Message : $"{where}: {Message}";
    }
}
