using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CatModManager.Theme;
using Xunit;

namespace CatModManager.Tests.Ui.Views;

/// <summary>
/// Guards the single-palette rule. The app previously carried two: App.axaml's named resources and
/// ~77 hex literals in code-behind, 22 of whose colours had no theme equivalent at all — so
/// retuning the accent repainted only half the UI. These tests fail the moment a new literal
/// appears outside <see cref="CmmPalette"/>.
/// </summary>
public class PaletteConsistencyTests
{
    private static readonly Regex HexColor = new(@"Color\.Parse\(""#[0-9A-Fa-f]{6,8}""\)", RegexOptions.Compiled);

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CatModManager.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new Exception("Could not find project root");
    }

    private static string PaletteFile => Path.Combine(ProjectRoot(), "src/CatModManager.Theme/CmmPalette.cs");

    [Fact]
    public void NoSourceFileDefinesItsOwnColorLiteral()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(ProjectRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !string.Equals(f, PaletteFile, StringComparison.OrdinalIgnoreCase))
            .Where(f => HexColor.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(ProjectRoot(), f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Colours must come from CmmPalette, not hex literals. Offending files:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void AppAxamlBrushesAllResolveFromThePalette()
    {
        var axaml = File.ReadAllText(Path.Combine(ProjectRoot(), "src/CatModManager.Ui/App.axaml"));
        var brushes = Regex.Matches(axaml, @"<SolidColorBrush[^>]*Color=""([^""]+)""");

        Assert.NotEmpty(brushes);
        foreach (Match brush in brushes)
        {
            Assert.StartsWith("{x:Static theme:CmmPalette.", brush.Groups[1].Value);
        }
    }

    [Fact]
    public void PaletteEntriesAreDistinct()
    {
        // Two names for the same colour is how a palette starts drifting: someone retunes one and
        // not the other. Store brand colours are exempt — they are fixed by the storefronts.
        var themeColours = typeof(CmmPalette)
            .GetFields()
            .Where(f => f.FieldType == typeof(Avalonia.Media.Color))
            .Where(f => !f.Name.StartsWith("Store", StringComparison.Ordinal))
            .ToDictionary(f => f.Name, f => f.GetValue(null)!.ToString()!);

        var duplicates = themeColours
            .GroupBy(kv => kv.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} = {string.Join(", ", g.Select(kv => kv.Key))}")
            .ToList();

        Assert.True(duplicates.Count == 0, "Duplicate palette colours:\n  " + string.Join("\n  ", duplicates));
    }
}
