using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CmmPlugin.REEngine.Models;

namespace CmmPlugin.REEngine.Services;

/// <summary>Detects RE Engine games and probes for REFramework installation.</summary>
public static class ReEngineDetector
{
    private static readonly Dictionary<string, ReEngineGame> _byExe =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["re2.exe"]                 = new("re2r",       "Resident Evil 2",       "re2.exe",                 true),
            ["re3.exe"]                 = new("re3r",       "Resident Evil 3",       "re3.exe",                 true),
            ["re7.exe"]                 = new("re7",        "Resident Evil 7",       "re7.exe",                 true),
            ["re8.exe"]                 = new("re_village", "Resident Evil Village", "re8.exe",                 true),
            ["re4.exe"]                 = new("re4r",       "Resident Evil 4",       "re4.exe",                 true),
            ["re9.exe"]                 = new("re9",        "Resident Evil 9",       "re9.exe",                 true),
            ["DevilMayCry5.exe"]        = new("dmc5",       "Devil May Cry 5",       "DevilMayCry5.exe",        true),
            ["MonsterHunterRise.exe"]   = new("mh_rise",    "Monster Hunter Rise",   "MonsterHunterRise.exe",   true),
            ["MonsterHunterWilds.exe"]  = new("mh_wilds",   "Monster Hunter Wilds",  "MonsterHunterWilds.exe",  true),
            ["DD2.exe"]                 = new("dd2",        "Dragon's Dogma 2",      "DD2.exe",                 true),
            ["StreetFighter6.exe"]      = new("sf6",        "Street Fighter 6",      "StreetFighter6.exe",      false),
        };

    public static ReEngineGame? Detect(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return null;
        var exeName = Path.GetFileName(executablePath);
        return _byExe.TryGetValue(exeName, out var game) ? game : null;
    }

    public static bool IsReFrameworkInstalled(string? gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return false;
        return File.Exists(Path.Combine(gameFolder, "dinput8.dll"))
            || File.Exists(Path.Combine(gameFolder, "REFramework.dll"));
    }

    public static string GetReFrameworkVersion(string? gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return "";
        var dll = Path.Combine(gameFolder, "dinput8.dll");
        if (!File.Exists(dll)) dll = Path.Combine(gameFolder, "REFramework.dll");
        if (!File.Exists(dll)) return "";
        try { return FileVersionInfo.GetVersionInfo(dll).FileVersion?.Trim() ?? ""; }
        catch { return ""; }
    }

    public static int CountReFrameworkScripts(string? gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return 0;
        var dir = Path.Combine(gameFolder, "reframework", "autorun");
        if (!Directory.Exists(dir)) return 0;
        return Directory.GetFiles(dir, "*.lua", SearchOption.AllDirectories).Length;
    }
}
