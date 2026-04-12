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
/// so mod files can be hard-linked into different target directories simultaneously
/// (e.g. game Data\ and an external AppData\ folder for .pak mods).
/// </summary>
public class VfsOrchestrationService : IVfsOrchestrationService
{
    private readonly IConflictResolver               _resolver;
    private readonly IHardlinkStateStore             _stateStore;
    private readonly ISafeSwapStrategy               _swapStrategy;
    private readonly IVfsStateService                _stateService;
    private readonly IRootSwapService                _rootSwapService;
    private readonly ILogService                     _logService;
    private readonly IReadOnlyList<IVfsLifecycleHook> _vfsHooks;

    // Active VFS instances — one per mount point.
    private readonly List<CatVirtualFileSystem> _mounted = new();
    private string?  _lastGameFolderPath;
    private bool     _rootSwapOnlyDeployed;
    private bool     _rootFilesDeployed;

    public bool IsMounted => _mounted.Count > 0 && _mounted.Any(v => v.IsMounted) || _rootSwapOnlyDeployed;

    public VfsOrchestrationService(
        IConflictResolver                resolver,
        IHardlinkStateStore              stateStore,
        ISafeSwapStrategy                swapStrategy,
        IVfsStateService                 stateService,
        ILogService                      logService,
        IRootSwapService                 rootSwapService,
        IReadOnlyList<IVfsLifecycleHook>? vfsHooks = null)
    {
        _resolver        = resolver;
        _stateStore      = stateStore;
        _swapStrategy    = swapStrategy;
        _stateService    = stateService;
        _rootSwapService = rootSwapService;
        _logService      = logService;
        _vfsHooks        = vfsHooks ?? [];
    }

    // ── Recovery ─────────────────────────────────────────────────────────────

    public void RecoverStaleMounts()
    {
        // Let HardlinkDriver clean up any stale links from a previous crash.
        var crashRecovery = new CatVirtualFileSystem(_resolver, FileSystemFactory.CreateDriver(_stateStore), _swapStrategy);
        try { crashRecovery.Unmount(); } catch { }

        _stateService.RecoverStaleMounts();
        _rootSwapService.RecoverStaleDeployments();
    }

    public void ShutdownCleanup()
    {
        if (IsMounted)
            try { UnmountAllAsync().GetAwaiter().GetResult(); } catch { }

        _rootSwapService.RecoverStaleDeployments();
        _stateService.RecoverStaleMounts();
    }

    // ── Mount ─────────────────────────────────────────────────────────────────

    public async Task<OperationResult> MountAsync(MountOptions options)
    {
        if (IsMounted)
            return OperationResult.Failure("VFS is already mounted.");

        if (string.IsNullOrEmpty(options.GameFolderPath))
            return OperationResult.Failure("ERROR: No game folder path specified.");

        if (options.RootSwapOnly)
            return await MountRootSwapOnlyAsync(options);

        try
        {
            _lastGameFolderPath = options.GameFolderPath;

            // Build effective mount point list.
            // Fall back to DataSubFolder when no mount points are configured.
            var mountPoints = BuildEffectiveMountPoints(options);

            var mountInfo = new MountInfo
            {
                GameFolderPath = options.GameFolderPath,
                DataSubFolder  = options.DataSubFolder,
                ActiveMods     = options.ActiveMods.Cast<IModInfo>().ToList()
            };
            foreach (var hook in _vfsHooks)
                await hook.OnBeforeMountAsync(mountInfo);

            _logService.Log($"Mounting {options.ActiveMods.Count} mod(s) → {options.GameFolderPath} ({mountPoints.Count} mount point(s))");

            foreach (var mp in mountPoints)
            {
                // Select mods for this mount point.
                // Mods with no MountPointId go to the first (default) mount point.
                var modsForMp = options.ActiveMods
                    .Where(m => MountPointMatches(m, mp, mountPoints[0]))
                    .ToList();

                if (modsForMp.Count == 0) continue;

                // Resolve the absolute target path.
                string targetPath = ResolveMountPointPath(options.GameFolderPath, mp.Path);

                var vfs = new CatVirtualFileSystem(_resolver, FileSystemFactory.CreateDriver(_stateStore), _swapStrategy);
                await Task.Run(() => vfs.Mount(options.GameFolderPath, modsForMp, GetDataSubFolder(mp, options)));
                _mounted.Add(vfs);

                _logService.Log($"  [{mp.Name}] → {targetPath} ({modsForMp.Count} mod(s))");
            }

            // Legacy Root/ files via RootSwapService (for backward compat with existing mods
            // that use the Root/ subfolder convention and have no MountPointId set).
            var modsWithRoot = options.ActiveMods.Where(m => m.HasRootFolder && string.IsNullOrEmpty(m.MountPointId)).ToList();
            if (modsWithRoot.Count > 0)
            {
                await _rootSwapService.DeployAsync(modsWithRoot, options.GameFolderPath!);
                _rootFilesDeployed = true;
                _logService.Log($"Root files deployed (legacy) for {modsWithRoot.Count} mod(s).");
            }

            _logService.Log("Mounted.");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _lastGameFolderPath = null;
            _rootFilesDeployed  = false;
            await UnmountAllAsync();
            return OperationResult.Failure($"MOUNT ERROR: {ex.Message}", ex);
        }
    }

