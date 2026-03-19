namespace CatModManager.PluginSdk;

/// <summary>
/// A plugin-contributed action that appears as a button in the CMM sidebar footer.
/// </summary>
public interface ISidebarAction
{
    /// <summary>Short button label, e.g. "NEXUS MODS".</summary>
    string Label { get; }

    /// <summary>Unicode icon shown before the label, e.g. "⬡".</summary>
    string Icon { get; }

    void Execute();
}
