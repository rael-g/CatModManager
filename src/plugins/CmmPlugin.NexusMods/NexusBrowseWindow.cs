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
using CatModManager.Theme;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Code-only Avalonia window for browsing and searching Nexus Mods.
/// Uses v2 GraphQL (no API key). Supports pagination, category filter, and direct download.
/// </summary>
public class NexusBrowseWindow : Window
{
    // ── Theme ──────────────────────────────────────────────────────────────────

    private static readonly IBrush BgBrush       = CmmPalette.Brushes.ContentBg;
    private static readonly IBrush CardBrush     = CmmPalette.Brushes.SidebarBg;
    private static readonly IBrush CardHover     = CmmPalette.Brushes.SurfaceSelected;
    private static readonly IBrush HeaderBrush   = CmmPalette.Brushes.AppBackground;
    private static readonly IBrush AccentBrush   = CmmPalette.Brushes.Accent;
    private static readonly IBrush GreenBrush    = CmmPalette.Brushes.StatusActive;
    private static readonly IBrush MutedBrush    = CmmPalette.Brushes.TextSubtle;
    private static readonly IBrush WhiteBrush    = Brushes.White;
    private static readonly IBrush GoldBrush     = CmmPalette.Brushes.StatusWarning;
    private static readonly IBrush DimBrush      = CmmPalette.Brushes.TextPrimary;

    private const int PageSize = 20;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly NexusApiService      _api             = null!;
    private readonly NexusDownloadService? _downloadService;
    private readonly Func<string>?        _getDownloadsFolder;
    /// <summary>Not readonly: picking a game by hand is what replaces a domain CMM could not supply.</summary>
    private string                        _gameDomain      = null!;
    private int                           _gameId;

    /// <summary>The CMM game the override is remembered against. Null when there is nothing to key it to.</summary>
    private readonly string?              _cmmGameId;

    // ── UI controls ───────────────────────────────────────────────────────────

    private readonly TextBox     _searchBox          = null!;
    private readonly StackPanel  _resultsPanel       = null!;
    private readonly TextBlock   _statusText         = null!;
    private readonly StackPanel  _sortButtons        = null!;
    private readonly ComboBox    _categoryCombo      = null!;
    private readonly Button      _loadMoreBtn        = null!;
    private readonly StackPanel  _modeButtons        = null!;
    private readonly Border      _collectionsNotice  = null!;

    // ── State ─────────────────────────────────────────────────────────────────

    private BrowseSort _sort              = BrowseSort.Trending;
    private bool       _includeAdult      = false;
    private bool       _browseCollections = false;
    private int        _offset            = 0;
    private int        _total             = 0;
    private CancellationTokenSource? _cts;

    // ── AVLN3001 parameterless constructor ────────────────────────────────────

    public NexusBrowseWindow() { }

    // ── Main constructor ──────────────────────────────────────────────────────

    public NexusBrowseWindow(
        NexusApiService api,
        string gameDomain,
        NexusDownloadService? downloadService = null,
        Func<string>? getDownloadsFolder = null,
        string? cmmGameId = null)
    {
        _api                = api;
        _gameDomain         = gameDomain;
        _gameId             = NexusApiService.GetGameId(gameDomain);
        _downloadService    = downloadService;
        _getDownloadsFolder = getDownloadsFolder;
        _cmmGameId          = cmmGameId;

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
            Background        = CmmPalette.Brushes.AppBackground,
            Foreground        = WhiteBrush,
            CaretBrush        = WhiteBrush,
            BorderThickness   = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _searchBox.KeyDown += (_, e) => { if (e.Key == Key.Return) FireSearch(); };

        var searchBtn = MakeBtn("Search", AccentBrush);
        searchBtn.Click += (_, _) => FireSearch();

        var clearBtn = MakeBtn("✕", CmmPalette.Brushes.SurfaceSelected);
        ToolTip.SetTip(clearBtn, "Clear search");
        clearBtn.Click += (_, _) => { _searchBox.Text = string.Empty; FireSearch(); };

        var searchRow = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        DockPanel.SetDock(clearBtn,  Dock.Right);
        DockPanel.SetDock(searchBtn, Dock.Right);
        searchRow.Children.Add(clearBtn);
        searchRow.Children.Add(searchBtn);
        searchRow.Children.Add(_searchBox);

        // ── Mode toggle row (Mods | Collections) ─────────────────────────────

        _modeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 0,
            Margin      = new Thickness(10, 4, 10, 0),
        };
        AddModeButton("Mods",        collections: false);
        AddModeButton("Collections", collections: true);

