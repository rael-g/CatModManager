using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.ViewModels;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Ui.Views;
using Xunit;

namespace CatModManager.Tests.Ui;

/// <summary>
/// Every dialog was built by hand in C#, so a broken layout was a compile error. Now that they are
/// XAML, a bad binding or a renamed control only blows up when the dialog is opened — which for
/// several of these is deep inside a workflow nobody runs on every change. These tests load each
/// one and touch the controls the code-behind wires up.
/// </summary>
public class DialogLoadTests
{
    [AvaloniaFact]
    public void ConfirmDialog_LoadsAndShowsItsText()
    {
        var dialog = new ConfirmDialog("Delete mod?", "This cannot be undone.");

        Assert.Equal("Delete mod?", dialog.FindControl<TextBlock>("TitleText")!.Text);
        Assert.Equal("This cannot be undone.", dialog.FindControl<TextBlock>("BodyText")!.Text);
    }

    [AvaloniaFact]
    public void MountPointEditorDialog_LoadsWithAllWiredControls()
    {
        var dialog = new MountPointEditorDialog();

        Assert.NotNull(dialog.NameBox);
        Assert.NotNull(dialog.PathBox);
        Assert.NotNull(dialog.BrowseBtn);
        Assert.NotNull(dialog.OkBtn);
        Assert.NotNull(dialog.CancelBtn);
        Assert.NotNull(dialog.HeaderText);
    }

    [AvaloniaFact]
    public void MountPointPickerDialog_RendersOneCardPerMountPoint()
    {
        var dialog = new MountPointPickerDialog();
        var choices = new List<MountPointChoice>
        {
            new("data", "Data", "/games/skyrim/Data", IsCurrent: true),
            new("root", "Root", "/games/skyrim",      IsCurrent: false),
        };
        dialog.Cards.ItemsSource = choices;
        dialog.Show();

        var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
        // One card per mount point, plus Cancel.
        Assert.Equal(3, buttons.Count);
        Assert.Contains(buttons, b => b.DataContext is MountPointChoice { Id: "data" });
        Assert.Contains(buttons, b => b.DataContext is MountPointChoice { Id: "root" });
    }

    [AvaloniaFact]
    public void GameDetectionDialog_ListsEveryInstallationItFound()
    {
        var vm = new GameDetectionDialogViewModel(
            new StubDiscovery(
                new GameInstallation("Skyrim SE", "/g/skyrim/S.exe", "/g/skyrim", "Steam", new GenericGameSupport()),
                new GameInstallation("Cyberpunk",  "/g/cp/C.exe",     "/g/cp",     "GOG",   null)),
            new[] { (IGameSupport)new GenericGameSupport() });

        var dialog = new GameDetectionDialog(vm);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var names = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();
        Assert.Contains("Skyrim SE", names);
        Assert.Contains("Cyberpunk", names);

        // The GOG entry has no detected support, so its badge must read "Generic".
        Assert.Contains("Generic", names);
    }

    private sealed class StubDiscovery : IGameDiscoveryService
    {
        private readonly GameInstallation[] _found;
        public StubDiscovery(params GameInstallation[] found) => _found = found;
        public Task<IReadOnlyList<GameInstallation>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameInstallation>>(_found);
    }

    [AvaloniaFact]
    public void MountPointChoice_HidesThePathLineWhenThereIsNoPath()
    {
        // The old imperative version only added the subtitle TextBlock when the path was non-empty;
        // the XAML template always creates it, so the empty case has to be bound to IsVisible.
        Assert.False(new MountPointChoice("root", "Root", "", IsCurrent: false).HasPath);
        Assert.True(new MountPointChoice("data", "Data", "/games", IsCurrent: false).HasPath);
    }
}
