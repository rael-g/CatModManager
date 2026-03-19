using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Code-only Avalonia UserControl that shows active and completed downloads in separate sections.
/// Double-clicking a completed download installs it via the provided callback.
/// </summary>
public class NexusDownloadsTabControl : UserControl
{
    private readonly NexusDownloadService _downloadService;
    private readonly NexusApiService _api;
    private readonly Action<string>? _installCallback;
    private readonly Func<string>? _getDownloadsFolder;
    private readonly StackPanel _activePanel;
    private readonly StackPanel _completedPanel;
    private readonly TextBlock _activeCountBadge;
    private readonly Border _completedSection;
    private Button? _nxmBtn;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#36393F"));
    private static readonly IBrush CardBrush       = new SolidColorBrush(Color.Parse("#1E2124"));
    private static readonly IBrush HeaderBrush     = new SolidColorBrush(Color.Parse("#2F3136"));
    private static readonly IBrush MutedBrush      = new SolidColorBrush(Color.Parse("#72767D"));
    private static readonly IBrush GreenBrush      = new SolidColorBrush(Color.Parse("#3BA55D"));
    private static readonly IBrush RedBrush        = new SolidColorBrush(Color.Parse("#ED4245"));
    private static readonly IBrush WhiteBrush      = Brushes.White;

    public NexusDownloadsTabControl(
        NexusDownloadService downloadService,
        NexusApiService api,
        Action<string>? installCallback = null,
        Func<string>? getDownloadsFolder = null)
    {
        _downloadService = downloadService;
        _api = api;
        _installCallback = installCallback;
        _getDownloadsFolder = getDownloadsFolder;
        Background = BackgroundBrush;

        // Active section header
        _activeCountBadge = new TextBlock
        {
            Text              = "0",
            Foreground        = WhiteBrush,
            FontSize          = 10,
            FontWeight        = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 0, 0)
        };

