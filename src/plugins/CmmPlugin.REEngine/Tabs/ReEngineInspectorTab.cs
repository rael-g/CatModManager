using CatModManager.PluginSdk;
using CmmPlugin.REEngine.Services;

namespace CmmPlugin.REEngine.Tabs;

/// <summary>
/// Inspector tab that shows RE Engine game info (REFramework status, script count).
/// Only visible when the configured executable is a recognised RE Engine title.
/// </summary>
public class ReEngineInspectorTab : IInspectorTab
{
    private readonly ReEngineTabViewModel _vm;
    private readonly IModManagerState     _state;
    private ReEngineTabControl?           _cached;

    public string TabId    => "capcom-reengine";
    public string TabLabel => "RE ENGINE";

    public ReEngineInspectorTab(ReEngineTabViewModel vm, IModManagerState state)
    {
        _vm    = vm;
        _state = state;
    }

    public bool IsVisible(IModInfo? selectedMod)
        => ReEngineDetector.Detect(_state.GameExecutablePath) != null;

    public object CreateView(IModInfo? mod)
    {
        _cached ??= new ReEngineTabControl(_vm);
        return _cached;
    }
}
