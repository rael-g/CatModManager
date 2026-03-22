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
    private readonly IDriverService           _driverService;
    private readonly IModManagementService    _modManagementService;
    private readonly IProcessService          _processService;
    private readonly IVfsOrchestrationService _vfsOrchestrator;
    private readonly IGameLaunchService       _gameLauncher;
    private readonly IFileService             _fileService;
    private readonly IRootSwapService         _rootSwapService;
    private readonly ICatPathService          _pathService;
    private readonly ILogService              _logService;
    private readonly IConfigService           _configService;
    private readonly IGameSupportService      _gameSupportService;
    private readonly IGameDiscoveryService    _gameDiscoveryService;
    private readonly UiExtensionHost?         _uiExtensionHost;
    private readonly PluginBrowserViewModel?  _pluginBrowserVm;

    // ── Sub-ViewModels ────────────────────────────────────────────────────────

    public ProfileManagerViewModel ProfileManager { get; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Mod> _allMods = new();
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private Mod? _selectedMod;
    [ObservableProperty] private int _selectedInspectorTab = 0;
    [ObservableProperty] private ObservableCollection<ModFileItem> _currentModFiles = new();
    [ObservableProperty] private string _currentModFolderPath = "";
    [ObservableProperty] private string? _modsFolderPath;
    [ObservableProperty] private string? _baseFolderPath;
    [ObservableProperty] private string? _gameExecutablePath;
    [ObservableProperty] private string? _launchArguments;
    [ObservableProperty] private string? _dataSubFolder;
    [ObservableProperty] private string? _downloadsFolderPath;
    [ObservableProperty] private bool _isVfsMounted;
    [ObservableProperty] private bool _isDriverMissing;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private IGameSupport _activeGameSupport;
    [ObservableProperty] private ObservableCollection<string> _logs = new();

    public ObservableCollection<IGameSupport> AvailableGameSupports { get; } = new();
    public ObservableCollection<string>       Categories            { get; } = new() { "All", "Uncategorized" };
    public ObservableCollection<IInspectorTab>  PluginInspectorTabs  { get; } = new();
    public ObservableCollection<ISidebarAction> PluginSidebarActions { get; } = new();
    public List<Mod> SelectedMods { get; set; } = new();

    partial void OnIsVfsMountedChanged(bool value) => UpdateMountButtonState();

    public string MountButtonText  => IsVfsMounted ? "Unmount" : "Mount";
    public string MountButtonIcon  => IsVfsMounted ? "◉" : "○";
    public IBrush MountButtonColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public string SafeSwapStatusText  => IsVfsMounted ? "Safe Swap: Active" : "Safe Swap: Standby";
    public IBrush SafeSwapStatusColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public ObservableCollection<Mod> DisplayedMods { get; } = new();
    private bool _isRebuildingDisplayedMods;

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
        UiExtensionHost?         uiExtensionHost = null,
        PluginBrowserViewModel?  pluginBrowserVm = null)
    {
        _modScanner           = modScanner;
        _driverService        = driverService;
        _modManagementService = modManagementService;
        _processService       = processService;
        _vfsOrchestrator      = vfsOrchestrator;
        _gameLauncher         = gameLauncher;
        _fileService          = fileService;
        _pathService          = pathService;
        _logService           = logService;
        _configService        = configService;
        _rootSwapService      = rootSwapService;
        _gameSupportService   = gameSupportService;
        _gameDiscoveryService = gameDiscoveryService;
        _uiExtensionHost      = uiExtensionHost;
        _pluginBrowserVm      = pluginBrowserVm;

        _activeGameSupport = _gameSupportService.Default;

        // Wire ProfileManagerViewModel
        ProfileManager = new ProfileManagerViewModel(profileService, pathService, fileService, configService, logService);
        ProfileManager.BuildSaveData  = BuildCurrentProfile;
        ProfileManager.IsVfsMounted   = () => IsVfsMounted;
        ProfileManager.ProfileLoaded += ApplyLoadedProfile;

        _logService.OnLog += AddLog;
        _vfsOrchestrator.RecoverStaleMounts();

        CheckDriverStatus();
        RefreshGameSupports();

        AllMods.CollectionChanged += OnAllModsCollectionChanged;

        var lastProfile = _configService.Current.LastProfileName;
        _ = ProfileManager.LoadInitialProfile(lastProfile);

        _logService.Log("Cat Mod Manager initialized.");
    }

    // ── Profile integration ───────────────────────────────────────────────────

    private Profile BuildCurrentProfile() => new Profile
    {
        Name               = ProfileManager.CurrentProfileName ?? "",
        ModsFolderPath     = ModsFolderPath     ?? "",
        BaseDataPath       = BaseFolderPath      ?? "",
        GameExecutablePath = GameExecutablePath  ?? "",
        DataSubFolder      = DataSubFolder       ?? "",
        GameSupportId      = ActiveGameSupport.GameId,
        LaunchArguments    = LaunchArguments     ?? "",
        DownloadsFolderPath = DownloadsFolderPath ?? "",
        Mods               = AllMods.ToList()
    };

    private void ApplyLoadedProfile(Profile p)
    {
        using (ProfileManager.SuppressAutoSave())
        {
            ModsFolderPath      = p.ModsFolderPath;
            BaseFolderPath      = p.BaseDataPath;
            GameExecutablePath  = p.GameExecutablePath;
            DataSubFolder       = p.DataSubFolder;
            DownloadsFolderPath = string.IsNullOrEmpty(p.DownloadsFolderPath) && !string.IsNullOrEmpty(p.ModsFolderPath)
                ? Path.Combine(p.ModsFolderPath, "downloads")
                : p.DownloadsFolderPath;

            AllMods.Clear();
            foreach (var m in p.Mods) AllMods.Add(m);

            ActiveGameSupport = AvailableGameSupports.FirstOrDefault(s => s.GameId == p.GameSupportId)
                ?? AvailableGameSupports.FirstOrDefault(s => s.CanSupport(p.GameExecutablePath))
                ?? _gameSupportService.Default;
            LaunchArguments = p.LaunchArguments;
        }
        UpdateCategories();
        RebuildDisplayedMods();
    }

    private void AutoSave() => ProfileManager.AutoSave();
    private IDisposable SuppressAutoSave() => ProfileManager.SuppressAutoSave();

    // ── Property changed handlers ─────────────────────────────────────────────

    partial void OnGameExecutablePathChanged(string? value) { AutoSave(); DetectSupport(value); }
    partial void OnModsFolderPathChanged(string? value)     => AutoSave();
    partial void OnDataSubFolderChanged(string? value)      => AutoSave();
    partial void OnDownloadsFolderPathChanged(string? value) => AutoSave();
    partial void OnBaseFolderPathChanged(string? value)     => AutoSave();
    partial void OnLaunchArgumentsChanged(string? value)    => AutoSave();
    partial void OnActiveGameSupportChanged(IGameSupport value) => AutoSave();

    partial void OnSelectedInspectorTabChanged(int value)
    {
        if (value == 1 && SelectedMod != null && CurrentModFiles.Count == 0)
            LoadModFiles(SelectedMod, CurrentModFolderPath);
    }

    partial void OnSelectedModChanged(Mod? value)
    {
        if (_isRebuildingDisplayedMods) return;
        CurrentModFolderPath = "";
        CurrentModFiles.Clear();
        if (value != null) LoadModFiles(value);
    }

    partial void OnSearchTextChanged(string? value)   => RebuildDisplayedMods();
    partial void OnSelectedCategoryChanged(string value) => RebuildDisplayedMods();

    // ── Mod list ──────────────────────────────────────────────────────────────

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
        finally { _isRebuildingDisplayedMods = false; }

        if (savedMod != null && DisplayedMods.Contains(savedMod))
            SelectedMod = savedMod;
        OnPropertyChanged(nameof(DisplayedMods));
    }

    private void OnAllModsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (Mod mod in e.NewItems) mod.PropertyChanged += OnModPropertyChanged;
        if (e.OldItems != null) foreach (Mod mod in e.OldItems) mod.PropertyChanged -= OnModPropertyChanged;
        AutoSave();
        RebuildDisplayedMods();
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Mod.IsEnabled) or nameof(Mod.Priority) or nameof(Mod.Name) or nameof(Mod.Version) or nameof(Mod.Category))
        {
            AutoSave();
            if (e.PropertyName == nameof(Mod.Category)) UpdateCategories();
            RebuildDisplayedMods();
        }
    }

    private void UpdateCategories()
    {
        var current = AllMods.Select(m => m.Category).Distinct();
        foreach (var cat in current)
            if (!Categories.Contains(cat)) Categories.Add(cat);
    }

    private void UpdatePriorities()
    {
        for (int i = 0; i < AllMods.Count; i++)
            AllMods[i].Priority = AllMods.Count - 1 - i;
    }

    public void MoveMod(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= AllMods.Count || newIndex < 0 || newIndex >= AllMods.Count) return;
        AllMods.Move(oldIndex, newIndex);
        using (SuppressAutoSave()) UpdatePriorities();
        RebuildDisplayedMods();
        AutoSave();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedMod == null) return;
        int index = AllMods.IndexOf(SelectedMod);
        if (index <= 0) return;
        AllMods.Move(index, index - 1);
        using (SuppressAutoSave()) UpdatePriorities();
        RebuildDisplayedMods();
        AutoSave();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedMod == null) return;
        int index = AllMods.IndexOf(SelectedMod);
        if (index >= AllMods.Count - 1) return;
        AllMods.Move(index, index + 1);
        using (SuppressAutoSave()) UpdatePriorities();
        RebuildDisplayedMods();
        AutoSave();
    }

    // ── Mod inspector ─────────────────────────────────────────────────────────

    [RelayCommand] private void SwitchToInfo()  => SelectedInspectorTab = 0;

    [RelayCommand]
    private void SwitchToFiles()
    {
        SelectedInspectorTab = 1;
        if (SelectedMod != null && CurrentModFiles.Count == 0)
            LoadModFiles(SelectedMod, CurrentModFolderPath);
    }

    [RelayCommand]
    private void NavigateInto(ModFileItem item)
    {
        if (!item.IsDirectory || SelectedMod == null) return;
        string rel = string.IsNullOrEmpty(CurrentModFolderPath)
            ? item.Name
            : Path.Combine(CurrentModFolderPath, item.Name);
        LoadModFiles(SelectedMod, rel);
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (string.IsNullOrEmpty(CurrentModFolderPath) || SelectedMod == null) return;
        LoadModFiles(SelectedMod, Path.GetDirectoryName(CurrentModFolderPath) ?? "");
    }

    private void LoadModFiles(Mod mod, string relativePath = "")
    {
        try
        {
            CurrentModFolderPath = relativePath;
            CurrentModFiles.Clear();
            string fullPath = Path.Combine(mod.RootPath, relativePath);
            if (!Directory.Exists(fullPath)) return;

            if (!string.IsNullOrEmpty(relativePath))
                CurrentModFiles.Add(new ModFileItem("..", true, 0));
            foreach (var d in Directory.GetDirectories(fullPath).OrderBy(x => x).Select(d => new ModFileItem(Path.GetFileName(d), true, 0)))
                CurrentModFiles.Add(d);
            foreach (var f in Directory.GetFiles(fullPath).OrderBy(x => x).Select(f => new ModFileItem(Path.GetFileName(f), false, new FileInfo(f).Length)))
                CurrentModFiles.Add(f);
        }
        catch (Exception ex) { _logService.LogError("Failed to list mod files", ex); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    // ── Game config ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void DetectGameSupport() => DetectSupport(GameExecutablePath);

    [RelayCommand]
    private async Task AutoDetectGameAsync()
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
        var mode = AvailableGameSupports.FirstOrDefault(s => s.GameId == resultMode.GameId) ?? resultMode;

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

    private void RefreshGameSupports()
    {
        _gameSupportService.RefreshSupports();
        AvailableGameSupports.Clear();
        foreach (var s in _gameSupportService.GetAllSupports())
            AvailableGameSupports.Add(s);
    }

    private void CheckDriverStatus()
    {
        IsDriverMissing = !_driverService.IsDriverInstalled();
        if (IsDriverMissing) _logService.Log("WARNING: File system driver not available.");
    }

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
            GameExecutablePath, LaunchArguments, ActiveGameSupport, AllMods.Where(m => m.IsEnabled));

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
        if (string.IsNullOrEmpty(ModsFolderPath)) return;
        StatusMessage = "Refreshing mods...";
        try
        {
            var scannedMods = await _modScanner.ScanDirectoryAsync(ModsFolderPath);
            var currentMap  = AllMods.ToDictionary(m => m.RootPath, m => m);
            var newList = scannedMods.Select(mod => {
                if (currentMap.TryGetValue(mod.RootPath, out var existing)) return existing;
                mod.Priority = -1;
                return mod;
            }).ToList();

            using (SuppressAutoSave())
            {
                AllMods.Clear();
                foreach (var mod in newList.OrderByDescending(m => m.Priority)) AllMods.Add(mod);
                UpdatePriorities(); UpdateCategories();
            }
            RebuildDisplayedMods();
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
                ModsFolderPath = path;
                AllMods.Clear();
                foreach (var mod in scannedMods) AllMods.Add(mod);
                UpdatePriorities(); UpdateCategories();
            }
            RebuildDisplayedMods();
            AutoSave();
        }
        catch (Exception ex) { _logService.Log($"SCAN ERROR: {ex.Message}"); StatusMessage = $"ERROR: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task AddMod(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return;
        if (string.IsNullOrEmpty(ModsFolderPath)) { _logService.Log("ERROR: Please select a Mods Folder first."); return; }
        try
        {
            StatusMessage = "Importing mod...";

            var installers     = _uiExtensionHost?.ModInstallers ?? Enumerable.Empty<IModInstaller>();
            IModInstaller? chosen = installers.FirstOrDefault(i => i.CanInstall(sourcePath));

            string installedPath;
            if (chosen != null)
            {
                var ctx           = new SimpleInstallContext(ModsFolderPath, new LogServiceAdapter(_logService));
                var installResult = await chosen.InstallAsync(sourcePath, ctx);
                if (installResult == null || !installResult.IsSuccess)
                {
                    StatusMessage = installResult?.ErrorMessage ?? "Install cancelled.";
                    return;
                }
                string modName = Path.GetFileNameWithoutExtension(sourcePath);
                installedPath  = await _modManagementService.InstallModFromMappingAsync(
                    sourcePath, modName, ModsFolderPath, installResult.FileMapping);
            }
            else
            {
                installedPath = await _modManagementService.InstallModAsync(sourcePath, ModsFolderPath);

                if (!string.IsNullOrEmpty(BaseFolderPath))
                {
                    var owner = Avalonia.Application.Current?.ApplicationLifetime
                                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
                                ? dt.MainWindow : null;

                    var destDialog = new CatModManager.Ui.Views.InstallDestinationDialog();
                    await destDialog.ShowDialog(owner!);

                    if (destDialog.WasCancelled)
                    {
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
                    }
                }
            }

            string name = Path.GetFileNameWithoutExtension(installedPath);
            var mod = new Mod(name, installedPath, AllMods.Count, true, "Uncategorized");

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

            AllMods.Insert(0, mod);
            UpdatePriorities(); UpdateCategories();
            RebuildDisplayedMods();
            SelectedMod = mod;
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
        var toRemove = (SelectedMods is { Count: > 1 })
            ? SelectedMods.ToList()
            : SelectedMod != null ? new List<Mod> { SelectedMod } : new List<Mod>();
        if (toRemove.Count == 0) return;
        try
        {
            using (SuppressAutoSave())
            {
                foreach (var mod in toRemove)
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

    // ── Sidebar / misc ────────────────────────────────────────────────────────

    [RelayCommand] private void ExecuteSidebarAction(ISidebarAction? action) => action?.Execute();
    [RelayCommand] private void ClearFocus() => RequestClearFocus?.Invoke();
    [RelayCommand] private void OpenPluginBrowser()
    {
        if (_pluginBrowserVm != null) new CatModManager.Ui.Views.PluginBrowserWindow(_pluginBrowserVm).Show();
    }

    [RelayCommand] private async Task OpenModsFolder()          => await _processService.OpenFolderAsync(ModsFolderPath ?? "");
    [RelayCommand] private async Task OpenGameFolder()          => await _processService.OpenFolderAsync(BaseFolderPath ?? "");
    [RelayCommand] private async Task OpenProfilesFolder()      => await _processService.OpenFolderAsync(_pathService.ProfilesPath);
    [RelayCommand] private async Task OpenAppDataFolder()       => await _processService.OpenFolderAsync(_pathService.BaseDataPath);
    [RelayCommand] private async Task OpenDownloadsFolder()     => await _processService.OpenFolderAsync(DownloadsFolderPath ?? "");
    [RelayCommand] private async Task OpenDataSubFolder()       =>
        await _processService.OpenFolderAsync(!string.IsNullOrEmpty(BaseFolderPath) && !string.IsNullOrEmpty(DataSubFolder)
            ? Path.Combine(BaseFolderPath, DataSubFolder) : DataSubFolder ?? "");
    [RelayCommand] private async Task OpenGameExecutableFolder() =>
        await _processService.OpenFolderAsync(!string.IsNullOrEmpty(GameExecutablePath)
            ? Path.GetDirectoryName(GameExecutablePath) ?? "" : "");
    [RelayCommand] private async Task OpenSelectedModFolder()
    {
        if (SelectedMod == null) return;
        string path = SelectedMod.RootPath;
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
        IsVfsMounted = false;
    }
}
