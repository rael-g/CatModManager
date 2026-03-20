using System.Collections.Generic;

namespace CatModManager.PluginSdk;

/// <summary>Snapshot of VFS mount parameters, passed to IVfsLifecycleHook.</summary>
public sealed class MountInfo
{
    public string? GameFolderPath { get; init; }
    public string? DataSubFolder  { get; init; }
    public IReadOnlyList<IModInfo> ActiveMods { get; init; } = [];
}
