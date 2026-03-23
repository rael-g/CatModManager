namespace CmmPlugin.SaveManager.Models;

/// <summary>Describes where a game stores its save files on disk.</summary>
public class SaveGameDef
{
    public string   GameId       { get; init; } = "";
    public string   DisplayName  { get; init; } = "";

    /// <summary>Executable filenames (case-insensitive) that identify this game.</summary>
    public string[] ExecutableNames { get; init; } = [];

    /// <summary>
    /// Save folder path. Supports %APPDATA% and %LOCALAPPDATA% placeholders.
    /// If the path ends with \* we scan for the first numeric (Steam-ID) sub-folder automatically.
    /// </summary>
    public string SaveFolderPattern { get; init; } = "";
}
