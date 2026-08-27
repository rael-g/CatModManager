using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using CatModManager.Core.Models;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Ui.Views;

/// <summary>
/// Drives the visuals of a drag-to-reorder: the held row follows the pointer continuously, and the
/// rows it displaces slide into their new places instead of jumping.
///
/// The row being dragged is detached from its slot by a <see cref="Visual.RenderTransform"/> that is
/// recomputed on every pointer event, so it stays glued to the cursor rather than only catching up
/// when the order changes. The slot it left keeps its place in the layout and simply draws empty.
///
/// Displaced rows are animated with the standard FLIP trick: measure where the row sits, let the
/// list reorder, then immediately offset the row back to where it just was and release it to the
/// transition. The eye sees a slide; the layout was never anything but instantaneous.
///
/// Swaps are decided by comparing the held row's centre against the *midpoint* of the neighbouring
/// slot, not by hit-testing whatever is under the pointer. That distinction is what stops the
/// flicker: after a swap the two rows have exchanged slots, so undoing it requires travelling back
/// across a full row height. Hit-testing has no such gap — a pointer resting near a boundary, or
/// moving fast enough that one event spans several rows, would resolve alternately to one row and
/// then the other and the pair would visibly oscillate.
/// </summary>
internal sealed class DragReorderAnimator
{
    private const double SettleMilliseconds = 140;

    private ListBox? _list;
    private ModListViewModel? _vm;
    private Mod? _dragged;

    /// <summary>Where inside the row the pointer grabbed it, so it does not jump on pick-up.</summary>
    private double _grabOffset;

    /// <summary>Every container given a transform, so all of them can be handed back untouched.</summary>
    private readonly HashSet<ListBoxItem> _touched = new();

    public bool IsActive => _dragged != null;

    public void Begin(ListBox list, ModListViewModel vm, Mod mod, double pointerY)
    {
        _list = list;
        _vm = vm;
        _dragged = mod;
        _touched.Clear();

        var container = ContainerOf(mod);
        _grabOffset = container == null ? 0 : pointerY - LayoutTop(container);

        if (container != null)
        {
            // Lifted above its neighbours: while it follows the pointer it overlaps them, and a row
            // that slid underneath the one being carried would read as the wrong one moving.
            container.ZIndex = 100;
            _touched.Add(container);
        }
    }

    /// <summary>Reconciles the list and the held row's position with the pointer at <paramref name="pointerY"/>.</summary>
    public void Update(double pointerY)
    {
        if (_list == null || _vm == null || _dragged == null) return;

        double desiredTop = pointerY - _grabOffset;

        // Loop rather than swap once. Each pass moves at most one place, so a fast drag — or a run
        // of pointer events coalesced into one — still lands where the pointer actually is instead
        // of falling a row behind and staying there.
        for (int guard = 0; guard < 128; guard++)
        {
            if (!TrySwapTowards(desiredTop)) break;
        }

        Follow(desiredTop);
    }

    /// <summary>Swaps the held row one place towards <paramref name="desiredTop"/>; false when settled.</summary>
    private bool TrySwapTowards(double desiredTop)
    {
        var container = ContainerOf(_dragged!);
        if (container == null) return false;

        int index = _vm!.DisplayedMods.IndexOf(_dragged!);
        if (index < 0) return false;

        double centre = desiredTop + container.Bounds.Height / 2;

        if (index + 1 < _vm.DisplayedMods.Count &&
            ContainerOf(_vm.DisplayedMods[index + 1]) is { } below &&
            centre > LayoutTop(below) + below.Bounds.Height / 2)
        {
            SwapWith(_vm.DisplayedMods[index + 1]);
            return true;
        }

        if (index - 1 >= 0 &&
            ContainerOf(_vm.DisplayedMods[index - 1]) is { } above &&
            centre < LayoutTop(above) + above.Bounds.Height / 2)
        {
            SwapWith(_vm.DisplayedMods[index - 1]);
            return true;
        }

        return false;
    }

    /// <summary>Reorders past <paramref name="displaced"/> and slides it into the vacated slot.</summary>
    private void SwapWith(Mod displaced)
    {
        var before = ContainerOf(displaced);
        double beforeTop = before == null ? double.NaN : LayoutTop(before);

        _vm!.DragOver(displaced);

        // The transform has to be applied against the post-reorder layout, and reordering an
        // ItemsSource does not lay out synchronously.
        _list!.UpdateLayout();

        if (double.IsNaN(beforeTop)) return;

        var after = ContainerOf(displaced);
        if (after == null) return;

        double delta = beforeTop - LayoutTop(after);
        if (Math.Abs(delta) < 0.5) return;

        // Snap back to the old position with transitions off — animating *into* the offset would
        // show the row drifting away from where it belongs before coming back.
        after.Transitions = null;
        after.RenderTransform = Translate(delta);
        _touched.Add(after);

        // Released a frame later, once the offset above has actually been rendered. Setting both in
        // one pass would collapse to no visible change at all.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_touched.Contains(after)) return;
            after.Transitions = SettleTransition();
            after.RenderTransform = TransformOperations.Identity;
        }, DispatcherPriority.Render);
    }

    /// <summary>Pins the held row under the pointer, whatever slot it currently occupies.</summary>
    private void Follow(double desiredTop)
    {
        var container = ContainerOf(_dragged!);
        if (container == null) return;

        // No transition here on purpose: the held row must track the pointer exactly. Easing it
        // would make it lag behind the cursor, which reads as the drag being unresponsive.
        container.Transitions = null;
        container.ZIndex = 100;
        container.RenderTransform = Translate(desiredTop - LayoutTop(container));
        _touched.Add(container);
    }

    /// <summary>Drops the held row into its slot and returns every container to its normal state.</summary>
    public void End()
    {
        var dragged = _dragged != null ? ContainerOf(_dragged) : null;

        foreach (var container in _touched)
        {
            container.ZIndex = 0;
            if (ReferenceEquals(container, dragged))
            {
                // The one row that earns an animation on release: it is visibly off its slot, so
                // cutting it there would undo the illusion at the last moment.
                container.Transitions = SettleTransition();
            }
            else
            {
                container.Transitions = null;
            }
            container.RenderTransform = TransformOperations.Identity;
        }

        _touched.Clear();
        _dragged = null;
        _vm = null;
        _list = null;
    }

    private ListBoxItem? ContainerOf(Mod mod)
    {
        if (_list == null || _vm == null) return null;
        int index = _vm.DisplayedMods.IndexOf(mod);
        return index < 0 ? null : _list.ContainerFromIndex(index) as ListBoxItem;
    }

    /// <summary>
    /// The row's slot in list coordinates, ignoring any transform currently applied to it.
    ///
    /// <see cref="Visual.Bounds"/> is the laid-out rectangle inside the items panel and so is not
    /// disturbed by the offsets this class applies — which matters, because the held row is always
    /// carrying one. Subtracting the scroll offset puts it in the same space as the pointer.
    /// </summary>
    private double LayoutTop(ListBoxItem container) =>
        container.Bounds.Y - (_list?.Scroll?.Offset.Y ?? 0);

    private static ITransform Translate(double y) =>
        TransformOperations.Parse(
            string.Format(CultureInfo.InvariantCulture, "translateY({0}px)", y));

    private static Transitions SettleTransition() => new()
    {
        new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(SettleMilliseconds),
            Easing   = new CubicEaseOut(),
        },
    };
}
