using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Ui.ViewModels;

public partial class ModInspectorViewModel : ViewModelBase
{
    private readonly ILogService _logService;

    /// <summary>Called by the host to push error messages to the status bar.</summary>
    public Action<string>? SetStatusMessage { get; set; }

    [ObservableProperty] private int _selectedTab = 0;
    [ObservableProperty] private string _currentFolderPath = "";
    [ObservableProperty] private ObservableCollection<ModFileItem> _files = new();

    private Mod? _currentMod;

    public ModInspectorViewModel(ILogService logService) => _logService = logService;

    /// <summary>Called by ModListViewModel (via event) when SelectedMod changes.</summary>
    public void OnModChanged(Mod? mod)
    {
        _currentMod      = mod;
        CurrentFolderPath = "";
        Files.Clear();
        if (mod != null) LoadFiles(mod, "");
    }

    partial void OnSelectedTabChanged(int value)
    {
        if (value == 1 && _currentMod != null && Files.Count == 0)
            LoadFiles(_currentMod, CurrentFolderPath);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand] private void SwitchToInfo()  => SelectedTab = 0;

    [RelayCommand]
    private void SwitchToFiles()
    {
        SelectedTab = 1;
        if (_currentMod != null && Files.Count == 0)
            LoadFiles(_currentMod, CurrentFolderPath);
    }

    [RelayCommand]
    private void NavigateInto(ModFileItem item)
    {
        if (!item.IsDirectory || _currentMod == null) return;
        string rel = string.IsNullOrEmpty(CurrentFolderPath)
            ? item.Name
            : Path.Combine(CurrentFolderPath, item.Name);
        LoadFiles(_currentMod, rel);
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (string.IsNullOrEmpty(CurrentFolderPath) || _currentMod == null) return;
        LoadFiles(_currentMod, Path.GetDirectoryName(CurrentFolderPath) ?? "");
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void LoadFiles(Mod mod, string relativePath)
    {
        try
        {
            CurrentFolderPath = relativePath;
            Files.Clear();
            string fullPath = Path.Combine(mod.RootPath, relativePath);
            if (!Directory.Exists(fullPath)) return;

            if (!string.IsNullOrEmpty(relativePath))
                Files.Add(new ModFileItem("..", true, 0));
            foreach (var d in Directory.GetDirectories(fullPath).OrderBy(x => x))
                Files.Add(new ModFileItem(Path.GetFileName(d), true, 0));
            foreach (var f in Directory.GetFiles(fullPath).OrderBy(x => x))
                Files.Add(new ModFileItem(Path.GetFileName(f), false, new FileInfo(f).Length));
        }
        catch (Exception ex)
        {
            _logService.LogError("Failed to list mod files", ex);
            SetStatusMessage?.Invoke($"ERROR: {ex.Message}");
        }
    }
}
