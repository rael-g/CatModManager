using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.PluginSdk;

namespace CatModManager.Core.Services;

/// <summary>
/// Moves the profiles an existing installation has on disk into the database, once.
///
/// This is C#, not a migration script: Lilmihe migrates schema, and reading the user's TOML files is
/// not something SQL can do. It runs after the migrations, on startup.
///
/// The trigger is "the profiles table is empty", not a flag. A flag would be a second piece of state
/// that can disagree with the first, and the empty table is the condition that actually matters —
/// which also makes re-running the import as simple as deleting the rows.
///
/// The .toml files are left in place. They are the only copy of a user's profiles that predates this
/// change, and the release that debuts the new storage is the worst possible one to delete them in.
/// </summary>
public class ProfileImporter
{
    private readonly IProfileService     _profiles;
    private readonly IGameService        _games;
    private readonly TomlProfileService  _toml;
    private readonly ICatPathService     _paths;
    private readonly ILogService         _log;

    public ProfileImporter(IProfileService profiles, IGameService games, TomlProfileService toml,
                           ICatPathService paths, ILogService log)
    {
        _profiles = profiles;
        _games    = games;
        _toml     = toml;
        _paths    = paths;
        _log      = log;
    }

    public async Task ImportIfEmptyAsync()
    {
        try
        {
            if ((await _profiles.ListAllProfilesAsync()).Any()) return;
            if (!Directory.Exists(_paths.ProfilesPath)) return;

            var files = Directory.GetFiles(_paths.ProfilesPath, "*.toml").OrderBy(f => f).ToList();
            if (files.Count == 0) return;

            int imported = 0;
            foreach (var file in files)
            {
                var legacy = await _toml.LoadProfileAsync(file);
                if (legacy == null) continue;

                // The file name wins over whatever Name the TOML holds: the file name is what the
                // UI has been listing and what LastProfileName refers to, so a profile whose Name
                // field drifted would otherwise come back under a name nothing points at.
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name)) continue;

                await _profiles.SaveProfileAsync(new Profile
                {
                    Name          = name,
                    GameId        = await GameIdFor(legacy),
                    Mods          = legacy.Mods,
                    ExternalTools = legacy.ExternalTools,
                });
                imported++;
            }

            _log.Log($"Imported {imported} profile(s) from {_paths.ProfilesPath} into cmm.db. " +
                     "The .toml files were left in place as a backup.");
        }
        catch (Exception ex)
        {
            // A failed import must not stop the application: the user still has their files, and
            // starting with an empty profile list is recoverable in a way that refusing to start
            // is not.
            _log.LogError("Could not import the existing TOML profiles.", ex);
        }
    }

    /// <summary>
    /// The game this file describes, created if this is the first profile to mention it.
    ///
    /// Two profiles of the same installation have to come back sharing one game, or the mods each
    /// lists become two separate inventories over the same folder. The game folder is what decides
    /// that; a profile that never had one is imported parked, with no game and no mods, which is
    /// what it already was.
    /// </summary>
    private async Task<long?> GameIdFor(LegacyTomlProfile legacy)
    {
        if (string.IsNullOrWhiteSpace(legacy.BaseDataPath)) return null;

        var existing = await _games.FindByBasePathAsync(legacy.BaseDataPath);
        if (existing != null) return existing.Id;

        return await _games.SaveGameAsync(new Game
        {
            DisplayName         = Path.GetFileName(legacy.BaseDataPath.TrimEnd('/', '\\')),
            BaseDataPath        = legacy.BaseDataPath,
            ModsFolderPath      = legacy.ModsFolderPath,
            DownloadsFolderPath = legacy.DownloadsFolderPath,
            GameExecutablePath  = legacy.GameExecutablePath,
            GameSupportId       = legacy.GameSupportId,
            LaunchArguments     = legacy.LaunchArguments,
            UserMountPoints     = legacy.UserMountPoints,
        });
    }
}
