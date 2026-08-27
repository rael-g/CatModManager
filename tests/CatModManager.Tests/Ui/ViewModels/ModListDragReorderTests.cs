using System;
using System.Collections.Generic;
using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Reordering happens while the pointer moves, not on release, so the list gives feedback during
/// the drag. That means the move runs many times per drag — which makes saving on each one the
/// obvious trap.
/// </summary>
public class ModListDragReorderTests
{
    /// <summary>
    /// A real suppressor, not a no-op. Changing a mod's Priority raises PropertyChanged, which the
    /// list turns into an AutoSave — so reordering saves unless something holds it back. A stub
    /// that suppressed nothing would make "saves once" pass or fail for the wrong reason.
    /// </summary>
    private sealed class SaveGate
    {
        private int _depth;
        public List<string> Saves { get; } = new();
        public IDisposable Suppress() { _depth++; return new Scope(this); }
        public void Save() { if (_depth == 0) Saves.Add("save"); }

        private sealed class Scope : IDisposable
        {
            private readonly SaveGate _gate;
            public Scope(SaveGate gate) => _gate = gate;
            public void Dispose() => _gate._depth--;
        }
    }

    private static (ModListViewModel vm, List<string> saves) Build()
    {
        var gate = new SaveGate();
        var vm = new ModListViewModel();
        vm.SuppressAutoSave = gate.Suppress;
        vm.AutoSave = gate.Save;

        foreach (var n in new[] { "A", "B", "C", "D" })
            vm.AllMods.Add(new Mod(n, $"/mods/{n}", 0));
        vm.UpdatePriorities();
        vm.RebuildDisplayedMods();

        // Building the list is itself a change; only what the drag causes is under test.
        gate.Saves.Clear();
        return (vm, gate.Saves);
    }

    private static Mod Row(ModListViewModel vm, string name) => vm.AllMods.First(m => m.Name == name);

    [Fact]
    public void DraggingOverARow_ReordersImmediately()
    {
        var (vm, _) = Build();
        var a = Row(vm, "A");

        vm.BeginDragReorder(a);
        vm.DragOver(Row(vm, "C"));

        // The whole point: the list has already changed before the button comes up.
        Assert.Equal(2, vm.AllMods.IndexOf(a));
    }

    [Fact]
    public void AWholeDrag_SavesExactlyOnce()
    {
        var (vm, saves) = Build();
        var a = Row(vm, "A");

        vm.BeginDragReorder(a);
        vm.DragOver(Row(vm, "B"));
        vm.DragOver(Row(vm, "C"));
        vm.DragOver(Row(vm, "D"));
        Assert.Empty(saves);

        vm.EndDragReorder();

        // Saving per step would rewrite the profile once per row crossed.
        Assert.Single(saves);
    }

    [Fact]
    public void DraggingOntoItself_DoesNoWorkAtAll()
    {
        var (vm, _) = Build();
        var b = Row(vm, "B");
        var before = vm.AllMods.Select(m => m.Name).ToArray();

        int rebuilds = 0;
        vm.DisplayedMods.CollectionChanged += (_, _) => rebuilds++;

        vm.BeginDragReorder(b);

        // DragOver fires continuously while the pointer rests on one row. Moving a mod onto its own
        // slot leaves the order alone, so asserting only on order would not notice the list being
        // torn down and rebuilt on every mouse event of a stationary pointer.
        for (int i = 0; i < 20; i++) vm.DragOver(b);

        Assert.Equal(before, vm.AllMods.Select(m => m.Name));
        Assert.Equal(0, rebuilds);
    }

    [Fact]
    public void TheDraggedRowIsMarkedWhileHeld_AndClearedAfter()
    {
        var (vm, _) = Build();
        var a = Row(vm, "A");

        vm.BeginDragReorder(a);
        Assert.True(a.IsDragging);
        Assert.Same(a, vm.DraggingMod);

        vm.EndDragReorder();
        Assert.False(a.IsDragging);
        Assert.Null(vm.DraggingMod);
    }

    [Fact]
    public void DragOverWithoutADrag_IsIgnored()
    {
        // A drop that lands outside the list, or a cancelled drag, must not leave the next stray
        // DragOver rearranging the load order.
        var (vm, saves) = Build();
        var before = vm.AllMods.Select(m => m.Name).ToArray();

        vm.DragOver(Row(vm, "C"));

        Assert.Equal(before, vm.AllMods.Select(m => m.Name));
        Assert.Empty(saves);
    }

    [Fact]
    public void PrioritiesFollowTheNewOrder_AfterADrag()
    {
        var (vm, _) = Build();
        var a = Row(vm, "A");

        vm.BeginDragReorder(a);
        vm.DragOver(Row(vm, "D"));
        vm.EndDragReorder();

        // Dropped onto D's slot, which is the end of AllMods and therefore the lowest priority.
        // Under the ascending display the lowest priority is drawn first, so A lands at the top —
        // exactly where the pointer left it, since the displayed list is the reverse of AllMods.
        Assert.Equal(vm.AllMods.Min(m => m.Priority), a.Priority);
        Assert.Same(a, vm.DisplayedMods.First());
    }
}
