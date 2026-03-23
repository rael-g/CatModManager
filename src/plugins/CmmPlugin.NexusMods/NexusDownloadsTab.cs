using System;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// IInspectorTab wrapper that always shows the downloads panel.
/// </summary>
public class NexusDownloadsTab : IInspectorTab
{
    private readonly NexusDownloadsTabControl _control;

    public string TabId    => "nexus-downloads";
    public string TabLabel => "DOWNLOADS";

    public bool IsVisible(IModInfo? mod) => true;

    public object CreateView(IModInfo? mod) => _control;

    public NexusDownloadsTab(NexusDownloadService downloadService, NexusApiService api, Action<string, FomodPreset?>? installCallback = null, Func<string>? getDownloadsFolder = null, Action<string>? installToRootCallback = null)
        => _control = new NexusDownloadsTabControl(downloadService, api, installCallback, getDownloadsFolder, installToRootCallback);
}
