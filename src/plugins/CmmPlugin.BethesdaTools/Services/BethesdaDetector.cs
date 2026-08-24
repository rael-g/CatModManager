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

    private static IReadOnlySet<string> Masters(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, BethesdaGame> _known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SkyrimSE"] = new("Skyrim Special Edition", UsesStarFormat: true,
                Masters("Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm")),

            ["TESV"] = new("Skyrim", UsesStarFormat: false,
                Masters("Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm")),

            ["Enderal"]   = new("Enderal", UsesStarFormat: false, Masters("Skyrim.esm", "Update.esm")),
            ["EnderalSE"] = new("Enderal Special Edition", UsesStarFormat: true, Masters("Skyrim.esm", "Update.esm")),

            ["Fallout4"] = new("Fallout4", UsesStarFormat: true,
                Masters("Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
                        "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm",
                        "DLCUltraHighResolution.esm"),
                CustomIniFile: "Fallout4Custom.ini"),

            ["Fallout4VR"] = new("Fallout4VR", UsesStarFormat: true,
                Masters("Fallout4.esm", "Fallout4_VR.esm")),

            ["FalloutNV"] = new("FalloutNV", UsesStarFormat: false, Masters("FalloutNV.esm")),
            ["Fallout3"]  = new("Fallout3",  UsesStarFormat: false, Masters("Fallout3.esm")),
            ["Oblivion"]  = new("Oblivion",  UsesStarFormat: false, Masters("Oblivion.esm")),
            ["Morrowind"] = new("Morrowind", UsesStarFormat: false, Masters("Morrowind.esm")),

            ["Starfield"] = new("Starfield", UsesStarFormat: true,
                Masters("Starfield.esm", "Constellation.esm", "OldMars.esm",
                        "BlueprintShips-Starfield.esm", "ShatteredSpace.esm",
                        "SFBGS003.esm", "SFBGS004.esm", "SFBGS006.esm",
                        "SFBGS007.esm", "SFBGS008.esm"),
                CustomIniFile: "StarfieldCustom.ini"),
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

    public bool IsBethesdaExecutable(string? executablePath) => Detect(executablePath) != null;
}
