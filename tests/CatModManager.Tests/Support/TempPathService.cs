using System;
using System.IO;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Support;

/// <summary>
/// An <see cref="ICatPathService"/> rooted in a throwaway directory.
///
/// Exists because using the real <see cref="CatPathService"/> in a test does not merely leave rows
/// behind in the developer's database — it points services at the developer's actual mount state.
/// A test calling something like <c>RecoverStaleMounts()</c> then performs directory moves and
/// deletes against whatever real game folder happens to be registered there.
///
/// Four separate test files each carry their own private copy of a mock like this one. New tests
/// should use this shared one.
/// </summary>
public sealed class TempPathService : ICatPathService, IDisposable
{
    public string BaseDataPath { get; }

    public TempPathService(string? baseDataPath = null)
    {
        BaseDataPath = baseDataPath ?? Path.Combine(Path.GetTempPath(), "CMM_Paths_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(BaseDataPath);
    }

    public string ProfilesPath     => Path.Combine(BaseDataPath, "profiles");
    public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
    public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
    public string DownloadsPath    => Path.Combine(BaseDataPath, "downloads");

    public string GetProfilePath(string profileName) => Path.Combine(ProfilesPath, profileName + ".toml");

    public void Dispose()
    {
        try { if (Directory.Exists(BaseDataPath)) Directory.Delete(BaseDataPath, true); } catch { }
    }
}
