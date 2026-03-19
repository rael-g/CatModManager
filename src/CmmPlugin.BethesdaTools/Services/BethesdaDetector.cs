using System;
using System.Collections.Generic;
using System.IO;
using CmmPlugin.BethesdaTools.Models;

namespace CmmPlugin.BethesdaTools.Services;

public static class BethesdaDetector
{
    private static readonly Dictionary<string, BethesdaGame> _known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Skyrim Special Edition / Anniversary Edition
            ["SkyrimSE"]         = new("Skyrim Special Edition", UsesStarFormat: true),
            // Skyrim Legendary Edition
            ["TESV"]             = new("Skyrim",                 UsesStarFormat: false),
            // Enderal (Skyrim total conversion)
            ["Enderal"]          = new("Enderal",                UsesStarFormat: false),
            ["EnderalSE"]        = new("Enderal Special Edition",UsesStarFormat: true),
            // Fallout
            ["Fallout4"]         = new("Fallout4",               UsesStarFormat: true),
            ["Fallout4VR"]       = new("Fallout4VR",             UsesStarFormat: true),
            ["FalloutNV"]        = new("FalloutNV",              UsesStarFormat: false),
            ["Fallout3"]         = new("Fallout3",               UsesStarFormat: false),
            // The Elder Scrolls
            ["Oblivion"]         = new("Oblivion",               UsesStarFormat: false),
            ["Morrowind"]        = new("Morrowind",              UsesStarFormat: false),
            // Starfield
            ["Starfield"]        = new("Starfield",              UsesStarFormat: true),
        };

    public static BethesdaGame? Detect(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return null;
        string exeName = Path.GetFileNameWithoutExtension(executablePath);
        return _known.TryGetValue(exeName, out var game) ? game : null;
    }

    public static string GetPluginsTextPath(BethesdaGame game)
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApp, game.LocalAppDataFolder, "plugins.txt");
    }

    public static bool IsBethesdaExecutable(string? executablePath) => Detect(executablePath) != null;
}
