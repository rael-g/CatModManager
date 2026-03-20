using System.IO;
using CatModManager.PluginSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CmmPlugin.Capcom.Services;

namespace CmmPlugin.Capcom.Tabs;

public partial class ReEngineTabViewModel : ObservableObject
{
    private readonly IModManagerState _state;

    [ObservableProperty] private bool   _isReEngineGame;
    [ObservableProperty] private string _gameName           = "—";
    [ObservableProperty] private string _reFrameworkStatus  = "—";
    [ObservableProperty] private string _reFrameworkVersion = "";
    [ObservableProperty] private int    _scriptCount;

    public ReEngineTabViewModel(IModManagerState state)
    {
        _state = state;
        Refresh();
    }

    public void Refresh()
    {
        var exe  = _state.GameExecutablePath;
        var game = ReEngineDetector.Detect(exe);

        if (game == null)
        {
            IsReEngineGame      = false;
            GameName            = "Not an RE Engine game";
            ReFrameworkStatus   = "—";
            ReFrameworkVersion  = "";
            ScriptCount         = 0;
            return;
        }

        var gameFolder = Path.GetDirectoryName(exe ?? "");
        var hasRef     = ReEngineDetector.IsReFrameworkInstalled(gameFolder);

        IsReEngineGame      = true;
        GameName            = game.DisplayName;
        ReFrameworkStatus   = game.HasReFrameworkSupport
                                ? (hasRef ? "Installed ✓" : "Not installed ✗")
                                : "N/A";
        ReFrameworkVersion  = hasRef ? ReEngineDetector.GetReFrameworkVersion(gameFolder) : "";
        ScriptCount         = ReEngineDetector.CountReFrameworkScripts(gameFolder);
    }
}
