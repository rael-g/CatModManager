using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CatModManager.Tests.Ui.Views;

public class XamlStructureTests
{
    private static XNamespace av = "https://github.com/avaloniaui";

    private static string GetProjectRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CatModManager.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new Exception("Could not find project root");
    }

    private static XDocument LoadMainWindow() =>
        XDocument.Load(Path.Combine(GetProjectRoot(), "src/CatModManager.Ui/Views/MainWindow.axaml"));

    private static XDocument LoadAppAxaml() =>
        XDocument.Load(Path.Combine(GetProjectRoot(), "src/CatModManager.Ui/App.axaml"));

    [Fact]
    public void ModList_HeaderGrid_And_RowTemplateGrid_HaveIdenticalColumnDefinitions()
    {
        var doc = LoadMainWindow();

        // Busca todos os Grid com ColumnDefinitions
        var grids = doc.Descendants(av + "Grid")
            .Select(g => g.Attribute("ColumnDefinitions")?.Value)
            .Where(v => v != null)
            .ToList();

        // O valor "44, 32, *, 130, 70" deve aparecer exatamente 2 vezes
        // (header + row template)
        var modListColDef = "44, 32, *, 100, 70";
        var count = grids.Count(v => v == modListColDef);
        Assert.Equal(2, count);
    }

    [Fact]
    public void RequiredControl_ModsListBox_ExistsWithCorrectName()
    {
        var doc = LoadMainWindow();
        
        var listBox = doc.Descendants(av + "ListBox")
            .FirstOrDefault(l => l.Attribute("Name")?.Value == "ModsListBox");
            
        Assert.NotNull(listBox);
    }

    [Fact]
    public void RequiredControl_ProfileSelector_ExistsWithCorrectName()
    {
        var doc = LoadMainWindow();
        
        var selector = doc.Descendants(av + "ComboBox")
            .FirstOrDefault(c => c.Attribute("Name")?.Value == "ProfileSelector");
            
        Assert.NotNull(selector);
    }

    /// <summary>
    /// The split the window is built on: configuring a game or a profile lives in the menus, the
    /// sidebar picks what is open and acts on it, and the command bar only touches the mod list.
    /// Regressions here are how the command bar grew to hold search, profile, category, reorder,
    /// add, mount and launch all at once.
    /// </summary>
    [Fact]
    public void TheCommandBarHoldsOnlyTheModListsOwnControls()
    {
        var doc = LoadMainWindow();

        var reorder = doc.Descendants(av + "ToggleButton")
            .FirstOrDefault(t => t.Attribute("Name")?.Value == "ReorderToggle");
        Assert.NotNull(reorder);

        // The command bar is the Border that holds the reorder toggle. Nothing about a game or a
        // profile belongs inside it.
        var commandBar = reorder!.Ancestors(av + "Border").First();
        var inside = commandBar.ToString();

        Assert.DoesNotContain("GameManager.", inside);
        Assert.DoesNotContain("ProfileManager.", inside);
        Assert.DoesNotContain("LaunchGameCommand", inside);
        Assert.DoesNotContain("ToggleMountCommand", inside);
    }

    [Fact]
    public void ProfileSelector_HasCorrectBindings()
    {
        var doc = LoadMainWindow();
        
        var selector = doc.Descendants(av + "ComboBox")
            .FirstOrDefault(c => c.Attribute("Name")?.Value == "ProfileSelector");
            
        Assert.NotNull(selector);
        Assert.Contains("AvailableProfiles", selector.Attribute("ItemsSource")?.Value);
        Assert.Contains("CurrentProfile", selector.Attribute("SelectedItem")?.Value);
    }

    [Fact]
    public void ImportantInputs_HaveEnterKeyBinding()
    {
        var doc = LoadMainWindow();
        
        // Helper to check if a TextBox has a KeyBinding for Enter
        bool HasEnterBinding(XElement textBox)
        {
            var keyBindings = textBox.Descendants(av + "KeyBinding");
            return keyBindings.Any(kb => kb.Attribute("Gesture")?.Value == "Enter");
        }

        // Search TextBox
        var searchTextBox = doc.Descendants(av + "TextBox")
            .FirstOrDefault(t => t.Attribute("Watermark")?.Value == "Search mods...");
        Assert.NotNull(searchTextBox);
        Assert.True(HasEnterBinding(searchTextBox), "Search TextBox should have Enter key binding.");

        // The rename box and the launch arguments box used to be checked here too. Renaming a
        // profile is a dialog now, and the launch line moved into the game settings window — this
        // file only reads MainWindow.axaml.
    }

    [Fact]
    public void AppAxaml_HasRequiredResources()
    {
        var doc = LoadAppAxaml();
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        
        var brushes = doc.Descendants(av + "SolidColorBrush")
            .Select(b => b.Attribute(x + "Key")?.Value)
            .ToList();
            
        Assert.Contains("AccentColor", brushes);
        Assert.Contains("StatusActive", brushes);
        Assert.Contains("StatusDanger", brushes);
    }

    [Fact]
    public void StatusBar_HasRequiredBindings()
    {
        var doc = LoadMainWindow();
        
        // Identified by the mount indicator, not by "StatusMessage" alone: several panels have a
        // status line of their own — the Tools editor binds Tools.StatusMessage — and matching on
        // the first Border containing that word found whichever one happened to come first in the
        // file. MountButtonColor belongs to the real status bar and nothing else.
        var statusBar = doc.Descendants(av + "Border")
            .FirstOrDefault(b =>
                b.Descendants(av + "TextBlock").Any(tb => tb.Attribute("Text")?.Value.Contains("StatusMessage") == true) &&
                b.Descendants().Attributes().Any(a => a.Value.Contains("MountButtonColor")));
            
        Assert.NotNull(statusBar);
        
        var bindings = statusBar.Descendants()
            .Attributes()
            .Select(a => a.Value)
            .Where(v => v.Contains("{Binding"))
            .ToList();
            
        Assert.Contains(bindings, b => b.Contains("MountButtonColor"));
        Assert.Contains(bindings, b => b.Contains("StatusMessage"));
        Assert.Contains(bindings, b => b.Contains("SafeSwapStatusText"));
        Assert.Contains(bindings, b => b.Contains("SafeSwapStatusColor"));
    }
}
