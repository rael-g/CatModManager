using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

public class ModManagementService : IModManagementService
{
    private readonly IFileService _fileService;
    private readonly ILogService _logService;
    private readonly IArchiveExtractor _extractor;

    public ModManagementService(IFileService fileService, ILogService logService, IArchiveExtractor extractor)
    {
        _fileService = fileService;
        _logService = logService;
        _extractor = extractor;
    }

    public async Task<string> InstallModAsync(
        string sourcePath,
        string targetBaseDir,
        string? overrideTargetPath = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!_fileService.FileExists(sourcePath) && !_fileService.DirectoryExists(sourcePath))
            throw new FileNotFoundException($"Source not found: {sourcePath}");

        _fileService.CreateDirectory(targetBaseDir);

        if (_fileService.DirectoryExists(sourcePath))
        {
            string folderName = Path.GetFileName(sourcePath);
            string finalPath = overrideTargetPath ?? GetUniquePath(targetBaseDir, folderName);
            _fileService.CopyDirectory(sourcePath, finalPath);
            _logService.Log($"Mod installed from folder: {folderName} → {finalPath}");
            return finalPath;
        }

        string modName = Path.GetFileNameWithoutExtension(sourcePath);
        string targetPath = overrideTargetPath ?? GetUniquePath(targetBaseDir, modName);

        using var workspace = new TempWorkspace(targetBaseDir);
        try
        {
            await _extractor.ExtractAsync(sourcePath, workspace.Path, progress, ct);
            _fileService.MoveDirectory(workspace.Path, targetPath);
            _logService.Log($"Mod installed from archive: {modName} → {targetPath}");
            return targetPath;
        }
        catch (OperationCanceledException)
        {
            _logService.Log($"Installation cancelled for: {modName}");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to install mod: {modName}", ex);
            throw;
        }
    }

    public async Task<string> InstallModFromMappingAsync(
        string archivePath,
        string modName,
        string targetBaseDir,
        Dictionary<string, string> fileMapping,
        string? overrideTargetPath = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        _fileService.CreateDirectory(targetBaseDir);
        string targetPath = overrideTargetPath ?? GetUniquePath(targetBaseDir, modName);

        using var workspace = new TempWorkspace(targetBaseDir);
        try
        {
            await _extractor.ExtractAsync(archivePath, workspace.Path, progress, ct);

            _fileService.CreateDirectory(targetPath);
            foreach (var mapping in fileMapping)
            {
                string sourceInTemp = Path.Combine(workspace.Path, mapping.Key);
                string destInTarget = Path.Combine(targetPath, mapping.Value);

                if (File.Exists(sourceInTemp))
                {
                    _fileService.CreateDirectory(Path.GetDirectoryName(destInTarget)!);
                    _fileService.CopyFile(sourceInTemp, destInTarget, true);
                }
                else if (Directory.Exists(sourceInTemp))
                {
                    _fileService.CopyDirectory(sourceInTemp, destInTarget);
                }
            }

            _logService.Log($"Mod installed via mapping: {modName} → {targetPath}");
            return targetPath;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to install mapped mod: {modName}", ex);
            throw;
        }
    }

    public async Task<string> InstallModToRootAsync(
        string archivePath,
        string modName,
        string targetBaseDir,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        _fileService.CreateDirectory(targetBaseDir);
        string targetPath = GetUniquePath(targetBaseDir, modName);
        string extractDir = Path.Combine(targetPath, "Root");

        using var workspace = new TempWorkspace(targetBaseDir);
        try
        {
            await _extractor.ExtractAsync(archivePath, workspace.Path, progress, ct);
            _fileService.CreateDirectory(targetPath);
            _fileService.MoveDirectory(workspace.Path, extractDir);
            
            _logService.Log($"Mod installed to Root: {modName} → {targetPath}");
            return targetPath;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to install root mod: {modName}", ex);
            throw;
        }
    }

    private string GetUniquePath(string baseDir, string name)
    {
        string path = Path.Combine(baseDir, name);
        if (!Directory.Exists(path)) return path;

        int i = 1;
        while (Directory.Exists($"{path} ({i})")) i++;
        return $"{path} ({i})";
    }
}
