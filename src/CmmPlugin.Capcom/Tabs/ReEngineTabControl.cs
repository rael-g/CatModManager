using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(12) };

        panel.Children.Add(Row("Game", _vm.GameName));

        if (_vm.IsReEngineGame)
        {
            var refLabel = _vm.ReFrameworkStatus;
            if (!string.IsNullOrEmpty(_vm.ReFrameworkVersion))
                refLabel += $"  v{_vm.ReFrameworkVersion}";

            panel.Children.Add(Row("REFramework", refLabel));
            panel.Children.Add(Row("Autorun scripts", _vm.ScriptCount.ToString()));
        }

        var refreshBtn = new Button
        {
            Content              = "↺ Refresh",
            Padding              = new Thickness(8, 3),
            Margin               = new Thickness(0, 8, 0, 0),
            HorizontalAlignment  = HorizontalAlignment.Left
        };
        refreshBtn.Click += (_, _) => { _vm.Refresh(); Content = Build(); };
        panel.Children.Add(refreshBtn);

        return new ScrollViewer { Content = panel };
    }

    private static Control Row(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(140, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1,   GridUnitType.Star));

        var lbl = new TextBlock
        {
            Text                = label,
            Foreground          = Brushes.Gray,
            VerticalAlignment   = VerticalAlignment.Center
        };
        var val = new TextBlock
        {
            Text                = value,
            VerticalAlignment   = VerticalAlignment.Center,
            TextWrapping        = TextWrapping.Wrap
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(val);
        return grid;
    }
}
