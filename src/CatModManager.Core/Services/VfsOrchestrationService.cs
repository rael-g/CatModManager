using System;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Vfs;

namespace CatModManager.Core.Services;

/// <summary>
/// Coordinates mount / unmount operations.
///
/// All SafeSwap logic has moved into <see cref="ISafeSwapStrategy"/> implementations
/// which are injected into <see cref="CatVirtualFileSystem"/>.
/// This service is now responsible only for:
///   - forwarding Mount / Unmount to the VFS
///   - crash recovery at startup via IVfsStateService / IRootSwapService
///   - the legacy RootSwap-only path (WinFsp + RE Engine games)
/// </summary>
public class VfsOrchestrationService : IVfsOrchestrationService
{
    private readonly IVirtualFileSystem _vfs;
    private readonly IVfsStateService    _stateService;
    private readonly IRootSwapService    _rootSwapService;
    private readonly ILogService         _logService;

    private string? _lastMountGameFolder;
    private bool    _rootSwapOnlyDeployed;

    public bool IsMounted => _vfs.IsMounted || _rootSwapOnlyDeployed;

    public VfsOrchestrationService(
        IVirtualFileSystem vfs,
        IVfsStateService    stateService,
        IDriverService      driverService,
        ILogService         logService,
        IRootSwapService    rootSwapService)
    {
        _vfs             = vfs;
        _stateService    = stateService;
        _rootSwapService = rootSwapService;
        _logService      = logService;
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

        // Legacy RootSwap-only path (WinFsp + RE Engine games that expose Root/ files).
        // The ISafeSwapStrategy handles everything for HardlinkDriver / FuseDriver,
        // so RootSwapOnly is only reached when it is explicitly set and a WinFsp driver
        // is in use (i.e. someone overrides FileSystemFactory back to WinFspDriver).
        if (options.RootSwapOnly)
            return await MountRootSwapOnlyAsync(options);

        try
        {
            _lastMountGameFolder = options.GameFolderPath;
            _logService.Log($"Mounting {options.ActiveMods.Count} mod(s) → {options.GameFolderPath}");
            await Task.Run(() => _vfs.Mount(
                options.GameFolderPath!,
                options.ActiveMods,
                options.DataSubFolder));
            _logService.Log("Mounted.");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _lastMountGameFolder = null;
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
            await Task.Run(() => _vfs.Unmount());
            _lastMountGameFolder = null;
            _logService.Log("Unmounted.");
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