        // ── Collections premium notice ────────────────────────────────────────

        var noticeText = new TextBlock
        {
            Text         = "Collections are a Nexus Premium feature. Click \"Open ↗\" on a collection, then click \"Add Collection\" on the website — CMM will handle the nxm:// link automatically.",
            FontSize     = 11,
            Foreground   = CmmPalette.Brushes.StatusWarning,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0),
        };
        _collectionsNotice = new Border
        {
            Background    = CmmPalette.Brushes.StatusWarningTint,
            BorderBrush   = CmmPalette.Brushes.StatusWarning,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding       = new Thickness(12, 6),
            Child         = noticeText,
            IsVisible     = false,
        };

        // ── Sort + category bar ───────────────────────────────────────────────

        _sortButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AddSortButton("Trending",       BrowseSort.Trending);
        AddSortButton("Latest Added",   BrowseSort.LatestAdded);
        AddSortButton("Latest Updated", BrowseSort.LatestUpdated);

        _categoryCombo = new ComboBox
        {
            PlaceholderText   = "All categories",
            MinWidth          = 180,
            Background        = CmmPalette.Brushes.AppBackground,
            Foreground        = WhiteBrush,
            BorderThickness   = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _categoryCombo.SelectionChanged += (_, _) => FireSearch();

        var adultCheck = new CheckBox
        {
            Content           = "Adult content",
            Foreground        = MutedBrush,
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked         = false,
        };
        adultCheck.IsCheckedChanged += (_, _) => { _includeAdult = adultCheck.IsChecked == true; FireSearch(); };

        var rightControls = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            Spacing           = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        rightControls.Children.Add(adultCheck);
        rightControls.Children.Add(_categoryCombo);

        var filterRow = new DockPanel { Margin = new Thickness(10, 4, 10, 8) };
        DockPanel.SetDock(rightControls, Dock.Right);
        filterRow.Children.Add(rightControls);
        filterRow.Children.Add(_sortButtons);

        var topPanel = new StackPanel { Background = HeaderBrush };
        topPanel.Children.Add(searchRow);
        topPanel.Children.Add(_modeButtons);
        topPanel.Children.Add(_collectionsNotice);
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
            Background        = CmmPalette.Brushes.SurfaceSelected,
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
        if (_gameId == 0) _gameId = await _api.GetGameIdAsync(_gameDomain);
        if (_gameId == 0) return;
        var names = await _api.GetCategoryNamesAsync(_gameDomain);
        // Insert blank "all" item at top (null tag = no filter)
        _categoryCombo.Items.Clear();
        _categoryCombo.Items.Add(new ComboBoxItem { Content = "All categories", Tag = (string?)null });
        foreach (var name in names)
            _categoryCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        _categoryCombo.SelectedIndex = 0;
    }

    // ── Mode buttons (Mods | Collections) ────────────────────────────────────

    private void AddModeButton(string label, bool collections)
    {
        var btn = new Button
        {
            Content         = label,
            Tag             = collections,
            Padding         = new Thickness(10, 4),
            FontSize        = 11,
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(3),
            Cursor          = new Cursor(StandardCursorType.Hand),
        };
        btn.Click += (_, _) =>
        {
            _browseCollections = collections;
            _searchBox.Text    = string.Empty;
            // Show sort + category only in Mods mode
            _sortButtons.IsVisible        = !_browseCollections;
            _categoryCombo.IsVisible      = !_browseCollections;
            _collectionsNotice.IsVisible  = _browseCollections;
            RefreshModeButtons();
            FireSearch();
        };
        _modeButtons.Children.Add(btn);
        RefreshModeButtons();
    }

    private void RefreshModeButtons()
    {
        foreach (var child in _modeButtons.Children.OfType<Button>())
        {
            bool active = child.Tag is bool b && b == _browseCollections;
            child.Background = active ? AccentBrush : CmmPalette.Brushes.SurfaceSelected;
            child.Foreground = WhiteBrush;
        }
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
            child.Background = active ? AccentBrush : CmmPalette.Brushes.SurfaceSelected;
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

        SetStatus("Loading…");
        _loadMoreBtn.IsEnabled = false;

        if (_browseCollections)
        {
            await LoadCollectionsAsync(reset, ct);
            return;
        }

        var query    = (_searchBox?.Text ?? "").Trim();
        var category = SelectedCategory;

        if (_gameId == 0)
        {
            SetStatus($"Resolving game ID for '{_gameDomain}'…");
            _gameId = await _api.GetGameIdAsync(_gameDomain, ct);
        }

        if (_gameId == 0)
        {
            ShowGamePicker();
            return;
        }

        List<NexusBrowseMod> mods;
        int total;
        string? error;

        if (string.IsNullOrEmpty(query))
            (mods, total, error) = await _api.GetBrowseModsAsync(
                _gameDomain, _gameId, _sort, categoryName: category, includeAdult: _includeAdult, offset: _offset, ct: ct);
        else
            (mods, total, error) = await _api.SearchModsAsync(
                _gameDomain, _gameId, query, categoryName: category, includeAdult: _includeAdult, offset: _offset, ct: ct);

        if (ct.IsCancellationRequested) return;

        // Before the empty-result message, because "no results" is a wrong thing to tell someone
        // whose query never reached the server.
        if (error != null)
        {
            SetStatus($"Nexus could not answer that: {error}");
            return;
        }

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

    private async Task LoadCollectionsAsync(bool reset, CancellationToken ct)
    {
        var query = (_searchBox?.Text ?? "").Trim();

        var (collections, total) = await _api.GetBrowseCollectionsAsync(
            _gameDomain,
            nameFilter: string.IsNullOrEmpty(query) ? null : query,
            count: PageSize, offset: _offset, ct: ct);

        if (ct.IsCancellationRequested) return;

        _total   = total;
        _offset += collections.Count;

        if (collections.Count == 0 && reset)
        {
            SetStatus(string.IsNullOrEmpty(query)
                ? $"No collections found for '{_gameDomain}'."
                : $"No collections matching '{query}'.");
            return;
        }

        foreach (var col in collections)
            _resultsPanel.Children.Add(BuildCollectionCard(col));

        _loadMoreBtn.IsVisible = _offset < total;
        _loadMoreBtn.IsEnabled = true;
        _loadMoreBtn.Content   = $"Load More ({_offset:N0} / {total:N0})";

        var label = string.IsNullOrEmpty(query)
            ? $"Showing {_offset:N0} of {total:N0} collections"
            : $"{_offset:N0} of {total:N0} collections for '{query}'";
        SetStatus(label);
    }

    // ── Game picker ───────────────────────────────────────────────────────────

    /// <summary>
    /// Asks which Nexus game this is, when CMM has no answer of its own.
    ///
    /// This used to be a dead end reading "game not found", which was true and useless: a game
    /// added by pointing at an executable has no game definition, so it has no Nexus domain, and
    /// nothing in the interface let the user supply one. The answer is remembered against the CMM
    /// game, so the question is asked once.
    /// </summary>
    private void ShowGamePicker()
    {
        _resultsPanel.Children.Clear();
        _loadMoreBtn.IsVisible = false;
        SetStatus($"CMM does not know which Nexus game '{_gameDomain}' is.");

        _resultsPanel.Children.Add(new TextBlock
        {
            Text         = "Which game on Nexus Mods is this?",
            FontSize     = 14,
            FontWeight   = FontWeight.Bold,
            Foreground   = WhiteBrush,
            Margin       = new Thickness(0, 8, 0, 2),
        });

        _resultsPanel.Children.Add(new TextBlock
        {
            Text         = "Search for it by name and pick it from the list. CMM will remember your "
                         + "choice for this game.",
            FontSize     = 11,
            Foreground   = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 8),
        });

        var box = new TextBox
        {
            Text            = _gameDomain,
            Watermark       = "Game name…",
            FontSize        = 13,
            Padding         = new Thickness(8, 6),
            Background      = CmmPalette.Brushes.AppBackground,
            Foreground      = WhiteBrush,
            CaretBrush      = WhiteBrush,
            BorderThickness = new Thickness(0),
        };

        var hits = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        var find = MakeBtn("Find", AccentBrush);

        async Task Search()
        {
            hits.Children.Clear();
            SetStatus("Searching games…");

            var games = await _api.SearchGamesAsync((box.Text ?? "").Trim());

            if (games.Count == 0)
            {
                SetStatus("No game on Nexus matched that name.");
                return;
            }

            SetStatus($"{games.Count} game(s) found — pick one.");

            foreach (var (id, name, domain) in games)
            {
                var hit = new Button
                {
                    Content         = new TextBlock
                    {
                        Text         = $"{name}   ({domain})",
                        FontSize      = 12,
                        Foreground    = WhiteBrush,
                        TextWrapping  = TextWrapping.Wrap,
                    },
                    Padding         = new Thickness(10, 6),
                    Background      = CardBrush,
                    BorderThickness = new Thickness(0),
                    CornerRadius    = new CornerRadius(4),
                    HorizontalAlignment        = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Cursor          = new Cursor(StandardCursorType.Hand),
                };

                hit.Click += async (_, _) =>
                {
                    _gameDomain = domain;
                    _gameId     = id;
                    _api.SetDomainOverride(_cmmGameId, domain);

                    Title = $"Browse Nexus Mods — {domain}";

                    // The categories come from the game, and until now there was no game to ask.
                    await LoadCategoriesAsync();
                    await LoadAsync(reset: true);
                };

                hits.Children.Add(hit);
            }
        }

        find.Click       += async (_, _) => await Search();
        box.KeyDown      += async (_, e) => { if (e.Key == Key.Return) await Search(); };

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
        DockPanel.SetDock(find, Dock.Right);
        row.Children.Add(find);
        row.Children.Add(box);

        _resultsPanel.Children.Add(row);
        _resultsPanel.Children.Add(hits);
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
        statsPanel.Children.Add(StatChip("♥", FormatNumber(mod.EndorsementCount), CmmPalette.Brushes.StatusDanger));

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

    // ── Collection card builder ───────────────────────────────────────────────

    private Control BuildCollectionCard(NexusBrowseCollection col)
    {
        var nexusUrl = $"https://www.nexusmods.com/games/{col.GameDomain}/collections/{col.Slug}";

        var nameLabel = new TextBlock
        {
            Text         = col.Name,
            FontSize     = 13,
            FontWeight   = FontWeight.Bold,
            Foreground   = WhiteBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(col.Author))   metaParts.Add($"by {col.Author}");
        if (!string.IsNullOrEmpty(col.Category)) metaParts.Add(col.Category);
        if (col.Revision > 0)                    metaParts.Add($"rev {col.Revision}");
        if (col.ModCount > 0)                    metaParts.Add($"{col.ModCount} mods");
        var metaLabel = new TextBlock
        {
            Text       = string.Join("  ·  ", metaParts),
            FontSize   = 11,
            Foreground = MutedBrush,
        };

        var summaryLabel = new TextBlock
        {
            Text         = col.Summary,
            FontSize     = 11,
            Foreground   = DimBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight    = 34,
            Margin       = new Thickness(0, 2, 0, 4),
        };

        var statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        statsPanel.Children.Add(StatChip("↓", FormatNumber(col.Downloads),    GoldBrush));
        statsPanel.Children.Add(StatChip("♥", FormatNumber(col.Endorsements), CmmPalette.Brushes.StatusDanger));

        var infoStack = new StackPanel { Spacing = 2 };
        infoStack.Children.Add(nameLabel);
        infoStack.Children.Add(metaLabel);
        infoStack.Children.Add(summaryLabel);
        infoStack.Children.Add(statsPanel);

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

        if (_downloadService != null && _api.HasApiKey && !string.IsNullOrEmpty(col.DownloadLink))
        {
            var dlBtn = MakeBtn("⬇ Download", GreenBrush);
            dlBtn.Click += async (_, _) => await QueueCollectionAsync(col, dlBtn);
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

    // ── Collection download ───────────────────────────────────────────────────

    private async Task QueueCollectionAsync(NexusBrowseCollection col, Button btn)
    {
        btn.IsEnabled = false;
        btn.Content   = "…";

        var url = await _api.GetCollectionDownloadUrlAsync(col.DownloadLink);
        if (url == null)
        {
            btn.Content   = "Failed";
            btn.IsEnabled = true;
            SetStatus("Collection download failed — check your API key.");
            return;
        }

        var folder = _getDownloadsFolder?.Invoke() ?? System.IO.Path.GetTempPath();
        _downloadService!.QueueCollectionDownload(col.Name, col.Slug, col.Revision, url, folder);

        btn.Content = "✓ Queued";
        SetStatus($"Queued collection: {col.Name}");
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

