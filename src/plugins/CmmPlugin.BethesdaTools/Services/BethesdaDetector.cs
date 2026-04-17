using System;
using System.Collections.Generic;
using System.IO;
using CmmPlugin.BethesdaTools.Models;
using CatModManager.PluginSdk;

namespace CmmPlugin.BethesdaTools.Services;

public class BethesdaDetector
{
    private readonly IFileService _fileService;

    public BethesdaDetector(IFileService fileService)
    {
        _fileService = fileService;
    }

    private static readonly Dictionary<string, BethesdaGame> _known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SkyrimSE"]         = new("Skyrim Special Edition", UsesStarFormat: true),
            ["TESV"]             = new("Skyrim",                 UsesStarFormat: false),
            ["Enderal"]          = new("Enderal",                UsesStarFormat: false),
            ["EnderalSE"]        = new("Enderal Special Edition",UsesStarFormat: true),
            ["Fallout4"]         = new("Fallout4",               UsesStarFormat: true),
            ["Fallout4VR"]       = new("Fallout4VR",             UsesStarFormat: true),
            ["FalloutNV"]        = new("FalloutNV",              UsesStarFormat: false),
            ["Fallout3"]         = new("Fallout3",               UsesStarFormat: false),
            ["Oblivion"]         = new("Oblivion",               UsesStarFormat: false),
            ["Morrowind"]        = new("Morrowind",              UsesStarFormat: false),
            ["Starfield"]        = new("Starfield",              UsesStarFormat: true),
        };

    public BethesdaGame? Detect(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return null;

        string exeName = Path.GetFileNameWithoutExtension(executablePath);
        if (_known.TryGetValue(exeName, out var game)) return game;

        string? dir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(dir)) return null;
        foreach (var (knownExe, knownGame) in _known)
            if (_fileService.FileExists(Path.Combine(dir, knownExe + ".exe")))
                return knownGame;

        return null;
    }

    public static string GetPluginsTextPath(BethesdaGame game)
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApp, game.LocalAppDataFolder, "plugins.txt");
    }

    public bool IsBethesdaExecutable(string? executablePath) => Detect(executablePath) != null;
}
