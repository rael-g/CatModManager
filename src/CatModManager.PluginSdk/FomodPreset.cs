using System.Collections.Generic;

namespace CatModManager.PluginSdk;

/// <summary>
/// Pre-selected FOMOD installer choices, typically sourced from a Nexus Collection manifest.
/// Allows the installer to auto-apply selections without showing the wizard UI.
/// </summary>
public class FomodPreset
{
    public List<FomodPresetGroup> Groups { get; set; } = new();
}

/// <summary>Preset selections for one FOMOD group (identified by its name in ModuleConfig.xml).</summary>
public class FomodPresetGroup
{
    public string       GroupName        { get; set; } = string.Empty;
    /// <summary>Plugin names selected within this group (primary match).</summary>
    public List<string> SelectedNames    { get; set; } = new();
    /// <summary>Plugin indices selected within this group (fallback when names don't match).</summary>
    public List<int>    SelectedIndices  { get; set; } = new();
}
