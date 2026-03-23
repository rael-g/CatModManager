using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Code-only browse window with two tabs: Mods search and Collections.
/// </summary>
public class NexusBrowseWindow : Window
{
    private readonly NexusApiService      _api;
    private readonly NexusDownloadService _downloads;
    private readonly Func<string>         _getDownloadsFolder;
    private readonly Func<string?>        _getCurrentGameDomain;

    // brushes
    private static readonly IBrush Bg      = new SolidColorBrush(Color.Parse("#36393F"));
    private static readonly IBrush Card    = new SolidColorBrush(Color.Parse("#2F3136"));
    private static readonly IBrush Header  = new SolidColorBrush(Color.Parse("#202225"));
    private static readonly IBrush Muted   = new SolidColorBrush(Color.Parse("#72767D"));
    private static readonly IBrush Accent  = new SolidColorBrush(Color.Parse("#4E7FD5"));
    private static readonly IBrush Green   = new SolidColorBrush(Color.Parse("#3BA55D"));
    private static readonly IBrush Red     = new SolidColorBrush(Color.Parse("#ED4245"));
    private static readonly IBrush White   = Brushes.White;

    // ── MODS tab ──────────────────────────────────────────────────────────────
    private readonly TextBox    _domainBox;
    private readonly TextBox    _queryBox;
    private readonly StackPanel _modResults;
    private readonly StackPanel _filesPanel;
    private readonly TextBlock  _modStatus;
    private NexusSearchHit?     _selectedHit;
    private CancellationTokenSource _searchCts = new();

    // ── COLLECTIONS tab ───────────────────────────────────────────────────────
    private readonly TextBox    _slugBox;
    private readonly TextBlock  _collInfo;
    private readonly StackPanel _collMods;
    private readonly TextBlock  _collStatus;
    private NexusCollectionRevision? _currentRevision;
    private CancellationTokenSource _collCts = new();

    public NexusBrowseWindow(
        NexusApiService      api,
        NexusDownloadService downloads,
        Func<string>         getDownloadsFolder,
        Func<string?>        getCurrentGameDomain)
    {
        _api                  = api;
        _downloads            = downloads;
        _getDownloadsFolder   = getDownloadsFolder;
        _getCurrentGameDomain = getCurrentGameDomain;

        Title                 = "Nexus Mods Browser";
        Width                 = 940;
        Height                = 660;
        Background            = Bg;
        Foreground            = White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // ── MODS tab ──────────────────────────────────────────────────────────
        _domainBox = MkTextBox(getCurrentGameDomain() ?? "", "Game domain (e.g. skyrimspecialedition)", 220);

        _queryBox = MkTextBox("", "Search Nexus mods…");
        _queryBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = RunModSearch(); };

        var searchBtn = MkBtn("Search", Accent);
        searchBtn.Click += (_, _) => _ = RunModSearch();

        var searchRow = new DockPanel { Margin = new Thickness(12, 8) };
        DockPanel.SetDock(_domainBox, Dock.Left);
        DockPanel.SetDock(searchBtn, Dock.Right);
        _domainBox.Margin = new Thickness(0, 0, 8, 0);
        searchBtn.Margin  = new Thickness(8, 0, 0, 0);
        searchRow.Children.Add(_domainBox);
        searchRow.Children.Add(searchBtn);
        searchRow.Children.Add(_queryBox);

