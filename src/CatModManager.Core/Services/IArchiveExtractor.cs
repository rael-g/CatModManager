using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

public interface IArchiveExtractor
{
    /// <summary>
    /// Extracts an archive to the specified directory with progress reporting.
    /// </summary>
    Task ExtractAsync(string archivePath, string destinationDir, IProgress<double>? progress = null, CancellationToken ct = default);
}
