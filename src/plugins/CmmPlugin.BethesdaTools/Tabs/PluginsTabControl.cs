using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using CmmPlugin.BethesdaTools.Models;

namespace CmmPlugin.BethesdaTools.Tabs;

/// <summary>
/// Code-only Avalonia UserControl for the PLUGINS inspector tab.
/// Shows the ESP/ESM/ESL load order with enable/disable and reorder controls.
/// </summary>
public class PluginsTabControl : UserControl
{
    private readonly PluginsTabViewModel _vm;
    private readonly TextBlock _statusText;

    public PluginsTabControl(PluginsTabViewModel vm)
    {
        _vm = vm;

        _statusText = new TextBlock
        {
            Margin = new Thickness(8, 4),
            Foreground = Brushes.Gray,
            FontSize = 11
        };
        UpdateStatus();

        var listBox = BuildListBox();
        var buttonBar = BuildButtonBar(listBox);

        var root = new DockPanel();
        DockPanel.SetDock(_statusText, Dock.Top);
        DockPanel.SetDock(buttonBar, Dock.Bottom);
        root.Children.Add(_statusText);
        root.Children.Add(buttonBar);
        root.Children.Add(listBox);

        Content = root;
    }

    private ListBox BuildListBox()
    {
        var listBox = new ListBox
        {
            ItemsSource = _vm.Entries,
            ItemTemplate = new FuncDataTemplate<EspEntry>((entry, _) =>
            {
                var grid = new Grid { Margin = new Thickness(2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));
                grid.ColumnDefinitions.Add(new ColumnDefinition(38, GridUnitType.Pixel));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(42, GridUnitType.Pixel));

                var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                cb.Bind(CheckBox.IsCheckedProperty,
                    new Binding(nameof(EspEntry.IsEnabled)) { Mode = BindingMode.TwoWay });
                cb.PropertyChanged += (_, _) => UpdateStatus();

                var order = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(2, 0)
                };
                order.Bind(TextBlock.TextProperty,
                    new Binding(nameof(EspEntry.LoadOrder)) { StringFormat = "{0:000}" });

                var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                name.Bind(TextBlock.TextProperty, new Binding(nameof(EspEntry.FileName)));

                var type = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                type.Bind(TextBlock.TextProperty, new Binding(nameof(EspEntry.Extension)));

                Grid.SetColumn(cb, 0);
                Grid.SetColumn(order, 1);
                Grid.SetColumn(name, 2);
                Grid.SetColumn(type, 3);
                grid.Children.Add(cb);
                grid.Children.Add(order);
                grid.Children.Add(name);
                grid.Children.Add(type);

                return grid;
            })
        };
        return listBox;
    }

    private Panel BuildButtonBar(ListBox listBox)
    {
        var btnRefresh = MakeButton("↺ Refresh", () => { _vm.Refresh(); UpdateStatus(); });
        var btnSave = MakeButton("💾 Save", () => _vm.Save());
        var btnSort = MakeButton("ESM first", () => _vm.SortMastersFirst());
        var btnUp = MakeButton("▲", () =>
        {
            if (listBox.SelectedItem is EspEntry e) _vm.MoveUp(e);
        });
        var btnDown = MakeButton("▼", () =>
        {
            if (listBox.SelectedItem is EspEntry e) _vm.MoveDown(e);
        });

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(4)
        };
        bar.Children.Add(btnRefresh);
        bar.Children.Add(btnSave);
        bar.Children.Add(btnSort);
        bar.Children.Add(btnUp);
        bar.Children.Add(btnDown);
        return bar;
    }

    private static Button MakeButton(string label, Action onClick)
    {
        var btn = new Button { Content = label, Padding = new Thickness(6, 2) };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void UpdateStatus()
    {
        _statusText.Text = _vm.Status;
    }
}
