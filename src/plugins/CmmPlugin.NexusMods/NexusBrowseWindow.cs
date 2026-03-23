using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Code-only Avalonia window for browsing and searching Nexus Mods.
/// Uses v2 GraphQL (no API key). Supports pagination, category filter, and direct download.
/// </summary>
public class NexusBrowseWindow : Window
{
    // ── Theme ──────────────────────────────────────────────────────────────────

    private static readonly IBrush BgBrush       = new SolidColorBrush(Color.Parse("#36393F"));
    private static readonly IBrush CardBrush     = new SolidColorBrush(Color.Parse("#2F3136"));
    private static readonly IBrush CardHover     = new SolidColorBrush(Color.Parse("#40444B"));
    private static readonly IBrush HeaderBrush   = new SolidColorBrush(Color.Parse("#1E2124"));
    private static readonly IBrush AccentBrush   = new SolidColorBrush(Color.Parse("#5865F2"));
    private static readonly IBrush GreenBrush    = new SolidColorBrush(Color.Parse("#3BA55D"));
    private static readonly IBrush MutedBrush    = new SolidColorBrush(Color.Parse("#72767D"));
    private static readonly IBrush WhiteBrush    = Brushes.White;
    private static readonly IBrush GoldBrush     = new SolidColorBrush(Color.Parse("#FAA61A"));
    private static readonly IBrush DimBrush      = new SolidColorBrush(Color.Parse("#DCDDDE"));

    private const int PageSize = 20;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly NexusApiService      _api             = null!;
    private readonly NexusDownloadService? _downloadService;
    private readonly Func<string>?        _getDownloadsFolder;
    private readonly string               _gameDomain      = null!;
    private readonly int                  _gameId;

    // ── UI controls ───────────────────────────────────────────────────────────

    private readonly TextBox     _searchBox     = null!;
    private readonly StackPanel  _resultsPanel  = null!;
    private readonly TextBlock   _statusText    = null!;
    private readonly StackPanel  _sortButtons   = null!;
    private readonly ComboBox    _categoryCombo = null!;
    private readonly Button      _loadMoreBtn   = null!;

    // ── State ─────────────────────────────────────────────────────────────────

    private BrowseSort _sort         = BrowseSort.Trending;
    private int        _offset       = 0;
    private int        _total        = 0;
    private CancellationTokenSource? _cts;

    // ── AVLN3001 parameterless constructor ────────────────────────────────────

    public NexusBrowseWindow() { }

    // ── Main constructor ──────────────────────────────────────────────────────

