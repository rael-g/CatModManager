using System;
using System.IO;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Tests.Support;

public class MockLogService : ILogService
{
    public event Action<string>? OnLog;
    public void Log(string message) => OnLog?.Invoke(message);
    public void LogError(string message, Exception? ex = null) {
        var msg = $"ERROR: {message}";
        if (ex != null) msg += $" {ex.Message}";
        OnLog?.Invoke(msg);
    }
}

public class MockCatPathService : ICatPathService
{
    public string BaseDataPath { get; set; }
    public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
    public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
    public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
    public string DownloadsPath => Path.Combine(BaseDataPath, "downloads");

    public MockCatPathService(string baseDataPath)
    {
        // Force absolute path in Temp if it looks relative
        if (!Path.IsPathRooted(baseDataPath))
            baseDataPath = Path.Combine(Path.GetTempPath(), "CMM_Tests", baseDataPath);

        BaseDataPath = baseDataPath;
        Directory.CreateDirectory(BaseDataPath);
        Directory.CreateDirectory(ProfilesPath);
        Directory.CreateDirectory(GameSupportsPath);
    }

    public string GetProfilePath(string profileName)
    {
        if (!profileName.EndsWith(".toml")) profileName += ".toml";
        return Path.Combine(ProfilesPath, profileName);
    }
}

public class MockPluginLoader : CatModManager.Ui.Plugins.PluginLoader
{
    public MockPluginLoader() : base(null!, null!, null!, null!, null!, null!, null!) { }
    public override System.Threading.Tasks.Task ShutdownAllAsync() => System.Threading.Tasks.Task.CompletedTask;
}
