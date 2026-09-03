using System;
using System.IO;

namespace CatModManager.Core.Services;

public class CatPathService : ICatPathService
{
    public string BaseDataPath { get; }
    public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
    public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
    public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
    public string DownloadsPath => Path.Combine(BaseDataPath, "downloads");

    /// <summary>
    /// The environment variable that moves the whole data directory somewhere else.
    ///
    /// It exists because the test suite has no other way to stay out of the real one: the UI tests
    /// boot the application's own DI container, which constructs this class itself, so there is no
    /// call site to pass an override to. Without it the suite wrote profiles and migrated the
    /// schema of the developer's actual cmm.db.
    /// </summary>
    public const string DataHomeVariable = "CMM_DATA_HOME";

    /// <summary>
    /// Where CMM keeps everything, for the callers that cannot be handed an <see cref="ICatPathService"/>.
    ///
    /// It is static because two of them exist: <c>LogService</c>, which is constructed before there
    /// is a path service to inject, and anything else tempted to spell the location out again. Each
    /// place that recomputed it was a place the environment override would not reach — which is how
    /// the test suite ended up writing to the developer's real log while carefully avoiding their
    /// real database.
    /// </summary>
    public static string ResolveDataHome()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(DataHomeVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.GetFullPath(Path.Combine(localAppData, "catmodmanager"));
    }

    public CatPathService(string? overrideBaseDir = null)
    {
        BaseDataPath = string.IsNullOrWhiteSpace(overrideBaseDir)
            ? ResolveDataHome()
            : Path.GetFullPath(overrideBaseDir);

        // Ensure all critical directories exist immediately in the correct OS location
        Directory.CreateDirectory(BaseDataPath);
        Directory.CreateDirectory(ProfilesPath);
        Directory.CreateDirectory(GameSupportsPath);
        Directory.CreateDirectory(DownloadsPath);
    }

    public string GetProfilePath(string profileName) 
    {
        if (!profileName.EndsWith(".toml")) profileName += ".toml";
        return Path.Combine(ProfilesPath, profileName);
    }
}
