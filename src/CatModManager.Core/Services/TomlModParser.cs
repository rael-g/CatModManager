using System.IO;
using CatModManager.Core.Models;
using Nett;

namespace CatModManager.Core.Services;

public class ModMetadata
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Category { get; set; } = "Uncategorized";
}

public class TomlModParser : IModParser
{
    private const string InfoFileName = "mod_info.toml";

    public Mod? ParseModInfo(string path)
    {
        // Only read mod_info.toml from already-installed mod directories (backward compatibility).
        // New installs get their metadata from the profile or from plugin events (e.g. ModInstalled).
        try
        {
            if (Directory.Exists(path))
            {
                string infoPath = Path.Combine(path, InfoFileName);
                if (File.Exists(infoPath))
                {
                    var meta = Toml.ReadFile<ModMetadata>(infoPath);
                    return new Mod(meta.Name, path, 0, true, meta.Category) { Version = meta.Version };
                }
            }
        }
        catch { }
        return null;
    }
}
