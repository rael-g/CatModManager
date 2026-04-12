using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace CatModManager.Core.Services;

public interface IModManagementService
{
    Task<string> InstallModAsync(string sourcePath, string targetBaseDir, string? overrideFolderPath = null, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<string> InstallModFromMappingAsync(string archivePath, string modName, string targetBaseDir, Dictionary<string, string> fileMapping, string? overrideFolderPath = null, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<string> InstallModToRootAsync(string archivePath, string modName, string targetBaseDir, IProgress<double>? progress = null, CancellationToken ct = default);
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

    public async Task<string> InstallModAsync(string sourcePath, string targetBaseDir, string? overrideFolderPath = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!_fileService.DirectoryExists(targetBaseDir))
            _fileService.CreateDirectory(targetBaseDir);

        bool isArchive = _fileService.FileExists(sourcePath)
                      && ArchiveExtensions.Contains(Path.GetExtension(sourcePath));

        string targetPath;
        if (!string.IsNullOrEmpty(overrideFolderPath))
        {
            targetPath = overrideFolderPath;
        }
        else
        {
            string baseName = isArchive
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : Path.GetFileName(sourcePath);

            targetPath = Path.Combine(targetBaseDir, baseName);

            int count = 1;
            while (_fileService.FileExists(targetPath) || _fileService.DirectoryExists(targetPath))
                targetPath = Path.Combine(targetBaseDir, $"{baseName} ({count++})");
        }

        // Atomic install: use a temporary folder for extraction/copying
        string tempPath = Path.Combine(targetBaseDir, ".cmm_tmp_" + Guid.NewGuid().ToString("N"));

        try
        {
            if (_fileService.DirectoryExists(sourcePath))
            {
                await Task.Run(() => _fileService.CopyDirectory(sourcePath, tempPath), ct);
                progress?.Report(100);
            }
            else if (isArchive)
            {
                await Task.Run(() => ExtractArchive(sourcePath, tempPath, progress, ct), ct);
            }
            else if (_fileService.FileExists(sourcePath))
            {
                _fileService.CreateDirectory(tempPath);
                string fileName = Path.GetFileName(sourcePath);
                await Task.Run(() => _fileService.CopyFile(sourcePath, Path.Combine(tempPath, fileName), true), ct);
                progress?.Report(100);
            }
            else
            {
                throw new FileNotFoundException("Source mod path not found.");
            }

            if (_fileService.DirectoryExists(targetPath))
                await Task.Run(() => _fileService.DeleteDirectory(targetPath, true), ct);
            
            _fileService.MoveDirectory(tempPath, targetPath);

            _logService.Log($"Mod installed: {targetPath}");
            return targetPath;
        }
        catch (OperationCanceledException)
        {
            _logService.Log($"Installation cancelled. Cleaning up temp files...");
            if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to install mod from {sourcePath}", ex);
            if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
            throw;
        }
    }

    private void ExtractArchive(string archivePath, string targetFolder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetFolder);
        var markerPath = Path.Combine(targetFolder, ".cmm_incomplete");
        File.WriteAllText(markerPath, "Installation in progress. If you see this, the installation was interrupted.");
        
        using var archive = ArchiveFactory.Open(archivePath);
        
        progress?.Report(1);
        
        long totalSize = 0;
        int fileCount = 0;
        
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        foreach (var entry in entries)
        {
            if (entry.Size > 0) totalSize += entry.Size;
            fileCount++;
        }

        _logService.Log($"[Extract] Archive {Path.GetFileName(archivePath)}: {fileCount} files, {totalSize} bytes.");

        long extractedSize = 0;
        int extractedCount = 0;
        byte[] buffer = new byte[81920]; 

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var key      = entry.Key ?? "";
            var destPath = Path.Combine(targetFolder,
                key.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
            
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            try
            {
                using (var entryStream = entry.OpenEntryStream())
                using (var outStream = File.Create(destPath))
                {
                    int bytesRead;
                    while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        outStream.Write(buffer, 0, bytesRead);
                        
                        if (totalSize > 0)
                        {
                            extractedSize += bytesRead;
                            double p = (double)extractedSize / totalSize * 100;
                            progress?.Report(Math.Clamp(p, 1, 99.9));
                        }
                    }
                }
                
                if (totalSize <= 0 && fileCount > 0)
                {
                    extractedCount++;
                    double p = (double)extractedCount / fileCount * 100;
                    progress?.Report(Math.Clamp(p, 1, 99.9));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logService.Log($"[Extract] Failed to extract '{key}': {ex.Message}");
                throw;
            }
        }
        
        if (File.Exists(markerPath)) File.Delete(markerPath);
        progress?.Report(100);
        _logService.Log($"[Extract] Done: {archivePath}");
    }

    public async Task<string> InstallModFromMappingAsync(string archivePath, string modName, string targetBaseDir, Dictionary<string, string> fileMapping, string? overrideFolderPath = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!_fileService.DirectoryExists(targetBaseDir))
            _fileService.CreateDirectory(targetBaseDir);

