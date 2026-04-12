using System;
using System.IO;
using System.Threading.Tasks;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Right-click context menu action: reinstalls the mod from its original downloaded archive.
/// Visible only when a source archive is tracked and still exists on disk.
/// </summary>
internal sealed class NexusReinstallAction : IModContextAction
{
    private readonly NexusModTrackingService                    _tracking;
    private readonly Action<string, FomodPreset?>               _installCallback;
    private readonly IPluginLogger                              _log;

    public string Label => "Reinstall (Nexus)";

    public NexusReinstallAction(
        NexusModTrackingService tracking,
        Action<string, FomodPreset?> installCallback,
        IPluginLogger log)
    {
        _tracking        = tracking;
        _installCallback = installCallback;
        _log             = log;
    }

    public bool IsVisible(IModInfo? mod)
    {
        if (mod == null) return false;
        var entry = _tracking.GetEntry(mod.RootPath);
        return entry?.SourceArchivePath != null && File.Exists(entry.SourceArchivePath);
    }

    public Task<string?> ExecuteAsync(IModInfo mod)
    {
        var entry = _tracking.GetEntry(mod.RootPath);
        if (entry?.SourceArchivePath == null || !File.Exists(entry.SourceArchivePath))
            return Task.FromResult<string?>($"[Nexus] Archive not found for {mod.Name}.");

        _log.Log($"[Nexus] Reinstalling {mod.Name} from {Path.GetFileName(entry.SourceArchivePath)}");
        _installCallback(entry.SourceArchivePath, null);
        return Task.FromResult<string?>(null);
    }
}

