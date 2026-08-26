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
    private readonly Action                   _refreshModMountPointDisplayNames;
    private readonly Action                   _syncActiveModsToState;

    public ProfileCoordinator(
        IProfileService profileService,
        IConfigService configService,
        ILogService logService,
        AppSessionState sessionState,
        Func<GameConfigViewModel> gameConfigProvider,
        Func<ModListViewModel> modListProvider,
        Action refreshModMountPointDisplayNames,
        Action syncActiveModsToState)
    {
        _profileService = profileService;
        _configService = configService;
        _logService = logService;
        _sessionState = sessionState;
        _gameConfigProvider = gameConfigProvider;
        _modListProvider = modListProvider;
        _refreshModMountPointDisplayNames = refreshModMountPointDisplayNames;
        _syncActiveModsToState = syncActiveModsToState;
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
            GameSupportId = config.ActiveGameSupport?.GameId ?? "generic",
            ModsFolderPath = config.ModsFolderPath ?? "",
            BaseDataPath = config.BaseFolderPath ?? "",
            GameExecutablePath = config.GameExecutablePath ?? "",
            DownloadsFolderPath = config.DownloadsFolderPath ?? "",
            LaunchArguments = config.LaunchArguments ?? "",
            UserMountPoints = config.UserMountPoints.ToList()
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
            config.ModsFolderPath = profile.ModsFolderPath;
            config.BaseFolderPath = profile.BaseDataPath;
            config.GameExecutablePath = profile.GameExecutablePath;
            config.DownloadsFolderPath = profile.DownloadsFolderPath;
            config.LaunchArguments = profile.LaunchArguments;

            config.UserMountPoints.Clear();
            foreach (var mp in profile.UserMountPoints) config.UserMountPoints.Add(mp);

            if (!string.IsNullOrEmpty(profile.GameSupportId))
            {
                var game = config.AvailableGameSupports.FirstOrDefault(g => g.GameId == profile.GameSupportId);
                if (game != null) config.ActiveGameSupport = game;
            }

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

    public void SaveLastProfileName(string? profileName)
    {
        if (string.IsNullOrEmpty(profileName)) return;
        _configService.Current.LastProfileName = profileName;
        _configService.Save();
    }
}
