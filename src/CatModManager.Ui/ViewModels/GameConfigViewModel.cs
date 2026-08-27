using System;
using System.Collections.Generic;
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
    private readonly ILogService           _logService;

    // Callback wired by MainWindowViewModel so config changes trigger a profile save.
    public Action? AutoSave { get; set; }

    [ObservableProperty] private string? _modsFolderPath;
    [ObservableProperty] private string? _baseFolderPath;
    [ObservableProperty] private string? _gameExecutablePath;
    [ObservableProperty] private string? _launchArguments;
    [ObservableProperty] private string? _downloadsFolderPath;
    [ObservableProperty] private IGameSupport _activeGameSupport;

    private int _detectionSuppressCount;

    /// <summary>User-defined mount points (editable; persisted in profile).</summary>
    public ObservableCollection<MountPointDef> UserMountPoints { get; } = new();

    /// <summary>
    /// Combined view: game-defined (read-only) + user-defined mount points.
    /// Game-defined entries are marked <see cref="MountPointDef.IsGameDefined"/> = true.
    /// </summary>
    public IReadOnlyList<MountPointDef> EffectiveMountPoints
    {
        get
        {
            var result = new List<MountPointDef>();

            // First game-defined additional mount points.
            // If a UserMountPoint overrides one (same Id), the user version wins.
            foreach (var mp in ActiveGameSupport?.GameDefinedMountPoints ?? [])
            {
                var userOverride = UserMountPoints.FirstOrDefault(u =>
                    string.Equals(u.Id, mp.Id, StringComparison.OrdinalIgnoreCase));
                result.Add(userOverride ?? mp);
            }

            // Then purely user-defined mount points (IDs not already covered).
            foreach (var mp in UserMountPoints)
                if (!result.Any(e => string.Equals(e.Id, mp.Id, StringComparison.OrdinalIgnoreCase)))
                    result.Add(mp);

            return result;
        }
    }

    /// <summary>
    /// Game-defined mount points from the active game support (read-only; game TOML-defined).
    /// </summary>
    public IReadOnlyList<MountPointDef> GameDefinedMountPoints
        => ActiveGameSupport?.GameDefinedMountPoints ?? [];

    /// <summary>
    /// Game-defined mount points with Path resolved to absolute for display purposes.
    /// </summary>
    public IReadOnlyList<MountPointDef> ResolvedGameDefinedMountPoints
    {
        get
        {
            return (ActiveGameSupport?.GameDefinedMountPoints ?? [])
                .Select(mp =>
                {
                    // Use user override path if one exists for this id.
                    var userOverride = UserMountPoints.FirstOrDefault(u =>
                        string.Equals(u.Id, mp.Id, StringComparison.OrdinalIgnoreCase));
                    var rawPath = userOverride?.Path ?? mp.Path ?? "";

                    var abs = MountPointDef.Resolve(rawPath, BaseFolderPath);
                    return new MountPointDef(mp.Id, mp.Name, abs) { IsGameDefined = true };
                })
                .ToList();
        }
    }

    public ObservableCollection<IGameSupport> AvailableGameSupports { get; } = new();

    public GameConfigViewModel(
        IGameSupportService   gameSupportService,
        IGameDiscoveryService gameDiscoveryService,
        ILogService           logService)
    {
        _gameSupportService   = gameSupportService;
        _gameDiscoveryService = gameDiscoveryService;
        _logService           = logService;

        _activeGameSupport = _gameSupportService.Default;
    }

    public void Initialize()
    {
        RefreshGameSupports();
    }

    public void RefreshGameSupports()
    {
        _gameSupportService.RefreshSupports();
        AvailableGameSupports.Clear();
        foreach (var s in _gameSupportService.GetAllSupports())
            AvailableGameSupports.Add(s);
    }

    public IDisposable SuppressDetection()
    {
        _detectionSuppressCount++;
        return new DetectionSuppressor(this);
    }

    private void EndSuppress() => _detectionSuppressCount = Math.Max(0, _detectionSuppressCount - 1);

    partial void OnGameExecutablePathChanged(string? value) { AutoSave?.Invoke(); DetectSupport(value); }
    partial void OnModsFolderPathChanged(string? value)     => AutoSave?.Invoke();
    partial void OnDownloadsFolderPathChanged(string? value) => AutoSave?.Invoke();
    partial void OnBaseFolderPathChanged(string? value)     { AutoSave?.Invoke(); OnPropertyChanged(nameof(ResolvedGameDefinedMountPoints)); }
    partial void OnLaunchArgumentsChanged(string? value)    => AutoSave?.Invoke();
    partial void OnActiveGameSupportChanged(IGameSupport value) { AutoSave?.Invoke(); OnPropertyChanged(nameof(EffectiveMountPoints)); OnPropertyChanged(nameof(GameDefinedMountPoints)); OnPropertyChanged(nameof(ResolvedGameDefinedMountPoints)); }

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

        // Temporarily disable AutoSave during batch updates to avoid redundant IO.
        var savedAutoSave = AutoSave;
        AutoSave = null;
        GameExecutablePath  = result.ExecutablePath;
        BaseFolderPath      = result.GameFolder;
        ModsFolderPath      = Path.Combine(result.GameFolder, "cmm", "mods");
        DownloadsFolderPath = Path.Combine(result.GameFolder, "cmm", "downloads");
        ActiveGameSupport   = mode;
        AutoSave = savedAutoSave;
        AutoSave?.Invoke();

        _logService.Log($"Game auto-detected: {result.DisplayName} [{result.StoreName}]");
    }

    public void DetectSupport(string? value)
    {
        if (_detectionSuppressCount > 0) return;

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
            }
        }
        if (string.IsNullOrEmpty(BaseFolderPath) && !string.IsNullOrEmpty(value))
            BaseFolderPath = Path.GetDirectoryName(value);
        if (!string.IsNullOrEmpty(BaseFolderPath))
        {
            if (string.IsNullOrEmpty(ModsFolderPath))
                ModsFolderPath = Path.Combine(BaseFolderPath, "cmm", "mods");
            if (string.IsNullOrEmpty(DownloadsFolderPath))
                DownloadsFolderPath = Path.Combine(BaseFolderPath, "cmm", "downloads");
        }
    }

    /// <summary>Notifies bindings that EffectiveMountPoints has changed. Called from code-behind after editing a mount point in-place.</summary>
    public void NotifyMountPointsChanged()
    {
        OnPropertyChanged(nameof(EffectiveMountPoints));
        OnPropertyChanged(nameof(ResolvedGameDefinedMountPoints));
    }

    /// <summary>
    /// Saves a user path override for a game-defined mount point (same Id).
    /// Creates a new UserMountPoint entry if none exists yet, updates path if it does.
    /// </summary>
    public void OverrideGameDefinedMountPointPath(string id, string name, string newPath)
    {
        var existing = UserMountPoints.FirstOrDefault(u =>
            string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.Path = newPath;
        else
            UserMountPoints.Add(new MountPointDef(id, name, newPath) { IsGameDefined = true });
        AutoSave?.Invoke();
        NotifyMountPointsChanged();
    }

    /// <summary>Adds a new user-defined mount point. Called from code-behind after dialog.</summary>
    public void AddUserMountPoint(string name, string path)
    {
        var id = name.ToLowerInvariant().Replace(' ', '_');
        if (UserMountPoints.Any(m => m.Id == id) || (ActiveGameSupport?.GameDefinedMountPoints?.Any(m => m.Id == id) ?? false))
            id += "_" + UserMountPoints.Count;
        UserMountPoints.Add(new MountPointDef(id, name, path));
        AutoSave?.Invoke();
        OnPropertyChanged(nameof(EffectiveMountPoints));
    }

    private class DetectionSuppressor(GameConfigViewModel vm) : IDisposable
    {
        public void Dispose() => vm.EndSuppress();
    }
}
