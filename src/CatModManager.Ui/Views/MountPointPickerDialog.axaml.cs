using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CatModManager.Core.Models;

namespace CatModManager.Ui.Views;

/// <summary>One selectable mount point card. Exists so the list can be data-bound in XAML.</summary>
public record MountPointChoice(string Id, string Name, string Path, bool IsCurrent)
{
    public bool HasPath => !string.IsNullOrEmpty(Path);
}

/// <summary>
/// Asks which mount point a mod should be installed into, highlighting the one already in use.
/// </summary>
public partial class MountPointPickerDialog : Window
{
    public MountPointPickerDialog()
    {
        InitializeComponent();
        CancelBtn.Click += (_, _) => Close(null);
    }

    /// <summary>
    /// Shows the picker and returns the chosen mount point id, or null if the user cancelled.
    /// </summary>
    public static Task<string?> ShowAsync(
        Window owner, IReadOnlyList<MountPointDef> mountPoints, string? currentMountPointId)
    {
        // A null MountPointId means "the default", which is the first entry.
        string effectiveCurrent = string.IsNullOrEmpty(currentMountPointId)
            ? (mountPoints.Count > 0 ? mountPoints[0].Id : "")
            : currentMountPointId;

        var dialog = new MountPointPickerDialog();
        dialog.Cards.ItemsSource = mountPoints
            .Select(mp => new MountPointChoice(mp.Id, mp.Name, mp.Path,
                string.Equals(mp.Id, effectiveCurrent, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return dialog.ShowDialog<string?>(owner);
    }

    private void Card_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MountPointChoice choice })
            Close(choice.Id);
    }
}
