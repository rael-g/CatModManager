using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Services;
using CatModManager.Ui.Models;
using Microsoft.Data.Sqlite;

namespace CatModManager.Ui.Services;

public class NuGetPluginService
{
    private const string SearchBase = "https://azuresearch-usnc.nuget.org/query";
    private const string FlatBase   = "https://api.nuget.org/v3-flatcontainer";
    private const string Tag        = "cmm-plugin";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private readonly ICatPathService _pathService;
    private readonly ILogService _log;
    private readonly AppDatabase _db;

    public string PluginsDir => Path.Combine(_pathService.BaseDataPath, "plugins");

    public NuGetPluginService(ICatPathService pathService, ILogService log, AppDatabase db)
    {
        _pathService = pathService;
        _log = log;
        _db = db;
    }

    // ── Manifest ─────────────────────────────────────────────────────────────

    public InstalledPluginManifest LoadManifest()
    {
        var manifest = new InstalledPluginManifest();
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT package_id, version, installed_at FROM installed_plugins";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                manifest.Installed.Add(new InstalledPlugin
                {
                    PackageId   = reader.GetString(0),
                    Version     = reader.GetString(1),
                    InstalledAt = DateTime.Parse(reader.GetString(2))
                });
            }
        }
        catch { }
        return manifest;
    }

    private void SavePlugin(InstalledPlugin plugin)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO installed_plugins (package_id, version, installed_at)
            VALUES (@id, @ver, @at)
            ON CONFLICT(package_id) DO UPDATE SET version = excluded.version, installed_at = excluded.installed_at
            """;
        cmd.Parameters.AddWithValue("@id",  plugin.PackageId);
        cmd.Parameters.AddWithValue("@ver", plugin.Version);
        cmd.Parameters.AddWithValue("@at",  plugin.InstalledAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void DeletePlugin(string packageId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM installed_plugins WHERE package_id = @id COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@id", packageId);
        cmd.ExecuteNonQuery();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    /// <param name="query">User text (empty = show all CMM plugins).</param>
    /// <param name="sortBy">null = relevance, "totalDownloads", "lastEdited".</param>
    public async Task<(List<NuGetPackageEntry> Results, int TotalHits)> SearchAsync(
        string query,
        string? sortBy = null,
        int take = 20,
        int skip = 0,
        CancellationToken ct = default)
    {
        string q = string.IsNullOrWhiteSpace(query) ? Tag : $"{Tag} {query.Trim()}";
        string url = $"{SearchBase}?q={Uri.EscapeDataString(q)}&take={take}&skip={skip}&prerelease=false";
        if (!string.IsNullOrEmpty(sortBy)) url += $"&sortBy={sortBy}";

        NuGetSearchResponse? response;
        try
        {
            response = await _http.GetFromJsonAsync<NuGetSearchResponse>(url, _json, ct);
        }
        catch (Exception ex)
        {
            _log.LogError("[NuGet] Search failed", ex);
            return (new List<NuGetPackageEntry>(), 0);
        }

        if (response == null) return (new List<NuGetPackageEntry>(), 0);

        var manifest = LoadManifest();
        var installed = manifest.Installed.ToDictionary(
            p => p.PackageId, p => p.Version, StringComparer.OrdinalIgnoreCase);

        var entries = response.Data.Select(d =>
        {
            var entry = NuGetPackageEntry.FromData(d);
            if (installed.TryGetValue(d.Id, out var ver))
            {
                entry.IsInstalled = true;
                entry.InstalledVersion = ver;
            }
            return entry;
        }).ToList();

        return (entries, response.TotalHits);
    }

    // ── Install ───────────────────────────────────────────────────────────────

    public async Task InstallAsync(NuGetPackageEntry entry, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        string id = entry.PackageId;
        string version = entry.LatestVersion;
        string idLower = id.ToLowerInvariant();
        string nupkgUrl = $"{FlatBase}/{idLower}/{version}/{idLower}.{version}.nupkg";

        progress?.Report($"Downloading {id} {version}…");
        _log.Log($"[NuGet] Installing {id} {version} from {nupkgUrl}");

        byte[] nupkgBytes;
        try
        {
            nupkgBytes = await _http.GetByteArrayAsync(nupkgUrl, ct);
        }
        catch (Exception ex)
        {
            _log.LogError($"[NuGet] Download failed for {id}", ex);
            throw;
        }

        string targetDir = Path.Combine(PluginsDir, id);
        Directory.CreateDirectory(targetDir);

        progress?.Report($"Extracting {id}…");
        using var ms = new MemoryStream(nupkgBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var dllEntries = zip.Entries
            .Where(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var preferred = dllEntries
            .Where(e => e.FullName.StartsWith("lib/net10.0/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var toExtract = preferred.Count > 0 ? preferred : dllEntries;

        foreach (var e in toExtract)
        {
            string dest = Path.Combine(targetDir, e.Name);
            e.ExtractToFile(dest, overwrite: true);
            _log.Log($"[NuGet] Extracted: {e.Name}");
        }

        SavePlugin(new InstalledPlugin
        {
            PackageId   = id,
            Version     = version,
            InstalledAt = DateTime.UtcNow
        });

        entry.IsInstalled = true;
        entry.InstalledVersion = version;

        _log.Log($"[NuGet] {id} {version} installed successfully.");
        progress?.Report($"{id} installed. Restart CMM to activate.");
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    public void UninstallById(string packageId)
    {
        string targetDir = Path.Combine(PluginsDir, packageId);
        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
            _log.Log($"[NuGet] Removed plugin folder: {targetDir}");
        }
        DeletePlugin(packageId);
        _log.Log($"[NuGet] {packageId} uninstalled.");
    }

    public void Uninstall(NuGetPackageEntry entry)
    {
        string id = entry.PackageId;
        string targetDir = Path.Combine(PluginsDir, id);

        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
            _log.Log($"[NuGet] Removed plugin folder: {targetDir}");
        }

        DeletePlugin(id);

        entry.IsInstalled = false;
        entry.InstalledVersion = null;

        _log.Log($"[NuGet] {id} uninstalled.");
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task UpdateAsync(NuGetPackageEntry entry, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Uninstall(entry);
        await InstallAsync(entry, progress, ct);
    }
}
