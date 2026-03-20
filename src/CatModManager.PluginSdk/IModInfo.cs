namespace CatModManager.PluginSdk;

/// <summary>Read/write view of a mod exposed to plugins — no dependency on CatModManager.Core.</summary>
public interface IModInfo
{
    string Name     { get; set; }
    string Version  { get; set; }
    string Category { get; set; }
    string RootPath { get; }
    bool   IsEnabled { get; }
    int    Priority  { get; }
}
