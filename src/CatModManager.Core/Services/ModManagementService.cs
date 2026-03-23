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
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            var destPath = Path.Combine(targetFolder,
                (entry.Key ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var inStream  = entry.OpenEntryStream();
            using var outStream = File.Create(destPath);
            inStream.CopyTo(outStream);
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

            using var archive = ArchiveFactory.Open(archivePath);

            var allEntries = archive.Entries
                .Where(e => !e.IsDirectory)
                .Select(e => (key: (e.Key ?? "").Replace('/', '\\').Trim('\\'), entry: e))
                .ToList();

            foreach (var (destRelative, sourceRelative) in fileMapping)
            {
                var normalizedDest   = destRelative.Replace('/', '\\').Trim('\\');
                var normalizedSource = sourceRelative.Replace('/', '\\').Trim('\\');

                foreach (var (key, entry) in allEntries)
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

                    using var inStream  = entry.OpenEntryStream();
                    using var outStream = File.Create(destPath);
                    inStream.CopyTo(outStream);
                }
            }
        });

        _logService.Log($"FOMOD installed: {modName} → {modFolder}");
        return modFolder;
    }
}
