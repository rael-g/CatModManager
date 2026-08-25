using System;
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
    private readonly BethesdaDetector _detector;
    private readonly GamePathResolver _paths;

    private string? _pluginsTextPath;

    public ObservableCollection<EspEntry> Entries => _loadOrder.Entries;

    [ObservableProperty]
    private string _status = "Select a Bethesda game to manage load order.";

    /// <summary>True when a supported Bethesda game is active and its plugins.txt was located.</summary>
    [ObservableProperty]
    private bool _canEdit;

    public PluginsTabViewModel(LoadOrderService loadOrder, IModManagerState state, IPluginLogger log,
                               BethesdaDetector detector, GamePathResolver paths)
    {
        _loadOrder = loadOrder;
        _state     = state;
        _log       = log;
        _detector  = detector;
        _paths     = paths;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        var game = _detector.Detect(_state.GameExecutablePath);
        if (game == null)
        {
            _pluginsTextPath = null;
            CanEdit = false;
            Entries.Clear();
            Status = string.IsNullOrEmpty(_state.GameExecutablePath)
                ? "No game executable configured."
                : "Game not recognized as a supported Bethesda game.";
            return;
        }

        _pluginsTextPath = _paths.GetPluginsTextPath(game, _state.GameExecutablePath);
        if (_pluginsTextPath == null)
        {
            CanEdit = false;
            Entries.Clear();
            Status = $"{game.GameFolder}: could not locate the game's Wine/Proton prefix. " +
                      "Launch the game once through Steam, then refresh.";
            return;
        }

        _loadOrder.Refresh(GamePathResolver.GetDataFolder(_state.DataFolderPath, _state.GameExecutablePath),
                           _pluginsTextPath, _state.ActiveMods, game);
        CanEdit = true;
        Status = $"{game.GameFolder}: {Entries.Count} plugins ({Entries.Count(e => e.IsEnabled)} enabled).";
    }

    [RelayCommand]
    public void Save()
    {
        if (string.IsNullOrEmpty(_pluginsTextPath)) return;

        var game = _detector.Detect(_state.GameExecutablePath);
        if (game == null) return;

        _loadOrder.Save(_pluginsTextPath, game.UsesStarFormat);
        Status = $"Saved {Entries.Count(e => e.IsEnabled)} enabled plugins to {Path.GetFileName(_pluginsTextPath)}.";
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
}
