using System.Threading.Tasks;

namespace CatModManager.PluginSdk;

/// <summary>
/// A plugin-contributed action that appears in the right-click context menu on the mod list.
/// </summary>
public interface IModContextAction
{
    /// <summary>Menu item label, e.g. "Check for Updates (Nexus)".</summary>
    string Label { get; }

    /// <summary>Returns true when this action is applicable to the given mod (controls visibility).</summary>
    bool IsVisible(IModInfo? mod);

    /// <summary>
    /// Executes the action for the given mod. Called on the UI thread.
    /// Returns an optional status message to display in the app's status bar (null = no message).
    /// </summary>
    Task<string?> ExecuteAsync(IModInfo mod);
}
