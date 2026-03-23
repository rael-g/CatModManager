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

    public CatPathService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        BaseDataPath = Path.Combine(localAppData, "catmodmanager");
        
        // Ensure all critical directories exist immediately
        if (!Directory.Exists(BaseDataPath)) Directory.CreateDirectory(BaseDataPath);
        if (!Directory.Exists(ProfilesPath)) Directory.CreateDirectory(ProfilesPath);
        if (!Directory.Exists(GameSupportsPath)) Directory.CreateDirectory(GameSupportsPath);
        if (!Directory.Exists(DownloadsPath)) Directory.CreateDirectory(DownloadsPath);
    }

    public string GetProfilePath(string profileName) 
    {
        if (!profileName.EndsWith(".toml")) profileName += ".toml";
        return Path.Combine(ProfilesPath, profileName);
    }
}