        string targetPath;
        if (!string.IsNullOrEmpty(overrideFolderPath))
        {
            targetPath = overrideFolderPath;
        }
        else
        {
            targetPath = Path.Combine(targetBaseDir, modName);
            int suffix = 1;
            while (_fileService.DirectoryExists(targetPath))
                targetPath = Path.Combine(targetBaseDir, $"{modName} ({suffix++})");
        }

        string tempPath = Path.Combine(targetBaseDir, ".cmm_tmp_fomod_" + Guid.NewGuid().ToString("N"));

        await Task.Run(() =>
        {
            var extractDir = Path.Combine(Path.GetTempPath(), $"cmm_extract_{Guid.NewGuid():N}");
            try
            {
                _fileService.CreateDirectory(tempPath);
                ExtractArchive(archivePath, extractDir, progress, ct);

                var extractedFiles = Directory
                    .EnumerateFiles(extractDir, "*", SearchOption.AllDirectories)
                    .Select(f => (
                        key: f[extractDir.Length..].TrimStart(Path.DirectorySeparatorChar)
                                               .Replace(Path.DirectorySeparatorChar, '\\'),
                        path: f))
                    .ToList();

                foreach (var (sourceRelative, destRelative) in fileMapping)
                {
                    ct.ThrowIfCancellationRequested();
                    var normalizedSource = sourceRelative.Replace('/', '\\').Trim('\\');
                    var normalizedDest   = destRelative.Replace('/', '\\').Trim('\\');

                    foreach (var (key, filePath) in extractedFiles)
                    {
                        string? outputRelative = null;

                        if (key.Equals(normalizedSource, StringComparison.OrdinalIgnoreCase))
                        {
                            outputRelative = string.IsNullOrEmpty(normalizedDest)
                                ? Path.GetFileName(filePath)
                                : normalizedDest;
                        }
                        else if (string.IsNullOrEmpty(normalizedSource))
                        {
                            outputRelative = string.IsNullOrEmpty(normalizedDest) ? key : normalizedDest + "\\" + key;
                        }
                        else if (key.StartsWith(normalizedSource + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            var part = key[(normalizedSource.Length + 1)..];
                            outputRelative = string.IsNullOrEmpty(normalizedDest)
                                ? part
                                : normalizedDest + "\\" + part;
                        }

                        if (outputRelative == null) continue;

                        var destPath = Path.Combine(tempPath, outputRelative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        File.Copy(filePath, destPath, overwrite: true);
                    }
                }

                if (_fileService.DirectoryExists(targetPath))
                    _fileService.DeleteDirectory(targetPath, true);
                
                _fileService.MoveDirectory(tempPath, targetPath);
            }
            catch (OperationCanceledException)
            {
                _logService.Log($"FOMOD installation cancelled. Cleaning up...");
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                throw;
            }
            catch (Exception ex)
            {
                _logService.LogError($"FOMOD installation failed: {ex.Message}", ex);
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                throw;
            }
            finally
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);
            }
        }, ct);

        _logService.Log($"FOMOD installed: {modName} → {targetPath}");
        return targetPath;
    }

    public async Task<string> InstallModToRootAsync(string archivePath, string modName, string targetBaseDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!_fileService.DirectoryExists(targetBaseDir))
            _fileService.CreateDirectory(targetBaseDir);

        var targetPath = Path.Combine(targetBaseDir, modName);
        int suffix = 1;
        while (_fileService.DirectoryExists(targetPath))
            targetPath = Path.Combine(targetBaseDir, $"{modName} ({suffix++})");

        string tempPath = Path.Combine(targetBaseDir, ".cmm_tmp_root_" + Guid.NewGuid().ToString("N"));
        var rootFolderInTemp = Path.Combine(tempPath, "Root");

        await Task.Run(() =>
        {
            var extractDir = Path.Combine(Path.GetTempPath(), $"cmm_root_{Guid.NewGuid():N}");
            try
            {
                _fileService.CreateDirectory(tempPath);
                ExtractArchive(archivePath, extractDir, progress, ct);

                var topEntries = Directory.GetFileSystemEntries(extractDir);
                var sourceDir  = topEntries.Length == 1 && Directory.Exists(topEntries[0])
                    ? topEntries[0]
                    : extractDir;

                Directory.CreateDirectory(rootFolderInTemp);
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel  = file[sourceDir.Length..].TrimStart(Path.DirectorySeparatorChar);
                    var dest = Path.Combine(rootFolderInTemp, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, overwrite: true);
                }

                if (_fileService.DirectoryExists(targetPath))
                    _fileService.DeleteDirectory(targetPath, true);
                
                _fileService.MoveDirectory(tempPath, targetPath);
            }
            catch (OperationCanceledException)
            {
                _logService.Log($"Root installation cancelled. Cleaning up...");
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                throw;
            }
            catch (Exception ex)
            {
                _logService.LogError($"Root installation failed: {ex.Message}", ex);
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                throw;
            }
            finally
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);
            }
        }, ct);

        _logService.Log($"Mod installed to Root: {modName} → {targetPath}");
        return targetPath;
    }
}
