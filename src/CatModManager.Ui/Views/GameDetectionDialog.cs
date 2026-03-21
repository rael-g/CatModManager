using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Ui.Views;

/// <summary>
/// Dialog that lists ALL games found across Steam, GOG and Epic.
/// Auto-selects a game mode when a match is known; otherwise lets the user choose.
/// </summary>
public class GameDetectionDialog : Window
{
    private readonly GameDetectionDialogViewModel _vm;

    public GameDetectionDialog(GameDetectionDialogViewModel vm)
    {
        _vm    = vm;
        Title  = "Detect Installed Games";
        Width  = 640;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize  = false;
        Background = new SolidColorBrush(Color.Parse("#2B2D31"));
        Foreground = new SolidColorBrush(Color.Parse("#DCDDDE"));

        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        // ── Spinner + status ──────────────────────────────────────────────
        var spinner = new ProgressBar { IsIndeterminate = true, Height = 3, IsVisible = false };
        spinner.Bind(ProgressBar.IsVisibleProperty,
            new Binding(nameof(GameDetectionDialogViewModel.IsScanning)) { Source = _vm });

        var statusText = new TextBlock
        {
            Margin    = new Thickness(12, 8),
            FontSize  = 11,
            Foreground = new SolidColorBrush(Color.Parse("#8E9297"))
        };
        statusText.Bind(TextBlock.TextProperty,
            new Binding(nameof(GameDetectionDialogViewModel.Status)) { Source = _vm });

        // ── Game list ─────────────────────────────────────────────────────
        var list = new ListBox
        {
            Margin     = new Thickness(8, 0),
            Background = new SolidColorBrush(Color.Parse("#1E1F22")),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<GameInstallation>((item, _) =>
            {
                if (item is null) return new TextBlock();
                var grid = new Grid { Margin = new Thickness(6, 5) };
                grid.ColumnDefinitions.Add(new ColumnDefinition(52, GridUnitType.Pixel));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1,  GridUnitType.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                // Store badge
                var badgeBg = item.StoreName switch
                {
                    "GOG"   => Color.Parse("#9B59B6"),
                    "Epic"  => Color.Parse("#2563EB"),
                    _       => Color.Parse("#1B5E8A"),   // Steam
                };
                var badge = new Border
                {
                    Background          = new SolidColorBrush(badgeBg),
                    CornerRadius        = new CornerRadius(3),
                    Padding             = new Thickness(4, 1),
                    VerticalAlignment   = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                badge.Child = new TextBlock
                {
                    Text       = item.StoreName,
                    FontSize   = 9,
                    FontWeight = FontWeight.Bold
                };

                // Game name + path
                var nameBlock = new TextBlock
                {
                    Text              = item.DisplayName,
                    FontWeight        = FontWeight.SemiBold,
                    FontSize          = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var pathBlock = new TextBlock
                {
                    Text         = item.GameFolder,
                    FontSize     = 10,
                    Foreground   = new SolidColorBrush(Color.Parse("#8E9297")),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var info = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(nameBlock);
                info.Children.Add(pathBlock);

                // Game mode badge (green if auto-detected, gray if unknown)
                var modeBadge = BuildModeBadge(item);

                Grid.SetColumn(badge,     0);
                Grid.SetColumn(info,      1);
                Grid.SetColumn(modeBadge, 2);
                grid.Children.Add(badge);
                grid.Children.Add(info);
                grid.Children.Add(modeBadge);

                return grid;
            })
        };
        list.Bind(ListBox.ItemsSourceProperty,
            new Binding(nameof(GameDetectionDialogViewModel.Installations)) { Source = _vm });
        list.Bind(ListBox.SelectedItemProperty,
            new Binding(nameof(GameDetectionDialogViewModel.SelectedInstallation))
            { Source = _vm, Mode = BindingMode.TwoWay });

        // ── Game mode selector (bottom panel) ─────────────────────────────
        var modeLabel = new TextBlock
        {
            Text              = "GAME MODE",
            FontSize          = 9,
            FontWeight        = FontWeight.Bold,
            Foreground        = new SolidColorBrush(Color.Parse("#8E9297")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0)
        };

        var modeCombo = new ComboBox
        {
            ItemsSource         = _vm.AvailableSupports,
            MinWidth            = 240,
            Padding             = new Thickness(6, 4),
            VerticalAlignment   = VerticalAlignment.Center
        };
        modeCombo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<IGameSupport>((s, _) =>
            new TextBlock { Text = s?.DisplayName ?? "" });
        modeCombo.Bind(ComboBox.SelectedItemProperty,
            new Binding(nameof(GameDetectionDialogViewModel.SelectedGameMode))
            { Source = _vm, Mode = BindingMode.TwoWay });

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 0,
            Margin      = new Thickness(12, 6)
        };
        modeRow.Children.Add(modeLabel);
        modeRow.Children.Add(modeCombo);

        // ── Footer buttons ────────────────────────────────────────────────
        var btnRescan = new Button { Content = "↺ Rescan", Padding = new Thickness(12, 5) };
        btnRescan.Click += async (_, _) => await _vm.ScanAsync();

        var btnApply = new Button
        {
            Content    = "Apply",
            Padding    = new Thickness(16, 5),
            Background = new SolidColorBrush(Color.Parse("#3BA55D"))
        };
        btnApply.Click += (_, _) => { _vm.Apply(); Close(); };

        var btnCancel = new Button { Content = "Cancel", Padding = new Thickness(12, 5) };
        btnCancel.Click += (_, _) => Close();

        var footer = new Grid { Margin = new Thickness(12, 8) };
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var rightBtns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        rightBtns.Children.Add(btnRescan);
        rightBtns.Children.Add(btnCancel);
        rightBtns.Children.Add(btnApply);

        Grid.SetColumn(rightBtns, 3);
        footer.Children.Add(rightBtns);

        // ── Bottom panel (mode row + footer) ──────────────────────────────
        var bottomPanel = new StackPanel();
        bottomPanel.Children.Add(new Border
        {
            Height     = 1,
            Background = new SolidColorBrush(Color.Parse("#3F4147")),
            Margin     = new Thickness(0, 4, 0, 0)
        });
        bottomPanel.Children.Add(modeRow);
        bottomPanel.Children.Add(footer);

        // ── Root layout ───────────────────────────────────────────────────
        var root = new DockPanel();
        DockPanel.SetDock(spinner,      Dock.Top);
        DockPanel.SetDock(statusText,   Dock.Top);
        DockPanel.SetDock(bottomPanel,  Dock.Bottom);
        root.Children.Add(spinner);
        root.Children.Add(statusText);
        root.Children.Add(bottomPanel);
        root.Children.Add(list);

        return root;
    }

    private static Control BuildModeBadge(GameInstallation item)
    {
        var isKnown = item.DetectedSupport != null;
        var text    = isKnown ? item.DetectedSupport!.DisplayName : "Generic";
        var bg      = isKnown ? Color.Parse("#3BA55D") : Color.Parse("#4F5460");

        return new Border
        {
            Background          = new SolidColorBrush(bg),
            CornerRadius        = new CornerRadius(3),
            Padding             = new Thickness(5, 2),
            Margin              = new Thickness(6, 0, 0, 0),
            VerticalAlignment   = VerticalAlignment.Center,
            Child               = new TextBlock
            {
                Text       = text,
                FontSize   = 9,
                FontWeight = FontWeight.SemiBold,
                MaxWidth   = 120,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        Dispatcher.UIThread.InvokeAsync(async () => await _vm.ScanAsync());
    }
}