    public NexusBrowseWindow(
        NexusApiService api,
        string gameDomain,
        NexusDownloadService? downloadService = null,
        Func<string>? getDownloadsFolder = null)
    {
        _api                = api;
        _gameDomain         = gameDomain;
        _gameId             = NexusApiService.GetGameId(gameDomain);
        _downloadService    = downloadService;
        _getDownloadsFolder = getDownloadsFolder;

        Title                 = $"Browse Nexus Mods — {gameDomain}";
        Width                 = 880;
        Height                = 640;
        MinWidth              = 640;
        MinHeight             = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background            = BgBrush;

        // ── Search bar ────────────────────────────────────────────────────────

        _searchBox = new TextBox
        {
            Watermark         = "Search mods…",
            FontSize          = 13,
            Padding           = new Thickness(8, 6),
            Background        = new SolidColorBrush(Color.Parse("#1E2124")),
            Foreground        = WhiteBrush,
            CaretBrush        = WhiteBrush,
            BorderThickness   = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _searchBox.KeyDown += (_, e) => { if (e.Key == Key.Return) FireSearch(); };

        var searchBtn = MakeBtn("Search", AccentBrush);
        searchBtn.Click += (_, _) => FireSearch();

        var clearBtn = MakeBtn("✕", new SolidColorBrush(Color.Parse("#4F545C")));
        ToolTip.SetTip(clearBtn, "Clear search");
        clearBtn.Click += (_, _) => { _searchBox.Text = string.Empty; FireSearch(); };

        var searchRow = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        DockPanel.SetDock(searchBtn, Dock.Right);
        DockPanel.SetDock(clearBtn,  Dock.Right);
        searchRow.Children.Add(searchBtn);
        searchRow.Children.Add(clearBtn);
        searchRow.Children.Add(_searchBox);

        // ── Sort + category bar ───────────────────────────────────────────────

        _sortButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AddSortButton("Trending",       BrowseSort.Trending);
        AddSortButton("Latest Added",   BrowseSort.LatestAdded);
        AddSortButton("Latest Updated", BrowseSort.LatestUpdated);

        _categoryCombo = new ComboBox
        {
            PlaceholderText   = "All categories",
            MinWidth          = 180,
            Background        = new SolidColorBrush(Color.Parse("#1E2124")),
            Foreground        = WhiteBrush,
            BorderThickness   = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _categoryCombo.SelectionChanged += (_, _) => FireSearch();

        var filterRow = new DockPanel { Margin = new Thickness(10, 0, 10, 8) };
        DockPanel.SetDock(_categoryCombo, Dock.Right);
        filterRow.Children.Add(_sortButtons);
        filterRow.Children.Add(_categoryCombo);

        var topPanel = new StackPanel { Background = HeaderBrush };
        topPanel.Children.Add(searchRow);
        topPanel.Children.Add(filterRow);

        // ── Status bar ────────────────────────────────────────────────────────

        _statusText = new TextBlock
        {
            Text       = "Loading…",
            Foreground = MutedBrush,
            FontSize   = 11,
            Margin     = new Thickness(12, 4),
        };

        // ── Load More button ──────────────────────────────────────────────────

        _loadMoreBtn = new Button
        {
            Content           = "Load More",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding           = new Thickness(24, 8),
            Background        = new SolidColorBrush(Color.Parse("#4F545C")),
            Foreground        = WhiteBrush,
            BorderThickness   = new Thickness(0),
            CornerRadius      = new CornerRadius(4),
            Margin            = new Thickness(0, 4, 0, 8),
            IsVisible         = false,
            Cursor            = new Cursor(StandardCursorType.Hand),
        };
        _loadMoreBtn.Click += (_, _) => FireLoadMore();

        // ── Results ───────────────────────────────────────────────────────────

        _resultsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(8) };

        var scrollContent = new StackPanel();
        scrollContent.Children.Add(_resultsPanel);
        scrollContent.Children.Add(_loadMoreBtn);

        var scroll = new ScrollViewer
        {
            Content = scrollContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // ── Root layout ───────────────────────────────────────────────────────

        var root = new DockPanel();
        DockPanel.SetDock(topPanel,    Dock.Top);
        DockPanel.SetDock(_statusText, Dock.Bottom);
        root.Children.Add(topPanel);
        root.Children.Add(_statusText);
        root.Children.Add(scroll);
        Content = root;

        Opened += async (_, _) =>
        {
            await LoadCategoriesAsync();
            await LoadAsync(reset: true);
        };
    }

    // ── Category population ───────────────────────────────────────────────────

    private async Task LoadCategoriesAsync()
    {
        if (_gameId == 0) return;
        var names = await _api.GetCategoryNamesAsync(_gameDomain);
        // Insert blank "all" item at top (null tag = no filter)
        _categoryCombo.Items.Clear();
        _categoryCombo.Items.Add(new ComboBoxItem { Content = "All categories", Tag = (string?)null });
        foreach (var name in names)
            _categoryCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        _categoryCombo.SelectedIndex = 0;
    }

    // ── Sort buttons ──────────────────────────────────────────────────────────

    private void AddSortButton(string label, BrowseSort sort)
    {
        var btn = new Button
        {
            Content         = label,
            Tag             = sort,
            Padding         = new Thickness(10, 4),
            FontSize        = 11,
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(3),
            Cursor          = new Cursor(StandardCursorType.Hand),
        };
        btn.Click += (_, _) =>
        {
            _sort = sort;
            _searchBox.Text = string.Empty;
            RefreshSortButtons();
            FireSearch();
        };
        _sortButtons.Children.Add(btn);
        RefreshSortButtons();
    }

    private void RefreshSortButtons()
    {
        foreach (var child in _sortButtons.Children.OfType<Button>())
        {
            bool active = child.Tag is BrowseSort s && s == _sort;
            child.Background = active ? AccentBrush : new SolidColorBrush(Color.Parse("#4F545C"));
            child.Foreground = WhiteBrush;
        }
    }

    // ── Query helpers ─────────────────────────────────────────────────────────

    private string? SelectedCategory =>
        (_categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void FireSearch()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Dispatcher.UIThread.InvokeAsync(() => LoadAsync(reset: true, _cts.Token));
    }

    private void FireLoadMore()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Dispatcher.UIThread.InvokeAsync(() => LoadAsync(reset: false, _cts.Token));
    }

    // ── Core load logic ───────────────────────────────────────────────────────

    private async Task LoadAsync(bool reset, CancellationToken ct = default)
    {
        if (reset)
        {
            _offset = 0;
            _resultsPanel.Children.Clear();
            _loadMoreBtn.IsVisible = false;
        }

        var query    = (_searchBox?.Text ?? "").Trim();
        var category = SelectedCategory;

        SetStatus("Loading…");
        _loadMoreBtn.IsEnabled = false;

        if (_gameId == 0)
        {
            SetStatus($"Game '{_gameDomain}' not in GameDomainToId map — browse unavailable.");
            return;
        }

        List<NexusBrowseMod> mods;
        int total;

        if (string.IsNullOrEmpty(query))
            (mods, total) = await _api.GetBrowseModsAsync(
                _gameDomain, _gameId, _sort, categoryName: category, offset: _offset, ct: ct);
        else
            (mods, total) = await _api.SearchModsAsync(
                _gameDomain, _gameId, query, categoryName: category, offset: _offset, ct: ct);

        if (ct.IsCancellationRequested) return;

        _total   = total;
        _offset += mods.Count;

        if (mods.Count == 0 && reset)
        {
            SetStatus(string.IsNullOrEmpty(query)
                ? $"No mods found for '{_gameDomain}'."
                : $"No results for '{query}'.");
            return;
        }

        foreach (var mod in mods)
            _resultsPanel.Children.Add(BuildCard(mod));

        _loadMoreBtn.IsVisible  = _offset < total;
        _loadMoreBtn.IsEnabled  = true;
        _loadMoreBtn.Content    = $"Load More ({_offset:N0} / {total:N0})";

        var label = string.IsNullOrEmpty(query)
            ? $"Showing {_offset:N0} of {total:N0} mods"
            : $"{_offset:N0} of {total:N0} results for '{query}'";
        SetStatus(label);
    }

    private void SetStatus(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
            _statusText.Text = text;
        else
            Dispatcher.UIThread.Post(() => _statusText.Text = text);
    }

    // ── Mod card builder ──────────────────────────────────────────────────────

    private Control BuildCard(NexusBrowseMod mod)
    {
        var nexusUrl = $"https://www.nexusmods.com/{mod.GameDomain}/mods/{mod.ModId}";

        var nameLabel = new TextBlock
        {
            Text         = mod.Name,
            FontSize     = 13,
            FontWeight   = FontWeight.Bold,
            Foreground   = WhiteBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(mod.Author))       parts.Add($"by {mod.Author}");
        if (!string.IsNullOrEmpty(mod.CategoryName)) parts.Add(mod.CategoryName);
        if (!string.IsNullOrEmpty(mod.Version))      parts.Add($"v{mod.Version}");
        var metaLabel = new TextBlock
        {
            Text       = string.Join("  ·  ", parts),
            FontSize   = 11,
            Foreground = MutedBrush,
        };

        var summaryLabel = new TextBlock
        {
            Text         = mod.Summary,
            FontSize     = 11,
            Foreground   = DimBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight    = 34,
            Margin       = new Thickness(0, 2, 0, 4),
        };

        var statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        statsPanel.Children.Add(StatChip("↓", FormatNumber(mod.DownloadCount),    GoldBrush));
        statsPanel.Children.Add(StatChip("♥", FormatNumber(mod.EndorsementCount), new SolidColorBrush(Color.Parse("#ED4245"))));

        var infoStack = new StackPanel { Spacing = 2 };
        infoStack.Children.Add(nameLabel);
        infoStack.Children.Add(metaLabel);
        infoStack.Children.Add(summaryLabel);
        infoStack.Children.Add(statsPanel);

        // Buttons
        var openBtn = MakeBtn("Open ↗", AccentBrush);
        openBtn.Click += (_, _) => OpenUrl(nexusUrl);

        var btnPanel = new StackPanel
        {
            Orientation       = Orientation.Vertical,
            Spacing           = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 0, 0),
        };
        btnPanel.Children.Add(openBtn);

        if (_downloadService != null)
        {
            var dlBtn = MakeBtn("⬇ Download", GreenBrush);
            dlBtn.Click += async (_, _) => await QueueBestFileAsync(mod, dlBtn);
            btnPanel.Children.Add(dlBtn);
        }

        var card = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(btnPanel, Dock.Right);
        card.Children.Add(btnPanel);
        card.Children.Add(infoStack);

        var border = new Border
        {
            Background   = CardBrush,
            CornerRadius = new CornerRadius(5),
            Padding      = new Thickness(12, 10),
            Child        = card,
            Cursor       = new Cursor(StandardCursorType.Hand),
        };

        border.PointerEntered += (_, _) => border.Background = CardHover;
        border.PointerExited  += (_, _) => border.Background = CardBrush;
        border.Tapped         += (_, _) => OpenUrl(nexusUrl);

        return border;
    }

