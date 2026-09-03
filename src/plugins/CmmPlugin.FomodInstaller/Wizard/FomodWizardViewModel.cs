using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CatModManager.PluginSdk;
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

    /// <summary>
    /// Steps the current choices actually lead through, in order.
    ///
    /// A step's <c>visible</c> condition tests flags that earlier steps set, so this is evaluated
    /// forwards: a step is reachable only if the flags accumulated from the reachable steps before
    /// it satisfy its condition — and only a reachable step contributes its own flags. That is what
    /// makes one option skip the thirty steps after it, and what makes un-choosing it bring them
    /// back. Every step was previously shown unconditionally.
    /// </summary>
    public IReadOnlyList<int> VisibleStepIndices
    {
        get
        {
            var visible = new List<int>();
            var flags   = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _config.InstallSteps.Count; i++)
            {
                var step = _config.InstallSteps[i];
                if (step.VisibleWhen != null && !step.VisibleWhen.IsSatisfiedBy(flags)) continue;

                visible.Add(i);

                foreach (var group in step.Groups)
                {
                    var selected = GetSelection(step, group);
                    foreach (var plugin in group.Plugins.Where(p => selected.Contains(p.Name)))
                        foreach (var flag in plugin.ConditionalFlags)
                            flags[flag.Flag] = flag.Value;
                }
            }

            return visible;
        }
    }

    public int TotalSteps => VisibleStepIndices.Count;

    /// <summary>Position among the visible steps, which is the only numbering the user can see.</summary>
    public int CurrentStepNumber => VisibleStepIndices.ToList().IndexOf(_currentStepIndex) + 1;

    public FomodInstallStep? CurrentStep =>
        _currentStepIndex >= 0 && _currentStepIndex < _config.InstallSteps.Count
            ? _config.InstallSteps[_currentStepIndex]
            : null;

    /// <summary>Selection state per group: groupName -> set of selected plugin names.</summary>
    public Dictionary<string, HashSet<string>> Selections { get; } = new();

    public bool CanGoBack => VisibleStepIndices.Any(i => i < _currentStepIndex);
    public bool CanGoNext => VisibleStepIndices.Any(i => i > _currentStepIndex);
    public bool IsLastStep => !CanGoNext;

    public FomodWizardViewModel(FomodModuleConfig config)
    {
        _config = config;
        ApplyDefaults();

        // The first step is not necessarily step 0: a config may gate even that one behind a flag.
        _currentStepIndex = VisibleStepIndices.FirstOrDefault();
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

                // SelectExactlyOne / SelectAtLeastOne with no default → select first
                if (defaultSet.Count == 0 && group.Plugins.Count > 0 &&
                    group.Type is GroupType.SelectExactlyOne or GroupType.SelectAtLeastOne)
                    defaultSet.Add(group.Plugins[0].Name);

                Selections[key] = defaultSet;
            }
        }
    }

    // Move to the next/previous *reachable* step, not the next index: the skipped ones are exactly
    // what the flags just ruled out.
    public void GoNext()
    {
        foreach (var i in VisibleStepIndices)
            if (i > _currentStepIndex) { _currentStepIndex = i; return; }
    }

    public void GoBack()
    {
        int? previous = null;
        foreach (var i in VisibleStepIndices)
        {
            if (i >= _currentStepIndex) break;
            previous = i;
        }
        if (previous.HasValue) _currentStepIndex = previous.Value;
    }

    /// <summary>
    /// Overrides default selections with choices from a collection preset.
    /// Matches by group name (case-insensitive); unmatched groups keep their defaults.
    /// </summary>
    public void ApplyPreset(FomodPreset preset)
    {
        var byGroupName = preset.Groups.ToDictionary(g => g.GroupName, System.StringComparer.OrdinalIgnoreCase);

        foreach (var step in _config.InstallSteps)
        {
            foreach (var group in step.Groups)
            {
                if (!byGroupName.TryGetValue(group.Name, out var pg)) continue;

                var key = GroupKey(step, group);
                var set = new HashSet<string>();

                if (pg.SelectedNames.Count > 0)
                {
                    foreach (var name in pg.SelectedNames)
                        set.Add(name);
                }
                else
                {
                    foreach (var idx in pg.SelectedIndices.Where(i => i >= 0 && i < group.Plugins.Count))
                        set.Add(group.Plugins[idx].Name);
                }

                if (set.Count > 0)
                    Selections[key] = set;
            }
        }
    }

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

        // Selected options — from the steps the user actually walked through. A selection left
        // behind in a step that later became unreachable is not a choice the user stands by.
        var flags = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var i in VisibleStepIndices)
        {
            var step = _config.InstallSteps[i];
            foreach (var group in step.Groups)
            {
                var selected = GetSelection(step, group);
                foreach (var plugin in group.Plugins.Where(p => selected.Contains(p.Name)))
                {
                    foreach (var f in plugin.Files)
                        AddFilesToMapping(mapping, f);
                    foreach (var flag in plugin.ConditionalFlags)
                        flags[flag.Flag] = flag.Value;
                }
            }
        }

        // Conditional installs, now that the final flag state is known.
        foreach (var conditional in _config.ConditionalInstalls)
        {
            if (conditional.When != null && !conditional.When.IsSatisfiedBy(flags)) continue;
            foreach (var f in conditional.Files)
                AddFilesToMapping(mapping, f);
        }

        return mapping;
    }

    private static void AddFilesToMapping(Dictionary<string, string> mapping, FomodInstallFile file)
    {
        // Key = archive-relative source path (unique per entry).
        // Value = destination path relative to mod root ("" means install to mod root).
        // Keying by source prevents multiple entries with dest="" from overwriting each other.
        string source = file.Source ?? "";
        string dest   = file.Destination ?? "";
        mapping[source] = dest;
    }

    /// <summary>
    /// Identity of a group, by position rather than by name.
    ///
    /// The step's name is optional in the FOMOD format, and authoring tools leave it blank freely:
    /// "My Little Nanako 3" (Fallout 4, Nexus 49813) has 43 steps all named "". Keying on
    /// "{step.Name}::{group.Name}" then collapsed those 43 groups onto 14 keys — one key,
    /// '::Eyelashes - Below', was shared by 14 different steps. Every step sharing a key shared its
    /// selection state and, because the same string is the RadioButton GroupName, its radio group
    /// too: choosing in one step cleared the others, and the install silently applied a single
    /// choice where fourteen were meant. Position is the only identity the format guarantees.
    /// </summary>
    public string GroupKey(FomodInstallStep step, FomodGroup group)
    {
        int stepIdx  = _config.InstallSteps.IndexOf(step);
        int groupIdx = stepIdx >= 0 ? _config.InstallSteps[stepIdx].Groups.IndexOf(group) : -1;
        return $"{stepIdx}::{groupIdx}::{group.Name}";
    }
}

