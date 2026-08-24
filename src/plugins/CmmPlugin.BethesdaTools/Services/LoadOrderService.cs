using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CatModManager.PluginSdk;
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

    private readonly IPluginLogger _log;
    private readonly IFileService _fileService;

    public ObservableCollection<EspEntry> Entries { get; } = new();

    public LoadOrderService(IPluginLogger log, IFileService fileService)
    {
        _log = log;
        _fileService = fileService;
    }

    /// <summary>
    /// Rebuilds the load order from disk + active mods.
    /// Order of precedence: existing plugins.txt (preserves user's load order), then new files at end.
    /// </summary>
    public void Refresh(string? dataFolderPath, string? pluginsTextPath, IEnumerable<IModInfo>? activeMods,
                        BethesdaGame? game = null)
    {
        // 1. Collect all plugin files available
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(dataFolderPath))
            foreach (var f in ScanForPlugins(dataFolderPath))
                discovered.Add(f);

        if (activeMods != null)
            foreach (var mod in activeMods.Where(m => m.IsEnabled && _fileService.DirectoryExists(m.ModRootPath)))
                foreach (var f in ScanForPlugins(mod.ModRootPath))
                    discovered.Add(f);

        // The engine always loads base game + official DLC plugins itself. Listing them in
        // plugins.txt is not how the format works and corrupts the order, so drop them here
        // instead of surfacing rows the user can break.
        if (game != null)
            discovered.RemoveWhere(game.IsImplicitMaster);

        // 2. Read existing plugins.txt to get enabled state + order
        var ordered = new List<(string FileName, bool IsEnabled)>();
        if (!string.IsNullOrEmpty(pluginsTextPath) && _fileService.FileExists(pluginsTextPath))
        {
            foreach (var line in _fileService.ReadAllLines(pluginsTextPath))
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
        foreach (var name in discovered.Where(d => !seen.Contains(d)).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            merged.Add((name, true));

        // The engine rejects a load order where a master sorts after a regular plugin, and newly
        // discovered files were just appended at the end — so hoist masters back to the front.
        // OrderBy is stable, which keeps the user's relative order within each group.
        merged = merged.OrderBy(m => IsMaster(m.FileName) ? 0 : 1).ToList();

        // 4. Rebuild observable collection
        Entries.Clear();
        for (int i = 0; i < merged.Count; i++)
            Entries.Add(new EspEntry(merged[i].FileName, merged[i].IsEnabled, i));

        _log.Log($"[BethesdaTools] Load order refreshed: {Entries.Count} plugins found.");
    }

    /// <summary>
    /// Writes the current load order back to plugins.txt.
    /// Skyrim SE / Fallout 4 / Starfield mark enabled entries with a leading '*' and list disabled
    /// ones unprefixed. Older engines (Oblivion, FO3/NV, Skyrim LE) have no disabled representation
    /// at all — the file lists enabled plugins only, so disabled entries are simply omitted.
    /// </summary>
    public void Save(string pluginsTextPath, bool useStarFormat)
    {
        try
        {
            var lines = useStarFormat
                ? Entries.Select(e => e.IsEnabled ? $"*{e.FileName}" : e.FileName)
                : Entries.Where(e => e.IsEnabled).Select(e => e.FileName);

            string? dir = Path.GetDirectoryName(pluginsTextPath);
            if (!string.IsNullOrEmpty(dir))
                _fileService.CreateDirectory(dir);
            
            _fileService.WriteAllLines(pluginsTextPath, lines.ToArray());
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

    private static bool IsMaster(string fileName) =>
        fileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Plugins live at the root of a mod folder (the VFS mounts the mod root as Data/), but plenty
    /// of archives ship them one level down under "Data/" and get installed that way, so check both.
    /// </summary>
    private IEnumerable<string> ScanForPlugins(string folder)
    {
        return ScanFolder(folder).Concat(ScanFolder(Path.Combine(folder, "Data")));
    }

    private IEnumerable<string> ScanFolder(string folder)
    {
        if (!_fileService.DirectoryExists(folder)) return Array.Empty<string>();

        try
        {
            return _fileService.GetFiles(folder, "*")
                .Where(f => _pluginExtensions.Contains(Path.GetExtension(f)))
                .Select(Path.GetFileName)
                .OfType<string>();
        }
        catch { return Array.Empty<string>(); }
    }
}
