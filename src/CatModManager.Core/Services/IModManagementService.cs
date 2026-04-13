using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

public interface IModManagementService
{
    Task<string> InstallModAsync(
        string sourcePath,
        string targetBaseDir,
        string? overrideTargetPath = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task<string> InstallModFromMappingAsync(
        string archivePath,
        string modName,
        string targetBaseDir,
        Dictionary<string, string> fileMapping,
        string? overrideTargetPath = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task<string> InstallModToRootAsync(
        string archivePath,
        string modName,
        string targetBaseDir,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
