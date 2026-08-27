using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Sorting is a view concern here, but the view it changes is the load order, so the boundary has
/// to be exact: re-sorting must never re-rank a mod, and it must never be possible to drag while
/// looking at an order that dragging cannot express.
/// </summary>
public class ModListSortTests
{
    private static ModListViewModel Build()
    {
        var vm = new ModListViewModel();
        vm.AllMods.Add(new Mod("Zebra",  "/mods/z", 0) { Category = "Gameplay" });
        vm.AllMods.Add(new Mod("apple",  "/mods/a", 0) { Category = "Visuals"  });
        vm.AllMods.Add(new Mod("Middle", "/mods/m", 0) { Category = "Audio"    });
        vm.UpdatePriorities();
        vm.RebuildDisplayedMods();
        return vm;
    }

    [Fact]
    public void DefaultsToPriorityAscending()
    {
        var vm = Build();

        Assert.Equal(ModSortColumn.Priority, vm.SortColumn);
        Assert.True(vm.SortAscending);
        Assert.Equal(vm.DisplayedMods.OrderBy(m => m.Priority), vm.DisplayedMods);
    }

    [Fact]
    public void ClickingTheSameColumnFlipsDirection()
    {
        var vm = Build();

        vm.SortByCommand.Execute(ModSortColumn.Priority);
        Assert.False(vm.SortAscending);

        vm.SortByCommand.Execute(ModSortColumn.Priority);
        Assert.True(vm.SortAscending);
    }

    [Fact]
    public void ClickingANewColumnStartsAscending()
    {
        var vm = Build();
        vm.SortByCommand.Execute(ModSortColumn.Priority);   // now descending

        vm.SortByCommand.Execute(ModSortColumn.Name);

        Assert.Equal(ModSortColumn.Name, vm.SortColumn);
        Assert.True(vm.SortAscending);
    }

    [Fact]
    public void SortsByNameCaseInsensitively()
    {
        var vm = Build();

        vm.SortByCommand.Execute(ModSortColumn.Name);

        // Ordinal ordering would put "Zebra" before "apple" — uppercase sorts first.
        Assert.Equal(new[] { "apple", "Middle", "Zebra" }, vm.DisplayedMods.Select(m => m.Name));
    }

    [Fact]
    public void SortingNeverChangesAnyPriority()
    {
        var vm = Build();
        var before = vm.AllMods.ToDictionary(m => m.Name, m => m.Priority);

        vm.SortByCommand.Execute(ModSortColumn.Name);
        vm.SortByCommand.Execute(ModSortColumn.Name);
        vm.SortByCommand.Execute(ModSortColumn.Category);

        // Looking at the list a different way must not decide who wins a file conflict.
        Assert.Equal(before, vm.AllMods.ToDictionary(m => m.Name, m => m.Priority));
    }

    [Fact]
    public void ReorderMode_ForcesPriorityAscending_AndIgnoresHeaderClicks()
    {
        var vm = Build();
        vm.SortByCommand.Execute(ModSortColumn.Name);

        vm.IsReorderEnabled = true;

        Assert.Equal(ModSortColumn.Priority, vm.SortColumn);
        Assert.True(vm.SortAscending);

        // A drop position means nothing under a name sort, so the headers stop responding.
        vm.SortByCommand.Execute(ModSortColumn.Category);
        Assert.Equal(ModSortColumn.Priority, vm.SortColumn);
    }

    [Fact]
    public void LeavingReorderMode_RestoresTheSortYouHadBefore()
    {
        var vm = Build();
        vm.SortByCommand.Execute(ModSortColumn.Name);
        vm.SortByCommand.Execute(ModSortColumn.Name);   // descending

        vm.IsReorderEnabled = true;
        vm.IsReorderEnabled = false;

        Assert.Equal(ModSortColumn.Name, vm.SortColumn);
        Assert.False(vm.SortAscending);
    }

    [Fact]
    public void IndicatorMarksOnlyTheActiveColumn()
    {
        var vm = Build();

        vm.SortByCommand.Execute(ModSortColumn.Name);

        Assert.Equal("▲", vm.NameSortIndicator);
        Assert.Equal("", vm.PrioritySortIndicator);
        Assert.Equal("", vm.CategorySortIndicator);

        vm.SortByCommand.Execute(ModSortColumn.Name);
        Assert.Equal("▼", vm.NameSortIndicator);
    }
}
