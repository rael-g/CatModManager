using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;
using CmmPlugin.BethesdaTools.Services;

namespace CmmPlugin.BethesdaTools.Tabs;

public partial class PluginsTabViewModel : ObservableObject
{
    private readonly LoadOrderService _loadOrder;
    private readonly IModManagerState _state;
    private readonly IPluginLogger    _log;
    private readonly BethesdaDetector  _detector;

    private string? _pluginsTextPath;

    public ObservableCollection<EspEntry> Entries => _loadOrder.Entries;

    [ObservableProperty]
    private string _status = "Select a Bethesda game to manage load order.";

    public PluginsTabViewModel(LoadOrderService loadOrder, IModManagerState state, IPluginLogger log, BethesdaDetector detector)
    {
        _loadOrder = loadOrder;
        _state     = state;
        _log       = log;
        _detector  = detector;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        var game = _detector.Detect(_state.GameExecutablePath);
        if (game == null)
        {
            _pluginsTextPath = null;
            Entries.Clear();
            Status = "Game not recognized as a supported Bethesda game.";
            return;
        }

        _pluginsTextPath = BethesdaDetector.GetPluginsTextPath(game);
        string? dataDir = !string.IsNullOrEmpty(_state.GameExecutablePath) 
            ? Path.Combine(Path.GetDirectoryName(_state.GameExecutablePath)!, "Data") 
            : null;

        _loadOrder.Refresh(dataDir, _pluginsTextPath, _state.ActiveMods);
        Status = $"Load order for {game.LocalAppDataFolder} refreshed ({Entries.Count} plugins).";
    }

    [RelayCommand]
    public void Save()
    {
        if (string.IsNullOrEmpty(_pluginsTextPath)) return;
        
        var game = _detector.Detect(_state.GameExecutablePath);
        if (game == null) return;

        _loadOrder.Save(_pluginsTextPath, game.UsesStarFormat);
        Status = "Load order saved to plugins.txt.";
    }

    [RelayCommand]
    public void MoveUp(EspEntry? entry)
    {
        if (entry == null) return;
        int idx = Entries.IndexOf(entry);
        if (idx <= 0) return;

        Entries.Move(idx, idx - 1);
        _loadOrder.RecalculateOrder();
    }

    [RelayCommand]
    public void MoveDown(EspEntry? entry)
    {
        if (entry == null) return;
        int idx = Entries.IndexOf(entry);
        if (idx < 0 || idx >= Entries.Count - 1) return;

        Entries.Move(idx, idx + 1);
        _loadOrder.RecalculateOrder();
    }

    [RelayCommand]
    public void SortMastersFirst()
    {
        var masters = Entries.Where(e => e.FileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)).ToList();
        var others  = Entries.Where(e => !e.FileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)).ToList();
        
        Entries.Clear();
        foreach (var m in masters) Entries.Add(m);
        foreach (var o in others)  Entries.Add(o);
        _loadOrder.RecalculateOrder();
        Status = "Masters moved to the top.";
    }

    [RelayCommand]
    public void OpenLoot()
    {
        // Placeholder or simple execution if path found
        Status = "LOOT execution not implemented yet.";
    }

    [RelayCommand]
    public void ImportLootOrder()
    {
        Status = "LOOT import not implemented yet.";
    }
}
