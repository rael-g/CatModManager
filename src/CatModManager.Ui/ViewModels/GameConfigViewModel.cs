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

    /// <summary>
    /// Wired by MainWindowViewModel. Everything on this panel describes the installation — the
    /// folders, the executable, the game mode, the launch line, the mount points — so every edit
    /// saves into the game, and none of it into whichever profile happens to be open.
    /// </summary>
    public Action? SaveGame { get; set; }

    [ObservableProperty] private string? _modsFolderPath;
    [ObservableProperty] private string? _baseFolderPath;
    [ObservableProperty] private string? _gameExecutablePath;
    [ObservableProperty] private string? _launchArguments;
    [ObservableProperty] private string? _downloadsFolderPath;
    [ObservableProperty] private IGameSupport _activeGameSupport;

    private int _detectionSuppressCount;
    private int _savingSuppressCount;

    /// <summary>User-defined mount points (editable; stored on the game).</summary>
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

    /// <summary>Exposed so the window can run the same store scan when adding a game.</summary>
    public IGameDiscoveryService GameDiscoveryService => _gameDiscoveryService;

    /// <summary>The game mode an executable points at, or "generic" — which is a fine answer.</summary>
    public string DetectSupportId(string? executablePath)
        => string.IsNullOrEmpty(executablePath)
            ? "generic"
            : _gameSupportService.DetectSupport(executablePath).GameId;

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

    /// <summary>
    /// Stops the field setters from saving while the panel is being filled in from a game.
    ///
    /// Filling it in is a dozen assignments, and each one used to write the whole panel back to the
    /// game — including the fields that had not been assigned yet. Loading a game with a launch line
    /// therefore saved an empty launch line over it before ever reaching that field. Caught on the
    /// developer's own Skyrim, whose "-applaunch 489830" was gone after one start.
    /// </summary>
    public IDisposable SuppressSaving()
    {
        _savingSuppressCount++;
        return new SavingSuppressor(this);
    }

    private void Save()
    {
        if (_savingSuppressCount > 0) return;
        SaveGame?.Invoke();
    }

    private void EndSuppress()     => _detectionSuppressCount = Math.Max(0, _detectionSuppressCount - 1);
    private void EndSaveSuppress() => _savingSuppressCount    = Math.Max(0, _savingSuppressCount - 1);

    partial void OnGameExecutablePathChanged(string? value) { Save(); DetectSupport(value); }
    partial void OnModsFolderPathChanged(string? value)     => Save();
    partial void OnDownloadsFolderPathChanged(string? value) => Save();
    partial void OnBaseFolderPathChanged(string? value)     { Save(); OnPropertyChanged(nameof(ResolvedGameDefinedMountPoints)); }
    partial void OnLaunchArgumentsChanged(string? value)    => Save();
    partial void OnActiveGameSupportChanged(IGameSupport value) { Save(); OnPropertyChanged(nameof(EffectiveMountPoints)); OnPropertyChanged(nameof(GameDefinedMountPoints)); OnPropertyChanged(nameof(ResolvedGameDefinedMountPoints)); }

    [RelayCommand]
    private void DetectGameSupport() => DetectSupport(GameExecutablePath);

    // AutoDetectGame lived here and is gone. It ran the store scan and then overwrote the *open*
    // game's executable, folders and mode in place — a leftover from when a profile was the thing
    // being configured. Detection now produces a new installation, through Game ▸ Add Game ▸ Auto
    // Detect, which is the only reading of "detect a game" that does not quietly repoint one the
    // user already set up.

    public void DetectSupport(string? value)
    {
        if (_detectionSuppressCount > 0) return;

        if (!string.IsNullOrEmpty(value))
        {
            var detected = _gameSupportService.DetectSupport(value);
            if (detected.GameId != "generic")
            {
                var saved = SaveGame;
                SaveGame = null;
                ActiveGameSupport = detected;
                SaveGame = saved;
                _logService.Log($"Auto-detected Game Support: {detected.DisplayName}");
            }
        }
        // Through the shared rule rather than spelled out here, so that the panel and "Add Game…"
        // cannot end up laying the folders out differently.
        var defaults = new Game
        {
            GameExecutablePath  = value               ?? "",
            BaseDataPath        = BaseFolderPath      ?? "",
            ModsFolderPath      = ModsFolderPath      ?? "",
            DownloadsFolderPath = DownloadsFolderPath ?? "",
        };
        GameFolderDefaults.Fill(defaults);

        BaseFolderPath      = defaults.BaseDataPath;
        ModsFolderPath      = defaults.ModsFolderPath;
        DownloadsFolderPath = defaults.DownloadsFolderPath;
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
        Save();
        NotifyMountPointsChanged();
    }

    /// <summary>Adds a new user-defined mount point. Called from code-behind after dialog.</summary>
    public void AddUserMountPoint(string name, string path)
    {
        var id = name.ToLowerInvariant().Replace(' ', '_');
        if (UserMountPoints.Any(m => m.Id == id) || (ActiveGameSupport?.GameDefinedMountPoints?.Any(m => m.Id == id) ?? false))
            id += "_" + UserMountPoints.Count;
        UserMountPoints.Add(new MountPointDef(id, name, path));
        Save();
        OnPropertyChanged(nameof(EffectiveMountPoints));
    }

    private class DetectionSuppressor(GameConfigViewModel vm) : IDisposable
    {
        public void Dispose() => vm.EndSuppress();
    }

    private class SavingSuppressor(GameConfigViewModel vm) : IDisposable
    {
        public void Dispose() => vm.EndSaveSuppress();
    }
}