        _modResults = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        var modScroll = new ScrollViewer
        {
            Content = _modResults,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        _modStatus = MkLabel("Enter a search query above.", Muted);
        _modStatus.Margin = new Thickness(12, 4);

        _filesPanel = new StackPanel { Spacing = 4 };
        var filesScroll = new ScrollViewer
        {
            Content = _filesPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(0)
        };

        var modSplit = new Grid { ColumnDefinitions = new ColumnDefinitions("*,360") };
        var leftStack = new DockPanel();
        DockPanel.SetDock(_modStatus, Dock.Bottom);
        leftStack.Children.Add(_modStatus);
        leftStack.Children.Add(modScroll);
        Grid.SetColumn(leftStack, 0);
        Grid.SetColumn(filesScroll, 1);
        modSplit.Children.Add(leftStack);
        modSplit.Children.Add(filesScroll);

        var modsTab = new DockPanel();
        DockPanel.SetDock(searchRow, Dock.Top);
        DockPanel.SetDock(new Border { Background = Header, Height = 1 }, Dock.Top);
        modsTab.Children.Add(searchRow);
        modsTab.Children.Add(new Border { Background = Header, Height = 1 });
        modsTab.Children.Add(modSplit);

        // ── COLLECTIONS tab ───────────────────────────────────────────────────
        _slugBox = MkTextBox("", "Collection URL or slug (e.g. abc123def)");
        var fetchBtn = MkBtn("Fetch", Accent);
        fetchBtn.Click += (_, _) => _ = FetchCollection();
        _slugBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = FetchCollection(); };

        var fetchRow = new DockPanel { Margin = new Thickness(12, 8) };
        DockPanel.SetDock(fetchBtn, Dock.Right);
        fetchBtn.Margin = new Thickness(8, 0, 0, 0);
        fetchRow.Children.Add(fetchBtn);
        fetchRow.Children.Add(_slugBox);

        _collInfo = MkLabel("", Muted);
        _collInfo.Margin = new Thickness(12, 4);

