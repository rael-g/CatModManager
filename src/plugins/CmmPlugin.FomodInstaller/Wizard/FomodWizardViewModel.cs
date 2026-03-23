using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CmmPlugin.FomodInstaller.Models;

namespace CmmPlugin.FomodInstaller.Wizard;

/// <summary>
/// State machine for the FOMOD installation wizard.
/// One step at a time, supports Single/Multi-select groups.
/// </summary>
public class FomodWizardViewModel
{
    private readonly FomodModuleConfig _config;
    private int _currentStepIndex;

    public string ModuleName => _config.ModuleName;
    public int TotalSteps => _config.InstallSteps.Count;
    public int CurrentStepNumber => _currentStepIndex + 1;

    public FomodInstallStep? CurrentStep =>
        _currentStepIndex >= 0 && _currentStepIndex < TotalSteps
            ? _config.InstallSteps[_currentStepIndex]
            : null;

    /// <summary>Selection state per group: groupName -> set of selected plugin names.</summary>
    public Dictionary<string, HashSet<string>> Selections { get; } = new();

    public bool CanGoBack => _currentStepIndex > 0;
    public bool CanGoNext => _currentStepIndex < TotalSteps - 1;
    public bool IsLastStep => _currentStepIndex == TotalSteps - 1;

    public FomodWizardViewModel(FomodModuleConfig config)
    {
        _config = config;
        _currentStepIndex = 0;
        ApplyDefaults();
    }

    private void ApplyDefaults()
    {
        foreach (var step in _config.InstallSteps)
        {
            foreach (var group in step.Groups)
            {
                var key = GroupKey(step, group);
                var defaultSet = new HashSet<string>(
                    group.Plugins.Where(p => p.IsDefault || group.Type == GroupType.SelectAll)
                                 .Select(p => p.Name));

                // SelectExactlyOne with no default → select first
                if (group.Type == GroupType.SelectExactlyOne && defaultSet.Count == 0 && group.Plugins.Count > 0)
                    defaultSet.Add(group.Plugins[0].Name);

                Selections[key] = defaultSet;
            }
        }
    }

    public void GoNext() { if (CanGoNext) _currentStepIndex++; }
    public void GoBack() { if (CanGoBack) _currentStepIndex--; }

    public HashSet<string> GetSelection(FomodInstallStep step, FomodGroup group)
    {
        var key = GroupKey(step, group);
        if (!Selections.TryGetValue(key, out var set))
        {
            set = new HashSet<string>();
            Selections[key] = set;
        }
        return set;
    }

    public void TogglePlugin(FomodInstallStep step, FomodGroup group, FomodPlugin plugin)
    {
        var set = GetSelection(step, group);
        switch (group.Type)
        {
            case GroupType.SelectExactlyOne:
            case GroupType.SelectAtMostOne:
                // Single-select: replace
                set.Clear();
                set.Add(plugin.Name);
                break;
            case GroupType.SelectAll:
                break; // cannot change
            default:
                // Multi-select: toggle
                if (!set.Remove(plugin.Name))
                    set.Add(plugin.Name);
                break;
        }
    }

    /// <summary>
    /// Builds the final file mapping from all user selections + required files.
    /// Returns: virtualDestPath -> archiveSourcePath
    /// </summary>
    public Dictionary<string, string> BuildFileMapping()
    {
        var mapping = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // Required files (always installed)
        foreach (var f in _config.RequiredInstallFiles)
            AddFilesToMapping(mapping, f);

        // Selected options
        foreach (var step in _config.InstallSteps)
        {
            foreach (var group in step.Groups)
            {
                var selected = GetSelection(step, group);
                foreach (var plugin in group.Plugins.Where(p => selected.Contains(p.Name)))
                    foreach (var f in plugin.Files)
                        AddFilesToMapping(mapping, f);
            }
        }

        return mapping;
    }

    private static void AddFilesToMapping(Dictionary<string, string> mapping, FomodInstallFile file)
    {
        // The destination key is the virtual path; source is the archive-relative path.
        // Folder entries use the destination as a prefix: actual file mapping is resolved during extraction.
        string dest = string.IsNullOrEmpty(file.Destination) ? file.Source : file.Destination;
        mapping[dest] = file.Source;
    }

    private static string GroupKey(FomodInstallStep step, FomodGroup group) =>
        $"{step.Name}::{group.Name}";
}
