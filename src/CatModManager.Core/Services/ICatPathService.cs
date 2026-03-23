namespace CatModManager.Core.Services;

public interface ICatPathService
{
    string BaseDataPath { get; }
    string ProfilesPath { get; }
    string GameSupportsPath { get; }
    string ActiveMountsFile { get; }
    string DownloadsPath { get; }
    string GetProfilePath(string profileName);
}
