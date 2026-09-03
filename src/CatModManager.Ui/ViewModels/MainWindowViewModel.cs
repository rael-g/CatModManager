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

    // Coordinators (specialized logic)
    public ProfileCoordinator         Profiles  { get; }
    public VfsLifecycleCoordinator    Vfs       { get; }
    public ModInstallationCoordinator Installer { get; }

    // Sub-ViewModels
    public GameManagerViewModel    GameManager    { get; }
    public ProfileManagerViewModel ProfileManager { get; }
    public GameConfigViewModel      GameConfig     { get; }
    public ModListViewModel         ModList        { get; }
    public ModInspectorViewModel    Inspector      { get; }
    public ExternalToolsViewModel   Tools          { get; }

    /// <summary>
    /// The plugin browser's state. Owned here rather than created on demand so the search results and
    /// the installed list survive closing the window. The view opens it — showing a window needs an
    /// owner, which is not something a view model should know about.
    /// </summary>
    public PluginBrowserViewModel? PluginBrowser { get; }

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
        IGameService             gameService,
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
        PluginBrowser         = pluginBrowserVm;

        // 1. Initialize Sub-ViewModels
        GameConfig = new GameConfigViewModel(gameSupportService, gameDiscoveryService, logService);
        ModList    = new ModListViewModel();
        Inspector  = new ModInspectorViewModel(logService);
        Tools      = new ExternalToolsViewModel(processService, vfsOrchestrator, logService);

        // 2. Initialize Coordinators
        Profiles  = new ProfileCoordinator(profileService, configService, logService, sessionState, () => GameConfig, () => ModList, () => Tools, RefreshModMountPointDisplayNames, SyncActiveModsToState);
        Vfs       = new VfsLifecycleCoordinator(vfsOrchestrator, logService, () => GameConfig, () => ModList, SyncActiveModsToState);
        // Finishing an install has to persist, same as any other edit to the list. This callback was
        // empty, so a freshly installed mod lived only in memory: it worked until the app closed and
        // was gone on the next start, still sitting in the mods folder with nothing referring to it.
        // It survived only by accident, when some later action — toggling it, reordering — saved.
        Installer = new ModInstallationCoordinator(modManagementService, modScanner, fileService, logService, sessionState, uiExtensionHost, () => GameConfig, () => ModList,
            (mod, source) => { SyncActiveModsToState(); ProfileManager.AutoSave(); });

        // 3. Wire Events & Callbacks
        Vfs.PropertyChanged       += (s, e) =>
        {
            if (e.PropertyName == nameof(Vfs.StatusMessage)) StatusMessage = Vfs.StatusMessage;
            else OnPropertyChanged(e.PropertyName);
        };
        Installer.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
        
        GameManager = new GameManagerViewModel(gameService, profileService, logService);
        GameManager.IsVfsMounted = () => Vfs.IsVfsMounted;

        ProfileManager = new ProfileManagerViewModel(profileService, configService, logService);
        ProfileManager.BuildSaveData  = () => Profiles.BuildCurrentProfile(ProfileManager.CurrentProfileName ?? "Untitled");
        ProfileManager.IsVfsMounted   = () => Vfs.IsVfsMounted;
        ProfileManager.CurrentGameId  = () => GameManager.CurrentGame is { Id: > 0 } g ? g.Id : null;
        ProfileManager.ProfileLoaded += p => Profiles.ApplyLoadedProfile(p);

        // Switching game is: show its configuration, then open its profiles. In that order — the
        // profile's mod list is read against the game's inventory, and applying it over the previous
        // game's paths is what used to make the list look empty.
        GameManager.GameActivated += async game =>
        {
            Profiles.ApplyLoadedGame(game);
            Profiles.SaveLastOpened(game?.Id, null);
            await ProfileManager.OpenGameProfilesAsync(
                game?.Id == _configService.Current.LastGameId
                    ? _configService.Current.LastProfileId : null);
        };

        GameConfig.SaveGame = () =>
        {
            SyncActiveModsToState();
            if (GameManager.CurrentGame is not { Id: > 0 } game) return;
            Profiles.ApplyConfigToGame(game);
            _ = GameManager.SaveCurrentGameAsync();
        };
        GameConfig.GameFoldersAdopted = AdoptGameFoldersAsync;
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
        Tools.RequestUnmount = async () =>
        {
            if (!Vfs.IsVfsMounted) return OperationResult.Success();
            return await Vfs.ToggleMountInternal();
        };
        Tools.AutoSave = () => ProfileManager.AutoSave();

        _sessionState.RequestInstallModAction = (path, _) => 
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Installer.InstallModAtMountPointAsync(path, null));

        _logService.OnLog += AddLog;
        _vfsOrchestrator.RecoverStaleMounts();

        // The game is what gets loaded now — selecting it is what opens its profiles, through
        // GameActivated above.
        InitialLoadTask = Task.Run(
            async () => await GameManager.LoadInitialGameAsync(_configService.Current.LastGameId));
    }

    /// <summary>
    /// The startup profile load, kicked off by the constructor. Exposed rather than fire-and-forget
    /// because it ends in RefreshListAsync, which clears AvailableProfiles and refills it from a
    /// snapshot of the profiles folder taken when the load began. Anything that adds a profile
    /// while it is still in flight gets erased by that clear — the snapshot predates it. Awaiting
    /// this is how a caller says "the list is settled" instead of hoping it is.
    /// </summary>
    public Task InitialLoadTask { get; }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand] private Task ToggleMount() => Vfs.ToggleMount();

    [RelayCommand]
    private async Task LaunchGame()
    {
        if (GameConfig.ActiveGameSupport == null) { StatusMessage = "Select a game first."; return; }

        // Only what this launch mounted gets unmounted afterwards. A mount you made yourself is a
        // decision of yours, and outliving the game is the point of having made it.
        bool mountedForThisLaunch = false;

        if (!Vfs.IsVfsMounted)
        {
            var res = await Vfs.ToggleMountInternal();
            if (!res.IsSuccess) { StatusMessage = $"Mount failed: {res.ErrorMessage}"; return; }
            mountedForThisLaunch = true;
        }

        StatusMessage = "Launching game...";
        try
        {
            var activeMods = ModList.AllMods.Where(m => m.IsEnabled && !m.IsBroken && !m.IsInstalling).ToList();
            var result = await _gameLauncher.LaunchGameAsync(GameConfig.GameExecutablePath, GameConfig.LaunchArguments, GameConfig.ActiveGameSupport, activeMods, GameConfig.BaseFolderPath);

            if (!result.IsSuccess)
            {
                // The result used to be discarded, so a launch that never happened still reported
                // "Game running."
                StatusMessage = $"Launch failed: {result.ErrorMessage}";
                return;
            }

            if (result.Value)
            {
                StatusMessage = "Game closed.";
                if (mountedForThisLaunch) await UnmountAfterPlaying();
            }
            else
            {
                // Started something, never saw the game. Steam refusing a licence looks like this,
                // and so does a first Proton run that is still compiling shaders past the wait
                // window — which is why the mount stays put. Pulling the files out from under a
                // game that is merely slow to appear would be far worse than leaving it mounted.
                StatusMessage = "Launched, but the game was never seen running — still mounted.";
                _logService.Log("Launch could not be confirmed; leaving the mount in place.");
            }
        }
        catch (Exception ex) { _logService.LogError("Launch failed", ex); StatusMessage = $"Launch error: {ex.Message}"; }
    }

    /// <summary>
    /// Closes out a mount this launch opened. Best effort: the game is already gone, so failing to
    /// unmount is worth reporting but not worth turning the launch into an error.
    /// </summary>
    private async Task UnmountAfterPlaying()
    {
        if (!Vfs.IsVfsMounted) return;

        var res = await Vfs.ToggleMountInternal();
        StatusMessage = res.IsSuccess
            ? "Game closed — unmounted."
            : $"Game closed, but unmounting failed: {res.ErrorMessage}";
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

                    // Only ever delete inside the mods folder. A mod whose install never finished
                    // still carries the source archive as its ModRootPath, and deleting that wipes
                    // the user's download — the archive they would need to install it again.
                    if (!IsInsideModsFolder(mod.ModRootPath))
                    {
                        _logService.Log($"Removed '{mod.Name}' from the list only: '{mod.ModRootPath}' " +
                                        "is outside the mods folder, so nothing was deleted.");
                        continue;
                    }

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

    /// <summary>
    /// Whether <paramref name="path"/> lives inside the configured mods folder. Compared on full,
    /// separator-terminated paths so that a sibling folder sharing a name prefix — "…/mods_backup"
    /// next to "…/mods" — is not mistaken for being inside it.
    /// </summary>
    internal bool IsInsideModsFolder(string? path)
    {
        string? modsFolder = GameConfig.ModsFolderPath;
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(modsFolder)) return false;

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modsFolder))
                          + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>
    /// Reads <paramref name="folder"/> and works out what would change, without changing anything.
    /// Returns null when the folder is unusable.
    /// </summary>
    public async Task<ModReconcileResult?> ScanModsFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !_fileService.DirectoryExists(folder))
        {
            // Bailing out rather than reconciling against nothing. An unreadable or unset folder
            // scans as empty, which is indistinguishable from "every mod was deleted" — and acting
            // on that would wipe the list over a typo in a path.
            StatusMessage = "Mods folder is not set or does not exist — nothing to scan.";
            return null;
        }

        var scanned = (await _modScanner.ScanDirectoryAsync(folder)).ToList();
        return ModFolderReconciler.Reconcile(ModList.AllMods.ToList(), scanned);
    }

    /// <summary>
    /// Called right after the game folders were filled in for this profile — by picking the
    /// executable, or by auto-detect. Until now those two paths only wrote the text boxes, so a game
    /// that already had mods came up with an empty list and looked like the mods had been lost.
    ///
    /// Saving the game first is what makes the folders findable at all; reloading the profile then
    /// brings back everything already installed for that game, and only then is the folder scanned,
    /// so mods dropped in by hand are picked up as well.
    ///
    /// Applied without confirming, unlike choosing the mods folder by hand: nothing is being
    /// replaced here — the list was empty a moment ago.
    /// </summary>
    public async Task AdoptGameFoldersAsync()
    {
        if (GameManager.CurrentGame is { Id: > 0 } game)
        {
            Profiles.ApplyConfigToGame(game);
            await GameManager.SaveCurrentGameAsync();
        }

        if (ProfileManager.CurrentProfile == null) return;

        await ProfileManager.SaveProfile();
        await ProfileManager.ReloadCurrentAsync();

        if (await ScanModsFolderAsync(GameConfig.ModsFolderPath) is { } result)
            ApplyModFolderScan(result);
    }

    /// <summary>Commits the outcome of <see cref="ScanModsFolderAsync"/> to the list and the profile.</summary>
    public void ApplyModFolderScan(ModReconcileResult result)
    {
        if (result.Added.Count == 0 && result.Removed.Count == 0)
        {
            StatusMessage = $"Up to date — {ModList.AllMods.Count} mods.";
            return;
        }

        using (ModList.SuppressUpdates())
        {
            ModList.AllMods.Clear();
            foreach (var mod in result.Mods)
                ModList.AllMods.Add(mod);
        }

        ModList.UpdatePriorities();
        ModList.UpdateCategories();
        ModList.RebuildDisplayedMods();
        RefreshModMountPointDisplayNames();
        ProfileManager.AutoSave();

        StatusMessage = $"{result.Added.Count} added, {result.Removed.Count} removed.";
        _logService.Log($"Mods folder scan: {result.Added.Count} added, {result.Removed.Count} removed.");
    }

    /// <summary>
    /// Re-reads the mods folder and reconciles it with the profile: picks up mods added by hand,
    /// drops rows whose folder is gone, and leaves everything else exactly as the user arranged it.
    ///
    /// Applied without confirming, unlike changing the folder. Here the folder has not moved, so a
    /// removal means that mod really was deleted from under the app — reporting it is the point.
    /// </summary>
    [RelayCommand]
    private async Task Refresh()
    {
        if (await ScanModsFolderAsync(GameConfig.ModsFolderPath) is { } result)
            ApplyModFolderScan(result);
    }
    [RelayCommand] private void ClearFocus() => RequestClearFocus?.Invoke();
    [RelayCommand] private void DeleteMountPoint(MountPointDef? mp) { if (mp != null) GameConfig.UserMountPoints.Remove(mp); }
    [RelayCommand] private void ExecuteSidebarAction(ISidebarAction? a) => a?.Execute();

    public Task OpenFolder(string path) => _processService.OpenFolderAsync(path);
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
    [RelayCommand] private async Task OpenGameDataFolder()
    {
        var points = GameConfig.EffectiveMountPoints;
        if (points.Count == 0) { await _processService.OpenFolderAsync(GameConfig.BaseFolderPath ?? ""); return; }
        
        await _processService.OpenFolderAsync(points[0].ResolveAbsolute(GameConfig.BaseFolderPath));
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
        Profiles.SaveLastOpened(GameManager.CurrentGame?.Id, ProfileManager.CurrentProfile?.Id);
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
