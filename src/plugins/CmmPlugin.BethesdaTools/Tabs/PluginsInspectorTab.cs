using CatModManager.PluginSdk;

namespace CmmPlugin.BethesdaTools.Tabs;

/// <summary>
/// Inspector tab that shows the ESP/ESM/ESL load order.
/// Always visible (global, not per-mod).
/// </summary>
public class PluginsInspectorTab : IInspectorTab
{
    private readonly PluginsTabViewModel _vm;
    private PluginsTabControl? _cachedControl;

    public string TabId    => "bethesda-plugins";
    public string TabLabel => "PLUGINS";

    public PluginsInspectorTab(PluginsTabViewModel vm) => _vm = vm;

    public bool IsVisible(IModInfo? selectedMod) => true;

    public object CreateView(IModInfo? mod)
    {
        _cachedControl ??= new PluginsTabControl(_vm);
        return _cachedControl;
    }
}
