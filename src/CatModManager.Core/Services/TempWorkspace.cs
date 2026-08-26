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
            // Cleanup can genuinely fail (a file still locked, a read-only mount). Leaving the
            // folder is the safe outcome; CleanupStale collects it on a later start.
        }
    }

    /// <summary>
    /// Deletes workspaces left behind by a previous run.
    ///
    /// Dispose handles the normal path, including cancellation, but nothing runs when the process
    /// is killed — a crash, an OOM kill, closing mid-extraction — and a half-extracted mod can be
    /// hundreds of megabytes sitting in the mods folder forever, invisible because the name starts
    /// with a dot.
    ///
    /// Only folders older than this process are touched, so a workspace belonging to an install
    /// running right now is never swept out from under it.
    /// </summary>
    public static void CleanupStale(string baseDir, Action<string>? log = null, string prefix = ".cmm_tmp_")
    {
        if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir)) return;

        DateTime processStart;
        try { processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return; }

        foreach (var dir in Directory.EnumerateDirectories(baseDir, prefix + "*"))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(dir) >= processStart) continue;

                Directory.Delete(dir, recursive: true);
                log?.Invoke($"Removed leftover install workspace from a previous run: {dir}");
            }
            catch
            {
                // Not ours to force. Try again next start.
            }
        }
    }
}
