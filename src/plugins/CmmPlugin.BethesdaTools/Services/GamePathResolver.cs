using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;

namespace CmmPlugin.BethesdaTools.Services;

/// <summary>
/// Resolves the per-user config locations Bethesda games read at startup
/// (<c>%LOCALAPPDATA%\&lt;Game&gt;\Plugins.txt</c> and
/// <c>%USERPROFILE%\Documents\My Games\&lt;Game&gt;\</c>).
///
/// On Windows these come straight from the shell folders. On Linux the game runs under
/// Proton/Wine, so the real files live inside a Wine prefix — writing to the host's
/// <c>~/.local/share</c> (which is what <see cref="Environment.SpecialFolder.LocalApplicationData"/>
/// returns there) produces a file no game will ever read.
/// </summary>
public class GamePathResolver
{
    private readonly IFileService _fileService;
    private readonly IPluginLogger _log;

    public GamePathResolver(IFileService fileService, IPluginLogger log)
    {
        _fileService = fileService;
        _log = log;
    }

    /// <summary>Full path to Plugins.txt, or null when the prefix could not be located.</summary>
    public string? GetPluginsTextPath(BethesdaGame game, string? gameExecutablePath)
    {
        string? localAppData = GetLocalAppDataRoot(gameExecutablePath, game.LocalAppDataFolder);
        if (localAppData == null) return null;

        string gameDir = ResolveChildDirectory(localAppData, game.LocalAppDataFolder);
        return ResolveChildFile(gameDir, "Plugins.txt");
    }

    /// <summary>
    /// The game's Data folder. Prefers the path CMM already resolved for the active profile and
    /// falls back to the folder next to the executable.
    /// </summary>
    public static string? GetDataFolder(string? configuredDataFolder, string? gameExecutablePath)
    {
        if (!string.IsNullOrEmpty(configuredDataFolder)) return configuredDataFolder;

        string? exeDir = string.IsNullOrEmpty(gameExecutablePath)
            ? null
            : Path.GetDirectoryName(gameExecutablePath);

        return string.IsNullOrEmpty(exeDir) ? null : Path.Combine(exeDir, "Data");
    }

    /// <summary>Full path to the "My Games/&lt;Game&gt;" folder holding the .ini files, or null.</summary>
    public string? GetMyGamesPath(BethesdaGame game, string? gameExecutablePath)
    {
        string? myGames = GetDocumentsRoot(gameExecutablePath, game.GameFolder);
        return myGames == null ? null : ResolveChildDirectory(myGames, game.GameFolder);
    }

    // ── Roots ────────────────────────────────────────────────────────────────

    private string? GetLocalAppDataRoot(string? gameExecutablePath, string gameFolder)
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return SelectRoot(gameExecutablePath, gameFolder, userDir =>
            ResolveChildDirectory(ResolveChildDirectory(userDir, "AppData"), "Local"));
    }

    private string? GetDocumentsRoot(string? gameExecutablePath, string gameFolder)
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return SelectRoot(gameExecutablePath, gameFolder, userDir =>
            ResolveChildDirectory(ResolveChildDirectory(userDir, "Documents"), "My Games"));
    }

    /// <summary>
    /// Picks the best Wine prefix for this game. We don't know the Steam AppId, so several prefixes
    /// may be probed — prefer one that already holds this game's folder, and only fall back to the
    /// first usable prefix (so a first run can still create the file) when none does.
    /// </summary>
    private string? SelectRoot(string? gameExecutablePath, string gameFolder, Func<string, string> toRoot)
    {
        string? fallback = null;

        foreach (string prefix in EnumerateCandidatePrefixes(gameExecutablePath))
        {
            string? userDir = GetUserDirectory(prefix);
            if (userDir == null) continue;

            string root = toRoot(userDir);
            if (_fileService.DirectoryExists(ResolveChildDirectory(root, gameFolder)))
            {
                _log.Log($"[BethesdaTools] Using Wine prefix: {prefix}");
                return root;
            }

            fallback ??= root;
        }

        if (fallback == null)
            _log.LogError(
                "[BethesdaTools] Could not locate a Wine/Proton prefix for this game — its config " +
                "files cannot be read or written. Run the game once through Steam/Proton first, " +
                "or set WINEPREFIX.", null);

        return fallback;
    }

    // ── Wine/Proton prefix discovery ─────────────────────────────────────────

    /// <summary>
    /// Prefixes worth probing, most specific first: the explicit environment overrides, then every
    /// Steam compatdata prefix sitting next to the game's own steamapps/common install.
    /// </summary>
    private IEnumerable<string> EnumerateCandidatePrefixes(string? gameExecutablePath)
    {
        string? compatData = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH");
        if (!string.IsNullOrEmpty(compatData))
            yield return Path.Combine(compatData, "pfx");

        string? winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        if (!string.IsNullOrEmpty(winePrefix))
            yield return winePrefix;

        string? steamApps = FindSteamAppsRoot(gameExecutablePath);
        if (steamApps == null) yield break;

        // Prefixes are keyed by Steam AppId; we don't know it, so probe each one and let
        // GetUserDirectory + the caller's folder lookup pick the one that has this game's data.
        string compatRoot = Path.Combine(steamApps, "compatdata");
        foreach (string dir in _fileService.GetDirectories(compatRoot))
            yield return Path.Combine(dir, "pfx");
    }

    /// <summary>Walks up from the executable looking for the enclosing "steamapps" directory.</summary>
    private static string? FindSteamAppsRoot(string? gameExecutablePath)
    {
        if (string.IsNullOrEmpty(gameExecutablePath)) return null;

        var dir = new DirectoryInfo(Path.GetDirectoryName(gameExecutablePath) ?? string.Empty);
        while (dir != null)
        {
            if (string.Equals(dir.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Returns <c>&lt;prefix&gt;/drive_c/users/&lt;user&gt;</c>. Proton always uses "steamuser";
    /// hand-rolled Wine prefixes use the real login name, so fall back to whatever single
    /// non-Public user directory exists.
    /// </summary>
    private string? GetUserDirectory(string prefix)
    {
        string driveC = ResolveChildDirectory(prefix, "drive_c");
        string users = ResolveChildDirectory(driveC, "users");
        if (!_fileService.DirectoryExists(users)) return null;

        var candidates = _fileService.GetDirectories(users)
            .Where(d => !string.Equals(Path.GetFileName(d), "Public", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return candidates.FirstOrDefault(d =>
                   string.Equals(Path.GetFileName(d), "steamuser", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    // ── Case-insensitive path resolution ─────────────────────────────────────
    //
    // Wine prefixes live on a case-sensitive filesystem but hold paths written by Windows
    // code, so casing is inconsistent ("Plugins.txt" vs "plugins.txt", "AppData" vs "appdata").
    // These helpers match an existing entry regardless of case and otherwise return the
    // requested name unchanged, so callers can still create it.

    private string ResolveChildDirectory(string parent, string name)
    {
        string direct = Path.Combine(parent, name);
        if (_fileService.DirectoryExists(direct)) return direct;

        var match = _fileService.GetDirectories(parent)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));

        return match ?? direct;
    }

    private string ResolveChildFile(string parent, string name)
    {
        string direct = Path.Combine(parent, name);
        if (_fileService.FileExists(direct)) return direct;
        if (!_fileService.DirectoryExists(parent)) return direct;

        var match = _fileService.GetFiles(parent, "*")
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));

        return match ?? direct;
    }
}
