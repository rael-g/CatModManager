using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using CmmPlugin.SaveManager.Services;

namespace CmmPlugin.SaveManager.Tabs;

public class SaveManagerTabControl : UserControl
{
    private readonly SaveManagerTabViewModel _vm;
    private readonly TextBlock               _statusText;

    public SaveManagerTabControl(SaveManagerTabViewModel vm)
    {
        _vm = vm;

        _statusText = new TextBlock
        {
            Margin      = new Thickness(8, 6),
            Foreground  = Brushes.Gray,
            FontSize    = 11,
            TextWrapping = TextWrapping.Wrap
        };

        var listBox = BuildListBox();
        var btnBar  = BuildButtonBar();

        var root = new DockPanel();
        DockPanel.SetDock(_statusText, Dock.Top);
        DockPanel.SetDock(btnBar, Dock.Bottom);
        root.Children.Add(_statusText);
        root.Children.Add(btnBar);
        root.Children.Add(listBox);

        Content = root;

        _vm.Refresh();
        SyncStatus();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SaveManagerTabViewModel.Status))
                SyncStatus();
        };
    }

    private ListBox BuildListBox() =>
        new()
        {
            ItemsSource  = _vm.Backups,
            ItemTemplate = new FuncDataTemplate<SaveBackup>((backup, _) =>
            {
                var grid = new Grid { Margin = new Thickness(2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition(1,  GridUnitType.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                var name = new TextBlock
                {
                    Text                = backup.Label,
                    VerticalAlignment   = VerticalAlignment.Center,
                    TextTrimming        = TextTrimming.CharacterEllipsis
                };
                ToolTip.SetTip(name, backup.FilePath);

                var size = new TextBlock
                {
                    Text              = FormatSize(backup.SizeBytes),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground        = Brushes.Gray,
                    FontSize          = 11,
                    Margin            = new Thickness(8, 0)
                };

                var btnRestore = new Button
                {
                    Content = "Restore",
                    Padding = new Thickness(6, 2),
                    Margin  = new Thickness(2, 0)
                };
                btnRestore.Click += async (_, _) => await _vm.Restore(backup);

                var btnDelete = new Button
                {
                    Content    = "✕",
                    Padding    = new Thickness(6, 2),
                    Foreground = Brushes.OrangeRed
                };
                btnDelete.Click += (_, _) => _vm.Delete(backup);

                Grid.SetColumn(name,       0);
                Grid.SetColumn(size,       1);
                Grid.SetColumn(btnRestore, 2);
                Grid.SetColumn(btnDelete,  3);

                grid.Children.Add(name);
                grid.Children.Add(size);
                grid.Children.Add(btnRestore);
                grid.Children.Add(btnDelete);

                return grid;
            })
        };

    private Panel BuildButtonBar()
    {
        var btnRefresh = MakeButton("↺ Refresh", () => _vm.Refresh());
        var btnBackup  = MakeButton("💾 Backup Now", async () => await _vm.BackupNowCommand.ExecuteAsync(null));

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 4,
            Margin      = new Thickness(4)
        };
        bar.Children.Add(btnRefresh);
        bar.Children.Add(btnBackup);
        return bar;
    }

    private static Button MakeButton(string label, Action onClick)
    {
        var btn = new Button { Content = label, Padding = new Thickness(6, 2) };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static Button MakeButton(string label, Func<Task> onClick)
    {
        var btn = new Button { Content = label, Padding = new Thickness(6, 2) };
        btn.Click += async (_, _) => await onClick();
        return btn;
    }

    private void SyncStatus() => _statusText.Text = _vm.Status;

    private static string FormatSize(long bytes) =>
        bytes switch
        {
            < 1_024           => $"{bytes} B",
            < 1_024 * 1_024   => $"{bytes / 1_024} KB",
            _                 => $"{bytes / (1_024 * 1_024)} MB"
        };
}
