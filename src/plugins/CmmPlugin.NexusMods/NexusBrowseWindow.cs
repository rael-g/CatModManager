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
/// Search: search.nexusmods.com (no API key).  Browse: v1 trending/latest.
/// </summary>
public class NexusBrowseWindow : Window
{
    // ── Theme ──────────────────────────────────────────────────────────────────

    private static readonly IBrush BgBrush     = new SolidColorBrush(Color.Parse("#36393F"));
    private static readonly IBrush CardBrush   = new SolidColorBrush(Color.Parse("#2F3136"));
    private static readonly IBrush CardHover   = new SolidColorBrush(Color.Parse("#40444B"));
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.Parse("#1E2124"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#5865F2"));
    private static readonly IBrush MutedBrush  = new SolidColorBrush(Color.Parse("#72767D"));
    private static readonly IBrush WhiteBrush  = Brushes.White;
    private static readonly IBrush GoldBrush   = new SolidColorBrush(Color.Parse("#FAA61A"));

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly NexusApiService _api        = null!;
    private readonly string          _gameDomain = null!;
    private readonly int             _gameId;

    private readonly TextBox     _searchBox    = null!;
    private readonly StackPanel  _resultsPanel = null!;
    private readonly TextBlock   _statusText   = null!;
    private readonly StackPanel  _sortButtons  = null!;

    private BrowseSort           _sort = BrowseSort.Trending;
    private CancellationTokenSource? _cts;

    // ── AVLN3001 parameterless constructor ────────────────────────────────────

    public NexusBrowseWindow() { }

    // ── Main constructor ──────────────────────────────────────────────────────

    public NexusBrowseWindow(NexusApiService api, string gameDomain)
    {
        _api        = api;
        _gameDomain = gameDomain;
        _gameId     = NexusApiService.GetGameId(gameDomain);

        Title                  = $"Browse Nexus Mods — {gameDomain}";
        Width                  = 860;
        Height                 = 620;
        MinWidth               = 600;
        MinHeight              = 400;
        WindowStartupLocation  = WindowStartupLocation.CenterOwner;
        Background             = BgBrush;

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
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return) FireSearch();
        };

        var searchBtn = new Button
        {
            Content           = "Search",
            Padding           = new Thickness(14, 6),
            Background        = AccentBrush,
            Foreground        = WhiteBrush,
            BorderThickness   = new Thickness(0),
            CornerRadius      = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor            = new Cursor(StandardCursorType.Hand),
        };
        searchBtn.Click += (_, _) => FireSearch();

        var clearBtn = new Button
        {
            Content           = "✕",
            Padding           = new Thickness(8, 6),
            Background        = new SolidColorBrush(Color.Parse("#4F545C")),
            Foreground        = WhiteBrush,
            BorderThickness   = new Thickness(0),
            CornerRadius      = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor            = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(clearBtn, "Clear search and show trending");
        clearBtn.Click += (_, _) =>
        {
            _searchBox.Text = string.Empty;
            FireSearch();
        };

        var searchRow = new DockPanel { Margin = new Thickness(10, 8) };
        DockPanel.SetDock(searchBtn, Dock.Right);
        DockPanel.SetDock(clearBtn,  Dock.Right);
        searchRow.Children.Add(searchBtn);
        searchRow.Children.Add(clearBtn);
        searchRow.Children.Add(_searchBox);

        // ── Sort toggle buttons ───────────────────────────────────────────────

        _sortButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(10, 0, 10, 8),
            Spacing     = 4,
        };
        AddSortButton("Trending",       BrowseSort.Trending);
        AddSortButton("Latest Added",   BrowseSort.LatestAdded);
        AddSortButton("Latest Updated", BrowseSort.LatestUpdated);

        var topPanel = new StackPanel
        {
            Background = HeaderBrush,
        };
        topPanel.Children.Add(searchRow);
        topPanel.Children.Add(_sortButtons);

        // ── Status bar ────────────────────────────────────────────────────────

        _statusText = new TextBlock
        {
            Text       = "Loading…",
            Foreground = MutedBrush,
            FontSize   = 11,
            Margin     = new Thickness(12, 4),
        };

        // ── Results ───────────────────────────────────────────────────────────

        _resultsPanel = new StackPanel
        {
            Spacing = 4,
            Margin  = new Thickness(8),
        };

        var scroll = new ScrollViewer
        {
            Content              = _resultsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        // ── Layout ────────────────────────────────────────────────────────────

        var root = new DockPanel();
        DockPanel.SetDock(topPanel,    Dock.Top);
        DockPanel.SetDock(_statusText, Dock.Bottom);
        root.Children.Add(topPanel);
        root.Children.Add(_statusText);
        root.Children.Add(scroll);
        Content = root;

        Opened += async (_, _) => await LoadAsync();
    }

