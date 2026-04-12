using System.Collections.ObjectModel;
using System.Diagnostics;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;
using CmmPlugin.BethesdaTools.Services;

namespace CmmPlugin.BethesdaTools.Tabs;

public class PluginsTabViewModel
{
    private readonly LoadOrderService _loadOrder;
    private readonly IModManagerState _state;
    private readonly IPluginLogger _log;

    private string? _pluginsTextPath;
    private bool    _usesStarFormat;

    public ObservableCollection<EspEntry> Entries => _loadOrder.Entries;
    public string Status => $"{Entries.Count(e => e.IsEnabled)}/{Entries.Count} plugins active";

    public PluginsTabViewModel(LoadOrderService loadOrder, IModManagerState state, IPluginLogger log)
    {
        _loadOrder = loadOrder;
        _state = state;
        _log = log;
    }

    public void Refresh()
    {
        var game = BethesdaDetector.Detect(_state.GameExecutablePath);
        if (game != null)
        {
            _pluginsTextPath = BethesdaDetector.GetPluginsTextPath(game);
            _usesStarFormat  = game.UsesStarFormat;
        }
        _loadOrder.Refresh(_state.DataFolderPath, _pluginsTextPath, _state.ActiveMods);
    }

    public void Save()
    {
        // Re-detect in case state was updated since last Refresh.
        var game = BethesdaDetector.Detect(_state.GameExecutablePath);
        if (game != null)
        {
            _pluginsTextPath = BethesdaDetector.GetPluginsTextPath(game);
            _usesStarFormat  = game.UsesStarFormat;
        }
        if (string.IsNullOrEmpty(_pluginsTextPath))
        {
            _log.Log("[BethesdaTools] Cannot save: plugins.txt location is unknown for this game.");
            return;
        }
        _loadOrder.Save(_pluginsTextPath, _usesStarFormat);
    }

    public void MoveUp(EspEntry entry)
    {
        int index = Entries.IndexOf(entry);
        if (index <= 0) return;
        Entries.Move(index, index - 1);
        _loadOrder.RecalculateOrder();
    }

    public void MoveDown(EspEntry entry)
    {
        int index = Entries.IndexOf(entry);
        if (index < 0 || index >= Entries.Count - 1) return;
        Entries.Move(index, index + 1);
        _loadOrder.RecalculateOrder();
    }

    public void SortMastersFirst()
    {
        var masters = Entries.Where(e => e.IsMaster).ToList();
        var plugins = Entries.Where(e => !e.IsMaster).ToList();
        Entries.Clear();
        int i = 0;
        foreach (var e in masters.Concat(plugins))
        {
            e.LoadOrder = i++;
            Entries.Add(e);
        }
    }

    public void OpenLoot()
    {
        // LOOT is a standalone tool — it detects the game itself, so no game detection check needed here.
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(localApp, "LOOT", "LOOT.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LOOT", "LOOT.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LOOT", "LOOT.exe"),
        ];

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return; }
            catch (Exception ex) { _log.Log($"[Bethesda] Failed to launch LOOT at '{path}': {ex.Message}"); return; }
        }
        _log.Log("[Bethesda] LOOT.exe not found in common locations. Install LOOT or add it to your External Tools.");
    }

    public void ImportLootOrder()
    {
        var game = BethesdaDetector.Detect(_state.GameExecutablePath);
        if (game == null) { _log.Log("[Bethesda] Cannot import LOOT order: no Bethesda game detected."); return; }

        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string loadOrderPath = Path.Combine(localApp, game.LocalAppDataFolder, "loadorder.txt");
        if (!File.Exists(loadOrderPath))
        {
            _log.Log($"[Bethesda] loadorder.txt not found at: {loadOrderPath}");
            return;
        }

        var lines = File.ReadAllLines(loadOrderPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] != '#')
            .ToList();

        var ordered = new List<EspEntry>();
        int i = 0;
        foreach (var name in lines)
        {
            var entry = Entries.FirstOrDefault(e =>
                string.Equals(e.FileName, name, StringComparison.OrdinalIgnoreCase));
            if (entry != null) { entry.LoadOrder = i++; ordered.Add(entry); }
        }
        foreach (var entry in Entries.Where(e => !ordered.Contains(e)))
        {
            entry.LoadOrder = i++;
            ordered.Add(entry);
        }

        Entries.Clear();
        foreach (var entry in ordered) Entries.Add(entry);
        _log.Log($"[Bethesda] Imported LOOT load order: {lines.Count} entries.");
        Save(); // persist the reordered load order immediately
    }
}