        // nxm:// protocol registration button
        bool isRegistered = NxmProtocolService.IsRegistered();
        _nxmBtn = new Button
        {
            Content         = isRegistered ? "✓ nxm" : "nxm",
            Padding         = new Thickness(8, 3),
            Background      = Brushes.Transparent,
            Foreground      = isRegistered ? GreenBrush : RedBrush,
            BorderBrush     = isRegistered ? GreenBrush : RedBrush,
            BorderThickness = new Thickness(1),
            FontSize        = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(_nxmBtn, isRegistered
            ? "nxm:// registered — Mod Manager Download links work"
            : "nxm:// not registered — click to register");
        _nxmBtn.Click += (_, _) =>
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                NxmProtocolService.Register(exePath);
                SetNxmBtnRegistered(true);
            }
            catch { }
        };

        // Ask to register nxm:// once the control is visible, unless suppressed
        AttachedToVisualTree += async (_, _) =>
        {
            if (NxmProtocolService.IsRegistered() || _api.NxmDontAskAgain) return;
            var parentWindow = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
            if (parentWindow == null) return;
            var (register, dontAsk) = await ShowNxmPromptAsync(parentWindow);
            if (dontAsk) _api.NxmDontAskAgain = true;
            if (register)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                NxmProtocolService.Register(exePath);
                SetNxmBtnRegistered(true);
            }
        };

        // Nexus website link button
        var nexusLinkBtn = new Button
        {
            Content         = "↗",
            Padding         = new Thickness(8, 3),
            Background      = Brushes.Transparent,
            Foreground      = MutedBrush,
            BorderBrush     = MutedBrush,
            BorderThickness = new Thickness(1),
            FontSize        = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(nexusLinkBtn, "Open Nexus Mods in your browser");
        nexusLinkBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://www.nexusmods.com", UseShellExecute = true }); }
            catch { }
        };

        var openFolderBtn = new Button
        {
            Content         = "📂",
            Padding         = new Thickness(8, 3),
            Background      = Brushes.Transparent,
            Foreground      = MutedBrush,
            BorderBrush     = MutedBrush,
            BorderThickness = new Thickness(1),
            FontSize        = 10,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible       = _getDownloadsFolder != null
        };
        openFolderBtn.Click += (_, _) =>
        {
            try
            {
                var folder = _getDownloadsFolder?.Invoke() ?? string.Empty;
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                }
            }
            catch { }
        };

        var authIndicator = BuildAuthIndicator();

        var rightBtns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightBtns.Children.Add(_nxmBtn);
        rightBtns.Children.Add(nexusLinkBtn);
        rightBtns.Children.Add(openFolderBtn);
        rightBtns.Children.Add(authIndicator);

        var activeCountRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        activeCountRow.Children.Add(new TextBlock { Text = "DOWNLOADS", Foreground = MutedBrush, FontSize = 11, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        activeCountRow.Children.Add(_activeCountBadge);

        // DockPanel: right buttons docked first so they always get their space; left text clips
        var activeHeaderDock = new DockPanel();
        DockPanel.SetDock(rightBtns, Dock.Right);
        activeHeaderDock.Children.Add(rightBtns);
        activeHeaderDock.Children.Add(new Border { ClipToBounds = true, Child = activeCountRow });

        var activeHeader = new Border { Background = HeaderBrush, Padding = new Thickness(12, 8), Child = activeHeaderDock };

        _activePanel = new StackPanel { Spacing = 4, Margin = new Thickness(8, 8, 8, 4) };

        // Completed section header with Clear button
        var clearBtn = new Button
        {
            Content         = "Clear all",
            Padding         = new Thickness(10, 3),
            Background      = Brushes.Transparent,
            Foreground      = MutedBrush,
            BorderBrush     = MutedBrush,
            BorderThickness = new Thickness(1),
            FontSize        = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        clearBtn.Click += (_, _) =>
        {
            var done = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Where(_downloadService.Downloads, e => !e.IsActive));
            foreach (var e in done)
            {
                if (e.LocalPath != null && File.Exists(e.LocalPath)) File.Delete(e.LocalPath);
                _downloadService.Downloads.Remove(e);
            }
        };

        var completedHeaderGrid = new Grid();
        completedHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        completedHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var completedLabel = new TextBlock { Text = "COMPLETED", Foreground = MutedBrush, FontSize = 11, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(completedLabel, 0);
        Grid.SetColumn(clearBtn, 1);
        completedHeaderGrid.Children.Add(completedLabel);
        completedHeaderGrid.Children.Add(clearBtn);

        var completedHeader = new Border
        {
            Background      = HeaderBrush,
            Padding         = new Thickness(12, 8),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush     = new SolidColorBrush(Color.Parse("#202225")),
            Child           = completedHeaderGrid
        };

        _completedPanel = new StackPanel { Spacing = 4, Margin = new Thickness(8, 8, 8, 4) };

        // Hint text for double-click install
        var hintText = new TextBlock
        {
            Text              = "Double-click to install",
            FontSize          = 9,
            Foreground        = MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin            = new Thickness(0, 0, 12, 4)
        };
        hintText.IsVisible = _installCallback != null;

        var completedInner = new StackPanel();
        completedInner.Children.Add(completedHeader);
        completedInner.Children.Add(hintText);
        completedInner.Children.Add(_completedPanel);

        _completedSection = new Border { Child = completedInner, IsVisible = false };

        var contentStack = new StackPanel();
        contentStack.Children.Add(_activePanel);
        contentStack.Children.Add(_completedSection);

        var scrollViewer = new ScrollViewer
        {
            Content                    = contentStack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto
        };

        var root = new DockPanel();
        DockPanel.SetDock(activeHeader, Dock.Top);
        root.Children.Add(activeHeader);
        root.Children.Add(scrollViewer);

        Content = root;

        _downloadService.Downloads.CollectionChanged += OnCollectionChanged;
        RebuildCards();
    }

    private Control BuildAuthIndicator()
    {
        bool connected = _api.HasApiKey;

        var dot = new Button
        {
            Content           = connected ? "✓ Nexus" : "Nexus",
            FontSize          = 10,
            Padding           = new Thickness(8, 3),
            Background        = Brushes.Transparent,
            BorderThickness   = new Thickness(1),
            BorderBrush       = connected ? GreenBrush : RedBrush,
            Foreground        = connected ? GreenBrush : RedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(dot, connected ? "Connected to Nexus — click to change key" : "Not connected to Nexus — click to add API key");

        _api.ApiKeyValidityChanged += valid =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                dot.Content    = valid ? "✓ Nexus" : "Nexus";
                dot.Foreground = valid ? GreenBrush : RedBrush;
                dot.BorderBrush = valid ? GreenBrush : RedBrush;
                ToolTip.SetTip(dot, valid ? "Connected to Nexus — click to change key" : "Not connected to Nexus — click to add API key");
            });

        // Popup content
        var keyBox = new TextBox
        {
            Watermark       = "Paste your Nexus API key here",
            Width           = 300,
            FontSize        = 11,
            Background      = new SolidColorBrush(Color.Parse("#1E2124")),
            Foreground      = WhiteBrush,
            BorderBrush     = MutedBrush,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(8, 5)
        };

        var getLinkBtn = new Button
        {
            Content         = "Get API key ↗  nexusmods.com/settings/api-keys",
            Padding         = new Thickness(0, 2),
            Background      = Brushes.Transparent,
            Foreground      = MutedBrush,
            BorderThickness = new Thickness(0),
            FontSize        = 10,
            Cursor          = new Cursor(StandardCursorType.Hand)
        };
        getLinkBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://www.nexusmods.com/settings/api-keys", UseShellExecute = true }); }
            catch { }
        };

        var saveBtn = new Button
        {
            Content         = "Save",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding         = new Thickness(16, 5),
            Background      = new SolidColorBrush(Color.Parse("#3BA55D")),
            Foreground      = WhiteBrush,
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(3),
            FontSize        = 11
        };

        var popupStack = new StackPanel { Spacing = 8 };
        popupStack.Children.Add(new TextBlock { Text = "Nexus API Key", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = WhiteBrush });
        popupStack.Children.Add(keyBox);
        popupStack.Children.Add(getLinkBtn);
        popupStack.Children.Add(saveBtn);

        var popupBorder = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#2C2F33")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#202225")),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(14),
            Child           = popupStack
        };

        var popup = new Popup
        {
            Child                = popupBorder,
            Placement            = PlacementMode.Bottom,
            PlacementTarget      = dot,
            IsLightDismissEnabled = true
        };

        saveBtn.Click += (_, _) =>
        {
            var key = keyBox.Text?.Trim() ?? string.Empty;
            _api.ApiKey = key;
            bool has = !string.IsNullOrEmpty(key);
            dot.Foreground = has ? GreenBrush : RedBrush;
            ToolTip.SetTip(dot, has ? "Connected to Nexus — click to change key" : "Not connected to Nexus — click to add API key");
            popup.IsOpen = false;
            keyBox.Text = string.Empty;
        };

        dot.Click += (_, _) => popup.IsOpen = !popup.IsOpen;

        // Container that holds the button and anchors the popup
        var container = new Panel();
        container.Children.Add(dot);
        container.Children.Add(popup);
        return container;
    }

    private void SetNxmBtnRegistered(bool registered)
    {
        if (_nxmBtn == null) return;
        _nxmBtn.Content    = registered ? "✓ nxm" : "nxm";
        _nxmBtn.Foreground = registered ? GreenBrush : RedBrush;
        _nxmBtn.BorderBrush = registered ? GreenBrush : RedBrush;
        ToolTip.SetTip(_nxmBtn, registered
            ? "nxm:// registered — Mod Manager Download links work"
            : "nxm:// not registered — click to register");
    }

    private static Task<(bool register, bool dontAsk)> ShowNxmPromptAsync(Avalonia.Controls.Window parent)
    {
        var tcs = new TaskCompletionSource<(bool, bool)>();

        var dontAskCheck = new CheckBox
        {
            Content   = "Don't ask again",
            FontSize  = 11,
            Foreground = new SolidColorBrush(Color.Parse("#DCDDDE"))
        };

        var yesBtn = new Button
        {
            Content         = "Yes, register",
            Padding         = new Thickness(16, 6),
            Background      = new SolidColorBrush(Color.Parse("#3BA55D")),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(3),
            FontSize        = 11
        };

        var noBtn = new Button
        {
            Content         = "Not now",
            Padding         = new Thickness(16, 6),
            Background      = new SolidColorBrush(Color.Parse("#4F545C")),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(3),
            FontSize        = 11
        };

        var dialog = new Avalonia.Controls.Window
        {
            Title                   = "Register nxm:// protocol",
            Width                   = 420,
            SizeToContent           = SizeToContent.Height,
            WindowStartupLocation   = WindowStartupLocation.CenterOwner,
            CanResize               = false,
            Background              = new SolidColorBrush(Color.Parse("#2C2F33")),
            Content = new StackPanel
            {
                Margin  = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text        = "Register CatModManager as the handler for nxm:// links so that \"Mod Manager Download\" buttons on Nexus Mods open directly in this app.",
                        FontSize    = 12,
                        Foreground  = new SolidColorBrush(Color.Parse("#DCDDDE")),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    dontAskCheck,
                    new StackPanel
                    {
                        Orientation         = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing             = 8,
                        Children            = { noBtn, yesBtn }
                    }
                }
            }
        };

        void Complete(bool register)
        {
            tcs.TrySetResult((register, dontAskCheck.IsChecked == true));
            dialog.Close();
        }

        yesBtn.Click += (_, _) => Complete(true);
        noBtn.Click  += (_, _) => Complete(false);
        dialog.Closed += (_, _) => tcs.TrySetResult((false, false));

        dialog.ShowDialog(parent);
        return tcs.Task;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) RebuildCards();
        else Dispatcher.UIThread.InvokeAsync(RebuildCards);
    }

    private void RebuildCards()
    {
        _activePanel.Children.Clear();
        _completedPanel.Children.Clear();

        int activeCount = 0;
        var completed = new System.Collections.Generic.List<DownloadEntry>();

        foreach (var entry in _downloadService.Downloads)
        {
            if (entry.IsActive)
            {
                _activePanel.Children.Add(BuildCard(entry));
                activeCount++;
            }
            else
            {
                completed.Add(entry);
            }
        }

        // Most recent first
        for (int i = completed.Count - 1; i >= 0; i--)
            _completedPanel.Children.Add(BuildCard(completed[i]));

        _activeCountBadge.Text      = activeCount.ToString();
        _completedSection.IsVisible = _completedPanel.Children.Count > 0;
    }

    private Border BuildCard(DownloadEntry entry)
    {
        var cancelOrDeleteBtn = new Button
        {
            Content         = "✕",
            Padding         = new Thickness(8, 4),
            Background      = Brushes.Transparent,
            Foreground      = MutedBrush,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top
        };

        if (entry.IsActive)
        {
            cancelOrDeleteBtn.Click += (_, _) => _downloadService.Cancel(entry);
        }
        else
        {
            cancelOrDeleteBtn.Click += (_, _) =>
            {
                if (entry.LocalPath != null && File.Exists(entry.LocalPath)) File.Delete(entry.LocalPath);
                _downloadService.Downloads.Remove(entry);
            };
        }

        DockPanel.SetDock(cancelOrDeleteBtn, Dock.Right);

        var progressBar = new ProgressBar
        {
            Value     = entry.Progress,
            Maximum   = 100,
            Height    = 4,
            IsVisible = entry.IsActive,
            Margin    = new Thickness(0, 2, 0, 0)
        };

        var statusText = new TextBlock
        {
            Text         = entry.Status,
            FontSize     = 10,
            Foreground   = GetStatusBrush(entry),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var fileNameText = new TextBlock
        {
            Text       = entry.FileName,
            FontSize   = 10,
            Foreground = MutedBrush
        };

        var modNameText = new TextBlock
        {
            Text       = entry.ModName,
            FontWeight = FontWeight.Bold,
            FontSize   = 12,
            Foreground = WhiteBrush
        };

        var infoStack = new StackPanel { Spacing = 3 };
        infoStack.Children.Add(modNameText);
        infoStack.Children.Add(fileNameText);
        infoStack.Children.Add(progressBar);
        infoStack.Children.Add(statusText);

        var cardDock = new DockPanel { LastChildFill = true };
        cardDock.Children.Add(cancelOrDeleteBtn);
        cardDock.Children.Add(infoStack);

        var cardBorder = new Border
        {
            Background   = CardBrush,
            CornerRadius = new CornerRadius(4),
            Padding      = new Thickness(10),
            Margin       = new Thickness(0, 0, 0, 4),
            Child        = cardDock,
            Cursor       = (!entry.IsActive && _installCallback != null) ? new Cursor(StandardCursorType.Hand) : Cursor.Default
        };

        // Double-click completed card to install
        if (!entry.IsActive && _installCallback != null)
        {
            cardBorder.DoubleTapped += (_, _) =>
            {
                if (entry.LocalPath != null && File.Exists(entry.LocalPath))
                    _installCallback(entry.LocalPath);
            };
        }

        entry.PropertyChanged += (_, args) =>
        {
            void Update()
            {
                switch (args.PropertyName)
                {
                    case nameof(DownloadEntry.IsActive):
                        RebuildCards();
                        break;
                    case nameof(DownloadEntry.Progress):
                        progressBar.Value = entry.Progress;
                        break;
                    case nameof(DownloadEntry.Status):
                        statusText.Text       = entry.Status;
                        statusText.Foreground = GetStatusBrush(entry);
                        break;
                    case nameof(DownloadEntry.ModName):
                        modNameText.Text = entry.ModName;
                        break;
                    case nameof(DownloadEntry.FileName):
                        fileNameText.Text = entry.FileName;
                        break;
                    case nameof(DownloadEntry.HasFailed):
                        statusText.Foreground = GetStatusBrush(entry);
                        break;
                }
            }

            if (Dispatcher.UIThread.CheckAccess()) Update();
            else Dispatcher.UIThread.InvokeAsync(Update);
        };

        return cardBorder;
    }

    private static IBrush GetStatusBrush(DownloadEntry entry)
    {
        if (entry.HasFailed) return RedBrush;
        if (!entry.IsActive && entry.Status == "Done") return GreenBrush;
        return MutedBrush;
    }

}
