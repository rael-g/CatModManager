using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CmmPlugin.NexusMods;

public class NexusApiService
{
    private const string BaseApiUrl = "https://api.nexusmods.com/v1";

    internal readonly HttpClient _http;
    private readonly NexusDatabase _db;

    private readonly Dictionary<string, Dictionary<int, string>> _categoryCache = new(StringComparer.OrdinalIgnoreCase);

    public string ApiKey
    {
        get => _db.GetSetting("api_key") ?? string.Empty;
        set => _db.SetSetting("api_key", value);
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public bool NxmDontAskAgain
    {
        get => _db.GetSetting("nxm_dont_ask") == "1";
        set => _db.SetSetting("nxm_dont_ask", value ? "1" : "0");
    }

    /// <summary>Fired with true on a successful authenticated call, false on 401.</summary>
    public event Action<bool>? ApiKeyValidityChanged;

    public NexusApiService(NexusDatabase db)
    {
        _db = db;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "CatModManager/1.0");
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
        _http.DefaultRequestHeaders.Add("Application-Name", "CatModManager");
        _http.DefaultRequestHeaders.Add("Application-Version", "1.0");
    }

    public async Task<NexusModDetails?> GetModDetailsAsync(string gameDomain, int modId, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseApiUrl}/games/{gameDomain}/mods/{modId}.json";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (HasApiKey) request.Headers.Add("apikey", ApiKey);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<NexusModDetails>(cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<NexusFilesResponse> GetFilesAsync(
        string gameDomain,
        int modId,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseApiUrl}/games/{gameDomain}/mods/{modId}/files.json";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (HasApiKey) request.Headers.Add("apikey", ApiKey);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<NexusFilesResponse>(cancellationToken: ct);
            if (result != null) result.Files ??= new List<NexusModFile>();
            return result ?? new NexusFilesResponse();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NexusApiService] GetFilesAsync error: {ex.Message}");
            return new NexusFilesResponse();
        }
    }

    public async Task<List<NexusDownloadLink>> GetDownloadLinksAsync(
        string gameDomain,
        int modId,
        int fileId,
        string? key = null,
        string? expires = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseApiUrl}/games/{gameDomain}/mods/{modId}/files/{fileId}/download_link.json";

            if (!string.IsNullOrEmpty(key) || !string.IsNullOrEmpty(expires))
            {
                var queryParts = new List<string>();
                if (!string.IsNullOrEmpty(key))
                    queryParts.Add($"key={Uri.EscapeDataString(key)}");
                if (!string.IsNullOrEmpty(expires))
                    queryParts.Add($"expires={Uri.EscapeDataString(expires)}");
                url += "?" + string.Join("&", queryParts);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (HasApiKey) request.Headers.Add("apikey", ApiKey);
            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ApiKeyValidityChanged?.Invoke(false);
                response.EnsureSuccessStatusCode();
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException("Nexus Premium required to download this file.");

            response.EnsureSuccessStatusCode();
            ApiKeyValidityChanged?.Invoke(true);

            var result = await response.Content.ReadFromJsonAsync<List<NexusDownloadLink>>(cancellationToken: ct);
            return result ?? new List<NexusDownloadLink>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[NexusApiService] GetDownloadLinksAsync error: {ex.Message}");
            return new List<NexusDownloadLink>();
        }
    }

    /// <summary>
    /// Downloads a URL to a byte array. Only use for small payloads (e.g. collection manifests).
    /// For large files, prefer <see cref="DownloadToFileAsync"/>.
    /// </summary>
    public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NexusApiService] GetBytesAsync error: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Downloads a URL directly to a file on disk. Streams in 80KB chunks — no full-file MemoryStream.
    /// Progress reports are throttled to ≥1% change to avoid flooding the UI thread.
    /// Returns true on success; false (and deletes the partial file) on failure.
    /// </summary>
    public async Task<bool> DownloadToFileAsync(
        string url,
        string destPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string tempPath = destPath + ".tmp";
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);
            
            // Download to .tmp file
            using (var fileStream = new System.IO.FileStream(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long bytesRead = 0;
                double lastReported = -1;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;

                    if (progress != null && totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double pct = (double)bytesRead / totalBytes.Value * 100.0;
                        if (pct - lastReported >= 1.0)
                        {
                            progress.Report(pct);
                            lastReported = pct;
                        }
                    }
                }
            }

            // Success: rename .tmp to actual file (overwrite if exists)
            if (System.IO.File.Exists(destPath))
                System.IO.File.Delete(destPath);
            
            System.IO.File.Move(tempPath, destPath);

            return true;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                Console.Error.WriteLine($"[NexusApiService] DownloadToFileAsync error: {ex.Message}");
            
            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); } catch { }
            return false;
        }
    }

    public static readonly Dictionary<string, int> GameDomainToId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["skyrimspecialedition"]    = 1704,
        ["skyrim"]                  = 110,
        ["skyrimvr"]                = 2531,
        ["enderal"]                 = 3174,
        ["enderalspecialedition"]   = 3685,
        ["fallout4"]                = 1151,
        ["fallout4vr"]              = 2148,
        ["newvegas"]                = 130,
        ["fallout3"]                = 120,
        ["oblivion"]                = 101,
        ["morrowind"]               = 100,
        ["starfield"]               = 4187,
    };

    public static int GetGameId(string gameDomain)
        => GameDomainToId.TryGetValue(gameDomain, out var id) ? id : 0;

    /// <summary>
    /// Resolves the Nexus numeric game ID for <paramref name="gameDomain"/>.
    /// Checks the hardcoded map first; falls back to the Nexus API (/v1/games/{domain}.json).
    /// Result is cached so subsequent calls are free.
    /// </summary>
    public async Task<int> GetGameIdAsync(string gameDomain, CancellationToken ct = default)
    {
        if (GameDomainToId.TryGetValue(gameDomain, out var id)) return id;

        // Fetch and cache via FetchGameDetailsAsync (also populates category cache)
        var details = await FetchGameDetailsAsync(gameDomain, ct);
        if (details?.Id > 0)
        {
            GameDomainToId[gameDomain] = details.Id;
            return details.Id;
        }
        return 0;
    }

    public async Task<string> ResolveCategoryAsync(string gameDomain, int categoryId, CancellationToken ct = default)
    {
        if (categoryId <= 0) return string.Empty;

        if (!_categoryCache.TryGetValue(gameDomain, out var map))
        {
            map = await FetchCategoriesAsync(gameDomain, ct);
            _categoryCache[gameDomain] = map;
        }

        return map.TryGetValue(categoryId, out var name) ? name : string.Empty;
    }

    private async Task<Dictionary<int, string>> FetchCategoriesAsync(string gameDomain, CancellationToken ct)
    {
        var details = await FetchGameDetailsAsync(gameDomain, ct);
        if (details == null) return new Dictionary<int, string>();

        if (details.Id > 0 && !GameDomainToId.ContainsKey(gameDomain))
            GameDomainToId[gameDomain] = details.Id;

        return details.Categories.ToDictionary(c => c.CategoryId, c => c.Name);
    }

    private async Task<NexusGameDetails?> FetchGameDetailsAsync(string gameDomain, CancellationToken ct)
    {
        try
        {
            var url = $"{BaseApiUrl}/games/{gameDomain}.json";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (HasApiKey) request.Headers.Add("apikey", ApiKey);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<NexusGameDetails>(cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to get the download URL for a collection archive using the NXM one-time key.
    /// Returns null if the endpoint is unavailable (requires premium or the key has expired).
    /// </summary>
    public async Task<string?> GetCollectionArchiveUrlAsync(
        string slug, int revision, string? key, string? expires, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseApiUrl}/collections/{slug}/revisions/{revision}/download_link.json";
            var queryParts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(key))     queryParts.Add($"key={Uri.EscapeDataString(key)}");
            if (!string.IsNullOrEmpty(expires)) queryParts.Add($"expires={Uri.EscapeDataString(expires)}");
            if (queryParts.Count > 0)           url += "?" + string.Join("&", queryParts);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (HasApiKey) request.Headers.Add("apikey", ApiKey);
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var links = await response.Content.ReadFromJsonAsync<List<NexusDownloadLink>>(cancellationToken: ct);
            return links?.FirstOrDefault()?.URI;
        }
        catch
        {
            return null;
        }
    }

    // ── Browse / search (v2 GraphQL) ─────────────────────────────────────────

    private const string ModsGqlQuery = """
        query Mods($filter: ModsFilter, $sort: [ModsSort!], $count: Int, $offset: Int) {
          mods(filter: $filter, sort: $sort, count: $count, offset: $offset) {
            nodes {
              modId name summary author category
              downloads endorsements version pictureUrl
            }
            totalCount
          }
        }
        """;

    /// <summary>
    /// Full-text mod search via v2 GraphQL. No API key required.
    ///
    /// The op is <c>WILDCARD</c> because the schema accepts only EQUALS, NOT_EQUALS and WILDCARD
    /// for <c>name</c> — <c>MATCHES</c> used to be sent here and the server rejected the whole
    /// query, so every search came back empty with the reason visible only on stderr. WILDCARD
    /// already matches on parts of words and on each word separately, so "unofficial patch" finds
    /// "Unofficial Skyrim Special Edition Patch"; no surrounding asterisks are needed.
    /// </summary>
    public Task<(List<NexusBrowseMod> Mods, int Total, string? Error)> SearchModsAsync(
        string gameDomain, int gameId, string query, string? categoryName = null,
        bool includeAdult = false, int count = 20, int offset = 0, CancellationToken ct = default)
    {
        var filter = new Dictionary<string, object>
        {
            ["gameId"] = new[] { new { op = "EQUALS", value = gameId.ToString() } },
            ["name"]   = new[] { new { op = "WILDCARD", value = query } },
            ["op"]     = "AND"
        };
        if (!includeAdult)
            filter["adultContent"] = new[] { new { op = "EQUALS", value = false } };
        if (!string.IsNullOrEmpty(categoryName))
            filter["categoryName"] = new[] { new { op = "EQUALS", value = categoryName } };

        return QueryModsAsync(gameDomain, filter,
            sort: new[] { new Dictionary<string, object> { ["relevance"] = new { direction = "DESC" } } },
            count, offset, ct);
    }

    /// <summary>
    /// Returns trending / latest-added / latest-updated mods for a game via v2 GraphQL.
    /// No API key required.
    /// </summary>
    public Task<(List<NexusBrowseMod> Mods, int Total, string? Error)> GetBrowseModsAsync(
        string gameDomain, int gameId, BrowseSort sort = BrowseSort.Trending,
        string? categoryName = null, bool includeAdult = false,
        int count = 20, int offset = 0, CancellationToken ct = default)
    {
        var sortField = sort switch
        {
            BrowseSort.LatestAdded   => "createdAt",
            BrowseSort.LatestUpdated => "updatedAt",
            _                        => "downloads"
        };

        var filter = new Dictionary<string, object>
        {
            ["gameId"] = new[] { new { op = "EQUALS", value = gameId.ToString() } },
            ["op"]     = "AND"
        };
        if (!includeAdult)
            filter["adultContent"] = new[] { new { op = "EQUALS", value = false } };
        if (!string.IsNullOrEmpty(categoryName))
            filter["categoryName"] = new[] { new { op = "EQUALS", value = categoryName } };

        return QueryModsAsync(gameDomain, filter,
            sort: new[] { new Dictionary<string, object> { [sortField] = new { direction = "DESC" } } },
            count, offset, ct);
    }

    /// <summary>Returns category names for a game, sorted alphabetically. Cached after first call.</summary>
    public async Task<IReadOnlyList<string>> GetCategoryNamesAsync(string gameDomain, CancellationToken ct = default)
    {
        if (!_categoryCache.TryGetValue(gameDomain, out var map))
        {
            map = await FetchCategoriesAsync(gameDomain, ct);
            _categoryCache[gameDomain] = map;
        }
        return map.Values.OrderBy(v => v).Distinct().ToList();
    }

    /// <summary>
    /// Runs a mods query and reports why it came back empty when it did.
    ///
    /// The third field exists because a query the server rejects and a search that genuinely
    /// matched nothing both arrive here as zero nodes. That ambiguity hid a malformed filter for
    /// as long as it took someone to notice the browser never found anything.
    /// </summary>
    private async Task<(List<NexusBrowseMod> Mods, int Total, string? Error)> QueryModsAsync(
        string gameDomain, object filter, object sort, int count, int offset, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                query     = ModsGqlQuery,
                variables = new { filter, sort, count, offset }
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            // v2 GraphQL works without an API key but requires a browser-like UA
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            if (HasApiKey) req.Headers.Add("apikey", ApiKey);

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<NexusModsGraphQlResponse>(cancellationToken: ct);

            if (result?.Errors is { Count: > 0 } errors)
            {
                foreach (var err in errors)
                    Console.Error.WriteLine($"[NexusApiService] GraphQL error: {err}");

                // Just the message: the rest of a GraphQL error is the offending payload echoed
                // back, which belongs on stderr and not in a status bar.
                var first = errors[0];
                var message = first.ValueKind == JsonValueKind.Object
                           && first.TryGetProperty("message", out var m)
                    ? m.GetString()
                    : first.ToString();

                return (new List<NexusBrowseMod>(), 0,
                    string.IsNullOrWhiteSpace(message) ? "the Nexus API rejected the query" : message);
            }

            var nodes = result?.Data?.Mods?.Nodes ?? new List<NexusGraphQlMod>();
            var total = result?.Data?.Mods?.TotalCount ?? 0;

            return (nodes.Select(m => new NexusBrowseMod
            {
                ModId            = m.ModId,
                Name             = m.Name,
                Summary          = m.Summary,
                Author           = m.Author,
                CategoryName     = m.Category,
                DownloadCount    = m.Downloads,
                EndorsementCount = m.Endorsements,
                Version          = m.Version,
                GameDomain       = gameDomain,
                TotalCount       = total
            }).ToList(), total, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[NexusApiService] QueryModsAsync error: {ex.Message}");
            return (new List<NexusBrowseMod>(), 0, ex.Message);
        }
    }

    private const string GraphQlUrl = "https://api.nexusmods.com/v2/graphql";

    // ── Collections browse (v2 GraphQL) ──────────────────────────────────────
    // collectionsV2 does not expose a named filter input type, so the filter
    // must be inlined directly into the query string rather than passed as a variable.

    /// <summary>
    /// Returns collections for the given game domain.
    /// When nameFilter is provided, uses WILDCARD op for partial name matching.
    /// No API key required.
    /// </summary>
    public Task<(List<NexusBrowseCollection> Collections, int Total)> GetBrowseCollectionsAsync(
        string gameDomain, string? nameFilter = null,
        int count = 20, int offset = 0, CancellationToken ct = default)
        => QueryCollectionsAsync(gameDomain, nameFilter, count, offset, ct);

    private async Task<(List<NexusBrowseCollection> Collections, int Total)> QueryCollectionsAsync(
        string gameDomain, string? nameFilter, int count, int offset, CancellationToken ct)
    {
        try
        {
            // Build filter inline — no named variable type exists in the schema for collectionsV2
            var filterStr = string.IsNullOrEmpty(nameFilter)
                ? $"{{gameDomain: {{value: \"{gameDomain}\", op: EQUALS}}}}"
                : $"{{gameDomain: {{value: \"{gameDomain}\", op: EQUALS}}, name: {{value: \"{nameFilter}\", op: WILDCARD}}}}";

            var query = $@"query Collections($count: Int, $offset: Int) {{
  collectionsV2(filter: {filterStr}, sort: {{endorsements: {{direction: DESC}}}}, count: $count, offset: $offset) {{
    nodes {{
      id slug name summary endorsements totalDownloads
      latestPublishedRevision {{ revision modCount downloadLink }}
      user {{ name memberId }}
      category {{ name }}
    }}
    totalCount
  }}
}}";

            var payload = JsonSerializer.Serialize(new
            {
                query,
                variables = new { count, offset }
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            if (HasApiKey) req.Headers.Add("apikey", ApiKey);

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<NexusCollectionsV2GraphQlResponse>(cancellationToken: ct);

            if (result?.Errors is { Count: > 0 } errors)
                foreach (var err in errors)
                    Console.Error.WriteLine($"[NexusApiService] Collections GraphQL error: {err}");

            var nodes = result?.Data?.CollectionsV2?.Nodes ?? new List<NexusCollectionV2Node>();
            var total = result?.Data?.CollectionsV2?.TotalCount ?? 0;

            return (nodes.Select(n => new NexusBrowseCollection
            {
                Id           = n.Id,
                Slug         = n.Slug,
                Name         = n.Name,
                Summary      = n.Summary,
                Author       = n.User?.Name ?? string.Empty,
                Category     = n.Category?.Name ?? string.Empty,
                Endorsements = n.Endorsements,
                Downloads    = n.TotalDownloads,
                Revision     = n.LatestPublishedRevision?.Revision ?? 0,
                ModCount     = n.LatestPublishedRevision?.ModCount ?? 0,
                GameDomain   = gameDomain,
                TotalCount   = total,
                DownloadLink = n.LatestPublishedRevision?.DownloadLink ?? string.Empty
            }).ToList(), total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[NexusApiService] QueryCollectionsAsync error: {ex.Message}");
            return (new List<NexusBrowseCollection>(), 0);
        }
    }

    /// <summary>
    /// Resolves a collection download URL from the relative path returned by GraphQL.
    /// Requires an API key. Returns null on failure or if no key is set.
    /// </summary>
    public async Task<string?> GetCollectionDownloadUrlAsync(string downloadLinkPath, CancellationToken ct = default)
    {
        if (!HasApiKey || string.IsNullOrEmpty(downloadLinkPath)) return null;
        try
        {
            var url = $"https://api.nexusmods.com{downloadLinkPath}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("apikey", ApiKey);
            req.Headers.TryAddWithoutValidation("User-Agent", "CatModManager/1.0");
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var links = await resp.Content.ReadFromJsonAsync<List<NexusDownloadLink>>(cancellationToken: ct);
            return links?.FirstOrDefault()?.URI;
        }
        catch { return null; }
    }

    /// <summary>
    /// Queries the Nexus v2 GraphQL API for the mod files in a collection revision.
    /// No API key required — same approach used by the MO2 NexusCollections plugin.
    /// </summary>
    public async Task<NexusCollectionGraphQlResponse?> QueryCollectionRevisionAsync(
        string slug, int revision, CancellationToken ct = default)
    {
        const string query = """
            query CollectionRevisionMods($revision: Int, $slug: String!, $viewAdultContent: Boolean) {
              collectionRevision(revision: $revision, slug: $slug, viewAdultContent: $viewAdultContent) {
                modFiles {
                  fileId
                  optional
                  file {
                    name
                    version
                    mod {
                      modId
                      name
                      game { domainName }
                    }
                  }
                }
              }
            }
            """;

        var payload = JsonSerializer.Serialize(new
        {
            query,
            variables  = new { revision, slug, viewAdultContent = true },
            operationName = "CollectionRevisionMods"
        });

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl);
            req.Headers.Add("User-Agent", "Mozilla/5.0");
            req.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<NexusCollectionGraphQlResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NexusApiService] QueryCollectionRevisionAsync error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens nexusmods.com/sso in the browser and waits for the user to authorize.
    /// WebSocket is connected BEFORE opening the browser to avoid the race condition
    /// where the user approves before the socket is ready ("expired" message).
    /// </summary>
    public async Task<string?> LoginWithSsoAsync(CancellationToken ct = default)
    {
        var uuid   = Guid.NewGuid().ToString();
        var ssoUrl = $"https://www.nexusmods.com/sso?id={uuid}&application=CatModManager";

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("User-Agent", "CatModManager/1.0");
        await ws.ConnectAsync(new Uri("wss://sso.nexusmods.com"), ct);

        var payload = JsonSerializer.Serialize(new { id = uuid, token = (string?)null });
        await ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);

        Process.Start(new ProcessStartInfo { FileName = ssoUrl, UseShellExecute = true });

        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var msg = await ws.ReceiveAsync(buffer, ct);
            if (msg.MessageType == WebSocketMessageType.Close) break;
            if (msg.MessageType != WebSocketMessageType.Text) continue;

            var json = Encoding.UTF8.GetString(buffer, 0, msg.Count);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("api_key", out var keyEl))
            {
                var key = keyEl.GetString();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    ApiKey = key;
                    return key;
                }
            }
        }

        return null;
    }
}

