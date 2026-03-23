using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

public class NexusBrowseSidebarAction : ISidebarAction
{
    private readonly NexusApiService      _api;
    private readonly NexusDownloadService _downloads;
    private readonly System.Func<string>  _getDownloadsFolder;
    private readonly System.Func<string?> _getCurrentGameDomain;

    private NexusBrowseWindow? _window;

    public string Label => "NEXUS BROWSE";
    public string Icon  => "⬡";

    public NexusBrowseSidebarAction(
        NexusApiService      api,
        NexusDownloadService downloads,
        System.Func<string>  getDownloadsFolder,
        System.Func<string?> getCurrentGameDomain)
    {
        _api                  = api;
        _downloads            = downloads;
        _getDownloadsFolder   = getDownloadsFolder;
        _getCurrentGameDomain = getCurrentGameDomain;
    }

    public void Execute()
    {
        if (_window != null && _window.IsVisible)
        {
            _window.Activate();
            return;
        }

        _window = new NexusBrowseWindow(_api, _downloads, _getDownloadsFolder, _getCurrentGameDomain);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
