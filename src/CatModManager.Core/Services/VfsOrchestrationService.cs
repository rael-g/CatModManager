using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Vfs;
using CatModManager.PluginSdk;
using CatModManager.VirtualFileSystem;

namespace CatModManager.Core.Services;

/// <summary>
/// Coordinates mount / unmount operations across one or more mount points.
/// Each active mount point gets its own <see cref="CatVirtualFileSystem"/> instance,
/// so mod files can be hard-linked into different target directories simultaneously.
/// </summary>
public class VfsOrchestrationService : IVfsOrchestrationService
{
    private readonly IConflictResolver               _resolver;
    private readonly IHardlinkStateStore             _stateStore;
    private readonly IVfsStateService                _stateService;
    private readonly ILogService                     _logService;
    private readonly IReadOnlyList<IVfsLifecycleHook> _vfsHooks;

    /// <summary>
    /// Builds the deployment driver for a resolved mount target.
    ///
    /// Injectable because the real factory decides between a FUSE mount and hard links by probing
    /// the machine — which makes the orchestration logic here (hook ordering, the already-mounted
    /// guard, unmount cleanup) impossible to test without touching a real filesystem.
    /// </summary>
    private readonly Func<string?, IFileSystemDriver> _driverFactory;

    // Active VFS instances — one per mount point.
    private readonly List<CatVirtualFileSystem> _mounted = new();
    private string?  _lastGameFolderPath;

    public bool IsMounted => _mounted.Count > 0 && _mounted.Any(v => v.IsMounted);

    public VfsOrchestrationService(
        IConflictResolver                resolver,
        IHardlinkStateStore              stateStore,
        IVfsStateService                 stateService,
        ILogService                      logService,
        IReadOnlyList<IVfsLifecycleHook>? vfsHooks = null,
        Func<string?, IFileSystemDriver>? driverFactory = null)
    {
        _resolver        = resolver;
        _stateStore      = stateStore;
        _stateService    = stateService;
        _logService      = logService;
        _vfsHooks        = vfsHooks ?? [];
        _driverFactory   = driverFactory
            ?? (target => FileSystemFactory.CreateDriver(stateStore, target, logService.Log));
    }

    public void RecoverStaleMounts()
    {
        // Clean up any stale links from a previous crash. Must be the crash-recovery driver:
        // it is the one that persists what was deployed and can revert it.
        var crashRecovery = new CatVirtualFileSystem(
            _resolver, FileSystemFactory.CreateCrashRecoveryDriver(_stateStore));
        try { crashRecovery.Unmount(); } catch { }

        _stateService.RecoverStaleMounts();
    }

    public async Task ShutdownCleanupAsync()
    {
        if (IsMounted)
            try { await UnmountAllAsync(); } catch { }

        _stateService.RecoverStaleMounts();
    }

    public async Task<OperationResult> MountAsync(MountOptions options)
    {
        if (IsMounted)
            return OperationResult.Failure("VFS is already mounted.");

        if (string.IsNullOrEmpty(options.GameFolderPath))
            return OperationResult.Failure("ERROR: No game folder path specified.");

        try
        {
            _lastGameFolderPath = options.GameFolderPath;

            var mountPoints = options.MountPoints;
            if (mountPoints.Count == 0)
                return OperationResult.Failure("ERROR: No mount points defined for this profile.");

            var mountInfo = new MountInfo
            {
                GameFolderPath = options.GameFolderPath,
                ActiveMods     = options.ActiveMods.Cast<IModInfo>().ToList()
            };
            foreach (var hook in _vfsHooks)
                await hook.OnBeforeMountAsync(mountInfo);

            _logService.Log($"Mounting {options.ActiveMods.Count} mod(s) → {options.GameFolderPath} ({mountPoints.Count} mount point(s))");

            foreach (var mp in mountPoints)
            {
                // Mods with no MountPointId go to the first (default) mount point.
                var modsForMp = options.ActiveMods
                    .Where(m => MountPointMatches(m, mp, mountPoints[0]))
                    .ToList();

                if (modsForMp.Count == 0) continue;

                string targetPath = ResolveMountPointPath(options.GameFolderPath, mp.Path);

                // The driver depends on where we are deploying: a game on NTFS cannot take the
                // FUSE overlay, so the factory needs the resolved target, not just the platform.
                var vfs = new CatVirtualFileSystem(_resolver, _driverFactory(targetPath));
                await Task.Run(() => vfs.Mount(targetPath, modsForMp));
                _mounted.Add(vfs);

                _logService.Log($"  [{mp.Name}] → {targetPath} ({modsForMp.Count} mod(s))");
            }

            _logService.Log("Mounted.");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _lastGameFolderPath = null;
            await UnmountAllAsync();
            return OperationResult.Failure($"MOUNT ERROR: {ex.Message}", ex);
        }
    }

    public async Task<OperationResult> UnmountAsync()
    {
        if (!IsMounted) return OperationResult.Success();

        try
        {
            string? mountedPath = _lastGameFolderPath;

            await UnmountAllAsync();
            _lastGameFolderPath = null;
            _logService.Log("Unmounted.");

            foreach (var hook in _vfsHooks)
                await hook.OnAfterUnmountAsync(mountedPath ?? string.Empty);

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"UNMOUNT ERROR: {ex.Message}", ex);
        }
    }

    internal static bool MountPointMatches(Mod mod, MountPointDef mp, MountPointDef defaultMp)
    {
        if (string.IsNullOrEmpty(mod.MountPointId))
            return mp == defaultMp;
        return string.Equals(mod.MountPointId, mp.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMountPointPath(string gameFolder, string mpPath)
        => MountPointDef.Resolve(mpPath, gameFolder);

    private async Task UnmountAllAsync()
    {
        foreach (var vfs in _mounted)
        {
            await Task.Run(async () =>
            {
                int retries = 3;
                while (retries > 0)
                {
                    try
                    {
                        vfs.Unmount();
                        vfs.Dispose();
                        break;
                    }
                    catch (IOException) when (retries > 1)
                    {
                        _logService.Log("[VFS] Files locked, retrying unmount...");
                        await Task.Delay(1000);
                        retries--;
                    }
                    catch (Exception ex)
                    {
                        _logService.Log($"[VFS] Unmount failed: {ex.Message}");
                        break;
                    }
                }
            });
        }
        _mounted.Clear();
    }
}
