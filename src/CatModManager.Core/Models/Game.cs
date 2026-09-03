using System.Collections.Generic;

namespace CatModManager.Core.Models;

/// <summary>
/// An installation the user manages: where the game is, where its mods and downloads go, and which
/// game definition applies to it.
///
/// These fields lived on <see cref="Profile"/> until the game became a row of its own. They belong
/// here because they describe the installation, not the arrangement of mods over it — two profiles
/// of the same game disagree about which mods are on, never about where the game is.
/// </summary>
public class Game
{
    /// <summary>Zero for a game that has not been saved yet.</summary>
    public long Id { get; set; }

    /// <summary>
    /// What the user sees in the game menu. Seeded from the game folder's name and editable from
    /// there on. Nothing is keyed off it, so two games may perfectly well share one.
    /// </summary>
    public string DisplayName { get; set; } = "";

    public string BaseDataPath        { get; set; } = "";
    public string ModsFolderPath      { get; set; } = "";
    public string DownloadsFolderPath { get; set; } = "";
    public string GameExecutablePath  { get; set; } = "";

    /// <summary>
    /// The game definition in use, or "generic". Optional by design: picking the executable is the
    /// whole of what CMM needs, and a game mode only adds the mount points a particular game wants.
    /// </summary>
    public string GameSupportId { get; set; } = "generic";

    /// <summary>
    /// Extra arguments on the launch line — "-windowed", or the whole steam invocation a Proton
    /// game needs. How the installation is started, which is not something two mod arrangements
    /// over it should be able to disagree about.
    /// </summary>
    public string LaunchArguments { get; set; } = "";

    /// <summary>
    /// The folders the user defined for this installation, on top of whatever the game definition
    /// already provides. Which mod goes into which of them stays with the profile — that is an
    /// arrangement, and it is the half that really does differ between profiles.
    /// </summary>
    public List<MountPointDef> UserMountPoints { get; set; } = new();

    /// <summary>
    /// SKSE, xEdit, Wrye Bash — the programs that operate on this installation. They were the
    /// profile's until 006, which meant the Tools tab emptied itself when the user switched mod
    /// lists, even though nothing about where xEdit lives had changed.
    /// </summary>
    public List<ExternalTool> ExternalTools { get; set; } = new();
}
