using System.IO;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

/// <summary>
/// The one rule for "you picked an executable, here is everything else".
///
/// This is the basic promise of the application: choosing the game's executable is all the user has
/// to do, and the game folder, the mods folder and the downloads folder follow from it. A game mode
/// is optional and changes none of this.
///
/// One place, because it is asked from two — the configuration panel as the user types, and adding
/// a game from the menu — and two copies of a rule about paths drift into two different layouts on
/// disk.
/// </summary>
public static class GameFolderDefaults
{
    /// <summary>
    /// Fills in whatever the game has not been told yet. Anything already set is left alone: the
    /// user who pointed the mods folder somewhere else meant it.
    /// </summary>
    public static void Fill(Game game)
    {
        if (string.IsNullOrEmpty(game.BaseDataPath) && !string.IsNullOrEmpty(game.GameExecutablePath))
            game.BaseDataPath = Path.GetDirectoryName(game.GameExecutablePath) ?? "";

        if (string.IsNullOrEmpty(game.BaseDataPath)) return;

        if (string.IsNullOrEmpty(game.ModsFolderPath))
            game.ModsFolderPath = Path.Combine(game.BaseDataPath, "cmm", "mods");

        if (string.IsNullOrEmpty(game.DownloadsFolderPath))
            game.DownloadsFolderPath = Path.Combine(game.BaseDataPath, "cmm", "downloads");

        if (string.IsNullOrEmpty(game.DisplayName))
            game.DisplayName = Path.GetFileName(game.BaseDataPath.TrimEnd('/', '\\'));
    }
}
