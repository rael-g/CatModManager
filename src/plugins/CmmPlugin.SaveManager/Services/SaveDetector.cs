using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Models;
using Nett;

namespace CmmPlugin.SaveManager.Services;

/// <summary>
/// Reads game definition TOMLs from the same directories the main app uses,
/// extracting the optional <c>SaveFolderPattern</c> field.
/// CMM itself has no knowledge of this field — the plugin owns it entirely.
/// </summary>
public class SaveDetector
{
    private readonly IPluginLogger       _log;
    private readonly WindowsUserFolders  _userFolders;
    private readonly List<SaveGameDef>   _defs = [];

    public SaveDetector(IPluginLogger log, WindowsUserFolders userFolders)
    {
        _log         = log;
        _userFolders = userFolders;
    }

    /// <summary>
    /// Loads (or re-loads) save definitions from the two standard game_definitions directories:
    /// the bundled one (alongside the executable) and the user-installed one (AppData).
    /// Call once during plugin initialization.
    /// </summary>
    public void Load(string appDataPath)
    {
        _defs.Clear();

        var bundled = Path.Combine(AppContext.BaseDirectory, "game_definitions");
        LoadDirectory(bundled);

        var user = Path.Combine(appDataPath, "game_definitions");
        LoadDirectory(user);   // user definitions can override bundled ones by GameId
    }

    private void LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.GetFiles(directory, "*.toml"))
        {
            try
            {
                var table = Toml.ReadFile(file);

                string pattern = table.TryGetValue("SaveFolderPattern", out var v)
                    ? v.Get<string>()
                    : "";

                if (string.IsNullOrWhiteSpace(pattern)) continue;  // not a save-managed game

                string gameId     = table.TryGetValue("GameId",      out var gid)  ? gid.Get<string>()  : "";
                string display    = table.TryGetValue("DisplayName",  out var dsp)  ? dsp.Get<string>()  : gameId;
                string[] required = table.TryGetValue("RequiredFiles", out var req)
                    ? req.Get<string[]>()
                    : [];

                // Keep only the exe files as executable matchers
                string[] exeNames = required
                    .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (exeNames.Length == 0 || string.IsNullOrEmpty(gameId)) continue;

                var def = new SaveGameDef
                {
                    GameId           = gameId,
                    DisplayName      = display,
                    ExecutableNames  = exeNames,
                    SaveFolderPattern = pattern
                };

                // User definitions override bundled ones with the same GameId
                int existing = _defs.FindIndex(d => d.GameId == gameId);
                if (existing >= 0) _defs[existing] = def;
                else               _defs.Add(def);
            }
            catch (Exception ex)
            {
                _log.LogError($"[SaveManager] Failed to read save definition from {Path.GetFileName(file)}", ex);
            }
        }
    }

    public int Count => _defs.Count;

    /// <summary>
    /// Detects a known save-managed game.
    /// </summary>
    /// <param name="gameFolder">
    /// The install folder. Checked because the configured executable is not necessarily the game —
    /// it can be a launcher, a wrapper script, or a bare command with no directory at all, which is
    /// normal for a game started through Proton or a container.
    /// </param>
    public SaveGameDef? Detect(string? executablePath, string? gameFolder = null)
    {
        if (!string.IsNullOrEmpty(executablePath))
        {
            string exeName = Path.GetFileName(executablePath);
            var byName = _defs.FirstOrDefault(d =>
                d.ExecutableNames.Any(e => string.Equals(e, exeName, StringComparison.OrdinalIgnoreCase)));
            if (byName != null) return byName;
        }

        return FindInFolder(gameFolder)
            ?? FindInFolder(string.IsNullOrEmpty(executablePath) ? null : Path.GetDirectoryName(executablePath));
    }

    private SaveGameDef? FindInFolder(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

        return _defs.FirstOrDefault(d =>
            d.ExecutableNames.Any(e => File.Exists(Path.Combine(dir, e))));
    }

    /// <summary>
    /// The game's save folder on disk, or null when it cannot be located.
    ///
    /// Every definition writes this as a Windows path (<c>%APPDATA%\RE2</c>). That used to be
    /// expanded against the host environment, which on Linux resolves to nothing at all — the
    /// variables do not exist there and the saves live inside the Wine prefix regardless. So the
    /// whole plugin reported "save folder not found" for every game on Linux.
    /// </summary>
    public string? ResolveSaveFolder(SaveGameDef def, string? gameFolder = null, string? executablePath = null)
        => _userFolders.Resolve(def.SaveFolderPattern, gameFolder, executablePath);
}

