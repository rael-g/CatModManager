namespace CatModManager.PluginSdk;

/// <summary>Adds a tab to the inspector panel for the selected mod.</summary>
public interface IInspectorTab
{
    string TabId    { get; }
    string TabLabel { get; }

    /// <summary>Whether this tab should be visible for the given mod selection.</summary>
    bool IsVisible(IModInfo? selectedMod);

    /// <summary>
    /// Creates (or returns a cached) view for this tab's content.
    /// The host casts the returned object to Avalonia.Controls.Control.
    /// Plugins that create UI tabs still reference Avalonia — the SDK itself does not.
    /// </summary>
    object CreateView(IModInfo? mod);
}
