using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.ViewModels;

/// <summary>
/// Rebuilding the displayed list when it already reads correctly is not free: clearing and
/// refilling recreates every row container, and that is visible on screen.
/// </summary>
public class ModListRebuildNoOpTests
{
    private static ModListViewModel WithMods(int count)
    {
        var vm = new ModListViewModel();
        for (int i = 0; i < count; i++)
            vm.AllMods.Add(new Mod($"Mod{i}", $"/mods/mod{i}", count - 1 - i));
        vm.RebuildDisplayedMods();
        return vm;
    }

    [Fact]
    public void ARebuildThatChangesNothingLeavesTheCollectionUntouched()
    {
        var vm = WithMods(5);

        var events = new List<NotifyCollectionChangedEventArgs>();
        vm.DisplayedMods.CollectionChanged += (_, e) => events.Add(e);

        vm.RebuildDisplayedMods();
        vm.RebuildDisplayedMods();

        Assert.Empty(events);
    }

    [Fact]
    public void ARebuildThatDoesChangeTheOrderStillRebuilds()
    {
        // The cheap way to make the test above pass is to stop rebuilding altogether.
        var vm = WithMods(5);
        var before = vm.DisplayedMods.ToList();

        vm.SortAscending = false;

        Assert.Equal(before.AsEnumerable().Reverse(), vm.DisplayedMods);
    }

    [Fact]
    public void ARebuildThatDoesChangeTheContentsStillRebuilds()
    {
        var vm = WithMods(5);
        vm.DisplayedMods[0].IsEnabled = false;

        vm.ShowOnlyEnabled = true;

        Assert.Equal(4, vm.DisplayedMods.Count);
        Assert.DoesNotContain(vm.DisplayedMods, m => !m.IsEnabled);
    }
}
