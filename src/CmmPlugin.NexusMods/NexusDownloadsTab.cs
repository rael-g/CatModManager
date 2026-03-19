using System;
using Avalonia.Controls;
using CatModManager.Core.Models;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// IInspectorTab wrapper that always shows the downloads panel.
/// </summary>
public class NexusDownloadsTab : IInspectorTab
{
    private readonly NexusDownloadsTabControl _control;

    public string TabId => "nexus-downloads";
    public string TabLabel => "DOWNLOADS";

    public bool IsVisible(Mod? mod) => true;

    public Control CreateView(Mod? mod) => _control;

    public NexusDownloadsTab(NexusDownloadService downloadService, NexusApiService api, Action<string>? installCallback = null, Func<string>? getDownloadsFolder = null)
        => _control = new NexusDownloadsTabControl(downloadService, api, installCallback, getDownloadsFolder);
}
