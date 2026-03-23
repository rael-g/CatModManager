using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

public class NexusBrowseSidebarAction : ISidebarAction
{
    private readonly NexusApiService      _api;
    private readonly IModManagerState     _state;
    private readonly NexusDownloadService? _downloadService;
    private readonly Func<string>?        _getDownloadsFolder;

    public string Label => "BROWSE NEXUS";
    public string Icon  => "⬡";

    public NexusBrowseSidebarAction(
        NexusApiService api,
        IModManagerState state,
        NexusDownloadService? downloadService = null,
        Func<string>? getDownloadsFolder = null)
    {
        _api                = api;
        _state              = state;
        _downloadService    = downloadService;
        _getDownloadsFolder = getDownloadsFolder;
    }

    public void Execute()
    {
        var gameDomain = _state.GameId ?? string.Empty;
        var mainWindow = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        var window = new NexusBrowseWindow(_api, gameDomain, _downloadService, _getDownloadsFolder);
        if (mainWindow != null)
            window.Show(mainWindow);
        else
            window.Show();
    }
}
