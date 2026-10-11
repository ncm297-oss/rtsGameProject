namespace Rts.Sim.Data;

/// <summary>An AI difficulty (docs/02 "AI opponent"), in <see cref="DataLimits.AiDifficultyIds"/> order: the index into <see cref="AiDef.Difficulties"/>.</summary>
public enum AiDifficulty
{
    /// <summary>Thinks slowly, small waves, no micro (<c>easy</c>).</summary>
    Easy,
    /// <summary>Fair play at a relaxed pace (<c>normal</c>).</summary>
    Normal,
    /// <summary>Tighter decisions plus a gather bonus (<c>hard</c>).</summary>
    Hard,
}
