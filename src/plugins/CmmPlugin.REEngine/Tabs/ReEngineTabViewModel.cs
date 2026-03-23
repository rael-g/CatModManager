using System.IO;
using CatModManager.PluginSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CmmPlugin.REEngine.Services;

namespace CmmPlugin.REEngine.Tabs;

public partial class ReEngineTabViewModel : ObservableObject
{
    private readonly IModManagerState _state;

    [ObservableProperty] private string _gameName        = "—";
    [ObservableProperty] private string _reFrameworkLabel = "—";
    [ObservableProperty] private string _scriptCount     = "—";

    public ReEngineTabViewModel(IModManagerState state)
    {
        _state = state;
        Refresh();
    }

    public void Refresh()
    {
        var exe        = _state.GameExecutablePath;
        var game       = ReEngineDetector.Detect(exe);
        var gameFolder = Path.GetDirectoryName(exe ?? "");

        if (game == null)
        {
            GameName         = "Not an RE Engine game";
            ReFrameworkLabel = "—";
            ScriptCount      = "—";
            return;
        }

        GameName = game.DisplayName;

        if (!game.HasReFrameworkSupport)
        {
            ReFrameworkLabel = "N/A";
            ScriptCount      = "—";
            return;
        }

        var installed = ReEngineDetector.IsReFrameworkInstalled(gameFolder);
        if (!installed)
        {
            ReFrameworkLabel = "Not installed ✗";
            ScriptCount      = "—";
            return;
        }

        var version = ReEngineDetector.GetReFrameworkVersion(gameFolder);
        ReFrameworkLabel = string.IsNullOrEmpty(version) ? "Installed ✓" : $"Installed ✓  v{version}";
        ScriptCount      = ReEngineDetector.CountReFrameworkScripts(gameFolder).ToString();
    }
}