    // ── Unmount ───────────────────────────────────────────────────────────────

    public async Task<OperationResult> UnmountAsync()
    {
        if (!IsMounted) return OperationResult.Success();

        if (_rootSwapOnlyDeployed)
        {
            try
            {
                string? gameFolder = _lastGameFolderPath;
                if (!string.IsNullOrEmpty(gameFolder))
                    await _rootSwapService.UndeployAsync(gameFolder);
                _rootSwapOnlyDeployed = false;
                _lastGameFolderPath   = null;
                _logService.Log("RootSwap undeployed.");
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                return OperationResult.Failure($"ROOTSWAP UNDEPLOY ERROR: {ex.Message}", ex);
            }
        }

        try
        {
            string? mountedPath = _lastGameFolderPath;

            if (_rootFilesDeployed && !string.IsNullOrEmpty(mountedPath))
            {
                await _rootSwapService.UndeployAsync(mountedPath);
                _rootFilesDeployed = false;
                _logService.Log("Root files undeployed.");
            }

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds effective mount point list from options, falling back to DataSubFolder.</summary>
    private static List<MountPointDef> BuildEffectiveMountPoints(MountOptions options)
    {
        if (options.MountPoints.Count > 0)
            return options.MountPoints;

        // Legacy: single mount point from DataSubFolder
        return [new MountPointDef("default", "Default", options.DataSubFolder ?? "")];
    }

    /// <summary>Returns true if <paramref name="mod"/> belongs to <paramref name="mp"/>.</summary>
    private static bool MountPointMatches(Mod mod, MountPointDef mp, MountPointDef defaultMp)
    {
        if (string.IsNullOrEmpty(mod.MountPointId))
            return mp == defaultMp; // null/empty → first mount point
        return string.Equals(mod.MountPointId, mp.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the DataSubFolder string to pass to CatVirtualFileSystem.Mount() for a given mount point.
    /// For absolute paths, returns the path as-is (VFS handles absolute dataSubFolder).
    /// For relative paths, returns the relative path.
    /// </summary>
    private static string? GetDataSubFolder(MountPointDef mp, MountOptions options)
    {
        if (string.IsNullOrEmpty(mp.Path)) return null; // game root
        return Environment.ExpandEnvironmentVariables(mp.Path); // relative or absolute
    }

    private static string ResolveMountPointPath(string gameFolder, string mpPath)
    {
        if (string.IsNullOrEmpty(mpPath)) return gameFolder;
        var expanded = Environment.ExpandEnvironmentVariables(mpPath);
        if (Path.IsPathRooted(expanded)) return expanded;
        return Path.Combine(gameFolder, expanded);
    }

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

    // ── Legacy RootSwap-only path ─────────────────────────────────────────────

    private async Task<OperationResult> MountRootSwapOnlyAsync(MountOptions options)
    {
        try
        {
            _lastGameFolderPath = options.GameFolderPath;
            _logService.Log($"RootSwap deploy: {options.ActiveMods.Count} mod(s) → {options.GameFolderPath}");
            await _rootSwapService.DeployAsync(options.ActiveMods, options.GameFolderPath!);
            _rootSwapOnlyDeployed = true;
            _logService.Log("RootSwap deployed.");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"ROOTSWAP ERROR: {ex.Message}", ex);
        }
    }
}
