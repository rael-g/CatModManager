using Avalonia.Controls;
using CatModManager.Core.Models;

namespace CatModManager.PluginSdk;

/// <summary>Adds a tab to the inspector panel for the selected mod.</summary>
public interface IInspectorTab
{
    string TabId { get; }
    string TabLabel { get; }

    /// <summary>Whether this tab should be visible for the given mod selection.</summary>
    bool IsVisible(Mod? selectedMod);

    /// <summary>Creates (or returns a cached) Avalonia Control for this tab's content.</summary>
    Control CreateView(Mod? mod);
}
