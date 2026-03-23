using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace CatModManager.Core.Services;

public interface IModManagementService
{
    Task<string> InstallModAsync(string sourcePath, string targetBaseDir);
    Task<string> InstallModFromMappingAsync(string archivePath, string modName, string targetBaseDir, Dictionary<string, string> fileMapping);
    Task<string> InstallModToRootAsync(string archivePath, string modName, string targetBaseDir);
}

public class ModManagementService : IModManagementService
{
    private readonly IFileService _fileService;
    private readonly ILogService _logService;

    public ModManagementService(IFileService fileService, ILogService logService)
    {
        _fileService = fileService;
        _logService = logService;
    }

    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar" };

    public async Task<string> InstallModAsync(string sourcePath, string targetBaseDir)
    {
        if (!_fileService.DirectoryExists(targetBaseDir))
            _fileService.CreateDirectory(targetBaseDir);

        bool isArchive = _fileService.FileExists(sourcePath)
                      && ArchiveExtensions.Contains(Path.GetExtension(sourcePath));

        // Archives are extracted to a folder named after the archive (without extension).
        // Directories are copied as-is.
        string baseName = isArchive
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : Path.GetFileName(sourcePath);

        string targetPath = Path.Combine(targetBaseDir, baseName);

        int count = 1;
        while (_fileService.FileExists(targetPath) || _fileService.DirectoryExists(targetPath))
            targetPath = Path.Combine(targetBaseDir, $"{baseName} ({count++})");

        try
        {
            if (_fileService.DirectoryExists(sourcePath))
            {
                await Task.Run(() => _fileService.CopyDirectory(sourcePath, targetPath));
            }
            else if (isArchive)
            {
                await Task.Run(() => ExtractArchive(sourcePath, targetPath));
            }
            else if (_fileService.FileExists(sourcePath))
            {
                await Task.Run(() => _fileService.CopyFile(sourcePath, targetPath, true));
            }
            else
            {
                throw new FileNotFoundException("Source mod path not found.");
            }

            _logService.Log($"Mod installed: {targetPath}");
            return targetPath;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to install mod from {sourcePath}", ex);
            throw;
        }
    }

    private static void ExtractArchive(string archivePath, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        using var archive = ArchiveFactory.Open(archivePath);
        using var reader  = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            var destPath = Path.Combine(targetFolder,
                (reader.Entry.Key ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var outStream = File.Create(destPath);
            reader.WriteEntryTo(outStream);
        }
    }

    public async Task<string> InstallModFromMappingAsync(string archivePath, string modName, string targetBaseDir, Dictionary<string, string> fileMapping)
    {
        if (!_fileService.DirectoryExists(targetBaseDir))
            _fileService.CreateDirectory(targetBaseDir);

        var modFolder = Path.Combine(targetBaseDir, modName);
        int suffix = 1;
        while (_fileService.DirectoryExists(modFolder))
            modFolder = Path.Combine(targetBaseDir, $"{modName} ({suffix++})");

        await Task.Run(() =>
        {
            _fileService.CreateDirectory(modFolder);

            // Extract entire archive to a temp folder first (handles solid 7z archives
            // where random-access OpenEntryStream() throws "File does not have a stream.").
            var tempDir = Path.Combine(Path.GetTempPath(), $"cmm_extract_{Guid.NewGuid():N}");
            try
            {
                ExtractArchive(archivePath, tempDir);

                // Build a flat lookup of all extracted files (key = normalised relative path).
                var extractedFiles = Directory
                    .EnumerateFiles(tempDir, "*", SearchOption.AllDirectories)
                    .Select(f => (
                        key: f[tempDir.Length..].TrimStart(Path.DirectorySeparatorChar)
                                               .Replace(Path.DirectorySeparatorChar, '\\'),
                        path: f))
                    .ToList();

                foreach (var (destRelative, sourceRelative) in fileMapping)
                {
                    var normalizedDest   = destRelative.Replace('/', '\\').Trim('\\');
                    var normalizedSource = sourceRelative.Replace('/', '\\').Trim('\\');

                    foreach (var (key, filePath) in extractedFiles)
                    {
                        string? outputRelative = null;

                        if (key.Equals(normalizedSource, StringComparison.OrdinalIgnoreCase))
                        {
                            outputRelative = normalizedDest;
                        }
                        else if (key.StartsWith(normalizedSource + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            var part = key[(normalizedSource.Length + 1)..];
                            outputRelative = string.IsNullOrEmpty(normalizedDest)
                                ? part
                                : normalizedDest + "\\" + part;
                        }

                        if (outputRelative == null) continue;

                        var destPath = Path.Combine(modFolder, outputRelative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        File.Copy(filePath, destPath, overwrite: true);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        });

        _logService.Log($"FOMOD installed: {modName} → {modFolder}");
        return modFolder;
    }

    public async Task<string> InstallModToRootAsync(string archivePath, string modName, string targetBaseDir)
    {
        if (!_fileService.DirectoryExists(targetBaseDir))
            _fileService.CreateDirectory(targetBaseDir);

        var modFolder = Path.Combine(targetBaseDir, modName);
        int suffix = 1;
        while (_fileService.DirectoryExists(modFolder))
            modFolder = Path.Combine(targetBaseDir, $"{modName} ({suffix++})");

        var rootFolder = Path.Combine(modFolder, "Root");

        await Task.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"cmm_root_{Guid.NewGuid():N}");
            try
            {
                ExtractArchive(archivePath, tempDir);

                // Strip single wrapper folder (e.g. "skse64_2_02_06/") if present.
                var topEntries = Directory.GetFileSystemEntries(tempDir);
                var sourceDir  = topEntries.Length == 1 && Directory.Exists(topEntries[0])
                    ? topEntries[0]
                    : tempDir;

                Directory.CreateDirectory(rootFolder);
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var rel  = file[sourceDir.Length..].TrimStart(Path.DirectorySeparatorChar);
                    var dest = Path.Combine(rootFolder, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, overwrite: true);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        });

        _logService.Log($"Mod installed to Root: {modName} → {rootFolder}");
        return modFolder;
    }
}
