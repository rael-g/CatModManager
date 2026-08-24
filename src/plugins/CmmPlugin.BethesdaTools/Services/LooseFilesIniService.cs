using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;

namespace CmmPlugin.BethesdaTools.Services;

/// <summary>
/// Ensures the game's user .ini override enables loose file loading.
///
/// Starfield and Fallout 4 read assets exclusively from their packed .ba2 archives unless the
/// [Archive] section is overridden. Without this, every loose-file mod CMM mounts into Data/ is
/// silently ignored and the user sees no effect at all — which looks like CMM doing nothing.
/// </summary>
public class LooseFilesIniService
{
    private const string ArchiveSection = "Archive";

    private static readonly (string Key, string Value)[] _requiredSettings =
    {
        ("bInvalidateOlderFiles", "1"),
        ("sResourceDataDirsFinal", ""),
    };

    private readonly IFileService _fileService;
    private readonly IPluginLogger _log;

    public LooseFilesIniService(IFileService fileService, IPluginLogger log)
    {
        _fileService = fileService;
        _log = log;
    }

    /// <summary>
    /// Applies the required [Archive] settings to the game's Custom.ini, preserving every other
    /// section and key the user already has. Returns true when the file is in the desired state.
    /// </summary>
    public bool Apply(BethesdaGame game, string? myGamesPath)
    {
        if (game.CustomIniFile == null) return true; // engine loads loose files natively

        if (string.IsNullOrEmpty(myGamesPath))
        {
            _log.LogError(
                $"[BethesdaTools] Could not locate the 'My Games' folder — {game.CustomIniFile} was not " +
                 "written, so loose-file mods will not load.", null);
            return false;
        }

        string iniPath = Path.Combine(myGamesPath, game.CustomIniFile);

        try
        {
            var lines = _fileService.FileExists(iniPath)
                ? _fileService.ReadAllLines(iniPath).ToList()
                : new List<string>();

            if (!UpsertArchiveSettings(lines)) return true; // already correct, don't touch the file

            _fileService.CreateDirectory(myGamesPath);
            _fileService.WriteAllLines(iniPath, lines.ToArray());
            _log.Log($"[BethesdaTools] Enabled loose file loading in {iniPath}");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError($"[BethesdaTools] Failed to update {iniPath}", ex);
            return false;
        }
    }

    /// <summary>Rewrites <paramref name="lines"/> in place. Returns true if anything changed.</summary>
    private static bool UpsertArchiveSettings(List<string> lines)
    {
        int sectionStart = lines.FindIndex(IsArchiveHeader);

        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add(string.Empty);
            lines.Add($"[{ArchiveSection}]");
            foreach (var (key, value) in _requiredSettings)
                lines.Add($"{key}={value}");
            return true;
        }

        // Bound the section at the next header so we only rewrite keys that belong to [Archive].
        int sectionEnd = lines.FindIndex(sectionStart + 1, IsAnyHeader);
        if (sectionEnd < 0) sectionEnd = lines.Count;

        bool changed = false;
        foreach (var (key, value) in _requiredSettings)
        {
            string desired = $"{key}={value}";
            int keyIndex = FindKey(lines, sectionStart + 1, sectionEnd, key);

            if (keyIndex < 0)
            {
                lines.Insert(sectionEnd, desired);
                sectionEnd++;
                changed = true;
            }
            else if (lines[keyIndex].Trim() != desired)
            {
                lines[keyIndex] = desired;
                changed = true;
            }
        }

        return changed;
    }

    private static int FindKey(List<string> lines, int start, int end, string key)
    {
        for (int i = start; i < end; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;

            int eq = trimmed.IndexOf('=');
            if (eq > 0 && trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static bool IsAnyHeader(string line)
    {
        string t = line.Trim();
        return t.StartsWith('[') && t.EndsWith(']');
    }

    private static bool IsArchiveHeader(string line) =>
        line.Trim().Equals($"[{ArchiveSection}]", StringComparison.OrdinalIgnoreCase);
}
