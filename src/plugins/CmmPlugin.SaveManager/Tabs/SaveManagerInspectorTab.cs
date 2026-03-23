using CatModManager.PluginSdk;

namespace CmmPlugin.SaveManager.Tabs;

public class SaveManagerInspectorTab : IInspectorTab
{
    private readonly SaveManagerTabViewModel _vm;
    private SaveManagerTabControl?           _cachedControl;

    public string TabId    => "save-manager";
    public string TabLabel => "SAVES";

    public SaveManagerInspectorTab(SaveManagerTabViewModel vm) => _vm = vm;

    /// <summary>Always visible so users can see the status even when no game is detected yet.</summary>
    public bool IsVisible(IModInfo? selectedMod) => true;

    public object CreateView(IModInfo? mod)
    {
        _cachedControl ??= new SaveManagerTabControl(_vm);
        return _cachedControl;
    }
}
