using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

    /// <summary>
    /// Serialises every mount and unmount, across all callers.
    ///
    /// Without it, an unmount still restoring backups and a mount starting concurrently race on the
    /// same paths: the mount checks <c>File.Exists(dest)</c>, sees nothing, and by the time it calls
    /// <c>link()</c> the unmount has moved the original back into place — EEXIST, mid-mount, with a
    /// half-deployed tree. The command attribute on the UI button was never enough, because mount
    /// and unmount are also reached by launching the game and by running an external tool.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

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
        await _gate.WaitAsync();
        try
        {
            if (IsMounted)
                try { await UnmountAllAsync(); } catch { }
        }
        finally { _gate.Release(); }

        _stateService.RecoverStaleMounts();
    }

    public async Task<OperationResult> MountAsync(MountOptions options)
    {
        await _gate.WaitAsync();
        try { return await MountCoreAsync(options); }
        finally { _gate.Release(); }
    }

    public async Task<OperationResult> UnmountAsync()
    {
        await _gate.WaitAsync();
        try { return await UnmountCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task<OperationResult> MountCoreAsync(MountOptions options)
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

            // Every mount point is resolved to its map first, and nothing is linked until all of
            // them are settled against each other. Mount points overlap by design — a "Game Root"
            // with Path "" contains a "Data" with Path "Data" — so the same physical file can be
            // claimed by two of them. Deploying them independently meant each set the other's link
            // aside as if it were the game's original, and on unmount a mod file was restored into
            // the game folder in the original's place.
            var planned = new List<(MountPointDef Mp, string Target, CatVirtualFileSystem Vfs,
                                    Dictionary<string, IFileSource> Map, int ModCount)>();

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
                var map = await Task.Run(() => vfs.BuildFileMap(targetPath, modsForMp));

                planned.Add((mp, targetPath, vfs, map, modsForMp.Count));
            }

            SettleOverlaps(planned, options.ActiveMods);

            foreach (var p in planned)
            {
                await Task.Run(() => p.Vfs.MountPrepared(p.Target, p.Map));
                _mounted.Add(p.Vfs);

                _logService.Log($"  [{p.Mp.Name}] → {p.Target} ({p.ModCount} mod(s), {p.Map.Count} file(s))");
            }

            _logService.Log("Mounted.");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            // Logged, not just returned. The failure used to reach the status bar and nowhere else,
            // so the one message that explained why a mount failed was gone the moment the app
            // closed — and the stack trace, which says *where* it failed, was never recorded at all.
            _logService.LogError("Mount failed", ex);

            _lastGameFolderPath = null;

            // Guarded: UnmountAllAsync now reports failures by throwing, and letting that escape
            // here would replace the exception that says why the mount failed with one that only
            // says the cleanup did too.
            try { await UnmountAllAsync(); }
            catch (Exception cleanupEx) { _logService.LogError("Rollback after failed mount", cleanupEx); }

            return OperationResult.Failure($"MOUNT ERROR: {ex.Message}", ex);
        }
    }

    private async Task<OperationResult> UnmountCoreAsync()
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
            _logService.LogError("Unmount failed", ex);
            return OperationResult.Failure($"UNMOUNT ERROR: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gives every physical destination a single owner, so no two mount points deploy to the same
    /// file. The winner is the higher-priority mod, which is the same rule that already settles
    /// conflicts within one mount point — the difference is that it now applies across them too.
    /// Losing entries are dropped from their map before anything reaches the disk.
    /// </summary>
    private void SettleOverlaps(
        List<(MountPointDef Mp, string Target, CatVirtualFileSystem Vfs,
              Dictionary<string, IFileSource> Map, int ModCount)> planned,
        IReadOnlyList<Mod> activeMods)
    {
        if (planned.Count < 2) return;

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        // absolute destination → (which map holds it, under which key, at which mod priority)
        var owners = new Dictionary<string, (int Index, string Key, int Priority)>(comparer);
        var drops  = new List<(int Index, string Key, string Dest, string WinnerMp)>();

        for (int i = 0; i < planned.Count; i++)
        {
            foreach (var (key, source) in planned[i].Map)
            {
                string dest = Path.GetFullPath(Path.Combine(
                    planned[i].Target, key.Replace('\\', Path.DirectorySeparatorChar)));

                // A file already sitting at its own destination is the game's own, which the driver
                // skips anyway. Letting it claim ownership would let an untouched game file beat a
                // real mod from another mount point and silently drop it.
                if (source is PhysicalFileSource pfs && comparer.Equals(Normalize(pfs.FilePath), dest))
                    continue;

                int priority = PriorityOf(source, activeMods);

                if (!owners.TryGetValue(dest, out var held))
                {
                    owners[dest] = (i, key, priority);
                    continue;
                }

                if (priority > held.Priority)
                {
                    drops.Add((held.Index, held.Key, dest, planned[i].Mp.Name));
                    owners[dest] = (i, key, priority);
                }
                else
                {
                    drops.Add((i, key, dest, planned[held.Index].Mp.Name));
                }
            }
        }

        foreach (var (index, key, dest, winner) in drops)
        {
            planned[index].Map.Remove(key);
            _logService.Log(
                $"[VFS] '{dest}' is claimed by two mount points; [{winner}] wins on mod priority, " +
                $"[{planned[index].Mp.Name}] drops '{key}'.");
        }

        if (drops.Count > 0)
            _logService.Log($"[VFS] Settled {drops.Count} overlapping file(s) between mount points.");
    }

    /// <summary>Windows paths carry a \\?\ prefix from PhysicalFileSource; strip it to compare.</summary>
    private static string Normalize(string path)
        => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

    /// <summary>
    /// The priority of the mod a file came from, found by which mod's folder contains it. Returns
    /// int.MinValue for a source no mod claims, so a real mod always outranks it.
    /// </summary>
    private static int PriorityOf(IFileSource source, IReadOnlyList<Mod> activeMods)
    {
        if (source is not PhysicalFileSource pfs) return int.MinValue;

        string path = Normalize(pfs.FilePath);
        int best = int.MinValue;

        foreach (var mod in activeMods)
        {
            if (string.IsNullOrEmpty(mod.ModRootPath)) continue;
            if (SimpleConflictResolver.IsUnder(path, mod.ModRootPath) && mod.Priority > best)
                best = mod.Priority;
        }
        return best;
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
        var stillMounted = new List<CatVirtualFileSystem>();
        Exception? firstFailure = null;

        foreach (var vfs in _mounted)
        {
            bool reverted = await Task.Run(async () =>
            {
                int retries = 3;
                while (true)
                {
                    try
                    {
                        vfs.Unmount();
                        vfs.Dispose();
                        return true;
                    }
                    catch (IOException) when (retries > 1)
                    {
                        _logService.Log("[VFS] Files locked, retrying unmount...");
                        await Task.Delay(1000);
                        retries--;
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError("[VFS] Unmount failed", ex);
                        firstFailure ??= ex;
                        return false;
                    }
                }
            });

            if (!reverted) stillMounted.Add(vfs);
        }

        // Only forget the mount points that actually came back. Clearing the whole list regardless
        // of the outcome is what made a failed unmount look like a successful one: IsMounted went
        // false, the UI showed "unmounted", and the hard links stayed in the game folder.
        _mounted.Clear();
        _mounted.AddRange(stillMounted);

        if (firstFailure != null)
            throw new IOException(
                $"{stillMounted.Count} mount point(s) could not be reverted: {firstFailure.Message}",
                firstFailure);
    }
}
