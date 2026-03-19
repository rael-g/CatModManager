using System.Collections.Generic;

namespace CatModManager.Core.Models;

public enum ConflictType
{
    /// <summary>This mod's file overrides a lower-priority mod.</summary>
    Wins,
    /// <summary>This mod's file is overridden by a higher-priority mod.</summary>
    Loses
}

public record ModConflictInfo(string FilePath, string OtherModName, ConflictType Type);

public class ConflictReport
{
    public string ModName { get; init; } = string.Empty;
    public List<ModConflictInfo> Conflicts { get; init; } = new();
    public bool HasConflicts => Conflicts.Count > 0;
}
