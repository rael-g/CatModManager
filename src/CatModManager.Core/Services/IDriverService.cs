namespace CatModManager.Core.Services;

public interface IDriverService
{
    bool IsDriverInstalled();
}

/// <summary>
/// Driver service for HardlinkDriver — kernel32 CreateHardLinkW is always
/// present on Windows, and hard links via POSIX are always available on Linux.
/// Returns true on all supported platforms.
/// </summary>
public class HardlinkDriverService : IDriverService
{
    public bool IsDriverInstalled() => true;
}
