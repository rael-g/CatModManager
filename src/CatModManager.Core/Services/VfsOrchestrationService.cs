using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Vfs;

namespace CatModManager.Core.Services;

public class VfsOrchestrationService : IVfsOrchestrationService
{
    private const string BackupSuffix = ".CMM_base";
    private readonly IVirtualFileSystem _vfs;
    private readonly IVfsStateService _stateService;
    private readonly IDriverService _driverService;
    private readonly ILogService _logService;
    private string? _lastMountPoint;

    public bool IsMounted => _vfs.IsMounted;

    public VfsOrchestrationService(
        IVirtualFileSystem vfs,
        IVfsStateService stateService,
        IDriverService driverService,
        ILogService logService)
    {
        _vfs = vfs;
        _stateService = stateService;
        _driverService = driverService;
        _logService = logService;
    }

    public void RecoverStaleMounts() => _stateService.RecoverStaleMounts();

    public void ShutdownCleanup()
    {
        if (IsMounted)
        {
            try { _vfs.Unmount(); } catch { }
        }
        _stateService.RecoverStaleMounts();
    }

    public async Task<OperationResult> UnmountAsync()
    {
        if (!IsMounted) return OperationResult.Success();

        try
        {
            string? targetToClean = _lastMountPoint;
            await Task.Run(() => _vfs.Unmount());

            await Task.Delay(500);

            if (!string.IsNullOrEmpty(targetToClean))
            {
                _stateService.UnregisterMount(targetToClean);
            }

            await Task.Run(() => _stateService.RecoverStaleMounts());
            _logService.Log("VFS Unmounted.");
            _lastMountPoint = null;
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"UNMOUNT ERROR: {ex.Message}", ex);
        }
    }

    public async Task<OperationResult> MountAsync(MountOptions options)
    {
        if (IsMounted) return OperationResult.Failure("VFS is already mounted.");

        if (!_driverService.IsDriverInstalled()) return OperationResult.Failure("ERROR: WinFsp Driver missing.");

        if (string.IsNullOrEmpty(options.GameFolderPath))
            return OperationResult.Failure("ERROR: No game folder path specified.");

        return await MountWithRetryAsync(options);
    }

    private async Task<OperationResult> MountWithRetryAsync(MountOptions options)
    {
        string targetPath = Path.GetFullPath(string.IsNullOrEmpty(options.DataSubFolder) 
            ? options.GameFolderPath! 
            : Path.Combine(options.GameFolderPath!, options.DataSubFolder));
            
        _lastMountPoint = targetPath;

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                string? effectiveBaseDataPath = null;
                if (Directory.Exists(targetPath))
                {
                    string normalized = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string folderName = Path.GetFileName(normalized);
                    string hiddenPath = Path.Combine(Path.GetDirectoryName(normalized)!, "." + folderName + BackupSuffix);

                    if (!Directory.Exists(hiddenPath))
                    {
                        _logService.Log($"Safe Swap: Moving '{normalized}' to backup.");
                        _stateService.RegisterMount(normalized, hiddenPath);
                        await Task.Run(() => Directory.Move(normalized, hiddenPath));
                        try { var di = new DirectoryInfo(hiddenPath); di.Attributes |= FileAttributes.Hidden; } catch { }
                    }
                    else
                    {
                        _logService.Log("Target path occupied. Clearing it.");
                        await Task.Run(() => Directory.Delete(normalized, true));
                    }
                    effectiveBaseDataPath = hiddenPath;
                }

                _logService.Log($"Mounting at {targetPath} with {options.ActiveMods.Count} mod(s).");
                await Task.Run(() => _vfs.Mount(targetPath, options.ActiveMods, effectiveBaseDataPath, options.DataSubFolder));
                _logService.Log($"VFS Mounted at {targetPath}");
                
                return OperationResult.Success();
            }
            catch (Exception ex) when ((ex.Message.Contains("0xC0000035") || ex.Message.Contains("access")) && attempt < 5)
            {
                _logService.Log($"Conflict detected... retrying ({attempt}/5)");
                await Task.Delay(1000 * attempt);
            }
            catch (Exception ex)
            {
                await Task.Run(() => _stateService.RecoverStaleMounts());
                _lastMountPoint = null;
                return OperationResult.Failure($"MOUNT ERROR: {ex.Message}", ex);
            }
        }

        return OperationResult.Failure("Mount failed after multiple attempts due to system conflicts.");
    }
}
