using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;

namespace CatModManager.Ui.ViewModels;

public partial class GameConfigViewModel : ViewModelBase
{
    private readonly IGameSupportService   _gameSupportService;
    private readonly IGameDiscoveryService _gameDiscoveryService;
    private readonly IDriverService        _driverService;
    private readonly ILogService           _logService;

    // Callback wired by MainWindowViewModel so config changes trigger a profile save.
    public Action? AutoSave { get; set; }

    [ObservableProperty] private string? _modsFolderPath;
    [ObservableProperty] private string? _baseFolderPath;
    [ObservableProperty] private string? _gameExecutablePath;
    [ObservableProperty] private string? _launchArguments;
    [ObservableProperty] private string? _dataSubFolder;
    [ObservableProperty] private string? _downloadsFolderPath;
    [ObservableProperty] private bool _isDriverMissing;
    [ObservableProperty] private IGameSupport _activeGameSupport;

    public ObservableCollection<IGameSupport> AvailableGameSupports { get; } = new();

    public GameConfigViewModel(
        IGameSupportService   gameSupportService,
        IGameDiscoveryService gameDiscoveryService,
        IDriverService        driverService,
        ILogService           logService)
    {
        _gameSupportService   = gameSupportService;
        _gameDiscoveryService = gameDiscoveryService;
        _driverService        = driverService;
        _logService           = logService;

        _activeGameSupport = _gameSupportService.Default;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public void Initialize()
    {
        CheckDriverStatus();
        RefreshGameSupports();
    }

    public void CheckDriverStatus()
    {
        IsDriverMissing = !_driverService.IsDriverInstalled();
        if (IsDriverMissing) _logService.Log("WARNING: File system driver not available.");
    }

    public void RefreshGameSupports()
    {
        _gameSupportService.RefreshSupports();
        AvailableGameSupports.Clear();
        foreach (var s in _gameSupportService.GetAllSupports())
            AvailableGameSupports.Add(s);
    }

    // ── Property change handlers ──────────────────────────────────────────────

    partial void OnGameExecutablePathChanged(string? value) { AutoSave?.Invoke(); DetectSupport(value); }
    partial void OnModsFolderPathChanged(string? value)     => AutoSave?.Invoke();
    partial void OnDataSubFolderChanged(string? value)      => AutoSave?.Invoke();
    partial void OnDownloadsFolderPathChanged(string? value) => AutoSave?.Invoke();
    partial void OnBaseFolderPathChanged(string? value)     => AutoSave?.Invoke();
    partial void OnLaunchArgumentsChanged(string? value)    => AutoSave?.Invoke();
    partial void OnActiveGameSupportChanged(IGameSupport value) => AutoSave?.Invoke();

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void DetectGameSupport() => DetectSupport(GameExecutablePath);

    [RelayCommand]
    private async Task AutoDetectGame()
    {
        var dialogVm = new GameDetectionDialogViewModel(_gameDiscoveryService, AvailableGameSupports);
        var dialog   = new CatModManager.Ui.Views.GameDetectionDialog(dialogVm);

        var owner = Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
                    ? dt.MainWindow : null;

        await dialog.ShowDialog(owner!);

        var result = dialogVm.Result;
        if (result == null) return;

        var resultMode = dialogVm.ResultMode ?? _gameSupportService.Default;
        var mode = AvailableGameSupports.Contains(resultMode)
            ? resultMode
            : AvailableGameSupports.FirstOrDefault(s => s.GameId == resultMode.GameId) ?? resultMode;

        // Suppress individual AutoSave calls; caller will save once at end.
        var savedAutoSave = AutoSave;
        AutoSave = null;
        GameExecutablePath  = result.ExecutablePath;
        BaseFolderPath      = result.GameFolder;
        DataSubFolder       = mode.DataSubFolder;
        ModsFolderPath      = Path.Combine(result.GameFolder, "mods");
        DownloadsFolderPath = Path.Combine(result.GameFolder, "downloads");
        ActiveGameSupport   = mode;
        AutoSave = savedAutoSave;
        AutoSave?.Invoke();

        _logService.Log($"Game auto-detected: {result.DisplayName} [{result.StoreName}]");
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    public void DetectSupport(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var detected = _gameSupportService.DetectSupport(value);
            if (detected.GameId != "generic")
            {
                var saved = AutoSave;
                AutoSave = null;
                ActiveGameSupport = detected;
                AutoSave = saved;
                _logService.Log($"Auto-detected Game Support: {detected.DisplayName}");
                if (string.IsNullOrEmpty(DataSubFolder) && !string.IsNullOrEmpty(detected.DataSubFolder))
                    DataSubFolder = detected.DataSubFolder;
            }
        }
        if (string.IsNullOrEmpty(BaseFolderPath) && !string.IsNullOrEmpty(value))
            BaseFolderPath = Path.GetDirectoryName(value);
        if (!string.IsNullOrEmpty(BaseFolderPath))
        {
            if (string.IsNullOrEmpty(ModsFolderPath))
                ModsFolderPath = Path.Combine(BaseFolderPath, "mods");
            if (string.IsNullOrEmpty(DownloadsFolderPath))
                DownloadsFolderPath = Path.Combine(BaseFolderPath, "downloads");
        }
    }
}