        _collMods   = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0) };
        _collStatus = MkLabel("Enter a collection URL or slug to get started.", Muted);
        _collStatus.Margin = new Thickness(12, 4);

        var dlAllBtn = MkBtn("Download All Mods", Green);
        dlAllBtn.Margin = new Thickness(12, 8);
        dlAllBtn.Click  += (_, _) => DownloadAllCollectionMods();

        var collScroll = new ScrollViewer
        {
            Content = _collMods,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        var collTab = new DockPanel();
        DockPanel.SetDock(fetchRow, Dock.Top);
        DockPanel.SetDock(new Border { Background = Header, Height = 1 }, Dock.Top);
        DockPanel.SetDock(_collInfo, Dock.Top);
        DockPanel.SetDock(_collStatus, Dock.Bottom);
        DockPanel.SetDock(dlAllBtn, Dock.Bottom);
        collTab.Children.Add(fetchRow);
        collTab.Children.Add(new Border { Background = Header, Height = 1 });
        collTab.Children.Add(_collInfo);
        collTab.Children.Add(_collStatus);
        collTab.Children.Add(dlAllBtn);
        collTab.Children.Add(collScroll);

        // ── Tab control ───────────────────────────────────────────────────────
        var tc = new TabControl
        {
            Background = Bg,
            Items =
            {
                new TabItem { Header = "MODS",        Content = modsTab  },
                new TabItem { Header = "COLLECTIONS",  Content = collTab  }
            }
        };

        Content = tc;
    }

    // ── MODS: search ──────────────────────────────────────────────────────────

    private async Task RunModSearch()
    {
        var query  = _queryBox.Text?.Trim() ?? "";
        var domain = _domainBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query)) return;

        _searchCts.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        _modStatus.Text = "Searching…";
        _modResults.Children.Clear();
        _filesPanel.Children.Clear();
        _selectedHit = null;

        int gameId = NexusApiService.GetGameId(domain);
        var response = await _api.SearchModsAsync(gameId, query, ct: ct);

        if (ct.IsCancellationRequested) return;

        if (response.Results.Count == 0)
        {
            _modStatus.Text = gameId == 0
                ? $"Unknown game domain '{domain}'. Try 'skyrimspecialedition', 'fallout4', etc."
                : "No results found.";
            return;
        }

        _modStatus.Text = $"{response.Total} results (showing {response.Results.Count})";

        foreach (var hit in response.Results)
        {
            var card = BuildModCard(hit, domain);
            _modResults.Children.Add(card);
        }
    }

    private Border BuildModCard(NexusSearchHit hit, string domain)
    {
        var nameText = new TextBlock
        {
            Text       = hit.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize   = 12,
            Foreground = White,
            TextWrapping = TextWrapping.Wrap
        };
        var authorText = MkLabel($"by {hit.Author}  ·  ↓ {hit.Downloads:N0}  ·  ✦ {hit.EndorsementCount:N0}", Muted);
        authorText.FontSize = 10;
        var summaryText = new TextBlock
        {
            Text         = hit.Summary,
            FontSize     = 10,
            Foreground   = Muted,
            TextWrapping = TextWrapping.Wrap,
            MaxLines     = 2
        };

        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(nameText);
        info.Children.Add(authorText);
        info.Children.Add(summaryText);

        var card = new Border
        {
            Background   = Card,
            CornerRadius = new CornerRadius(4),
            Padding      = new Thickness(10),
            Margin       = new Thickness(8, 2),
            Child        = info,
            Cursor       = new Cursor(StandardCursorType.Hand)
        };
        card.PointerPressed += (_, _) =>
        {
            _selectedHit = hit;
            _ = LoadModFiles(hit, domain);
        };
        return card;
    }

    private async Task LoadModFiles(NexusSearchHit hit, string domain)
    {
        _filesPanel.Children.Clear();

        var header = new TextBlock
        {
            Text         = hit.Name,
            FontWeight   = FontWeight.Bold,
            FontSize     = 13,
            Foreground   = White,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(10, 10, 10, 2)
        };
        _filesPanel.Children.Add(header);

        var loadingLabel = MkLabel("Loading files…", Muted);
        loadingLabel.Margin = new Thickness(10, 4);
        _filesPanel.Children.Add(loadingLabel);

        var filesResp = await _api.GetFilesAsync(domain, hit.ModId);

        _filesPanel.Children.Remove(loadingLabel);

        var mainFiles = filesResp.Files.Where(f => f.CategoryName == "MAIN" || f.IsPrimary).ToList();
        var otherFiles = filesResp.Files.Where(f => f.CategoryName != "MAIN" && !f.IsPrimary).ToList();

        void AddFileSection(string sectionLabel, IEnumerable<NexusModFile> files)
        {
            bool first = true;
            foreach (var file in files)
            {
                if (first)
                {
                    var sectionHdr = MkLabel(sectionLabel, Muted);
                    sectionHdr.FontSize = 9;
                    sectionHdr.Margin = new Thickness(10, 8, 10, 2);
                    _filesPanel.Children.Add(sectionHdr);
                    first = false;
                }

                var fileCard = BuildFileCard(hit, file, domain);
                _filesPanel.Children.Add(fileCard);
            }
        }

        if (mainFiles.Count > 0)  AddFileSection("MAIN FILES", mainFiles);
        if (otherFiles.Count > 0) AddFileSection("OTHER FILES", otherFiles);

        if (filesResp.Files.Count == 0)
            _filesPanel.Children.Add(MkLabel("No files available.", Muted));
    }

    private Border BuildFileCard(NexusSearchHit hit, NexusModFile file, string domain)
    {
        var dlBtn = MkBtn("↓ Download", Accent);
        dlBtn.FontSize = 10;
        dlBtn.Padding  = new Thickness(10, 4);
        dlBtn.Click   += (_, _) =>
        {
            var folder = _getDownloadsFolder();
            _downloads.QueueDownloadDirect(domain, hit.ModId, file.FileId,
                hit.Name, folder, file.Version, file.CategoryName);
        };

        var nameText = new TextBlock
        {
            Text       = file.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize   = 11,
            Foreground = White
        };
        var metaText = MkLabel(
            $"v{file.Version}  ·  {file.SizeKb / 1024.0:F1} MB  ·  {file.CategoryName}",
            Muted);
        metaText.FontSize = 10;

        var textStack = new StackPanel { Spacing = 2 };
        textStack.Children.Add(nameText);
        textStack.Children.Add(metaText);
        if (!string.IsNullOrEmpty(file.Description))
        {
            var descText = new TextBlock
            {
                Text = file.Description, FontSize = 9, Foreground = Muted,
                TextWrapping = TextWrapping.Wrap, MaxLines = 2
            };
            textStack.Children.Add(descText);
        }

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(dlBtn, Dock.Right);
        dlBtn.VerticalAlignment = VerticalAlignment.Center;
        dlBtn.Margin = new Thickness(8, 0, 0, 0);
        dock.Children.Add(dlBtn);
        dock.Children.Add(textStack);

        return new Border
        {
            Background   = new SolidColorBrush(Color.Parse("#1E2124")),
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(8),
            Margin       = new Thickness(10, 2),
            Child        = dock
        };
    }

    // ── COLLECTIONS ───────────────────────────────────────────────────────────

    private async Task FetchCollection()
    {
        var input = _slugBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(input)) return;

        // Extract slug from full URL: https://www.nexusmods.com/{game}/collections/{slug}
        var slug = ExtractSlug(input);

        _collCts.Cancel();
        _collCts = new CancellationTokenSource();
        var ct = _collCts.Token;

        _collInfo.Text = "";
        _collStatus.Text = "Fetching collection info…";
        _collMods.Children.Clear();
        _currentRevision = null;

        var info = await _api.GetCollectionAsync(slug, ct);
        if (ct.IsCancellationRequested) return;

        if (info == null)
        {
            _collStatus.Text = $"Collection '{slug}' not found or API error. Check the slug and your API key.";
            return;
        }

        int revision = info.LatestPublishedRevision?.RevisionNumber ?? 1;
        _collInfo.Text = $"{info.Name}  ·  revision {revision}  ·  {info.LatestPublishedRevision?.ModCount ?? 0} mods\n{info.Summary}";
        _collStatus.Text = $"Loading revision {revision}…";

        var rev = await _api.GetCollectionRevisionAsync(slug, revision, ct);
        if (ct.IsCancellationRequested) return;

        if (rev == null || rev.Mods.Count == 0)
        {
            _collStatus.Text = "No mod data found in this collection revision.";
            return;
        }

        _currentRevision = rev;
        _collStatus.Text = $"{rev.Mods.Count} mods — click 'Download All Mods' to queue them.";

        foreach (var m in rev.Mods)
        {
            if (m.Mod == null) continue;
            var row = new DockPanel { Margin = new Thickness(0, 1) };
            var label = MkLabel($"{m.Mod.Name}  ({m.Mod.DomainName}, mod #{m.Mod.ModId}, file #{m.FileId})", White);
            label.FontSize = 11;
            row.Children.Add(label);
            var card = new Border
            {
                Background = Card, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 4), Child = row
            };
            _collMods.Children.Add(card);
        }
    }

    private void DownloadAllCollectionMods()
    {
        if (_currentRevision == null) return;
        var folder = _getDownloadsFolder();
        int queued = 0;
        foreach (var m in _currentRevision.Mods)
        {
            if (m.Mod == null || m.FileId == 0) continue;
            _downloads.QueueDownloadDirect(
                m.Mod.DomainName, m.Mod.ModId, m.FileId,
                m.Mod.Name, folder);
            queued++;
        }
        _collStatus.Text = $"Queued {queued} mods for download.";
    }

    private static string ExtractSlug(string input)
    {
        if (input.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(input);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.LastOrDefault() ?? input;
            }
            catch { }
        }
        return input;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Button MkBtn(string text, IBrush bg) => new Button
    {
        Content         = text,
        Background      = bg,
        Foreground      = White,
        BorderThickness = new Thickness(0),
        CornerRadius    = new CornerRadius(3),
        Padding         = new Thickness(14, 6),
        FontSize        = 11
    };

    private static TextBox MkTextBox(string text, string watermark, double width = double.NaN) => new TextBox
    {
        Text            = text,
        Watermark       = watermark,
        Width           = double.IsNaN(width) ? double.NaN : width,
        FontSize        = 11,
        Background      = Card,
        Foreground      = White,
        BorderBrush     = Muted,
        BorderThickness = new Thickness(1),
        Padding         = new Thickness(8, 5),
        CornerRadius    = new CornerRadius(3)
    };

    private static TextBlock MkLabel(string text, IBrush fg) => new TextBlock
    {
        Text       = text,
        Foreground = fg,
        FontSize   = 11
    };
}
