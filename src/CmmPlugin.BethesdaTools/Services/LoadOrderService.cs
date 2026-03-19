using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CmmPlugin.BethesdaTools.Models;

namespace CmmPlugin.BethesdaTools.Services;

/// <summary>
/// Manages the ESP/ESM/ESL load order for Bethesda games.
/// Merges entries from active mods, base game Data folder, and the existing plugins.txt.
/// </summary>
public class LoadOrderService
{
    private static readonly HashSet<string> _pluginExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".esp", ".esm", ".esl" };

    private readonly ILogService _log;

    public ObservableCollection<EspEntry> Entries { get; } = new();

    public LoadOrderService(ILogService log) => _log = log;

    /// <summary>
    /// Rebuilds the load order from disk + active mods.
    /// Order of precedence: existing plugins.txt (preserves user's load order), then new files at end.
    /// </summary>
    public void Refresh(string? dataFolderPath, string? pluginsTextPath, IEnumerable<Mod>? activeMods)
    {
        // 1. Collect all plugin files available
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(dataFolderPath) && Directory.Exists(dataFolderPath))
            foreach (var f in ScanForPlugins(dataFolderPath))
                discovered.Add(f);

        if (activeMods != null)
            foreach (var mod in activeMods.Where(m => m.IsEnabled && Directory.Exists(m.RootPath)))
                foreach (var f in ScanForPlugins(mod.RootPath))
                    discovered.Add(f);

        // 2. Read existing plugins.txt to get enabled state + order
        var ordered = new List<(string FileName, bool IsEnabled)>();
        if (!string.IsNullOrEmpty(pluginsTextPath) && File.Exists(pluginsTextPath))
        {
            foreach (var line in File.ReadAllLines(pluginsTextPath))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

                bool enabled = trimmed.StartsWith('*');
                string name = enabled ? trimmed[1..] : trimmed;

                if (_pluginExtensions.Contains(Path.GetExtension(name)))
                    ordered.Add((name, enabled));
            }
        }

        // 3. Merge: keep existing order from plugins.txt, append newly discovered files
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<(string FileName, bool IsEnabled)>();

        foreach (var (name, enabled) in ordered)
        {
            if (discovered.Contains(name)) // only keep entries that actually exist
            {
                merged.Add((name, enabled));
                seen.Add(name);
            }
        }

        // New files not yet in plugins.txt — enabled by default
        foreach (var name in discovered.Where(d => !seen.Contains(d)))
            merged.Add((name, true));

        // 4. Rebuild observable collection
        Entries.Clear();
        for (int i = 0; i < merged.Count; i++)
            Entries.Add(new EspEntry(merged[i].FileName, merged[i].IsEnabled, i));

        _log.Log($"[BethesdaTools] Load order refreshed: {Entries.Count} plugins found.");
    }

    /// <summary>Writes the current load order back to plugins.txt.</summary>
    public void Save(string pluginsTextPath, bool useStarFormat)
    {
        try
        {
            var lines = Entries.Select(e =>
                useStarFormat
                    ? (e.IsEnabled ? $"*{e.FileName}" : e.FileName)
                    : (e.IsEnabled ? e.FileName : $"#{e.FileName}"));

            Directory.CreateDirectory(Path.GetDirectoryName(pluginsTextPath)!);
            File.WriteAllLines(pluginsTextPath, lines);
            _log.Log($"[BethesdaTools] plugins.txt written: {Entries.Count(e => e.IsEnabled)} active plugins.");
        }
        catch (Exception ex)
        {
            _log.LogError("[BethesdaTools] Failed to write plugins.txt", ex);
        }
    }

    public void RecalculateOrder()
    {
        for (int i = 0; i < Entries.Count; i++)
            Entries[i].LoadOrder = i;
    }

    private static IEnumerable<string> ScanForPlugins(string folder)
    {
        try
        {
            return Directory.GetFiles(folder)
                .Where(f => _pluginExtensions.Contains(Path.GetExtension(f)))
                .Select(Path.GetFileName)
                .OfType<string>();
        }
        catch { return Array.Empty<string>(); }
    }
}
