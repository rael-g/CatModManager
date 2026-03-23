using System.Collections.Generic;

namespace CatModManager.VirtualFileSystem;

/// <summary>
/// Persists the set of hard links created during a mount so they can be
/// reversed on unmount or after a crash.
/// </summary>
public interface IHardlinkStateStore
{
    void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries);

    /// <param name="mountPoint">null = load all entries (crash recovery).</param>
    IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint);

    /// <param name="mountPoint">null = delete all entries.</param>
    void Clear(string? mountPoint);
}

public record HardlinkStateEntry(string RelPath, string DestPath, string? BackupPath);
