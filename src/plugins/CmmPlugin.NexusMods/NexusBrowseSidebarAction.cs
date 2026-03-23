using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Sidebar button that opens the Nexus Mods browse / search window.
/// </summary>
public class NexusBrowseSidebarAction : ISidebarAction
{
    private readonly NexusApiService   _api;
    private readonly IModManagerState  _state;

    public string Label => "BROWSE NEXUS";
    public string Icon  => "⬡";

    public NexusBrowseSidebarAction(NexusApiService api, IModManagerState state)
    {
        _api   = api;
        _state = state;
    }

    public void Execute()
    {
        var gameDomain = _state.GameId ?? string.Empty;

        var mainWindow = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        var window = new NexusBrowseWindow(_api, gameDomain);
        if (mainWindow != null)
            window.Show(mainWindow);
        else
            window.Show();
    }
}
