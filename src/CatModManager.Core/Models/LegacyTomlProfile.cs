using System.Collections.Generic;

namespace CatModManager.Core.Models;

/// <summary>
/// The shape a profile had when it was a TOML file: game paths and mod list in one object.
///
/// Kept apart from <see cref="Profile"/> on purpose. Profile has since given its paths to
/// <see cref="Game"/>, and the file on disk cannot follow — it is a record of what an older version
/// wrote. Reading it into today's model would mean keeping fields alive on Profile that nothing but
/// the import uses, which is exactly the drift the split was meant to end.
///
/// Read by <see cref="Services.TomlProfileService"/>, mapped by
/// <see cref="Services.ProfileImporter"/>, and gone when the import is.
/// </summary>
public class LegacyTomlProfile
{
    public string Name { get; set; } = "Default";
    public string ModsFolderPath { get; set; } = "";
    public string BaseDataPath { get; set; } = "";
    public string GameExecutablePath { get; set; } = "";
    public string GameSupportId { get; set; } = "generic";
    public string LaunchArguments { get; set; } = "";
    public string DownloadsFolderPath { get; set; } = "";

    public List<Mod>          Mods          { get; set; } = new();
    public List<ExternalTool> ExternalTools { get; set; } = new();
    public List<MountPointDef> UserMountPoints { get; set; } = new();
}
