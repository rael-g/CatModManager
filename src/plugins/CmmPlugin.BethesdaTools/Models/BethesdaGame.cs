namespace CmmPlugin.BethesdaTools.Models;

/// <param name="LocalAppDataFolder">Subfolder in %LOCALAPPDATA% that holds plugins.txt.</param>
/// <param name="UsesStarFormat">Whether plugins.txt uses * prefix for enabled entries (Skyrim SE, FO4+).</param>
public record BethesdaGame(string LocalAppDataFolder, bool UsesStarFormat);
