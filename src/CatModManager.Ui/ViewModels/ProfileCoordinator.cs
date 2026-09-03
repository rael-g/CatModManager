using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Ui.Plugins;

namespace CatModManager.Ui.ViewModels;

public partial class ProfileCoordinator : ObservableObject
{
    private readonly IProfileService       _profileService;
    private readonly IConfigService        _configService;
    private readonly ILogService           _logService;
    private readonly AppSessionState       _sessionState;
    private readonly Func<GameConfigViewModel> _gameConfigProvider;
    private readonly Func<ModListViewModel>   _modListProvider;
    private readonly Func<ExternalToolsViewModel> _toolsProvider;
    private readonly Action                   _refreshModMountPointDisplayNames;
    private readonly Action                   _syncActiveModsToState;

    public ProfileCoordinator(
        IProfileService profileService,
        IConfigService configService,
        ILogService logService,
        AppSessionState sessionState,
        Func<GameConfigViewModel> gameConfigProvider,
        Func<ModListViewModel> modListProvider,
        Func<ExternalToolsViewModel> toolsProvider,
        Action refreshModMountPointDisplayNames,
        Action syncActiveModsToState)
    {
        _profileService = profileService;
        _configService = configService;
        _logService = logService;
        _sessionState = sessionState;
        _gameConfigProvider = gameConfigProvider;
        _modListProvider = modListProvider;
        _toolsProvider = toolsProvider;
        _refreshModMountPointDisplayNames = refreshModMountPointDisplayNames;
        _syncActiveModsToState = syncActiveModsToState;
    }

    /// <summary>
    /// Copies the configuration panel into <paramref name="game"/>, which is the object the game
    /// manager holds and saves. The panel edits an installation, not the profile that happens to be
    /// open over it — two profiles of one game never disagree about where the game is.
    /// </summary>
    public void ApplyConfigToGame(Game game)
    {
        var config = _gameConfigProvider();

        game.ModsFolderPath      = config.ModsFolderPath      ?? "";
        game.BaseDataPath        = config.BaseFolderPath      ?? "";
        game.GameExecutablePath  = config.GameExecutablePath  ?? "";
        game.DownloadsFolderPath = config.DownloadsFolderPath ?? "";
        game.GameSupportId       = config.ActiveGameSupport?.GameId ?? "generic";
        game.LaunchArguments     = config.LaunchArguments     ?? "";
        game.UserMountPoints     = config.UserMountPoints.ToList();
    }

    /// <summary>Fills the configuration panel from the game the user just opened.</summary>
    public void ApplyLoadedGame(Game? game)
    {
        var config = _gameConfigProvider();

        // Saving is suppressed as well as detection: each assignment below would otherwise write
        // the whole panel back to the game while it is still half filled in.
        using (config.SuppressSaving())
        using (config.SuppressDetection())
        {
            config.ModsFolderPath      = game?.ModsFolderPath      ?? "";
            config.BaseFolderPath      = game?.BaseDataPath        ?? "";
            config.GameExecutablePath  = game?.GameExecutablePath  ?? "";
            config.DownloadsFolderPath = game?.DownloadsFolderPath ?? "";

            config.LaunchArguments     = game?.LaunchArguments     ?? "";

            var support = config.AvailableGameSupports.FirstOrDefault(
                g => g.GameId == (game?.GameSupportId ?? "generic"));
            if (support != null) config.ActiveGameSupport = support;

            config.UserMountPoints.Clear();
            foreach (var mp in game?.UserMountPoints ?? []) config.UserMountPoints.Add(mp);
        }

        // A previous run killed mid-install leaves its extraction workspace behind — potentially
        // hundreds of megabytes, and invisible in a file manager because the name starts with a dot.
        // Only folders predating this process are removed, so an install in flight is never hit.
        TempWorkspace.CleanupStale(config.ModsFolderPath, _logService.Log);
    }

    public Profile BuildCurrentProfile(string profileName)
    {
        var config = _gameConfigProvider();
        var modList = _modListProvider();

        return new Profile
        {
            Name = profileName,
            // A mod still installing has no installed folder yet: its ModRootPath is still the
            // source archive, and only becomes the real folder once the install finishes. Persisting
            // it means that after closing or crashing mid-install it comes back looking installed
            // while pointing at the downloaded archive — and removing it then deletes that archive.
            Mods = modList.AllMods.Where(m => !m.IsInstalling).ToList(),

            // Profile.ExternalTools existed and was serialised from the day the Tools tab was
            // written, but nothing ever filled it in or read it back — so a tool lived in memory
            // only and was gone on the next start. Tools belong to the game rather than the
            // profile, and will move there once the two are separated; until then this is where
            // they can be kept without inventing a second store for them.
            ExternalTools = _toolsProvider().GetTools()
        };
    }

    public void ApplyLoadedProfile(Profile profile)
    {
        var config = _gameConfigProvider();
        var modList = _modListProvider();

        using (modList.SuppressAutoSave?.Invoke())
        using (modList.SuppressUpdates())
        using (config.SuppressDetection())
        {
            // Only what a profile owns. The folders, the game mode, the launch line and the mount
            // points all come from the game, and are applied by ApplyLoadedGame before this runs.
            _toolsProvider().LoadTools(profile.ExternalTools);

            modList.AllMods.Clear();
            foreach (var m in profile.Mods) modList.AllMods.Add(m);

            MigrateOrphanedMountPointIds(modList.AllMods, config.EffectiveMountPoints);
        }

        _refreshModMountPointDisplayNames();
        _syncActiveModsToState();
        
        _sessionState.NotifyProfileChanged(profile.Name);
        _logService.Log($"Profile '{profile.Name}' applied with {profile.Mods.Count} mods.");
    }

    /// <summary>
    /// Resets any MountPointId that no longer refers to an existing mount point back to null
    /// ("use the default"). Older builds stored the literal "Default" when no mount point was
    /// available, and no game defines that id — such mods match no mount point at all and are
    /// silently never mounted. This also cleans up ids left behind when a game definition changes.
    /// </summary>
    internal static void MigrateOrphanedMountPointIds(
        IEnumerable<Mod> mods, IReadOnlyList<MountPointDef> mountPoints)
    {
        foreach (var mod in mods)
        {
            if (string.IsNullOrEmpty(mod.MountPointId)) continue;

            bool exists = mountPoints.Any(mp =>
                string.Equals(mp.Id, mod.MountPointId, StringComparison.OrdinalIgnoreCase));

            if (!exists) mod.MountPointId = null;
        }
    }

    /// <summary>Remembers what to reopen next time: the game, and which of its profiles.</summary>
    public void SaveLastOpened(long? gameId, long? profileId)
    {
        if (gameId is > 0)    _configService.Current.LastGameId    = gameId.Value;
        if (profileId is > 0) _configService.Current.LastProfileId = profileId.Value;
        _configService.Save();
    }
}
