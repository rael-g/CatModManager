using System.Collections.Generic;

namespace CatModManager.Core.Models;

public class ExternalTool
{
    public string Name            { get; set; } = "";
    public string ExecutablePath  { get; set; } = "";
    public string Arguments       { get; set; } = "";
    public bool   MountBeforeLaunch { get; set; } = false;
}

public class Profile
{
    public string Name { get; set; } = "Default";
    public string ModsFolderPath { get; set; } = "";
    public string BaseDataPath { get; set; } = "";
    public string GameExecutablePath { get; set; } = "";

    // Identificador da definição de suporte de jogo associada (usado pelo plugin Nexus para NexusDomain)
    public string GameSupportId { get; set; } = "generic";

    // Argumentos de lançamento específicos deste perfil (ex: -windowed, -no-splash)
    public string LaunchArguments { get; set; } = "";

    public string DownloadsFolderPath { get; set; } = "";

    public List<Mod>          Mods          { get; set; } = new();
    public List<ExternalTool> ExternalTools { get; set; } = new();

    /// <summary>
    /// User-defined mount points for this profile.
    /// Combined with game-defined mount points at runtime (game-defined take precedence by Id).
    /// </summary>
    public List<MountPointDef> UserMountPoints { get; set; } = new();
}


