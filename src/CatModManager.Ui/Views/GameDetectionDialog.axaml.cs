using System;
using Avalonia.Controls;
using Avalonia.Threading;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Ui.Views;

/// <summary>
/// Dialog that lists ALL games found across Steam, GOG and Epic.
/// Auto-selects a game mode when a match is known; otherwise lets the user choose.
/// </summary>
public partial class GameDetectionDialog : Window
{
    private readonly GameDetectionDialogViewModel? _vm;

    /// <summary>Design-time / XAML loader constructor.</summary>
    public GameDetectionDialog() => InitializeComponent();

    public GameDetectionDialog(GameDetectionDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        RescanBtn.Click += async (_, _) => await vm.ScanAsync();
        CancelBtn.Click += (_, _) => Close();
        ApplyBtn.Click  += (_, _) => { vm.Apply(); Close(); };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_vm != null)
            Dispatcher.UIThread.InvokeAsync(async () => await _vm.ScanAsync());
    }
}