    // ── Direct download ───────────────────────────────────────────────────────

    private async Task QueueBestFileAsync(NexusBrowseMod mod, Button btn)
    {
        btn.IsEnabled = false;
        btn.Content   = "…";

        var files = await _api.GetFilesAsync(mod.GameDomain, mod.ModId);
        var main  = files.Files
            .Where(f => string.Equals(f.CategoryName, "MAIN", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.SizeKb)
            .FirstOrDefault()
            ?? files.Files.OrderByDescending(f => f.SizeKb).FirstOrDefault();

        if (main == null)
        {
            btn.Content   = "No files";
            return;
        }

        var folder = _getDownloadsFolder?.Invoke() ?? System.IO.Path.GetTempPath();
        _downloadService!.QueueDownloadDirect(
            mod.GameDomain, mod.ModId, main.FileId,
            mod.Name, folder, mod.Version, mod.CategoryName);

        btn.Content = "✓ Queued";
        SetStatus($"Queued: {mod.Name}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Button MakeBtn(string label, IBrush bg) => new Button
    {
        Content         = label,
        Padding         = new Thickness(10, 5),
        FontSize        = 11,
        Background      = bg,
        Foreground      = WhiteBrush,
        BorderThickness = new Thickness(0),
        CornerRadius    = new CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center,
        Cursor          = new Cursor(StandardCursorType.Hand),
    };

    private static Control StatChip(string icon, string value, IBrush color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        panel.Children.Add(new TextBlock { Text = icon,  Foreground = color,    FontSize = 11 });
        panel.Children.Add(new TextBlock { Text = value, Foreground = MutedBrush, FontSize = 11 });
        return panel;
    }

    private static string FormatNumber(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:F1}M",
        >= 1_000     => $"{n / 1_000.0:F1}K",
        _            => n.ToString()
    };

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }
}
