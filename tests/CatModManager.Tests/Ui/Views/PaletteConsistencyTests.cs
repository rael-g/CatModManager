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

    /// <summary>
    /// Strips comments before matching.
    ///
    /// Without this the rule reads its own documentation as a violation: the palette and the control
    /// kit both name the brush they exist to replace, and the guard flagged both. Exempting those
    /// files one by one would have hidden real offenders in them — a rule about what the code does
    /// should not be looking at prose in the first place.
    /// </summary>
    private static string WithoutComments(string source) =>
        Regex.Replace(source, @"/\*.*?\*/|//[^\n]*", "", RegexOptions.Singleline);

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
    public void NoXamlFileHardcodesAColour()
    {
        var literal = new Regex(@"=""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled);

        var offenders = Directory
            .EnumerateFiles(Path.Combine(ProjectRoot(), "src"), "*.axaml", SearchOption.AllDirectories)
            .Where(f => literal.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(ProjectRoot(), f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "XAML must use {DynamicResource ...} from the palette, not hex literals. Offending files:\n  " +
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

    /// <summary>
    /// The blind spot the hex-literal rules had.
    ///
    /// Every check above looks for <c>#RRGGBB</c>, so Avalonia's named brushes walked straight past
    /// them: the plugins were colouring text with <c>Brushes.Gray</c> and a destructive button with
    /// <c>Brushes.OrangeRed</c> — twenty-odd sites the suite reported as clean. Worse than no guard,
    /// because green tests said the single-palette rule was being kept.
    ///
    /// <c>Transparent</c> is allowed: it is the absence of a colour, and no palette entry could
    /// replace it.
    /// </summary>
    [Fact]
    public void NoSourceFileUsesAnAvaloniaNamedBrush()
    {
        // Not preceded by "CmmPalette." — the palette exposes its own Brushes class by the same name.
        var named = new Regex(@"(?<!CmmPalette\.)\bBrushes\.(?!Transparent\b)([A-Z]\w+)", RegexOptions.Compiled);

        var offenders = Directory
            .EnumerateFiles(Path.Combine(ProjectRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (File: f, Hits: named.Matches(WithoutComments(File.ReadAllText(f)))))
            .Where(x => x.Hits.Count > 0)
            .Select(x => $"{Path.GetRelativePath(ProjectRoot(), x.File)}: "
                       + string.Join(", ", x.Hits.Select(m => m.Value).Distinct()))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Colours must come from CmmPalette.Brushes, not Avalonia's named brushes. Offenders:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// A plugin that cannot reach the palette has no way to match the host theme, and reaches for
    /// <c>Brushes.Gray</c> instead — which is exactly how the drift above happened. Four of the five
    /// plugins were in that position.
    /// </summary>
    [Fact]
    public void EveryPluginCanReachThePalette()
    {
        var pluginDir = Path.Combine(ProjectRoot(), "src/plugins");

        var offenders = Directory
            .EnumerateFiles(pluginDir, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !File.ReadAllText(f).Contains("CatModManager.Theme.csproj"))
            .Select(f => Path.GetRelativePath(ProjectRoot(), f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These plugins cannot reference CmmPalette:\n  " + string.Join("\n  ", offenders));
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
