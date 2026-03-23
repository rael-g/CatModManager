using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Core.Vfs;
using CatModManager.PluginSdk;
using CatModManager.Ui.Plugins;
using Avalonia.Media;
using Nett;

namespace CatModManager.Ui.ViewModels;

public enum InspectorTab { Info, Files }
public record ModFileItem(string Name, bool IsDirectory, long Size);

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly IBrush _mountedBrush   = Brush.Parse("#3BA55D");
    private static readonly IBrush _unmountedBrush = Brush.Parse("#80848E");

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IModScanner              _modScanner;
    private readonly IModManagementService    _modManagementService;
    private readonly IProcessService          _processService;
    private readonly IVfsOrchestrationService _vfsOrchestrator;
    private readonly IGameLaunchService       _gameLauncher;
    private readonly IFileService             _fileService;
    private readonly IRootSwapService         _rootSwapService;
    private readonly ICatPathService          _pathService;
    private readonly ILogService              _logService;
    private readonly IConfigService           _configService;
    private readonly UiExtensionHost?         _uiExtensionHost;
    private readonly PluginBrowserViewModel?  _pluginBrowserVm;
    private readonly AppSessionState          _sessionState;

    // ── Sub-ViewModels ────────────────────────────────────────────────────────

    public ProfileManagerViewModel ProfileManager { get; }
    public GameConfigViewModel      GameConfig     { get; }
    public ModListViewModel         ModList        { get; }
    public ModInspectorViewModel    Inspector      { get; }
    public ExternalToolsViewModel   Tools          { get; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty] private bool _isVfsMounted;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private ObservableCollection<string> _logs = new();
    public ObservableCollection<IInspectorTab>  PluginInspectorTabs  { get; } = new();
    public ObservableCollection<ISidebarAction> PluginSidebarActions { get; } = new();

    partial void OnIsVfsMountedChanged(bool value) => UpdateMountButtonState();

    public string MountButtonText  => IsVfsMounted ? "Unmount" : "Mount";
    public string MountButtonIcon  => IsVfsMounted ? "◉" : "○";
    public IBrush MountButtonColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public string SafeSwapStatusText  => IsVfsMounted ? "Safe Swap: Active" : "Safe Swap: Standby";
    public IBrush SafeSwapStatusColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public string AppDataPath => _pathService.BaseDataPath;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action? RequestClearFocus;
    public event Action<Mod, string>? ModInstalled;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindowViewModel(
        IModScanner              modScanner,
        IProfileService          profileService,
        IDriverService           driverService,
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
        IRootSwapService         rootSwapService,
        AppSessionState          sessionState,
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
        _rootSwapService      = rootSwapService;
        _sessionState         = sessionState;
        _uiExtensionHost      = uiExtensionHost;
        _pluginBrowserVm      = pluginBrowserVm;

        // Sub-ViewModels
        ProfileManager = new ProfileManagerViewModel(profileService, pathService, fileService, configService, logService);
        ProfileManager.BuildSaveData  = BuildCurrentProfile;
        ProfileManager.IsVfsMounted   = () => IsVfsMounted;
        ProfileManager.ProfileLoaded += ApplyLoadedProfile;

        GameConfig = new GameConfigViewModel(gameSupportService, gameDiscoveryService, driverService, logService);
        GameConfig.AutoSave = () => ProfileManager.AutoSave();
        GameConfig.Initialize();

        Inspector = new ModInspectorViewModel(logService);
        Inspector.SetStatusMessage = msg => StatusMessage = msg;

        ModList = new ModListViewModel();
        ModList.AutoSave        = () => ProfileManager.AutoSave();
        ModList.SuppressAutoSave = () => ProfileManager.SuppressAutoSave();
        ModList.SyncActiveMods  = SyncActiveModsToState;
        ModList.SelectedModChanged += mod => Inspector.OnModChanged(mod);

        Tools = new ExternalToolsViewModel(processService, vfsOrchestrator, logService);
        Tools.IsVfsMounted  = () => IsVfsMounted;
        Tools.EnsureMounted = async () =>
        {
            if (IsVfsMounted) return OperationResult.Success();
            return await ToggleMountInternal();
        };
        Tools.AutoSave = () => ProfileManager.AutoSave();

        // Wire AppSessionState
        _sessionState.RequestInstallModAction = archivePath =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AddModCommand.Execute(archivePath));
        GameConfig.PropertyChanged += (_, _) => SyncGameConfigToState();
        ProfileManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfileManagerViewModel.CurrentProfileName))
            {
                _sessionState.CurrentProfileName = ProfileManager.CurrentProfileName;
                if (ProfileManager.CurrentProfileName is { } name)
                    _sessionState.NotifyProfileChanged(name);
            }
        };
        ModInstalled += (mod, sourcePath) => _sessionState.NotifyModInstalled(mod, sourcePath);

        _logService.OnLog += AddLog;
        _vfsOrchestrator.RecoverStaleMounts();

        var lastProfile = _configService.Current.LastProfileName;
        _ = ProfileManager.LoadInitialProfile(lastProfile);

        _logService.Log("Cat Mod Manager initialized.");
    }

    // ── Profile integration ───────────────────────────────────────────────────

    private Profile BuildCurrentProfile() => new Profile
    {
        Name               = ProfileManager.CurrentProfileName ?? "",
        ModsFolderPath     = GameConfig.ModsFolderPath     ?? "",
        BaseDataPath       = GameConfig.BaseFolderPath      ?? "",
        GameExecutablePath = GameConfig.GameExecutablePath  ?? "",
        DataSubFolder      = GameConfig.DataSubFolder       ?? "",
        GameSupportId      = GameConfig.ActiveGameSupport.GameId,
        LaunchArguments    = GameConfig.LaunchArguments     ?? "",
        DownloadsFolderPath = GameConfig.DownloadsFolderPath ?? "",
        Mods               = ModList.AllMods.ToList(),
        ExternalTools      = Tools.GetTools()
    };

    private void ApplyLoadedProfile(Profile p)
    {
        // Suppress GameConfig autosave during bulk-load; ProfileManager suppression
        // covers AllMods changes via its own counter.
        var savedAutoSave = GameConfig.AutoSave;
        GameConfig.AutoSave = null;
        using (ProfileManager.SuppressAutoSave())
        {
            GameConfig.ModsFolderPath      = p.ModsFolderPath;
            GameConfig.BaseFolderPath      = p.BaseDataPath;
            GameConfig.GameExecutablePath  = p.GameExecutablePath;
            GameConfig.DataSubFolder       = p.DataSubFolder;
            GameConfig.DownloadsFolderPath = string.IsNullOrEmpty(p.DownloadsFolderPath)
                ? _pathService.DownloadsPath
                : p.DownloadsFolderPath;

            ModList.AllMods.Clear();
            foreach (var m in p.Mods) ModList.AllMods.Add(m);

            GameConfig.ActiveGameSupport = GameConfig.AvailableGameSupports.FirstOrDefault(s => s.GameId == p.GameSupportId)
                ?? GameConfig.AvailableGameSupports.FirstOrDefault(s => s.CanSupport(p.GameExecutablePath))
                ?? GameConfig.AvailableGameSupports.FirstOrDefault()
                ?? GameConfig.ActiveGameSupport;
            GameConfig.LaunchArguments = p.LaunchArguments;
        }
        GameConfig.AutoSave = savedAutoSave;
        ModList.UpdateCategories();
        ModList.RebuildDisplayedMods();
        Tools.LoadTools(p.ExternalTools);
    }

    private void AutoSave() => ProfileManager.AutoSave();
    private IDisposable SuppressAutoSave() => ProfileManager.SuppressAutoSave();

    // ── AppSessionState sync ──────────────────────────────────────────────────

    private void SyncGameConfigToState()
    {
        _sessionState.DataFolderPath      = GameConfig.BaseFolderPath;
        _sessionState.ModsFolderPath      = GameConfig.ModsFolderPath;
        _sessionState.DownloadsFolderPath = GameConfig.DownloadsFolderPath;
        _sessionState.GameExecutablePath  = GameConfig.GameExecutablePath;
        _sessionState.GameId              = GameConfig.ActiveGameSupport?.GameId;
        _sessionState.DataSubFolder       = GameConfig.ActiveGameSupport?.DataSubFolder;
        _sessionState.RootSwapOnly        = GameConfig.ActiveGameSupport?.RootSwapOnly ?? false;
    }

    private void SyncActiveModsToState() =>
        _sessionState.ActiveMods = ModList.AllMods.Where(m => m.IsEnabled).Cast<IModInfo>().ToList();

    // ── Mount / launch ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleMount()
    {
        RequestClearFocus?.Invoke();
        var result = await ToggleMountInternal();
        StatusMessage = result.IsSuccess ? "Ready" : result.ErrorMessage ?? "Operation Failed";
    }

    private async Task<OperationResult> ToggleMountInternal()
    {
        StatusMessage = IsVfsMounted ? "Unmounting..." : "Mounting...";
        OperationResult result;
        if (IsVfsMounted)
        {
            result = await _vfsOrchestrator.UnmountAsync();
        }
        else
        {
            result = await _vfsOrchestrator.MountAsync(new MountOptions
            {
                GameFolderPath = GameConfig.BaseFolderPath,
                DataSubFolder  = GameConfig.DataSubFolder,
                RootSwapOnly   = GameConfig.ActiveGameSupport?.RootSwapOnly ?? false,
                ActiveMods     = ModList.AllMods.Where(m => m.IsEnabled).ToList()
            });
        }
        IsVfsMounted = _vfsOrchestrator.IsMounted;
        UpdateMountButtonState();
        return result;
    }

    private void UpdateMountButtonState()
    {
        OnPropertyChanged(nameof(MountButtonText));
        OnPropertyChanged(nameof(MountButtonIcon));
        OnPropertyChanged(nameof(MountButtonColor));
        OnPropertyChanged(nameof(SafeSwapStatusText));
        OnPropertyChanged(nameof(SafeSwapStatusColor));
    }

    [RelayCommand]
    private async Task LaunchGame()
    {
        RequestClearFocus?.Invoke();
        StatusMessage = "Launching...";

        bool wasAutoMounted = false;
        if (!IsVfsMounted)
        {
            var mountResult = await ToggleMountInternal();
            if (mountResult.IsSuccess) wasAutoMounted = true;
            else { StatusMessage = $"Auto-mount failed: {mountResult.ErrorMessage}"; return; }
        }

        var result = await _gameLauncher.LaunchGameAsync(
            GameConfig.GameExecutablePath, GameConfig.LaunchArguments, GameConfig.ActiveGameSupport, ModList.AllMods.Where(m => m.IsEnabled));

        if (wasAutoMounted && IsVfsMounted)
        {
            _logService.Log("Game closed. Auto-unmounting...");
            await ToggleMountInternal();
        }

        IsVfsMounted = _vfsOrchestrator.IsMounted;
        UpdateMountButtonState();
        StatusMessage = result.IsSuccess ? "Ready" : result.ErrorMessage ?? "Launch Failed";
    }

    // ── Mod install / remove ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh()
    {
        RequestClearFocus?.Invoke();
        if (string.IsNullOrEmpty(GameConfig.ModsFolderPath)) return;
        StatusMessage = "Refreshing mods...";
        try
        {
            var scannedMods = await _modScanner.ScanDirectoryAsync(GameConfig.ModsFolderPath);
            var currentMap  = ModList.AllMods.ToDictionary(m => m.RootPath, m => m);
            var newList = scannedMods.Select(mod => {
                if (currentMap.TryGetValue(mod.RootPath, out var existing)) return existing;
                mod.Priority = -1;
                return mod;
            }).ToList();

            using (SuppressAutoSave())
            {
                ModList.AllMods.Clear();
                foreach (var mod in newList.OrderByDescending(m => m.Priority)) ModList.AllMods.Add(mod);
                ModList.UpdatePriorities(); ModList.UpdateCategories();
            }
            ModList.RebuildDisplayedMods();
            AutoSave();
            StatusMessage = "Mods refreshed.";
        }
        catch (Exception ex) { _logService.Log($"REFRESH ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ScanDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var scannedMods = await _modScanner.ScanDirectoryAsync(path);
            using (SuppressAutoSave())
            {
                GameConfig.ModsFolderPath = path;
                ModList.AllMods.Clear();
                foreach (var mod in scannedMods) ModList.AllMods.Add(mod);
                ModList.UpdatePriorities(); ModList.UpdateCategories();
            }
            ModList.RebuildDisplayedMods();
            AutoSave();
        }
        catch (Exception ex) { _logService.Log($"SCAN ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task AddMod(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return;
        if (string.IsNullOrEmpty(GameConfig.ModsFolderPath)) { _logService.Log("ERROR: Please select a Mods Folder first."); return; }
        try
        {
            StatusMessage = "Importing mod...";

            var installers     = _uiExtensionHost?.ModInstallers ?? Enumerable.Empty<IModInstaller>();
            IModInstaller? chosen = installers.FirstOrDefault(i => i.CanInstall(sourcePath));

            string installedPath;
            if (chosen != null)
            {
                var ctx           = new SimpleInstallContext(GameConfig.ModsFolderPath, new LogServiceAdapter(_logService));
                var installResult = await chosen.InstallAsync(sourcePath, ctx);
                if (installResult == null || !installResult.IsSuccess)
                {
                    StatusMessage = installResult?.ErrorMessage ?? "Install cancelled.";
                    return;
                }
                string modName = Path.GetFileNameWithoutExtension(sourcePath);
                installedPath  = await _modManagementService.InstallModFromMappingAsync(
                    sourcePath, modName, GameConfig.ModsFolderPath, installResult.FileMapping);
            }
            else
            {
                installedPath = await _modManagementService.InstallModAsync(sourcePath, GameConfig.ModsFolderPath);
            }

            string name = Path.GetFileNameWithoutExtension(installedPath);
            var mod = new Mod(name, installedPath, ModList.AllMods.Count, true, "Uncategorized");

            string sidecar = Path.Combine(installedPath, ".cmm_metadata.toml");
            if (File.Exists(sidecar))
            {
                try
                {
                    var meta = Toml.ReadFile<ModMetadata>(sidecar);
                    if (meta != null) { mod.Name = meta.Name; mod.Version = meta.Version; mod.Category = meta.Category; }
                }
                catch { }
            }

            ModList.AllMods.Insert(0, mod);
            ModList.UpdatePriorities(); ModList.UpdateCategories();
            ModList.RebuildDisplayedMods();
            ModList.SelectedMod = mod;
            AutoSave();
            ModInstalled?.Invoke(mod, sourcePath);
            StatusMessage = "Mod imported.";
            _logService.Log($"Mod imported to: {installedPath}");
        }
        catch (Exception ex) { _logService.Log($"IMPORT ERROR: {ex.Message}"); StatusMessage = $"IMPORT ERROR: {ex.Message}"; }
    }

    private sealed class SimpleInstallContext : IInstallContext
    {
        public string       DestinationFolder { get; }
        public IPluginLogger Log              { get; }
        public SimpleInstallContext(string dest, IPluginLogger log) { DestinationFolder = dest; Log = log; }
    }

    [RelayCommand]
    private async Task RemoveMod()
    {
        var toRemove = (ModList.SelectedMods is { Count: > 1 })
            ? ModList.SelectedMods.ToList()
            : ModList.SelectedMod != null ? new List<Mod> { ModList.SelectedMod } : new List<Mod>();
        if (toRemove.Count == 0) return;
        try
        {
            using (SuppressAutoSave())
            {
                foreach (var mod in toRemove)
                {
                    if (!string.IsNullOrEmpty(GameConfig.BaseFolderPath) && mod.HasRootFolder)
                        await _rootSwapService.UndeployModAsync(mod.RootPath, GameConfig.BaseFolderPath);
                    ModList.AllMods.Remove(mod);
                    _logService.Log($"Mod '{mod.Name}' removed.");
                    if (!string.IsNullOrEmpty(GameConfig.ModsFolderPath) && mod.RootPath.StartsWith(GameConfig.ModsFolderPath))
                    {
                        if (Directory.Exists(mod.RootPath)) Directory.Delete(mod.RootPath, true);
                        else if (File.Exists(mod.RootPath)) File.Delete(mod.RootPath);
                    }
                }
                ModList.UpdatePriorities();
            }
            ModList.RebuildDisplayedMods();
            AutoSave();
        }
        catch (Exception ex) { _logService.Log($"REMOVE ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    // ── Sidebar / misc ────────────────────────────────────────────────────────

    [RelayCommand] private void ExecuteSidebarAction(ISidebarAction? action) => action?.Execute();
    [RelayCommand] private void ClearFocus() => RequestClearFocus?.Invoke();
    [RelayCommand] private void OpenPluginBrowser()
    {
        if (_pluginBrowserVm != null) new CatModManager.Ui.Views.PluginBrowserWindow(_pluginBrowserVm).Show();
    }

    [RelayCommand] private async Task OpenModsFolder()          => await _processService.OpenFolderAsync(GameConfig.ModsFolderPath ?? "");
    [RelayCommand] private async Task OpenGameFolder()          => await _processService.OpenFolderAsync(GameConfig.BaseFolderPath ?? "");
    [RelayCommand] private async Task OpenProfilesFolder()      => await _processService.OpenFolderAsync(_pathService.ProfilesPath);
    [RelayCommand] private async Task OpenAppDataFolder()       => await _processService.OpenFolderAsync(_pathService.BaseDataPath);
    [RelayCommand] private async Task OpenDownloadsFolder()     => await _processService.OpenFolderAsync(GameConfig.DownloadsFolderPath ?? "");
    [RelayCommand] private async Task OpenDataSubFolder()       =>
        await _processService.OpenFolderAsync(!string.IsNullOrEmpty(GameConfig.BaseFolderPath) && !string.IsNullOrEmpty(GameConfig.DataSubFolder)
            ? Path.Combine(GameConfig.BaseFolderPath, GameConfig.DataSubFolder) : GameConfig.DataSubFolder ?? "");
    [RelayCommand] private async Task OpenGameExecutableFolder() =>
        await _processService.OpenFolderAsync(!string.IsNullOrEmpty(GameConfig.GameExecutablePath)
            ? Path.GetDirectoryName(GameConfig.GameExecutablePath) ?? "" : "");
    [RelayCommand] private async Task OpenSelectedModFolder()
    {
        if (ModList.SelectedMod == null) return;
        string path = ModList.SelectedMod.RootPath;
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        await _processService.OpenFolderAsync(path);
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private void AddLog(string formattedMessage)
    {
        void Action()
        {
            Logs.Insert(0, formattedMessage);
            if (Logs.Count > 100) Logs.RemoveAt(100);
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Action();
        else if (Avalonia.Application.Current != null) Avalonia.Threading.Dispatcher.UIThread.Post(Action);
        else Action();
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────

    public void Shutdown()
    {
        _logService.Log("Shutdown detected. Cleaning up VFS...");
        _vfsOrchestrator.ShutdownCleanup();
        if (!string.IsNullOrEmpty(ProfileManager.CurrentProfileName))
        {
            _configService.Current.LastProfileName = ProfileManager.CurrentProfileName;
            _configService.Save();
        }
        IsVfsMounted = false;
    }
}
