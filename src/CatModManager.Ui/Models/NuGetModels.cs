using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CatModManager.Ui.Models;

// ── NuGet Search API response ────────────────────────────────────────────────

public class NuGetSearchResponse
{
    [JsonPropertyName("totalHits")]
    public int TotalHits { get; set; }

    [JsonPropertyName("data")]
    public List<NuGetPackageData> Data { get; set; } = new();
}

public class NuGetPackageData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("authors")]
    public List<string> Authors { get; set; } = new();

    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("projectUrl")]
    public string? ProjectUrl { get; set; }
}

// ── View model for a single result row ───────────────────────────────────────

public class NuGetPackageEntry
{
    public string PackageId { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Authors { get; init; } = string.Empty;
    public long TotalDownloads { get; init; }
    public string? IconUrl { get; init; }
    public bool IsVerified { get; init; }
    public string? ProjectUrl { get; init; }

    // Set after comparing against installed manifest
    public bool IsInstalled { get; set; }
    public string? InstalledVersion { get; set; }
    public bool HasUpdate => IsInstalled && InstalledVersion != null && InstalledVersion != LatestVersion;

    public string ActionLabel => HasUpdate ? $"Update → {LatestVersion}" : IsInstalled ? "Installed ✓" : "Install";
    public bool IsOfficial => PackageId.StartsWith("CmmPlugin.", System.StringComparison.OrdinalIgnoreCase);

    public static NuGetPackageEntry FromData(NuGetPackageData d) => new()
    {
        PackageId = d.Id,
        LatestVersion = d.Version,
        Description = d.Description,
        Authors = string.Join(", ", d.Authors),
        TotalDownloads = d.TotalDownloads,
        IconUrl = d.IconUrl,
        IsVerified = d.Verified,
        ProjectUrl = d.ProjectUrl
    };
}
