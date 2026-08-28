using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CatModManager.PluginSdk;

/// <summary>
/// Turns a Windows user-folder path — <c>%APPDATA%\RE2</c>, <c>%USERPROFILE%\Documents\My Games\…</c> —
/// into a real path on this machine.
///
/// On Windows that is just environment expansion. On Linux it is not: the variables do not exist,
/// the separators are wrong, and even once both are fixed the folder is not on the host at all. A
/// game running under Proton writes its saves and config inside the Wine prefix, under
/// <c>drive_c/users/steamuser</c>. Expanding such a path with the host environment yields either the
/// literal string back or a plausible-looking host path that no game will ever read or write.
///
/// The prefix is found from the game's install folder, which is the reliable anchor: the configured
/// executable may be a launcher, a script, or a bare command.
/// </summary>
public class WindowsUserFolders
{
    private readonly IFileService  _fileService;
    private readonly IPluginLogger _log;

    public WindowsUserFolders(IFileService fileService, IPluginLogger log)
    {
        _fileService = fileService;
        _log         = log;
    }

    /// <summary>
    /// Resolves <paramref name="windowsPath"/>, or null when it cannot be located.
    ///
    /// A trailing <c>\*</c> means "the one numeric subfolder", which is how the Steam-ID directory
    /// that FromSoftware and others create is addressed.
    /// </summary>
    /// <param name="gameFolder">The game's install folder — the anchor for finding the Wine prefix.</param>
    public string? Resolve(string? windowsPath, string? gameFolder, string? executablePath = null)
    {
        if (string.IsNullOrWhiteSpace(windowsPath)) return null;

        bool wantsNumericChild = windowsPath.EndsWith(@"\*", StringComparison.Ordinal)
                              || windowsPath.EndsWith("/*", StringComparison.Ordinal);
        if (wantsNumericChild) windowsPath = windowsPath[..^2];

        string? resolved = ResolveFolder(windowsPath, gameFolder, executablePath);
        if (resolved == null) return null;

        if (!wantsNumericChild) return resolved;

        // Steam-ID subfolder. Fall back to any single subfolder: a Wine prefix sometimes holds a
        // differently-named one, and returning the parent would make a backup of the wrong tree.
        var children = _fileService.GetDirectories(resolved).ToList();
        return children.FirstOrDefault(d => Path.GetFileName(d)!.All(char.IsDigit))
            ?? (children.Count == 1 ? children[0] : null);
    }

    private string? ResolveFolder(string windowsPath, string? gameFolder, string? executablePath)
    {
        var (variable, rest) = SplitLeadingVariable(windowsPath);

        if (OperatingSystem.IsWindows())
        {
            string expanded = Environment.ExpandEnvironmentVariables(windowsPath);
            return _fileService.DirectoryExists(expanded) ? expanded : null;
        }

        if (variable == null)
        {
            // Already an absolute host path, or something we cannot interpret.
            return _fileService.DirectoryExists(windowsPath) ? windowsPath : null;
        }

        string? best = null;
        foreach (string userDir in EnumerateWinePrefixUserDirs(gameFolder, executablePath))
        {
            string? root = MapVariable(variable, userDir);
            if (root == null) continue;

            string candidate = DescendCaseInsensitively(root, rest);
            if (_fileService.DirectoryExists(candidate))
            {
                _log.Log($"[SaveManager] Resolved {variable} via Wine prefix: {candidate}");
                return candidate;
            }

            best ??= candidate;
        }

        // Only report a path that exists. A save folder that is not there yet is not a folder to
        // back up, and pointing a restore at an invented path would write saves into nowhere.
        return best != null && _fileService.DirectoryExists(best) ? best : null;
    }

    /// <summary>Splits "%APPDATA%\RE2\Sub" into ("APPDATA", ["RE2", "Sub"]).</summary>
    private static (string? Variable, IReadOnlyList<string> Segments) SplitLeadingVariable(string windowsPath)
    {
        var segments = windowsPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return (null, Array.Empty<string>());

        string head = segments[0];
        if (head.Length < 3 || head[0] != '%' || head[^1] != '%') return (null, segments);

        return (head[1..^1].ToUpperInvariant(), segments[1..]);
    }

    private string? MapVariable(string variable, string userDir) => variable switch
    {
        "APPDATA"      => DescendCaseInsensitively(userDir, ["AppData", "Roaming"]),
        "LOCALAPPDATA" => DescendCaseInsensitively(userDir, ["AppData", "Local"]),
        "USERPROFILE"  => userDir,
        _              => null,
    };

    /// <summary>
    /// Wine prefixes hold Windows-authored paths on a case-sensitive filesystem, so casing is
    /// inconsistent ("AppData" vs "appdata", "Saves" vs "saves"). Match what is actually there,
    /// and otherwise keep the requested name so the caller can still see what was looked for.
    /// </summary>
    private string DescendCaseInsensitively(string root, IReadOnlyList<string> segments)
    {
        string current = root;
        foreach (string segment in segments)
        {
            string direct = Path.Combine(current, segment);
            if (_fileService.DirectoryExists(direct)) { current = direct; continue; }

            string? match = _fileService.GetDirectories(current)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), segment, StringComparison.OrdinalIgnoreCase));

            current = match ?? direct;
        }
        return current;
    }

    // ── Wine prefix discovery ────────────────────────────────────────────────

    private IEnumerable<string> EnumerateWinePrefixUserDirs(string? gameFolder, string? executablePath)
    {
        foreach (string prefix in EnumerateCandidatePrefixes(gameFolder, executablePath))
        {
            string? userDir = GetUserDirectory(prefix);
            if (userDir != null) yield return userDir;
        }
    }

    private IEnumerable<string> EnumerateCandidatePrefixes(string? gameFolder, string? executablePath)
    {
        string? compatData = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH");
        if (!string.IsNullOrEmpty(compatData)) yield return Path.Combine(compatData, "pfx");

        string? winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        if (!string.IsNullOrEmpty(winePrefix)) yield return winePrefix;

        string? steamApps = WalkUpToSteamApps(gameFolder)
                         ?? WalkUpToSteamApps(string.IsNullOrEmpty(executablePath)
                                              ? null : Path.GetDirectoryName(executablePath));
        if (steamApps == null) yield break;

        // Prefixes are keyed by Steam AppId, which we do not have here, so probe each and let the
        // caller's existence check pick the one that actually holds this game's folder.
        foreach (string dir in _fileService.GetDirectories(Path.Combine(steamApps, "compatdata")))
            yield return Path.Combine(dir, "pfx");
    }

    private static string? WalkUpToSteamApps(string? startDirectory)
    {
        if (string.IsNullOrEmpty(startDirectory)) return null;

        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            if (string.Equals(dir.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// <c>&lt;prefix&gt;/drive_c/users/&lt;user&gt;</c>. Proton always uses "steamuser"; hand-rolled
    /// Wine prefixes use the real login name.
    /// </summary>
    private string? GetUserDirectory(string prefix)
    {
        string users = DescendCaseInsensitively(prefix, ["drive_c", "users"]);
        if (!_fileService.DirectoryExists(users)) return null;

        var candidates = _fileService.GetDirectories(users)
            .Where(d => !string.Equals(Path.GetFileName(d), "Public", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return candidates.FirstOrDefault(d =>
                   string.Equals(Path.GetFileName(d), "steamuser", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }
}
