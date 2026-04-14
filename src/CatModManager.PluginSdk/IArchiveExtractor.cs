using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CatModManager.PluginSdk;

public interface IArchiveExtractor
{
    /// <summary>
    /// Extracts an archive to the specified directory with progress reporting.
    /// </summary>
    Task ExtractAsync(string archivePath, string destinationDir, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Returns a list of all file and directory paths inside the archive.
    /// Paths are relative to the archive root and use backslashes.
    /// </summary>
    IEnumerable<string> GetFileList(string archivePath);

    /// <summary>
    /// Opens a read-only stream for a specific file inside the archive.
    /// Returns null if the file is not found. The caller is responsible for disposing the stream.
    /// </summary>
    Stream? OpenFileStream(string archivePath, string entryPath);
}
