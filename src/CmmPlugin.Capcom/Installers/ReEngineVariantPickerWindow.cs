using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CmmPlugin.Capcom.Installers;

/// <summary>
/// Shown when a RE Engine zip contains multiple top-level variant folders
/// (the Fluffy Mod Manager convention).
/// The user picks which variants to install; each becomes its own mod entry.
/// </summary>
public class ReEngineVariantPickerWindow : Window
{
    private readonly List<(string Name, CheckBox Cb)> _rows = [];

    public IReadOnlyList<string> SelectedVariants { get; private set; } = [];

    public ReEngineVariantPickerWindow(IReadOnlyList<string> variantNames)
    {
        Title  = "Select variant(s) to install";
        Width  = 480;
        Height = Math.Min(80 + variantNames.Count * 34 + 60, 520);
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var header = new TextBlock
        {
            Text       = "This archive contains multiple variants. Choose which to install:",
            Margin     = new Thickness(16, 12, 16, 8),
            TextWrapping = TextWrapping.Wrap
        };

        var list = new StackPanel { Spacing = 4, Margin = new Thickness(16, 0) };
        foreach (var name in variantNames)
        {
            var cb = new CheckBox { Content = name, IsChecked = false };
            _rows.Add((name, cb));
            list.Children.Add(cb);
        }
        // Select first variant by default
        if (_rows.Count > 0) _rows[0].Cb.IsChecked = true;

        var scroll = new ScrollViewer { Content = list };

        var btnInstall = new Button { Content = "Install selected", HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel  = new Button { Content = "Cancel",           HorizontalAlignment = HorizontalAlignment.Right };
        btnInstall.Click += (_, _) =>
        {
            SelectedVariants = _rows.Where(r => r.Cb.IsChecked == true).Select(r => r.Name).ToList();
            Close(true);
        };
        btnCancel.Click += (_, _) => Close(false);

        var footer = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing             = 8,
            Margin              = new Thickness(16, 8)
        };
        footer.Children.Add(btnCancel);
        footer.Children.Add(btnInstall);

        var footerBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush     = Brushes.Gray,
            Child           = footer
        };

        var root = new DockPanel();
        DockPanel.SetDock(header,       Dock.Top);
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footerBorder);
        root.Children.Add(scroll);
        Content = root;
    }
}
