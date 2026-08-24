using System;
using System.Collections.Generic;

namespace CmmPlugin.BethesdaTools.Models;

/// <param name="LocalAppDataFolder">Subfolder in %LOCALAPPDATA% that holds plugins.txt.</param>
/// <param name="UsesStarFormat">Whether plugins.txt uses * prefix for enabled entries (Skyrim SE, FO4+).</param>
/// <param name="ImplicitMasters">
/// Base game and official DLC plugins the engine always loads first. They must never be written to
/// plugins.txt — listing them there is at best ignored and at worst corrupts the load order.
/// </param>
/// <param name="MyGamesFolder">
/// Subfolder under Documents/My Games holding the .ini files. Defaults to <paramref name="LocalAppDataFolder"/>.
/// </param>
/// <param name="CustomIniFile">
/// Name of the user override .ini (e.g. "StarfieldCustom.ini"). On these engines loose files in Data/
/// are ignored entirely unless the archive settings are overridden here, so CMM has to write it.
/// Null for engines that load loose files by default.
/// </param>
public record BethesdaGame(
    string LocalAppDataFolder,
    bool UsesStarFormat,
    IReadOnlySet<string>? ImplicitMasters = null,
    string? MyGamesFolder = null,
    string? CustomIniFile = null)
{
    public IReadOnlySet<string> Masters =>
        ImplicitMasters ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string GameFolder => MyGamesFolder ?? LocalAppDataFolder;

    public bool IsImplicitMaster(string fileName) => Masters.Contains(fileName);
}
