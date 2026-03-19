namespace CatModManager.PluginSdk;

public class LaunchContext
{
    public string? ExecutablePath { get; init; }
    public string? Arguments { get; init; }
    public string? MountPath { get; init; }
    public string? GameId { get; init; }
}
