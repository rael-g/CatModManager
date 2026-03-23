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
    private readonly List<SaveGameDef>   _defs = [];

    public SaveDetector(IPluginLogger log) => _log = log;

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

    /// <summary>Detects a known save-managed game from an executable path.</summary>
    public SaveGameDef? Detect(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return null;
        string exeName = Path.GetFileName(executablePath);
        return _defs.FirstOrDefault(d =>
            d.ExecutableNames.Any(e => string.Equals(e, exeName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Resolves the actual save folder on disk, expanding environment variables.
    /// If the pattern ends with <c>\*</c>, scans for the first numeric (Steam-ID) subfolder.
    /// </summary>
    public static string? ResolveSaveFolder(SaveGameDef def)
    {
        string expanded = Environment.ExpandEnvironmentVariables(def.SaveFolderPattern);

        if (expanded.EndsWith("\\*") || expanded.EndsWith("/*"))
        {
            string parent = expanded[..^2];
            if (!Directory.Exists(parent)) return null;

            return Directory.EnumerateDirectories(parent)
                .FirstOrDefault(d => Path.GetFileName(d).All(char.IsDigit));
        }

        return Directory.Exists(expanded) ? expanded : null;
    }
}
