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

    /// <summary>
    /// Opens several entries in a single forward pass, keyed by the paths that were asked for.
    /// Entries not found in the archive are simply absent from the result.
    ///
    /// Use this instead of a loop over <see cref="OpenFileStream"/> whenever more than one entry is
    /// needed. In a <em>solid</em> archive — the default for .7z — every file shares one LZMA2
    /// stream, so opening any single entry decodes that stream from the beginning: N entries cost
    /// N × the whole archive. Reading a 335 MB mod's 16 preview images that way took minutes, and
    /// it happened while the FOMOD wizard was being built on the UI thread.
    ///
    /// Deliberately not a default interface method. As one, a mock that does not stub it returns
    /// nothing instead of falling back, and the caller sees an archive with no entries in it —
    /// a silent wrong answer rather than a compile error. Implementers that cannot iterate in order
    /// can loop over <see cref="OpenFileStream"/> themselves; they just have to say so.
    /// </summary>
    IReadOnlyDictionary<string, Stream> OpenFileStreams(string archivePath, IEnumerable<string> entryPaths);
}
