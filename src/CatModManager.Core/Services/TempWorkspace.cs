using System;
using System.IO;

namespace CatModManager.Core.Services;

/// <summary>
/// Manages a temporary directory that is automatically deleted when disposed.
/// Useful for atomic installations and archive extraction.
/// </summary>
public class TempWorkspace : IDisposable
{
    public string Path { get; }

    public TempWorkspace(string baseDir, string prefix = ".cmm_tmp_")
    {
        Path = System.IO.Path.Combine(baseDir, $"{prefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Silently fail on cleanup errors (e.g. file lock) 
            // Stale folders will be handled by RecoverStaleMounts or manually
        }
    }
}
