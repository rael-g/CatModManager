using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CmmPlugin.Capcom.Tabs;

/// <summary>Code-only Avalonia UserControl for the RE ENGINE inspector tab.</summary>
public class ReEngineTabControl : UserControl
{
    private readonly ReEngineTabViewModel _vm;

    public ReEngineTabControl(ReEngineTabViewModel vm)
    {
        _vm     = vm;
        Content = Build();
    }

    private Control Build()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(12) };

        panel.Children.Add(Row("Game",             nameof(_vm.GameName)));
        panel.Children.Add(Row("REFramework",      nameof(_vm.ReFrameworkLabel)));
        panel.Children.Add(Row("Autorun scripts",  nameof(_vm.ScriptCount)));

        var refreshBtn = new Button
        {
            Content             = "↺ Refresh",
            Padding             = new Avalonia.Thickness(8, 3),
            Margin              = new Avalonia.Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        refreshBtn.Click += (_, _) => _vm.Refresh();
        panel.Children.Add(refreshBtn);

        return new ScrollViewer { Content = panel };
    }

    private Control Row(string label, string vmProperty)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(140, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1,   GridUnitType.Star));

        var lbl = new TextBlock
        {
            Text              = label,
            Foreground        = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        var val = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping      = TextWrapping.Wrap
        };
        val.Bind(TextBlock.TextProperty, new Binding(vmProperty) { Source = _vm });

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(val);
        return grid;
    }
}
