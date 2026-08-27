using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// In this list position *is* priority, so anything that changes the displayed order risks silently
/// changing which mod wins a file conflict. These pin the two directions apart: what the user sees,
/// and what the conflict resolver reads.
/// </summary>
public class ModListReorderTests
{
    private static ModListViewModel WithMods(params string[] names)
    {
        var vm = new ModListViewModel();
        foreach (var n in names) vm.AllMods.Add(new Mod(n, $"/mods/{n}", 0));
        vm.UpdatePriorities();
        vm.RebuildDisplayedMods();
        return vm;
    }

    [Fact]
    public void ReorderMode_ShowsLowestPriorityFirst_SoTheWinnerIsLast()
    {
        var vm = WithMods("A", "B", "C");

        vm.IsReorderEnabled = true;

        // The conflict resolver applies mods in ascending priority, last write winning, so the
        // bottom row is the one that overwrites the others.
        Assert.Equal(new[] { "C", "B", "A" }, vm.DisplayedMods.Select(m => m.Name));
        Assert.Equal(vm.DisplayedMods.Max(m => m.Priority), vm.DisplayedMods.Last().Priority);
    }

    [Fact]
    public void TogglingReorder_DoesNotChangeAnyPriority()
    {
        var vm = WithMods("A", "B", "C");
        var before = vm.AllMods.ToDictionary(m => m.Name, m => m.Priority);

        vm.IsReorderEnabled = true;
        vm.IsReorderEnabled = false;

        // Sorting is a view concern. If merely looking at the list a different way re-ranked mods,
        // the load order would drift without the user ever editing it.
        Assert.Equal(before, vm.AllMods.ToDictionary(m => m.Name, m => m.Priority));
    }

    [Fact]
    public void TurningReorderOff_RestoresThePreviousSort()
    {
        var vm = WithMods("A", "B", "C");
        var before = vm.DisplayedMods.Select(m => m.Name).ToArray();

        vm.IsReorderEnabled = true;
        vm.IsReorderEnabled = false;

        Assert.Equal(before, vm.DisplayedMods.Select(m => m.Name));
    }

    [Fact]
    public void MoveUp_MovesTheRowUpOnScreen_UnderEitherSortDirection()
    {
        // The trap: AllMods is stored highest-priority-first, so under an ascending sort it is the
        // reverse of the screen, and moving "up" by AllMods index visibly moves the row down.
        foreach (bool ascending in new[] { false, true })
        {
            var vm = WithMods("A", "B", "C");
            vm.SortByPriorityAscending = ascending;

            var middle = vm.DisplayedMods[1];
            vm.SelectedMod = middle;
            vm.MoveUpCommand.Execute(null);

            Assert.Equal(0, vm.DisplayedMods.IndexOf(middle));
        }
    }

    [Fact]
    public void MoveDown_MovesTheRowDownOnScreen_UnderEitherSortDirection()
    {
        foreach (bool ascending in new[] { false, true })
        {
            var vm = WithMods("A", "B", "C");
            vm.SortByPriorityAscending = ascending;

            var middle = vm.DisplayedMods[1];
            vm.SelectedMod = middle;
            vm.MoveDownCommand.Execute(null);

            Assert.Equal(2, vm.DisplayedMods.IndexOf(middle));
        }
    }

    [Fact]
    public void MovingARow_ChangesWhoWins()
    {
        // The point of reordering at all: the mod dragged to the bottom must overwrite the rest.
        var vm = WithMods("A", "B", "C");
        vm.IsReorderEnabled = true;

        var top = vm.DisplayedMods[0];
        vm.SelectedMod = top;
        vm.MoveDownCommand.Execute(null);
        vm.MoveDownCommand.Execute(null);

        Assert.Same(top, vm.DisplayedMods.Last());
        Assert.Equal(vm.AllMods.Max(m => m.Priority), top.Priority);
    }
}
