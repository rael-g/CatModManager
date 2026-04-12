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
using Avalonia.Media;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.PluginSdk;
using CatModManager.Ui.Plugins;

namespace CatModManager.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
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
    private readonly AppSessionState          _sessionState;
    private readonly PluginLoader             _pluginLoader;
    private readonly UiExtensionHost?         _uiExtensionHost;
    private readonly PluginBrowserViewModel?  _pluginBrowserVm;

    // ── Sub-ViewModels ────────────────────────────────────────────────────────

    public ProfileManagerViewModel ProfileManager { get; }
    public GameConfigViewModel      GameConfig     { get; }
    public ModListViewModel         ModList        { get; }
    public ModInspectorViewModel    Inspector      { get; }
    public ExternalToolsViewModel   Tools          { get; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty] private bool _isVfsMounted;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private ObservableCollection<string> _logs = new();
    public ObservableCollection<IInspectorTab>   PluginInspectorTabs   { get; } = new();
    public ObservableCollection<ISidebarAction>  PluginSidebarActions  { get; } = new();
    public IReadOnlyList<IModContextAction>      PluginModContextActions => _uiExtensionHost?.ModContextActions ?? System.Array.Empty<IModContextAction>();

    partial void OnIsVfsMountedChanged(bool value) => UpdateMountButtonState();
    
    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(TotalInstallProgress));
        OnPropertyChanged(nameof(IsTotalProgressIndeterminate));
    }

    public string MountButtonText  => IsVfsMounted ? "Unmount" : "Mount";
    public string MountButtonIcon  => IsVfsMounted ? "◉" : "○";
    public IBrush MountButtonColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public string SafeSwapStatusText  => IsVfsMounted ? "Safe Swap: Active" : "Safe Swap: Standby";
    public IBrush SafeSwapStatusColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public string AppDataPath => _pathService.BaseDataPath;

    public void NotifySelectedModMountPointChanged() => OnPropertyChanged(nameof(SelectedModMountPointName));

    public void RefreshModMountPointDisplayNames()
    {
        var points = GameConfig.EffectiveMountPoints;
        foreach (var mod in ModList.AllMods)
        {
            var id = mod.MountPointId;
            mod.MountPointDisplayName = string.IsNullOrEmpty(id)
                ? null
                : points.FirstOrDefault(mp => string.Equals(mp.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
        }
    }

    public string SelectedModMountPointName
    {
        get
        {
            var id = ModList.SelectedMod?.MountPointId;
            if (string.IsNullOrEmpty(id))
                return GameConfig.EffectiveMountPoints.FirstOrDefault()?.Name ?? "Default";
            return GameConfig.EffectiveMountPoints.FirstOrDefault(mp =>
                string.Equals(mp.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
        }
    }

    public bool HasActiveDownloads => _sessionState.CheckHasActiveDownloads?.Invoke() ?? false;

    public double TotalInstallProgress
    {
        get
        {
            var installing = ModList.AllMods.Where(m => m.IsInstalling).ToList();
            if (installing.Count == 0) return 0;
            return installing.Average(m => m.InstallProgress);
        }
    }

    public bool IsTotalProgressIndeterminate => IsInstalling && TotalInstallProgress <= 0;

    private readonly List<Task> _activeInstallTasks = new();

    public event Action? RequestClearFocus;
    public event Action<Mod, string>? ModInstalled;

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
        _rootSwapService      = rootSwapService;
        _sessionState         = sessionState;
        _pluginLoader         = pluginLoader;
        _uiExtensionHost      = uiExtensionHost;
        _pluginBrowserVm      = pluginBrowserVm;

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
        ModList.SelectedModChanged += mod => { Inspector.OnModChanged(mod); OnPropertyChanged(nameof(SelectedModMountPointName)); };

        Tools = new ExternalToolsViewModel(processService, vfsOrchestrator, logService);
        Tools.IsVfsMounted  = () => IsVfsMounted;
        Tools.EnsureMounted = async () =>
        {
            if (IsVfsMounted) return OperationResult.Success();
            return await ToggleMountInternal();
        };
        Tools.AutoSave = () => ProfileManager.AutoSave();

        _sessionState.RequestInstallModAction = (archivePath, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try { await AddModCommand.ExecuteAsync(archivePath); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logService.LogError("Mod install via SDK failed", ex); }
            });
        };

        _logService.OnLog += AddLog;
        _vfsOrchestrator.RecoverStaleMounts();

        var lastProfile = _configService.Current.LastProfileName;
        _ = ProfileManager.LoadInitialProfile(lastProfile);

        UpdateMountButtonState();
    }

    private Profile BuildCurrentProfile()
    {
        return new Profile
        {
            Name = ProfileManager.CurrentProfileName ?? "Untitled",
            Mods = ModList.AllMods.ToList(),
            GameSupportId = GameConfig.ActiveGameSupport?.GameId ?? "generic",
            LaunchArguments = GameConfig.LaunchArguments ?? "",
            ModsFolderPath = GameConfig.ModsFolderPath ?? "",
            BaseDataPath = GameConfig.BaseFolderPath ?? "",
            GameExecutablePath = GameConfig.GameExecutablePath ?? "",
            DownloadsFolderPath = GameConfig.DownloadsFolderPath ?? "",
            DataSubFolder = GameConfig.DataSubFolder ?? "",
            UserMountPoints = GameConfig.UserMountPoints.ToList()
        };
    }

    private void ApplyLoadedProfile(Profile profile)
    {
        using (ModList.SuppressAutoSave())
        using (ModList.SuppressUpdates())
        {
            GameConfig.ModsFolderPath = profile.ModsFolderPath;
            GameConfig.BaseFolderPath = profile.BaseDataPath;
            GameConfig.GameExecutablePath = profile.GameExecutablePath;
            GameConfig.DownloadsFolderPath = profile.DownloadsFolderPath;
            GameConfig.DataSubFolder = profile.DataSubFolder;
            GameConfig.LaunchArguments = profile.LaunchArguments;

            GameConfig.UserMountPoints.Clear();
            foreach (var mp in profile.UserMountPoints) GameConfig.UserMountPoints.Add(mp);

            if (!string.IsNullOrEmpty(profile.GameSupportId))
            {
                var game = GameConfig.AvailableGameSupports.FirstOrDefault(g => g.GameId == profile.GameSupportId);
                if (game != null) GameConfig.ActiveGameSupport = game;
            }

            ModList.AllMods.Clear();
            foreach (var m in profile.Mods) ModList.AllMods.Add(m);
        }
        
        RefreshModMountPointDisplayNames();
        SyncActiveModsToState();
        
        // Notify SDK/Plugins that the profile has changed (Fixes Nexus persistence/names)
        _sessionState.NotifyProfileChanged(profile.Name);

        _logService.Log($"Profile '{profile.Name}' applied with {profile.Mods.Count} mods.");
    }

    private void SyncActiveModsToState()
    {
        var active = ModList.AllMods
            .Where(m => m.IsEnabled && !m.IsBroken && !m.IsInstalling)
            .OrderBy(m => m.Priority)
            .ToList();
        
        _sessionState.ActiveMods = active;
        _sessionState.NexusDomain = GameConfig.ActiveGameSupport?.NexusDomain;
        _sessionState.GameId = GameConfig.ActiveGameSupport?.GameId;
        _sessionState.ModsFolderPath = GameConfig.ModsFolderPath;
        _sessionState.DataFolderPath = GameConfig.BaseFolderPath;
        _sessionState.DownloadsFolderPath = GameConfig.DownloadsFolderPath;
        _sessionState.GameExecutablePath = GameConfig.GameExecutablePath;
        _sessionState.CurrentProfileName = ProfileManager.CurrentProfileName;
        _sessionState.DataSubFolder = GameConfig.DataSubFolder;
    }

    [RelayCommand]
    private async Task ToggleMount()
    {
        var res = await ToggleMountInternal();
        if (!res.IsSuccess) StatusMessage = res.ErrorMessage;
    }

    private async Task<OperationResult> ToggleMountInternal()
    {
        if (IsVfsMounted)
        {
            StatusMessage = "Unmounting...";
            var res = await _vfsOrchestrator.UnmountAsync();
            if (res.IsSuccess)
            {
                IsVfsMounted = false;
                StatusMessage = "Unmounted successfully.";
            }
            return res;
        }
        else
        {
            if (GameConfig.ActiveGameSupport == null) return OperationResult.Failure("No game selected.");
            if (string.IsNullOrEmpty(GameConfig.BaseFolderPath)) return OperationResult.Failure("Game folder not set.");

            StatusMessage = "Mounting Virtual File System...";
            SyncActiveModsToState();
            var res = await _vfsOrchestrator.MountAsync(new MountOptions
            {
                GameFolderPath = GameConfig.BaseFolderPath,
                DataSubFolder  = GameConfig.DataSubFolder,
                RootSwapOnly   = GameConfig.ActiveGameSupport?.RootSwapOnly ?? false,
                ActiveMods     = ModList.AllMods.Where(m => m.IsEnabled && !m.IsBroken).ToList(),
                MountPoints    = GameConfig.EffectiveMountPoints.ToList()
            });
            
            if (res.IsSuccess)
            {
                IsVfsMounted = true;
                StatusMessage = "VFS Mounted & Safe Swap Active.";
            }
            return res;
        }
    }

    private void UpdateMountButtonState()
    {
        OnPropertyChanged(nameof(MountButtonText));
        OnPropertyChanged(nameof(MountButtonIcon));
        OnPropertyChanged(nameof(MountButtonColor));
        OnPropertyChanged(nameof(SafeSwapStatusText));
        OnPropertyChanged(nameof(SafeSwapStatusColor));
    }

    private static readonly IBrush _mountedBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush _unmountedBrush = new SolidColorBrush(Color.Parse("#757575"));

    [RelayCommand]
    private async Task LaunchGame()
    {
        if (GameConfig.ActiveGameSupport == null) { StatusMessage = "Select a game first."; return; }
        
        if (!IsVfsMounted)
        {
            var res = await ToggleMountInternal();
            if (!res.IsSuccess) { StatusMessage = $"Mount failed: {res.ErrorMessage}"; return; }
        }

        StatusMessage = "Launching game...";
        try
        {
            var activeMods = ModList.AllMods.Where(m => m.IsEnabled && !m.IsBroken && !m.IsInstalling).ToList();
            await _gameLauncher.LaunchGameAsync(
                GameConfig.GameExecutablePath, 
                GameConfig.LaunchArguments, 
                GameConfig.ActiveGameSupport, 
                activeMods);
            StatusMessage = "Game running.";
        }
        catch (Exception ex)
        {
            _logService.LogError("Launch failed", ex);
            StatusMessage = $"Launch error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddMod(string? sourcePath = null)
    {
        if (!string.IsNullOrEmpty(sourcePath))
        {
            await InstallModAtMountPointAsync(sourcePath, null);
            return;
        }

        var top = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Mod Archives",
            AllowMultiple = true,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Archives") { Patterns = new[] { "*.zip", "*.7z", "*.rar" } } }
        });

        foreach (var file in files)
        {
            await InstallModAtMountPointAsync(file.Path.LocalPath, null);
        }
    }

    [RelayCommand]
    private async Task AddModFromFolder()
    {
        var top = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top == null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Mod Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            await InstallModAtMountPointAsync(folders[0].Path.LocalPath, null);
        }
    }

    private async Task InstallModAtMountPointAsync(string sourcePath, string? mountPointId)
    {
        if (string.IsNullOrEmpty(GameConfig.ModsFolderPath))
        {
            StatusMessage = "Error: Mods folder not set in Game Config.";
            return;
        }

        // 1. Storage is ALWAYS in the Mods Folder
        string targetBaseDir = GameConfig.ModsFolderPath;

        // 2. Identify intended mount point (metadata only)
        var mountPoint = GameConfig.EffectiveMountPoints.FirstOrDefault(mp => mp.Id == mountPointId) 
                         ?? GameConfig.EffectiveMountPoints.FirstOrDefault();
        
        // 3. Proper Update Detection: Match by archive name vs existing folder name
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var existingMod = ModList.AllMods.FirstOrDefault(m => 
            string.Equals(Path.GetFileName(m.RootPath), baseName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Name, baseName, StringComparison.OrdinalIgnoreCase));

        Mod targetMod;
        bool isUpdate = existingMod != null;

        if (isUpdate)
        {
            targetMod = existingMod!;
            targetMod.IsInstalling = true;
            targetMod.InstallProgress = 0;
        }
        else
        {
            targetMod = new Mod(baseName, sourcePath, ModList.AllMods.Count + 1)
            {
                IsInstalling = true,
                InstallProgress = 0,
                MountPointId = mountPoint?.Id ?? "Default",
                Category = "Uncategorized"
            };

            // Pre-fill metadata (Name/Category) from Nexus BEFORE adding to list
            _sessionState.NotifyModInstalled(targetMod, sourcePath);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                ModList.AllMods.Insert(0, targetMod);
                ModList.RebuildDisplayedMods();
            });
        }

        var cts = new System.Threading.CancellationTokenSource();
        targetMod.SetInstallCancellationTokenSource(cts);

        StatusMessage = $"Installing {targetMod.Name}...";
        IsInstalling = true;

        try
        {
            var progress = new Progress<double>(p => targetMod.InstallProgress = p);
            string installedPath = string.Empty;

            var chosen = _uiExtensionHost?.ModInstallers.FirstOrDefault(i => i.CanInstall(sourcePath));
            
            Task<string> installTask;
            if (chosen != null)
            {
                var ctx = new SimpleInstallContext(GameConfig.ModsFolderPath, new LogServiceAdapter(_logService), _sessionState.ConsumePendingPreset());
                var installResult = await chosen.InstallAsync(sourcePath, ctx);
                if (installResult == null || !installResult.IsSuccess)
                {
                    if (!isUpdate) ModList.AllMods.Remove(targetMod);
                    targetMod.IsInstalling = false;
                    ModList.RebuildDisplayedMods();
                    StatusMessage = installResult?.ErrorMessage ?? "Install cancelled.";
                    return;
                }
                installTask = _modManagementService.InstallModFromMappingAsync(
                    sourcePath, baseName, targetBaseDir, installResult.FileMapping, isUpdate ? targetMod.RootPath : null, progress, cts.Token);
            }
            else
            {
                installTask = _modManagementService.InstallModAsync(sourcePath, targetBaseDir, isUpdate ? targetMod.RootPath : null, progress, cts.Token);
            }

            lock (_activeInstallTasks) _activeInstallTasks.Add(installTask);
            try { installedPath = await installTask; }
            catch (OperationCanceledException) { installedPath = string.Empty; }
            catch (Exception ex) { _logService.LogError("Install task failed", ex); installedPath = string.Empty; }
            finally { lock (_activeInstallTasks) _activeInstallTasks.Remove(installTask); }

            if (string.IsNullOrEmpty(installedPath))
            {
                if (!isUpdate)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                        ModList.AllMods.Remove(targetMod);
                        ModList.RebuildDisplayedMods();
                    });
                }
                targetMod.IsInstalling = false;
                StatusMessage = "Installation cancelled.";
                return;
            }

            targetMod.RootPath = installedPath;
            targetMod.IsArchive = false;
            
            // Link Path to Nexus Tracking AFTER successful install
            _sessionState.NotifyModInstalled(targetMod, sourcePath);

            try
            {
                string sidecar = Path.Combine(installedPath, ".cmm_metadata.toml");
                if (File.Exists(sidecar))
                {
                    var meta = Nett.Toml.ReadFile<ModMetadata>(sidecar);
                    if (meta != null)
                    {
                        if (!string.IsNullOrEmpty(meta.Name) && string.Equals(targetMod.Name, baseName, StringComparison.OrdinalIgnoreCase))
                            targetMod.Name = meta.Name;
                        if (!string.IsNullOrEmpty(meta.Version) && meta.Version != "1.0.0")
                            targetMod.Version = meta.Version;
                        if (targetMod.Category == "Uncategorized" && !string.IsNullOrEmpty(meta.Category))
                            targetMod.Category = meta.Category;
                    }
                }
            }
            catch { }

            targetMod.IsInstalling = false;
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                ModList.UpdatePriorities();
                ModList.UpdateCategories();
                ModList.RebuildDisplayedMods();
            });
            
            ModInstalled?.Invoke(targetMod, sourcePath);
            StatusMessage = $"Mod '{targetMod.Name}' installed.";
            ProfileManager.AutoSave();
        }
        catch (Exception ex)
        {
            _logService.LogError("Installation error", ex);
            StatusMessage = $"Error: {ex.Message}"; 
            if (!isUpdate)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                    ModList.AllMods.Remove(targetMod);
                    ModList.RebuildDisplayedMods();
                });
            }
            targetMod.IsInstalling = false;
        }
        finally 
        { 
            IsInstalling = ModList.AllMods.Any(m => m.IsInstalling); 
            cts.Dispose();
        }
    }

    private sealed class SimpleInstallContext : IInstallContext
    {
        public string TargetFolder { get; }
        public string DestinationFolder => TargetFolder;
        public IPluginLogger Log { get; }
        public FomodPreset? FomodPreset { get; }
        public SimpleInstallContext(string target, IPluginLogger log, FomodPreset? preset) 
        { TargetFolder = target; Log = log; FomodPreset = preset; }
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
            using (ProfileManager.SuppressAutoSave())
            {
                foreach (var mod in toRemove)
                {
                    if (!mod.IsBroken && !string.IsNullOrEmpty(GameConfig.BaseFolderPath) && mod.HasRootFolder)
                        await _rootSwapService.UndeployModAsync(mod.RootPath, GameConfig.BaseFolderPath);

                    ModList.AllMods.Remove(mod);
                    _logService.Log($"Mod '{mod.Name}' removed.");

                    // PHYSICAL DELETION
                    if (_fileService.DirectoryExists(mod.RootPath))
                        await Task.Run(() => _fileService.DeleteDirectory(mod.RootPath, true));
                    else if (_fileService.FileExists(mod.RootPath))
                        await Task.Run(() => _fileService.DeleteFile(mod.RootPath));
                }
                ModList.UpdatePriorities();
            }
            ModList.RebuildDisplayedMods();
            ProfileManager.AutoSave();
        }
        catch (Exception ex) { _logService.Log($"REMOVE ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    [RelayCommand] private void OpenPluginBrowser() { }
    [RelayCommand] private void ShowAppData() => _processService.OpenFolderAsync(_pathService.BaseDataPath);
    [RelayCommand] private async Task OpenModsFolder() => await _processService.OpenFolderAsync(GameConfig.ModsFolderPath ?? "");
    [RelayCommand] private async Task OpenDownloadsFolder() => await _processService.OpenFolderAsync(GameConfig.DownloadsFolderPath ?? "");
    [RelayCommand] private async Task OpenGameDataFolder()
    {
        var sub = GameConfig.DataSubFolder;
        if (string.IsNullOrEmpty(sub)) { await _processService.OpenFolderAsync(GameConfig.BaseFolderPath ?? ""); return; }
        var expanded = Environment.ExpandEnvironmentVariables(sub);
        string folder = Path.IsPathRooted(expanded) ? expanded :
            !string.IsNullOrEmpty(GameConfig.BaseFolderPath) ? Path.Combine(GameConfig.BaseFolderPath, expanded) : expanded;
        await _processService.OpenFolderAsync(folder);
    }
    [RelayCommand] private async Task OpenGameFolder() => await _processService.OpenFolderAsync(GameConfig.BaseFolderPath ?? "");
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
    [RelayCommand] private void OpenDataSubFolder() => OpenGameDataFolderCommand.Execute(null);
    [RelayCommand] private void OpenAppDataFolder() => ShowAppDataCommand.Execute(null);
    [RelayCommand] private void Refresh() { }
    [RelayCommand] private void ClearFocus() { RequestClearFocus?.Invoke(); }
    [RelayCommand] private void DeleteMountPoint(MountPointDef? mp) { if (mp != null) GameConfig.UserMountPoints.Remove(mp); }
    [RelayCommand] private void ExecuteSidebarAction(ISidebarAction? action) { if (action != null) action.Execute(); }

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

    public async Task Shutdown()
    {
        try
        {
            _logService.Log("Shutdown detected. Cancelling active tasks...");
            
            // 1. Shutdown plugins FIRST (saves Nexus DB while everything is still open)
            await _pluginLoader.ShutdownAllAsync();

            // 2. Cancel installs
            var installingMods = ModList.AllMods.Where(m => m.IsInstalling).ToList();
            foreach (var mod in installingMods) mod.CancelInstall();
            if (_activeInstallTasks.Count > 0) await Task.WhenAll(_activeInstallTasks.ToArray());

            // 3. Cleanup VFS and Save Config
            _vfsOrchestrator.ShutdownCleanup();
            if (!string.IsNullOrEmpty(ProfileManager.CurrentProfileName))
            {
                _configService.Current.LastProfileName = ProfileManager.CurrentProfileName;
                _configService.Save();
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Shutdown] Error during cleanup: {ex.Message}"); }
        finally { IsVfsMounted = false; }
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
