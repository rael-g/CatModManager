namespace CatModManager.Ui.Models;

public class InstalledPluginItem
{
    public string DisplayName { get; init; } = string.Empty;
    public string Version     { get; init; } = string.Empty;
    public string Author      { get; init; } = string.Empty;
    public bool   CanUninstall { get; init; }
    public string? PackageId  { get; init; }
}
