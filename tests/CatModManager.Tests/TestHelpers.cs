using System;
using System.IO;
using CatModManager.Core.Services;

namespace CatModManager.Tests;

public class MockLogService : ILogService
{
    public event Action<string>? OnLog;
    public void Log(string message) => OnLog?.Invoke(message);
    public void LogError(string message, Exception? ex = null) => OnLog?.Invoke($"ERROR: {message} {ex?.Message}");
}

public class MockCatPathService : ICatPathService
{
    public string BaseDataPath { get; set; }
    public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
    public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
    public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");

    public MockCatPathService(string baseDataPath)
    {
        BaseDataPath = baseDataPath;
        if (!Directory.Exists(BaseDataPath)) Directory.CreateDirectory(BaseDataPath);
        if (!Directory.Exists(ProfilesPath)) Directory.CreateDirectory(ProfilesPath);
        if (!Directory.Exists(GameSupportsPath)) Directory.CreateDirectory(GameSupportsPath);
    }

    public string GetProfilePath(string profileName)
    {
        if (!profileName.EndsWith(".toml")) profileName += ".toml";
        return Path.Combine(ProfilesPath, profileName);
    }
}
