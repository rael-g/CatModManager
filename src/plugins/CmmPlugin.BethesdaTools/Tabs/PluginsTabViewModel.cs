using System.Collections.ObjectModel;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;
using CmmPlugin.BethesdaTools.Services;

namespace CmmPlugin.BethesdaTools.Tabs;

public class PluginsTabViewModel
{
    private readonly LoadOrderService _loadOrder;
    private readonly IModManagerState _state;
    private readonly IPluginLogger _log;

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
        string? pluginsTextPath = game != null ? BethesdaDetector.GetPluginsTextPath(game) : null;
        _loadOrder.Refresh(_state.DataFolderPath, pluginsTextPath, _state.ActiveMods);
    }

    public void Save()
    {
        var game = BethesdaDetector.Detect(_state.GameExecutablePath);
        if (game == null)
        {
            _log.Log("[BethesdaTools] No Bethesda game detected — plugins.txt not written.");
            return;
        }
        string pluginsTextPath = BethesdaDetector.GetPluginsTextPath(game);
        _loadOrder.Save(pluginsTextPath, game.UsesStarFormat);
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
}
