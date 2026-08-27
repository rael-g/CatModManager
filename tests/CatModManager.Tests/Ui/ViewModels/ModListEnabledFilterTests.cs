using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Filtering removes rows rather than reordering them, which makes it more dangerous than sorting:
/// a shorter list reads as mods having disappeared, and the row visually below a dragged one may
/// not be its neighbour in the real load order.
/// </summary>
public class ModListEnabledFilterTests
{
    private static ModListViewModel Build()
    {
        var vm = new ModListViewModel();
        vm.AllMods.Add(new Mod("On1",  "/mods/on1",  0) { IsEnabled = true });
        vm.AllMods.Add(new Mod("Off1", "/mods/off1", 0) { IsEnabled = false });
        vm.AllMods.Add(new Mod("On2",  "/mods/on2",  0) { IsEnabled = true });
        vm.UpdatePriorities();
        vm.RebuildDisplayedMods();
        return vm;
    }

    [Fact]
    public void ShowsEverythingByDefault()
    {
        Assert.Equal(3, Build().DisplayedMods.Count);
    }

    [Fact]
    public void TogglingHidesDisabledMods_AndTogglingBackRestoresThem()
    {
        var vm = Build();

        vm.ToggleEnabledFilterCommand.Execute(null);
        Assert.Equal(new[] { "On1", "On2" }, vm.DisplayedMods.Select(m => m.Name).OrderBy(n => n));

        vm.ToggleEnabledFilterCommand.Execute(null);
        Assert.Equal(3, vm.DisplayedMods.Count);
    }

    [Fact]
    public void FilteringNeverRemovesAMod_NorChangesItsPriority()
    {
        var vm = Build();
        var before = vm.AllMods.ToDictionary(m => m.Name, m => m.Priority);

        vm.ToggleEnabledFilterCommand.Execute(null);

        // Hiding is a view state. The mod is still installed, still ranked, still deployed.
        Assert.Equal(3, vm.AllMods.Count);
        Assert.Equal(before, vm.AllMods.ToDictionary(m => m.Name, m => m.Priority));
    }

    [Fact]
    public void DisablingAModWhileFiltered_DropsItFromView()
    {
        var vm = Build();
        vm.ToggleEnabledFilterCommand.Execute(null);

        vm.AllMods.First(m => m.Name == "On1").IsEnabled = false;

        Assert.DoesNotContain(vm.DisplayedMods, m => m.Name == "On1");
    }

    [Fact]
    public void IndicatorShowsOnlyWhileFiltering()
    {
        var vm = Build();
        Assert.Equal("", vm.EnabledFilterIndicator);

        vm.ToggleEnabledFilterCommand.Execute(null);
        Assert.NotEqual("", vm.EnabledFilterIndicator);
    }

    [Fact]
    public void FilterCombinesWithSearch()
    {
        var vm = Build();
        vm.ToggleEnabledFilterCommand.Execute(null);

        vm.SearchText = "On2";

        Assert.Equal(new[] { "On2" }, vm.DisplayedMods.Select(m => m.Name));
    }
}
