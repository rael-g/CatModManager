using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Core.Vfs;
using CatModManager.VirtualFileSystem;
using CatModManager.PluginSdk;
using CatModManager.Ui.Plugins;
using Avalonia.Media;
using Avalonia.Threading;
using Nett;

namespace CatModManager.Ui.ViewModels;

public enum InspectorTab { Info, Files }
public record ModFileItem(string Name, bool IsDirectory, long Size);

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly IBrush _mountedBrush = Brush.Parse("#3BA55D");
    private static readonly IBrush _unmountedBrush = Brush.Parse("#80848E");

    private readonly IModScanner _modScanner;
    private readonly IProfileService _profileService;
    private readonly IDriverService _driverService;
    private readonly IModManagementService _modManagementService;
    private readonly IProcessService _processService;
    private readonly IVfsOrchestrationService _vfsOrchestrator;
    private readonly IGameLaunchService _gameLauncher;
    private readonly IFileService _fileService;
    private readonly IRootSwapService _rootSwapService;
    
    private readonly ICatPathService _pathService;
    private readonly ILogService _logService;
    private readonly IConfigService _configService;
    private readonly IGameSupportService    _gameSupportService;
    private readonly IGameDiscoveryService  _gameDiscoveryService;
    private readonly UiExtensionHost? _uiExtensionHost;
    private readonly PluginBrowserViewModel? _pluginBrowserVm;

    private readonly SemaphoreSlim _profileLock = new(1, 1);
    private int _autoSaveSuppressionCount = 0;
    private bool IsAutoSaveSuppressed => _autoSaveSuppressionCount > 0;

    [ObservableProperty]
    private ObservableCollection<Mod> _allMods = new();

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private Mod? _selectedMod;

    [ObservableProperty]
    private int _selectedInspectorTab = 0;

    [ObservableProperty]
    private ObservableCollection<ModFileItem> _currentModFiles = new();

    [ObservableProperty]
    private string _currentModFolderPath = ""; 

    [ObservableProperty]
    private string? _modsFolderPath;

    [ObservableProperty]
    private string? _baseFolderPath;

    [ObservableProperty]
    private string? _gameExecutablePath;

    [ObservableProperty]
    private string? _launchArguments;

    [ObservableProperty]
    private string? _dataSubFolder;

    [ObservableProperty]
    private string? _downloadsFolderPath;

    [ObservableProperty]
    private bool _isVfsMounted;

    partial void OnIsVfsMountedChanged(bool value) => UpdateMountButtonState();

    [ObservableProperty]
    private bool _isDriverMissing;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string? _currentProfileName;

    [ObservableProperty]
    private string? _profileDisplayName;

    [ObservableProperty]
    private IGameSupport _activeGameSupport;

    [ObservableProperty]
    private ObservableCollection<string> _availableProfiles = new();

    [ObservableProperty]
    private ObservableCollection<IGameSupport> _availableGameSupports = new();

    [ObservableProperty]
    private ObservableCollection<string> _logs = new();

    public ObservableCollection<string> Categories { get; } = new() { "All", "Uncategorized" };

    public ObservableCollection<IInspectorTab>  PluginInspectorTabs  { get; } = new();

    public ObservableCollection<ISidebarAction> PluginSidebarActions { get; } = new();

    public string MountButtonText => IsVfsMounted ? "Unmount" : "Mount";
    public string MountButtonIcon => IsVfsMounted ? "◉" : "○";
    public IBrush MountButtonColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public string SafeSwapStatusText => IsVfsMounted ? "Safe Swap: Active" : "Safe Swap: Standby";
    public IBrush SafeSwapStatusColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public ObservableCollection<Mod> DisplayedMods { get; } = new();

    private bool _isRebuildingDisplayedMods;

    private void RebuildDisplayedMods()
    {
        var savedMod = SelectedMod;
        _isRebuildingDisplayedMods = true;
        try
        {
            DisplayedMods.Clear();
            var query = AllMods.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(m => m.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            if (SelectedCategory != "All")
                query = query.Where(m => m.Category == SelectedCategory);
            foreach (var mod in query.OrderByDescending(m => m.Priority))
                DisplayedMods.Add(mod);
        }
        finally
        {
            _isRebuildingDisplayedMods = false;
        }
        if (savedMod != null && DisplayedMods.Contains(savedMod))
            SelectedMod = savedMod;
        OnPropertyChanged(nameof(DisplayedMods));
    }

    public List<Mod> SelectedMods { get; set; } = new();

    public event Action? RequestClearFocus;

    public event Action<CatModManager.Core.Models.Mod, string>? ModInstalled;

    /// <summary>Set by the View to show a confirmation dialog before deleting a profile.</summary>
    public Func<string, Task<bool>>? ConfirmDeleteProfile;

    public MainWindowViewModel(
        IModScanner modScanner,
        IProfileService profileService,
        IDriverService driverService,
        IModManagementService modManagementService,
        IProcessService processService,
        IVfsOrchestrationService vfsOrchestrator,
        IGameLaunchService gameLauncher,
        IFileService fileService,
        ICatPathService pathService,
        ILogService logService,
        IConfigService configService,
        IGameSupportService gameSupportService,
        IGameDiscoveryService gameDiscoveryService,
        IRootSwapService rootSwapService,
        UiExtensionHost? uiExtensionHost = null,
        PluginBrowserViewModel? pluginBrowserVm = null)
    {
        _modScanner = modScanner;
        _profileService = profileService;
        _driverService = driverService;
        _modManagementService = modManagementService;
        _processService = processService;
        _vfsOrchestrator = vfsOrchestrator;
        _gameLauncher = gameLauncher;
        _fileService = fileService;
        _pathService = pathService;
        _logService = logService;
        _configService = configService;
        _rootSwapService = rootSwapService;
        _gameSupportService   = gameSupportService;
        _gameDiscoveryService = gameDiscoveryService;
        _uiExtensionHost = uiExtensionHost;
        _pluginBrowserVm = pluginBrowserVm;

        _activeGameSupport = _gameSupportService.Default;

        _logService.OnLog += AddLog;
        _vfsOrchestrator.RecoverStaleMounts();
        
        CheckDriverStatus();
        SyncAllAppData();
        
        var lastProfile = _configService.Current.LastProfileName;
        if (!string.IsNullOrEmpty(lastProfile)) _ = LoadProfile(lastProfile);

        AllMods.CollectionChanged += OnAllModsCollectionChanged;

        _logService.Log("Cat Mod Manager initialized.");
    }

    private IDisposable SuppressAutoSave() => new AutoSaveSuppressor(this);

    private class AutoSaveSuppressor : IDisposable
    {
        private readonly MainWindowViewModel _vm;
        public AutoSaveSuppressor(MainWindowViewModel vm) { _vm = vm; Interlocked.Increment(ref _vm._autoSaveSuppressionCount); }
        public void Dispose() { Interlocked.Decrement(ref _vm._autoSaveSuppressionCount); }
    }

    [RelayCommand]
    private void ExecuteSidebarAction(ISidebarAction? action) => action?.Execute();

    [RelayCommand]
    private async Task OpenModsFolder() => await _processService.OpenFolderAsync(ModsFolderPath ?? "");

    [RelayCommand]
    private async Task OpenGameFolder() => await _processService.OpenFolderAsync(BaseFolderPath ?? "");

    [RelayCommand]
    private async Task OpenProfilesFolder() => await _processService.OpenFolderAsync(_pathService.ProfilesPath);

    [RelayCommand]
    private async Task OpenAppDataFolder() => await _processService.OpenFolderAsync(_pathService.BaseDataPath);

    [RelayCommand]
    private async Task OpenDownloadsFolder() => await _processService.OpenFolderAsync(DownloadsFolderPath ?? "");

    [RelayCommand]
    private async Task OpenDataSubFolder() =>
        await _processService.OpenFolderAsync(
            !string.IsNullOrEmpty(BaseFolderPath) && !string.IsNullOrEmpty(DataSubFolder)
                ? Path.Combine(BaseFolderPath, DataSubFolder)
                : DataSubFolder ?? "");

    [RelayCommand]
    private async Task OpenGameExecutableFolder() =>
        await _processService.OpenFolderAsync(
            !string.IsNullOrEmpty(GameExecutablePath)
                ? Path.GetDirectoryName(GameExecutablePath) ?? ""
                : "");

    public string AppDataPath => _pathService.BaseDataPath;

    [RelayCommand]
    private async Task OpenSelectedModFolder()
    {
        if (SelectedMod != null)
        {
            string path = SelectedMod.RootPath;
            if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
            await _processService.OpenFolderAsync(path);
        }
    }

    private void OnAllModsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Mod mod in e.NewItems) mod.PropertyChanged += OnModPropertyChanged;
        if (e.OldItems != null)
            foreach (Mod mod in e.OldItems) mod.PropertyChanged -= OnModPropertyChanged;
        AutoSave();
        RebuildDisplayedMods();
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Mod.IsEnabled) || e.PropertyName == nameof(Mod.Priority) || e.PropertyName == nameof(Mod.Name) || e.PropertyName == nameof(Mod.Version) || e.PropertyName == nameof(Mod.Category))
        {
            AutoSave();
            if (e.PropertyName == nameof(Mod.Category)) UpdateCategories();
            RebuildDisplayedMods();
        }
    }

    private void AutoSave() 
    { 
        if (IsAutoSaveSuppressed || string.IsNullOrWhiteSpace(CurrentProfileName)) return; 
        _ = SaveProfile(CurrentProfileName); 
    }

    private void SyncAllAppData()
    {
        var current = CurrentProfileName;
        var currentGameId = ActiveGameSupport?.GameId;

        RefreshProfilesAsync().ContinueWith(_ => {
            SyncCurrentSelection(current, currentGameId);
        }, TaskScheduler.FromCurrentSynchronizationContext());
        
        RefreshGameSupports();
    }

    private async Task RefreshProfilesAsync(string? selectAfter = null)
    {
        var profiles = await _profileService.ListProfilesAsync(_pathService.ProfilesPath);
        var names = profiles.Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
        
        AvailableProfiles.Clear();
        foreach (var p in names) if (p != null) AvailableProfiles.Add(p);

        if (!string.IsNullOrEmpty(selectAfter) && AvailableProfiles.Contains(selectAfter))
        {
            CurrentProfileName = selectAfter;
            ProfileDisplayName = selectAfter;
        }
    }

    private void RefreshGameSupports()
    {
        _gameSupportService.RefreshSupports(); 
        AvailableGameSupports.Clear();
        foreach (var s in _gameSupportService.GetAllSupports()) 
            AvailableGameSupports.Add(s);
    }

    private void SyncCurrentSelection(string? currentProfileName, string? currentGameId)
    {
        using (SuppressAutoSave())
        {
            if (!string.IsNullOrEmpty(currentProfileName) && AvailableProfiles.Contains(currentProfileName, StringComparer.OrdinalIgnoreCase))
            {
                CurrentProfileName = currentProfileName;
                ProfileDisplayName = currentProfileName;
            }

            if (!string.IsNullOrEmpty(currentGameId))
                ActiveGameSupport = AvailableGameSupports.FirstOrDefault(s => s.GameId == currentGameId) ?? _gameSupportService.Default;
        }
    }

    private void CheckDriverStatus()
    {
        IsDriverMissing = !_driverService.IsDriverInstalled();
        if (IsDriverMissing) _logService.Log("WARNING: File system driver not available.");
        else if (!IsDriverMissing) _logService.Log("VFS driver detected.");
    }

    partial void OnGameExecutablePathChanged(string? value) { AutoSave(); DetectSupport(value); }
    partial void OnModsFolderPathChanged(string? value) => AutoSave();
    partial void OnDataSubFolderChanged(string? value) => AutoSave();
    partial void OnDownloadsFolderPathChanged(string? value) => AutoSave();
    partial void OnBaseFolderPathChanged(string? value) => AutoSave();
    partial void OnLaunchArgumentsChanged(string? value) => AutoSave();
    partial void OnActiveGameSupportChanged(IGameSupport value) => AutoSave();

    [RelayCommand]
    private void DetectGameSupport() => DetectSupport(GameExecutablePath);

    [RelayCommand]
    private async Task AutoDetectGameAsync()
    {
        var dialogVm = new GameDetectionDialogViewModel(
            _gameDiscoveryService,
            AvailableGameSupports);

        var dialog = new CatModManager.Ui.Views.GameDetectionDialog(dialogVm);

        var owner = Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
                    ? dt.MainWindow : null;

        await dialog.ShowDialog(owner!);

        var result = dialogVm.Result;
        if (result == null) return;

        // RefreshSupports() inside ScanAsync recreates instances, so match by GameId
        // to get the reference that lives in AvailableGameSupports (used by the ComboBox).
        var resultMode = dialogVm.ResultMode ?? _gameSupportService.Default;
        var mode = AvailableGameSupports.FirstOrDefault(s => s.GameId == resultMode.GameId)
                   ?? resultMode;

        using (SuppressAutoSave())
        {
            GameExecutablePath  = result.ExecutablePath;
            BaseFolderPath      = result.GameFolder;
            DataSubFolder       = mode.DataSubFolder;
            ModsFolderPath      = Path.Combine(result.GameFolder, "mods");
            DownloadsFolderPath = Path.Combine(result.GameFolder, "downloads");
            ActiveGameSupport   = mode;
        }
        AutoSave();
        _logService.Log($"Game auto-detected: {result.DisplayName} [{result.StoreName}]");
        StatusMessage = $"Game configured: {result.DisplayName}";
    }

    private void DetectSupport(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var detected = _gameSupportService.DetectSupport(value);
            if (detected.GameId != "generic")
            {
                using (SuppressAutoSave()) ActiveGameSupport = detected;
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

    partial void OnCurrentProfileNameChanged(string? value)
    {
        if (IsAutoSaveSuppressed) return;
        if (!string.IsNullOrEmpty(value) && AvailableProfiles.Contains(value))
        {
            ProfileDisplayName = value;
            _ = LoadProfile(value);
        }
    }

    partial void OnSelectedInspectorTabChanged(int value)
    {
        if (value == 1) // Files tab
        {
            if (SelectedMod != null && CurrentModFiles.Count == 0) LoadModFiles(SelectedMod, CurrentModFolderPath);
        }
    }

    partial void OnSelectedModChanged(Mod? value)
    {
        if (_isRebuildingDisplayedMods) return;
        CurrentModFolderPath = "";
        CurrentModFiles.Clear();
        if (value != null) LoadModFiles(value);
    }

    [RelayCommand]
    private void SwitchToInfo() => SelectedInspectorTab = 0;

    [RelayCommand]
    private void SwitchToFiles()
    {
        SelectedInspectorTab = 1;
        if (SelectedMod != null && CurrentModFiles.Count == 0) LoadModFiles(SelectedMod, CurrentModFolderPath);
    }

    [RelayCommand]
    private void NavigateInto(ModFileItem item)
    {
        if (!item.IsDirectory || SelectedMod == null) return;
        string newRelativePath = string.IsNullOrEmpty(CurrentModFolderPath)
            ? item.Name
            : Path.Combine(CurrentModFolderPath, item.Name);
        LoadModFiles(SelectedMod, newRelativePath);
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (string.IsNullOrEmpty(CurrentModFolderPath) || SelectedMod == null) return;
        var parent = Path.GetDirectoryName(CurrentModFolderPath);
        LoadModFiles(SelectedMod, parent ?? "");
    }

    private void LoadModFiles(Mod mod, string relativePath = "")
    {
        try
        {
            CurrentModFolderPath = relativePath;
            CurrentModFiles.Clear();
            string fullPath = Path.Combine(mod.RootPath, relativePath);
            if (Directory.Exists(fullPath))
            {
                var dirs = Directory.GetDirectories(fullPath).Select(d => new ModFileItem(Path.GetFileName(d), true, 0));
                var files = Directory.GetFiles(fullPath).Select(f => new ModFileItem(Path.GetFileName(f), false, new FileInfo(f).Length));
                
                if (!string.IsNullOrEmpty(relativePath))
                    CurrentModFiles.Add(new ModFileItem("..", true, 0));

                foreach (var d in dirs.OrderBy(x => x.Name)) CurrentModFiles.Add(d);
                foreach (var f in files.OrderBy(x => x.Name)) CurrentModFiles.Add(f);
            }
        }
        catch (Exception ex) { _logService.LogError($"Failed to list mod files", ex); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    [RelayCommand]
    private void OpenPluginBrowser() 
    {
        if (_pluginBrowserVm != null)
        {
            var win = new CatModManager.Ui.Views.PluginBrowserWindow(_pluginBrowserVm);
            win.Show();
        }
    }

    [RelayCommand]
    private void ClearFocus() => RequestClearFocus?.Invoke();

    [RelayCommand]
    private async Task RenameProfile()
    {
        RequestClearFocus?.Invoke();
        if (string.IsNullOrEmpty(CurrentProfileName) || string.IsNullOrEmpty(ProfileDisplayName) || CurrentProfileName == ProfileDisplayName) return;
        
        await _profileLock.WaitAsync();
        try {
            string oldPath = _pathService.GetProfilePath(CurrentProfileName);
            string newPath = _pathService.GetProfilePath(ProfileDisplayName);
            if (_fileService.FileExists(oldPath)) {
                await SaveProfileInternal(ProfileDisplayName);
                File.Delete(oldPath);
                var newName = ProfileDisplayName;
                _logService.Log($"Profile renamed: '{CurrentProfileName}' -> '{newName}'");
                _configService.Current.LastProfileName = newName;
                _configService.Save();

                using (SuppressAutoSave())
                {
                    CurrentProfileName = newName;
                }
                await RefreshProfilesAsync();
                SyncCurrentSelection(CurrentProfileName, ActiveGameSupport?.GameId);
            }
        } 
        catch (Exception ex) { _logService.Log($"RENAME ERROR: {ex.Message}"); }
        finally { _profileLock.Release(); }
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
            else 
            {
                StatusMessage = $"Auto-mount failed: {mountResult.ErrorMessage}";
                return;
            }
        }

        var result = await _gameLauncher.LaunchGameAsync(
            GameExecutablePath, 
            LaunchArguments, 
            ActiveGameSupport, 
            AllMods.Where(m => m.IsEnabled));
        
        if (wasAutoMounted && IsVfsMounted)
        {
            _logService.Log("Game closed. Auto-unmounting...");
            await ToggleMountInternal();
        }

        IsVfsMounted = _vfsOrchestrator.IsMounted;
        UpdateMountButtonState();
        StatusMessage = result.IsSuccess ? "Ready" : result.ErrorMessage ?? "Launch Failed";
    }

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
                GameFolderPath = BaseFolderPath,
                DataSubFolder  = DataSubFolder,
                RootSwapOnly   = ActiveGameSupport?.RootSwapOnly ?? false,
                ActiveMods     = AllMods.Where(m => m.IsEnabled).ToList()
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
    private async Task Refresh()
    {
        RequestClearFocus?.Invoke();
        if (string.IsNullOrEmpty(ModsFolderPath)) return;
        StatusMessage = "Refreshing mods...";
        try {
            var scannedMods = await _modScanner.ScanDirectoryAsync(ModsFolderPath);
            var currentMap = AllMods.ToDictionary(m => m.RootPath, m => m);
            var newList = new List<Mod>();
            foreach (var mod in scannedMods) { if (currentMap.TryGetValue(mod.RootPath, out var existing)) newList.Add(existing); else { mod.Priority = -1; newList.Add(mod); } }
            
            using (SuppressAutoSave())
            {
                AllMods.Clear();
                foreach (var mod in newList.OrderByDescending(m => m.Priority))
                    AllMods.Add(mod);
                UpdatePriorities(); UpdateCategories();
            }
            RebuildDisplayedMods(); 
            AutoSave(); 
            StatusMessage = "Mods refreshed.";
        } catch (Exception ex) { _logService.Log($"REFRESH ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
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
                ModsFolderPath = path;
                AllMods.Clear();
                foreach (var mod in scannedMods) AllMods.Add(mod);
                UpdatePriorities(); 
                UpdateCategories(); 
            } 
            RebuildDisplayedMods(); 
            AutoSave(); 
        } catch (Exception ex) { _logService.Log($"SCAN ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task AddMod(string? sourcePath) 
    { 
        if (string.IsNullOrEmpty(sourcePath)) return; 
        if (string.IsNullOrEmpty(ModsFolderPath)) { _logService.Log("ERROR: Please select a Mods Folder first."); return; } 
        try { 
            StatusMessage = "Importing mod..."; 
            
            var installers = _uiExtensionHost?.ModInstallers ?? Enumerable.Empty<IModInstaller>();
            IModInstaller? chosenInstaller = null;
            foreach (var inst in installers)
            {
                if (inst.CanInstall(sourcePath))
                {
                    chosenInstaller = inst;
                    break;
                }
            }

            string installedPath;
            if (chosenInstaller != null)
            {
                var ctx = new SimpleInstallContext(ModsFolderPath, new LogServiceAdapter(_logService));
                var installResult = await chosenInstaller.InstallAsync(sourcePath, ctx);
                
                if (installResult == null || !installResult.IsSuccess) 
                { 
                    StatusMessage = installResult?.ErrorMessage ?? "Install cancelled."; 
                    return; 
                }

                string modName = Path.GetFileNameWithoutExtension(sourcePath);
                installedPath = await _modManagementService.InstallModFromMappingAsync(
                    sourcePath, modName, ModsFolderPath, installResult.FileMapping);
            }
            else
            {
                installedPath = await _modManagementService.InstallModAsync(sourcePath, ModsFolderPath); 
            }

            // Ask installation destination only for generic installs (no plugin handled it)
            if (chosenInstaller == null && !string.IsNullOrEmpty(BaseFolderPath))
            {
                var owner = Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
                    ? dt.MainWindow : null;

                var destDialog = new CatModManager.Ui.Views.InstallDestinationDialog();
                await destDialog.ShowDialog(owner!);

                if (destDialog.WasCancelled)
                {
                    // Clean up the extracted mod folder and bail
                    if (Directory.Exists(installedPath)) Directory.Delete(installedPath, true);
                    StatusMessage = "Installation cancelled.";
                    return;
                }

                if (destDialog.ResultIsGameFolder)
                {
                    var rootSubDir = Path.Combine(installedPath, "Root");
                    Directory.CreateDirectory(rootSubDir);
                    foreach (var f in Directory.GetFiles(installedPath, "*", SearchOption.TopDirectoryOnly))
                        File.Move(f, Path.Combine(rootSubDir, Path.GetFileName(f)));
                    _logService.Log($"Files moved to Root/ subfolder for game-root deployment at mount time.");
                }
            }

            string name = Path.GetFileNameWithoutExtension(installedPath);
            var mod = new Mod(name, installedPath, AllMods.Count, true, "Uncategorized");

            string sidecar = Path.Combine(installedPath, ".cmm_metadata.toml");
            if (File.Exists(sidecar))
            {
                try {
                    var meta = Toml.ReadFile<ModMetadata>(sidecar);
                    if (meta != null) {
                        mod.Name = meta.Name;
                        mod.Version = meta.Version;
                        mod.Category = meta.Category;
                    }
                } catch { }
            }

            AllMods.Insert(0, mod);
            UpdatePriorities(); UpdateCategories(); 
            RebuildDisplayedMods(); 
            SelectedMod = mod; 
            AutoSave(); 
            ModInstalled?.Invoke(mod, sourcePath);
            StatusMessage = "Mod imported."; 
            _logService.Log($"Mod imported to: {installedPath}"); 
        } catch (Exception ex) { _logService.Log($"IMPORT ERROR: {ex.Message}"); StatusMessage = $"IMPORT ERROR: {ex.Message}"; } 
    }

    private class SimpleInstallContext : IInstallContext
    {
        public string DestinationFolder { get; }
        public IPluginLogger Log { get; }
        public SimpleInstallContext(string dest, IPluginLogger log) { DestinationFolder = dest; Log = log; }
    }


    [RelayCommand]
    private async Task RemoveMod()
    {
        var modsToRemove = (SelectedMods != null && SelectedMods.Count > 1)
            ? SelectedMods.ToList()
            : (SelectedMod != null ? new List<Mod> { SelectedMod } : new List<Mod>());
        if (modsToRemove.Count == 0) return;
        try
        {
            using (SuppressAutoSave())
            {
                foreach (var mod in modsToRemove)
                {
                    if (!string.IsNullOrEmpty(BaseFolderPath) && mod.HasRootFolder)
                        await _rootSwapService.UndeployModAsync(mod.RootPath, BaseFolderPath);

                    AllMods.Remove(mod);
                    _logService.Log($"Mod '{mod.Name}' removed.");
                    if (!string.IsNullOrEmpty(ModsFolderPath) && mod.RootPath.StartsWith(ModsFolderPath))
                    {
                        if (Directory.Exists(mod.RootPath)) Directory.Delete(mod.RootPath, true);
                        else if (File.Exists(mod.RootPath)) File.Delete(mod.RootPath);
                    }
                }
                UpdatePriorities();
            }
            RebuildDisplayedMods();
            AutoSave();
        }
        catch (Exception ex) { _logService.Log($"REMOVE ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    private void UpdateCategories() 
    { 
        var currentCategories = AllMods.Select(m => m.Category).Distinct().ToList(); 
        foreach (var cat in currentCategories) 
            if (!Categories.Contains(cat)) Categories.Add(cat); 
    }

    private void AddLog(string formattedMessage) 
    { 
        void Action() 
        { 
            Logs.Insert(0, formattedMessage); 
            if (Logs.Count > 100) Logs.RemoveAt(100); 
        } 

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Action();
        }
        else if (Avalonia.Application.Current != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Action);
        }
        else
        {
            // Fallback for unit tests where no Avalonia App is running
            Action();
        }
    }

    public void MoveMod(int oldIndex, int newIndex) 
    { 
        if (oldIndex < 0 || oldIndex >= AllMods.Count || newIndex < 0 || newIndex >= AllMods.Count) return; 
        AllMods.Move(oldIndex, newIndex); 
        
        using (SuppressAutoSave())
        {
            UpdatePriorities(); 
        }

        RebuildDisplayedMods(); 
        AutoSave(); 
    }

    partial void OnSearchTextChanged(string? value) => RebuildDisplayedMods();
    partial void OnSelectedCategoryChanged(string value) => RebuildDisplayedMods();


    [RelayCommand]
    private void MoveUp() 
    { 
        if (SelectedMod == null) return; 
        int index = AllMods.IndexOf(SelectedMod); 
        if (index > 0) 
        { 
            AllMods.Move(index, index - 1); 
            
            using (SuppressAutoSave())
            {
                UpdatePriorities(); 
            }

            RebuildDisplayedMods(); 
            AutoSave(); 
        } 
    }

    [RelayCommand]
    private void MoveDown() 
    { 
        if (SelectedMod == null) return; 
        int index = AllMods.IndexOf(SelectedMod); 
        if (index < AllMods.Count - 1) 
        { 
            AllMods.Move(index, index + 1); 
            
            using (SuppressAutoSave())
            {
                UpdatePriorities(); 
            }

            RebuildDisplayedMods(); 
            AutoSave(); 
        } 
    }

    private void UpdatePriorities() { for (int i = 0; i < AllMods.Count; i++) AllMods[i].Priority = AllMods.Count - 1 - i; }

    [RelayCommand]
    private async Task NewProfile()
    {
        string newName = GetUniqueProfileName("NewProfile");

        using (SuppressAutoSave())
        {
            AllMods.Clear();
            ModsFolderPath = null;
            BaseFolderPath = null;
            GameExecutablePath = null;
            DownloadsFolderPath = null;
            DataSubFolder = null;
            ProfileDisplayName = newName;
            ActiveGameSupport = _gameSupportService.Default;
            LaunchArguments = "";
        }
        RebuildDisplayedMods();

        await SaveProfileInternal(newName);
        CurrentProfileName = newName;
    }

    private string GetUniqueProfileName(string baseName)
    {
        string name = baseName;
        int counter = 1;
        while (AvailableProfiles.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            name = $"{baseName} {counter++}";
        }
        return name;
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (string.IsNullOrEmpty(CurrentProfileName)) return;

        if (ConfirmDeleteProfile != null)
        {
            bool confirmed = await ConfirmDeleteProfile(CurrentProfileName);
            if (!confirmed) return;
        }

        if (IsVfsMounted)
        {
            _logService.Log("ERROR: Cannot delete active profile while Safe Swap is active. Please unmount first.");
            StatusMessage = "Unmount before deleting";
            return;
        }

        string profileToDelete = CurrentProfileName;
        string path = _pathService.GetProfilePath(profileToDelete);

        using (SuppressAutoSave())
        {
            await _profileLock.WaitAsync();
            try
            {
                CurrentProfileName = null;
                ProfileDisplayName = null;

                if (File.Exists(path))
                {
                    File.Delete(path);
                    _logService.Log($"Profile '{profileToDelete}' deleted from disk.");
                }
                
                AvailableProfiles.Remove(profileToDelete);
                await RefreshProfilesAsync();
                
                _logService.Log($"Profile '{profileToDelete}' removed from session.");
                StatusMessage = "Profile deleted.";
            }
            catch (Exception ex) 
            { 
                _logService.Log($"DELETE ERROR: {ex.Message}"); 
                return;
            }
            finally 
            { 
                _profileLock.Release(); 
            }
        }

        if (AvailableProfiles.Count > 0)
        {
            await LoadProfile(AvailableProfiles[0]);
        }
        else
        {
            await NewProfile();
        }
    }
    [RelayCommand]
    private async Task SaveProfile(string? name) 
    { 
        await _profileLock.WaitAsync();
        try { await SaveProfileInternal(name); }
        finally { _profileLock.Release(); }
    }

    private async Task SaveProfileInternal(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        try {
            string filePath = _pathService.GetProfilePath(name);
            await _profileService.SaveProfileAsync(new Profile { Name = name, ModsFolderPath = ModsFolderPath ?? "", BaseDataPath = BaseFolderPath ?? "", GameExecutablePath = GameExecutablePath ?? "", DataSubFolder = DataSubFolder ?? "", GameSupportId = ActiveGameSupport.GameId, LaunchArguments = LaunchArguments ?? "", DownloadsFolderPath = DownloadsFolderPath ?? "", Mods = AllMods.ToList() }, filePath);
            
            using (SuppressAutoSave())
            {
                _configService.Current.LastProfileName = name;
                _configService.Save();
            }
            await RefreshProfilesAsync();
        } catch (Exception ex) { _logService.Log($"SAVE ERROR: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task LoadProfile(string? name) 
    { 
        if (string.IsNullOrEmpty(name)) return;
        await _profileLock.WaitAsync();
        try {
            string filePath = _pathService.GetProfilePath(name);
            if (!_fileService.FileExists(filePath)) 
            {
                _logService.Log($"LOAD ERROR: Profile file not found: {name}");
                return;
            }
            var p = await _profileService.LoadProfileAsync(filePath);
            if (p != null) {
                using (SuppressAutoSave())
                {
                    ModsFolderPath = p.ModsFolderPath;
                    BaseFolderPath = p.BaseDataPath;
                    GameExecutablePath = p.GameExecutablePath;
                    DataSubFolder = p.DataSubFolder;
                    DownloadsFolderPath = string.IsNullOrEmpty(p.DownloadsFolderPath) && !string.IsNullOrEmpty(p.ModsFolderPath)
                        ? Path.Combine(p.ModsFolderPath, "downloads")
                        : p.DownloadsFolderPath;

                    AllMods.Clear();
                    foreach (var m in p.Mods) AllMods.Add(m);

                    // Always resolve from AvailableGameSupports so the ComboBox binding finds the same instance.
                    ActiveGameSupport = AvailableGameSupports.FirstOrDefault(s => s.GameId == p.GameSupportId)
                        ?? AvailableGameSupports.FirstOrDefault(s => s.CanSupport(p.GameExecutablePath))
                        ?? _gameSupportService.Default;
                    LaunchArguments = p.LaunchArguments;
                    CurrentProfileName = name;
                    ProfileDisplayName = name;
                    _configService.Current.LastProfileName = name;
                    _configService.Save();
                }
                UpdateCategories();
                RebuildDisplayedMods();
                _logService.Log($"Profile '{name}' loaded.");
            }
        } catch (Exception ex) { _logService.Log($"LOAD ERROR: {ex.Message}"); }
        finally { _profileLock.Release(); }
    }

    [RelayCommand]
    private async Task InstallDriver() { string msi = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dependencies", "winfsp.msi"); if (!File.Exists(msi)) return; try { bool success = await _processService.StartProcessAsync("msiexec.exe", $"/i \"{msi}\" /passive", true); if (success) CheckDriverStatus(); } catch (Exception ex) { _logService.Log($"INSTALL ERROR: {ex.Message}"); } }

    public void Shutdown()
    {
        _logService.Log("Shutdown detected. Cleaning up VFS...");
        _vfsOrchestrator.ShutdownCleanup();
        IsVfsMounted = false;
    }
}
