using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.PluginSdk;
using CatModManager.Ui.Plugins;

namespace CatModManager.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IModScanner              _modScanner;
    private readonly IModManagementService    _modManagementService;
    private readonly IProcessService          _processService;
    private readonly IVfsOrchestrationService _vfsOrchestrator;
    private readonly IGameLaunchService       _gameLauncher;
    private readonly IFileService             _fileService;
    private readonly ICatPathService          _pathService;
    private readonly ILogService              _logService;
    private readonly IConfigService           _configService;
    private readonly AppSessionState          _sessionState;
    private readonly PluginLoader             _pluginLoader;
    private readonly UiExtensionHost?         _uiExtensionHost;
    private readonly PluginBrowserViewModel?  _pluginBrowserVm;

    // Coordinators (specialized logic)
    public ProfileCoordinator         Profiles  { get; }
    public VfsLifecycleCoordinator    Vfs       { get; }
    public ModInstallationCoordinator Installer { get; }

    // Sub-ViewModels
    public ProfileManagerViewModel ProfileManager { get; }
    public GameConfigViewModel      GameConfig     { get; }
    public ModListViewModel         ModList        { get; }
    public ModInspectorViewModel    Inspector      { get; }
    public ExternalToolsViewModel   Tools          { get; }

    public bool HasActiveDownloads => _sessionState.CheckHasActiveDownloads?.Invoke() ?? false;

    public string AppDataPath => _pathService.BaseDataPath;

    [ObservableProperty] private string _statusMessage = "Ready";
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<IInspectorTab>   PluginInspectorTabs   { get; } = new();
    public ObservableCollection<ISidebarAction>  PluginSidebarActions  { get; } = new();
    public IReadOnlyList<IModContextAction>      PluginModContextActions => _uiExtensionHost?.ModContextActions ?? Array.Empty<IModContextAction>();

    public event Action? RequestClearFocus;

    public void NotifySelectedModMountPointChanged() => OnPropertyChanged(nameof(SelectedModMountPointName));

    public MainWindowViewModel(
        IModScanner              modScanner,
        IProfileService          profileService,
        IModManagementService    modManagementService,
        IProcessService          processService,
        IVfsOrchestrationService vfsOrchestrator,
        IGameLaunchService       gameLauncher,
        IFileService             fileService,
        ICatPathService          pathService,
        ILogService              logService,
        IConfigService           configService,
        IGameSupportService      gameSupportService,
        IGameDiscoveryService    gameDiscoveryService,
        AppSessionState          sessionState,
        PluginLoader             pluginLoader,
        UiExtensionHost?         uiExtensionHost = null,
        PluginBrowserViewModel?  pluginBrowserVm = null)
    {
        _modScanner           = modScanner;
        _modManagementService = modManagementService;
        _processService       = processService;
        _vfsOrchestrator      = vfsOrchestrator;
        _gameLauncher         = gameLauncher;
        _fileService          = fileService;
        _pathService          = pathService;
        _logService           = logService;
        _configService        = configService;
        _sessionState         = sessionState;
        _pluginLoader         = pluginLoader;
        _uiExtensionHost      = uiExtensionHost;
        _pluginBrowserVm      = pluginBrowserVm;

        // 1. Initialize Sub-ViewModels
        GameConfig = new GameConfigViewModel(gameSupportService, gameDiscoveryService, logService);
        ModList    = new ModListViewModel();
        Inspector  = new ModInspectorViewModel(logService);
        Tools      = new ExternalToolsViewModel(processService, vfsOrchestrator, logService);

        // 2. Initialize Coordinators
        Profiles  = new ProfileCoordinator(profileService, configService, logService, sessionState, () => GameConfig, () => ModList, RefreshModMountPointDisplayNames, SyncActiveModsToState);
        Vfs       = new VfsLifecycleCoordinator(vfsOrchestrator, logService, () => GameConfig, () => ModList, SyncActiveModsToState);
        Installer = new ModInstallationCoordinator(modManagementService, modScanner, fileService, logService, sessionState, uiExtensionHost, () => GameConfig, () => ModList, (m, s) => { });

        // 3. Wire Events & Callbacks
        Vfs.PropertyChanged       += (s, e) => OnPropertyChanged(e.PropertyName);
        Installer.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
        
        ProfileManager = new ProfileManagerViewModel(profileService, pathService, fileService, configService, logService);
        ProfileManager.BuildSaveData  = () => Profiles.BuildCurrentProfile(ProfileManager.CurrentProfileName ?? "Untitled");
        ProfileManager.IsVfsMounted   = () => Vfs.IsVfsMounted;
        ProfileManager.ProfileLoaded += p => Profiles.ApplyLoadedProfile(p);

        GameConfig.AutoSave = () => ProfileManager.AutoSave();
        GameConfig.Initialize();

        Inspector.SetStatusMessage = msg => StatusMessage = msg;

        ModList.AutoSave         = () => ProfileManager.AutoSave();
        ModList.SuppressAutoSave = () => ProfileManager.SuppressAutoSave();
        ModList.SyncActiveMods   = SyncActiveModsToState;
        ModList.SelectedModChanged += mod => { Inspector.OnModChanged(mod); OnPropertyChanged(nameof(SelectedModMountPointName)); };

        Tools.IsVfsMounted  = () => Vfs.IsVfsMounted;
        Tools.EnsureMounted = async () =>
        {
            if (Vfs.IsVfsMounted) return OperationResult.Success();
            return await Vfs.ToggleMountInternal();
        };
        Tools.AutoSave = () => ProfileManager.AutoSave();

        _sessionState.RequestInstallModAction = (path, _) => 
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Installer.InstallModAtMountPointAsync(path, null));

        _logService.OnLog += AddLog;
        _vfsOrchestrator.RecoverStaleMounts();

        _ = Task.Run(async () => await ProfileManager.LoadInitialProfile(_configService.Current.LastProfileName));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand] private Task ToggleMount() => Vfs.ToggleMount();

    [RelayCommand]
    private async Task LaunchGame()
    {
        if (GameConfig.ActiveGameSupport == null) { StatusMessage = "Select a game first."; return; }
        if (!Vfs.IsVfsMounted)
        {
            var res = await Vfs.ToggleMountInternal();
            if (!res.IsSuccess) { StatusMessage = $"Mount failed: {res.ErrorMessage}"; return; }
        }

        StatusMessage = "Launching game...";
        try
        {
            var activeMods = ModList.AllMods.Where(m => m.IsEnabled && !m.IsBroken && !m.IsInstalling).ToList();
            await _gameLauncher.LaunchGameAsync(GameConfig.GameExecutablePath, GameConfig.LaunchArguments, GameConfig.ActiveGameSupport, activeMods);
            StatusMessage = "Game running.";
        }
        catch (Exception ex) { _logService.LogError("Launch failed", ex); StatusMessage = $"Launch error: {ex.Message}"; }
    }

    [RelayCommand] private async Task AddMod(string? path = null) => await Installer.InstallModAtMountPointAsync(path ?? "", null);
    [RelayCommand] private async Task AddModFromFolder() { /* handled in coordinator-like flow in view if needed, or proxy here */ }

    [RelayCommand]
    private async Task RemoveMod()
    {
        var toRemove = (ModList.SelectedMods is { Count: > 1 }) ? ModList.SelectedMods.ToList() : ModList.SelectedMod != null ? new List<Mod> { ModList.SelectedMod } : new List<Mod>();
        if (toRemove.Count == 0) return;
        try
        {
            using (ProfileManager.SuppressAutoSave())
            {
                foreach (var mod in toRemove)
                {
                    ModList.AllMods.Remove(mod);
                    if (_fileService.DirectoryExists(mod.ModRootPath)) await Task.Run(() => _fileService.DeleteDirectory(mod.ModRootPath, true));
                    else if (_fileService.FileExists(mod.ModRootPath)) await Task.Run(() => _fileService.DeleteFile(mod.ModRootPath));
                }
                ModList.UpdatePriorities();
            }
            ModList.RebuildDisplayedMods();
            ProfileManager.AutoSave();
        }
        catch (Exception ex) { _logService.LogError("Remove error", ex); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    [RelayCommand] private Task Refresh() { ModList.RebuildDisplayedMods(); return Task.CompletedTask; }
    [RelayCommand] private void ClearFocus() => RequestClearFocus?.Invoke();
    [RelayCommand] private void DeleteMountPoint(MountPointDef? mp) { if (mp != null) GameConfig.UserMountPoints.Remove(mp); }
    [RelayCommand] private void ExecuteSidebarAction(ISidebarAction? a) => a?.Execute();

    [RelayCommand] private void OpenPluginBrowser() { }
    [RelayCommand] private async Task OpenModsFolder() => await _processService.OpenFolderAsync(GameConfig.ModsFolderPath ?? "");
    [RelayCommand] private async Task OpenDownloadsFolder() => await _processService.OpenFolderAsync(GameConfig.DownloadsFolderPath ?? "");
    [RelayCommand] private async Task OpenGameFolder() => await _processService.OpenFolderAsync(GameConfig.BaseFolderPath ?? "");
    [RelayCommand] private async Task OpenGameExecutableFolder() =>
        await _processService.OpenFolderAsync(!string.IsNullOrEmpty(GameConfig.GameExecutablePath)
            ? Path.GetDirectoryName(GameConfig.GameExecutablePath) ?? "" : "");
    [RelayCommand] private async Task OpenSelectedModFolder()
    {
        if (ModList.SelectedMod == null) return;
        string path = ModList.SelectedMod.ModRootPath;
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        await _processService.OpenFolderAsync(path);
    }
    [RelayCommand] private void OpenDataSubFolder() => _ = OpenGameDataFolder();
    [RelayCommand] private async Task OpenGameDataFolder()
    {
        var points = GameConfig.EffectiveMountPoints;
        if (points.Count == 0) { await _processService.OpenFolderAsync(GameConfig.BaseFolderPath ?? ""); return; }
        
        var sub = points[0].Path;
        if (string.IsNullOrEmpty(sub)) { await _processService.OpenFolderAsync(GameConfig.BaseFolderPath ?? ""); return; }
        
        var expanded = Environment.ExpandEnvironmentVariables(sub);
        string folder = Path.IsPathRooted(expanded) ? expanded :
            !string.IsNullOrEmpty(GameConfig.BaseFolderPath) ? Path.Combine(GameConfig.BaseFolderPath, expanded) : expanded;
        await _processService.OpenFolderAsync(folder);
    }
    [RelayCommand] private void OpenAppDataFolder() => _processService.OpenFolderAsync(_pathService.BaseDataPath);

    // ── Helpers ───────────────────────────────────────────────────────────────

    public string SelectedModMountPointName => ModList.SelectedMod?.MountPointId ?? "Default";

    public void RefreshModMountPointDisplayNames()
    {
        var points = GameConfig.EffectiveMountPoints;
        foreach (var mod in ModList.AllMods)
        {
            var id = mod.MountPointId;
            mod.MountPointDisplayName = string.IsNullOrEmpty(id) ? null : points.FirstOrDefault(mp => string.Equals(mp.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
        }
    }

    private void SyncActiveModsToState()
    {
        var active = ModList.AllMods.Where(m => m.IsEnabled && !m.IsBroken && !m.IsInstalling).OrderBy(m => m.Priority).ToList();
        _sessionState.ActiveMods = active;
        _sessionState.NexusDomain = GameConfig.ActiveGameSupport?.NexusDomain;
        _sessionState.GameId = GameConfig.ActiveGameSupport?.GameId;
        _sessionState.ModsFolderPath = GameConfig.ModsFolderPath;
        _sessionState.DataFolderPath = GameConfig.BaseFolderPath;
        _sessionState.DownloadsFolderPath = GameConfig.DownloadsFolderPath;
        _sessionState.GameExecutablePath = GameConfig.GameExecutablePath;
        _sessionState.CurrentProfileName = ProfileManager.CurrentProfileName;
    }

    private void AddLog(string msg)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Logs.Insert(0, msg);
            if (Logs.Count > 100) Logs.RemoveAt(100);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                Logs.Insert(0, msg);
                if (Logs.Count > 100) Logs.RemoveAt(100);
            });
        }
    }

    public async Task Shutdown()
    {
        _logService.Log("Shutdown detected...");
        await _pluginLoader.ShutdownAllAsync();
        Installer.CancelAll();
        await Installer.WaitForTasks();
        await _vfsOrchestrator.ShutdownCleanupAsync();
        Profiles.SaveLastProfileName(ProfileManager.CurrentProfileName);
    }
}

public class ModFileItem
{
    public string Name { get; }
    public bool IsDirectory { get; }
    public long RawSize { get; }
    public string Size => IsDirectory ? "" : FormatSize(RawSize);
    public ModFileItem(string name, bool isDir, long size) { Name = name; IsDirectory = isDir; RawSize = size; }
    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1) { size /= 1024; unitIndex++; }
        return $"{size:F1} {units[unitIndex]}";
    }
}
