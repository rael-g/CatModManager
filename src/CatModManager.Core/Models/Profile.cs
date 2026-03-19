using System.Collections.Generic;

namespace CatModManager.Core.Models;

public class Profile
{
    public string Name { get; set; } = "Default";
    public string ModsFolderPath { get; set; } = "";
    public string BaseDataPath { get; set; } = "";
    public string GameExecutablePath { get; set; } = "";
    /// <summary>Relative path inside the game folder where the VFS mounts (e.g. "Data" or "LiesofP\Content\Paks\~mods").</summary>
    public string DataSubFolder { get; set; } = "";

    // Identificador da definição de suporte de jogo associada (usado pelo plugin Nexus para NexusDomain)
    public string GameSupportId { get; set; } = "generic";

    // Argumentos de lançamento específicos deste perfil (ex: -windowed, -no-splash)
    public string LaunchArguments { get; set; } = "";

    public string DownloadsFolderPath { get; set; } = "";

    public List<Mod> Mods { get; set; } = new();
}