    // ── Sort button helper ────────────────────────────────────────────────────

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
            child.Background = active
                ? AccentBrush
                : new SolidColorBrush(Color.Parse("#4F545C"));
            child.Foreground = WhiteBrush;
        }
    }

    // ── Search trigger ────────────────────────────────────────────────────────

    private void FireSearch()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        // LoadAsync is already async; run on the UI thread so Avalonia controls are accessible.
        Dispatcher.UIThread.InvokeAsync(() => LoadAsync(ct));
    }

    private async Task LoadAsync(CancellationToken ct = default)
    {
        var query = (_searchBox?.Text ?? "").Trim();

        SetStatus("Loading…");
        _resultsPanel.Children.Clear();

        if (_gameId == 0)
        {
            SetStatus($"Game '{_gameDomain}' not recognised — add it to NexusApiService.GameDomainToId.");
            return;
        }

        List<NexusBrowseMod> mods;
        int total;

        if (string.IsNullOrEmpty(query))
            (mods, total) = await _api.GetBrowseModsAsync(_gameDomain, _gameId, _sort, ct: ct);
        else
            (mods, total) = await _api.SearchModsAsync(_gameDomain, _gameId, query, ct: ct);

        if (ct.IsCancellationRequested) return;

        if (mods.Count == 0)
        {
            SetStatus(string.IsNullOrEmpty(query)
                ? $"No mods found for game '{_gameDomain}'."
                : $"No results for '{query}'.");
            return;
        }

        foreach (var mod in mods)
            _resultsPanel.Children.Add(BuildCard(mod));

        var label = string.IsNullOrEmpty(query)
            ? $"Showing {mods.Count} of {total:N0} mods"
            : $"{mods.Count} of {total:N0} results for '{query}'";
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

        // Name
        var nameLabel = new TextBlock
        {
            Text            = mod.Name,
            FontSize        = 13,
            FontWeight      = FontWeight.Bold,
            Foreground      = WhiteBrush,
            TextWrapping    = TextWrapping.NoWrap,
            TextTrimming    = TextTrimming.CharacterEllipsis,
        };

        // Author + category + version
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(mod.Author))        parts.Add($"by {mod.Author}");
        if (!string.IsNullOrEmpty(mod.CategoryName))  parts.Add(mod.CategoryName);
        if (!string.IsNullOrEmpty(mod.Version))       parts.Add($"v{mod.Version}");
        var metaLabel = new TextBlock
        {
            Text       = string.Join("  ·  ", parts),
            FontSize   = 11,
            Foreground = MutedBrush,
        };

        // Summary
        var summaryLabel = new TextBlock
        {
            Text         = mod.Summary,
            FontSize     = 11,
            Foreground   = new SolidColorBrush(Color.Parse("#DCDDDE")),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight    = 34,   // ~2 lines
            Margin       = new Thickness(0, 2, 0, 4),
        };

        // Stats row
        var statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        statsPanel.Children.Add(StatChip("↓", FormatNumber(mod.DownloadCount),    GoldBrush));
        statsPanel.Children.Add(StatChip("♥", FormatNumber(mod.EndorsementCount), new SolidColorBrush(Color.Parse("#ED4245"))));

        var infoStack = new StackPanel { Spacing = 2 };
        infoStack.Children.Add(nameLabel);
        infoStack.Children.Add(metaLabel);
        infoStack.Children.Add(summaryLabel);
        infoStack.Children.Add(statsPanel);

        // Open button
        var openBtn = new Button
        {
            Content         = "Open ↗",
            Padding         = new Thickness(10, 5),
            FontSize        = 11,
            Background      = AccentBrush,
            Foreground      = WhiteBrush,
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor          = new Cursor(StandardCursorType.Hand),
        };
        openBtn.Click += (_, _) => OpenUrl(nexusUrl);

        var card = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(openBtn, Dock.Right);
        card.Children.Add(openBtn);
        card.Children.Add(infoStack);

        var border = new Border
        {
            Background   = CardBrush,
            CornerRadius = new CornerRadius(5),
            Padding      = new Thickness(12, 10),
            Child        = card,
            Cursor       = new Cursor(StandardCursorType.Hand),
        };

        // Hover highlight
        border.PointerEntered += (_, _) => border.Background = CardHover;
        border.PointerExited  += (_, _) => border.Background = CardBrush;

        // Click anywhere on the card opens the mod page
        border.Tapped += (_, _) => OpenUrl(nexusUrl);

        return border;
    }

    private static Control StatChip(string icon, string value, IBrush color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        panel.Children.Add(new TextBlock { Text = icon,  Foreground = color,      FontSize = 11 });
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
        catch { /* silently ignore: no browser or sandboxed */ }
    }
}
