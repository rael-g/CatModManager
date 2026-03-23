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

            response.EnsureSuccessStatusCode();
            ApiKeyValidityChanged?.Invoke(true);

            var result = await response.Content.ReadFromJsonAsync<List<NexusDownloadLink>>(cancellationToken: ct);
            return result ?? new List<NexusDownloadLink>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[NexusApiService] GetDownloadLinksAsync error: {ex.Message}");
            return new List<NexusDownloadLink>();
        }
    }

    public async Task<byte[]> GetBytesAsync(
        string url,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var memStream = new System.IO.MemoryStream();

            var buffer = new byte[81920];
            long bytesRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await memStream.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesRead += read;

                if (progress != null && totalBytes.HasValue && totalBytes.Value > 0)
                    progress.Report((double)bytesRead / totalBytes.Value * 100.0);
            }

            return memStream.ToArray();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NexusApiService] GetBytesAsync error: {ex.Message}");
            return Array.Empty<byte>();
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
        try
        {
            var url = $"{BaseApiUrl}/games/{gameDomain}.json";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (HasApiKey) request.Headers.Add("apikey", ApiKey);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var gameDetails = await response.Content.ReadFromJsonAsync<NexusGameDetails>(cancellationToken: ct);
            return gameDetails?.Categories.ToDictionary(c => c.CategoryId, c => c.Name)
                   ?? new Dictionary<int, string>();
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private const string GraphQlUrl = "https://api.nexusmods.com/v2/graphql";

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
