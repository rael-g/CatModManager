using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Vfs;
using CatModManager.PluginSdk;

namespace CatModManager.Core.Services;

/// <summary>
/// Coordinates mount / unmount operations.
/// Forwards Mount/Unmount to the VFS and handles crash recovery at startup.
/// </summary>
public class VfsOrchestrationService : IVfsOrchestrationService
{
    private readonly IVirtualFileSystem          _vfs;
    private readonly IVfsStateService            _stateService;
    private readonly IRootSwapService            _rootSwapService;
    private readonly ILogService                 _logService;
    private readonly IReadOnlyList<IVfsLifecycleHook> _vfsHooks;

    private string? _lastMountGameFolder;
    private bool    _rootSwapOnlyDeployed;
    private bool    _rootFilesDeployed;

    public bool IsMounted => _vfs.IsMounted || _rootSwapOnlyDeployed;

    public VfsOrchestrationService(
        IVirtualFileSystem vfs,
        IVfsStateService    stateService,
        IDriverService      driverService,
        ILogService         logService,
        IRootSwapService    rootSwapService,
        IReadOnlyList<IVfsLifecycleHook>? vfsHooks = null)
    {
        _vfs             = vfs;
        _stateService    = stateService;
        _rootSwapService = rootSwapService;
        _logService      = logService;
        _vfsHooks        = vfsHooks ?? [];
    }

    // ── Recovery ─────────────────────────────────────────────────────────────

    public void RecoverStaleMounts()
    {
        _stateService.RecoverStaleMounts();
        _rootSwapService.RecoverStaleDeployments();
    }

    public void ShutdownCleanup()
    {
        if (IsMounted)
            try { _vfs.Unmount(); } catch { }

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

        // Legacy RootSwap-only path for RE Engine games that expose Root/ files.
        if (options.RootSwapOnly)
            return await MountRootSwapOnlyAsync(options);

        try
        {
            _lastMountGameFolder = options.GameFolderPath;

            var mountInfo = new MountInfo
            {
                GameFolderPath = options.GameFolderPath,
                DataSubFolder  = options.DataSubFolder,
                ActiveMods     = options.ActiveMods.Cast<IModInfo>().ToList()
            };
            foreach (var hook in _vfsHooks)
                await hook.OnBeforeMountAsync(mountInfo);

            _logService.Log($"Mounting {options.ActiveMods.Count} mod(s) → {options.GameFolderPath}");
            await Task.Run(() => _vfs.Mount(
                options.GameFolderPath!,
                options.ActiveMods,
                options.DataSubFolder));
            _logService.Log("Mounted.");

            // Deploy Root/ files from any mod that has them to the actual game root.
            var modsWithRoot = options.ActiveMods.Where(m => m.HasRootFolder).ToList();
            if (modsWithRoot.Count > 0)
            {
                await _rootSwapService.DeployAsync(modsWithRoot, options.GameFolderPath!);
                _rootFilesDeployed = true;
                _logService.Log($"Root files deployed for {modsWithRoot.Count} mod(s).");
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _lastMountGameFolder = null;
            _rootFilesDeployed   = false;
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
                string? gameFolder = _lastMountGameFolder;
                if (!string.IsNullOrEmpty(gameFolder))
                    await _rootSwapService.UndeployAsync(gameFolder);
                _rootSwapOnlyDeployed = false;
                _lastMountGameFolder  = null;
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
            string? mountedPath = _lastMountGameFolder;

            // Undeploy Root/ files before unmounting the VFS.
            if (_rootFilesDeployed && !string.IsNullOrEmpty(mountedPath))
            {
                await _rootSwapService.UndeployAsync(mountedPath);
                _rootFilesDeployed = false;
                _logService.Log("Root files undeployed.");
            }

            await Task.Run(() => _vfs.Unmount());
            _lastMountGameFolder = null;
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

    // ── Legacy RootSwap-only path ─────────────────────────────────────────────

    private async Task<OperationResult> MountRootSwapOnlyAsync(MountOptions options)
    {
        try
        {
            _lastMountGameFolder = options.GameFolderPath;
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
