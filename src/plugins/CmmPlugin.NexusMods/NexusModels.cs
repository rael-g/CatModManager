using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CmmPlugin.NexusMods;

// ---------------------------------------------------------------------------
// API response models
// ---------------------------------------------------------------------------

public class NexusModFile
{
    [JsonPropertyName("file_id")]
    public int FileId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("size_kb")]
    public int SizeKb { get; set; }

    [JsonPropertyName("uploaded_timestamp")]
    public long UploadedTimestamp { get; set; }

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }
}

public class NexusFilesResponse
{
    [JsonPropertyName("files")]
    public List<NexusModFile> Files { get; set; } = new();
}

public class NexusDownloadLink
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("short_name")]
    public string ShortName { get; set; } = string.Empty;

    [JsonPropertyName("URI")]
    public string URI { get; set; } = string.Empty;
}

public class NexusModDetails
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// The Nexus API returns category_id (int), not category_name.
    /// category_name is added in case newer API versions include it.
    /// </summary>
    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }

    /// <summary>Derives a displayable category string: prefers category_name, falls back to category_id if present.</summary>
    public string ResolvedCategory =>
        !string.IsNullOrEmpty(CategoryName) ? CategoryName
        : CategoryId > 0 ? $"Category {CategoryId}"
        : string.Empty;
}

// ---------------------------------------------------------------------------
// NxmLink — parses nxm:// URIs
// ---------------------------------------------------------------------------

public record NxmLink(
    string GameDomain,
    int ModId,
    int FileId,
    string? Key,
    string? Expires,
    int? UserId)
{
    /// <summary>
    /// Parses nxm://{gameDomain}/mods/{modId}/files/{fileId}?key={key}&amp;expires={expires}&amp;user_id={userId}
    /// </summary>
    public static NxmLink Parse(string uri)
    {
        var parsed = new Uri(uri);

        var gameDomain = parsed.Host;

        // path segments: /mods/{modId}/files/{fileId}
        var segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var modId  = segments.Length > 1 ? int.Parse(segments[1]) : 0;
        var fileId = segments.Length > 3 ? int.Parse(segments[3]) : 0;

        var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);
        var key     = query["key"];
        var expires = query["expires"];
        int? userId = null;
        if (int.TryParse(query["user_id"], out var uid))
            userId = uid;

        return new NxmLink(gameDomain, modId, fileId, key, expires, userId);
    }
}

// ---------------------------------------------------------------------------
// DownloadEntry — observable download queue item
// ---------------------------------------------------------------------------

public partial class DownloadEntry : ObservableObject
{
    [ObservableProperty]
    private string _modName = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _hasFailed;

    public string? LocalPath { get; set; }
    public int ModId { get; set; }
    public int FileId { get; set; }
    public string GameDomain { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = "Uncategorized";

    public CancellationTokenSource Cts { get; } = new();
}

// ---------------------------------------------------------------------------
// Tracking models
// ---------------------------------------------------------------------------

public class NexusTrackEntry
{
    public string ModFolderPath { get; set; } = string.Empty;
    public int ModId { get; set; }
    public int FileId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string GameDomain { get; set; } = string.Empty;
    /// <summary>Original archive path that was downloaded, if this entry was created from a download.</summary>
    public string? SourceArchivePath { get; set; }
}

// ---------------------------------------------------------------------------
// Category resolution helpers
// ---------------------------------------------------------------------------

public class NexusCategory
{
    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    // parent_category is omitted: Nexus API returns false (bool) for root categories
    // and an integer (parent ID) for subcategories — mixed type breaks System.Text.Json.
}

/// <summary>Partial deserialization of GET /v1/games/{domain}.json — only the categories array is needed.</summary>
public class NexusGameDetails
{
    [JsonPropertyName("categories")]
    public List<NexusCategory> Categories { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Nexus search (search.nexusmods.com)
// ---------------------------------------------------------------------------

public class NexusSearchHit
{
    [JsonPropertyName("mod_id")]            public int    ModId            { get; set; }
    [JsonPropertyName("game_id")]           public int    GameId           { get; set; }
    [JsonPropertyName("name")]              public string Name             { get; set; } = string.Empty;
    [JsonPropertyName("author")]            public string Author           { get; set; } = string.Empty;
    [JsonPropertyName("summary")]           public string Summary          { get; set; } = string.Empty;
    [JsonPropertyName("endorsement_count")] public int    EndorsementCount { get; set; }
    [JsonPropertyName("downloads")]         public int    Downloads        { get; set; }
}

public class NexusSearchResponse
{
    [JsonPropertyName("results")] public List<NexusSearchHit> Results { get; set; } = new();
    [JsonPropertyName("total")]   public int                  Total   { get; set; }
}

// ---------------------------------------------------------------------------
// Nexus Collections
// ---------------------------------------------------------------------------

public class NexusCollectionInfo
{
    [JsonPropertyName("name")]    public string Name    { get; set; } = string.Empty;
    [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
    [JsonPropertyName("latest_published_revision")]
    public NexusCollectionRevisionRef? LatestPublishedRevision { get; set; }
}

public class NexusCollectionRevisionRef
{
    [JsonPropertyName("revision_number")] public int RevisionNumber { get; set; }
    [JsonPropertyName("mod_count")]       public int ModCount       { get; set; }
}

public class NexusCollectionRevision
{
    [JsonPropertyName("collection")] public NexusCollectionInfo?       Collection { get; set; }
    [JsonPropertyName("mods")]       public List<NexusCollectionMod>   Mods       { get; set; } = new();
}

public class NexusCollectionMod
{
    [JsonPropertyName("nexus_file_id")] public int            FileId { get; set; }
    [JsonPropertyName("mod")]           public NexusModRef?   Mod    { get; set; }
}

public class NexusModRef
{
    [JsonPropertyName("mod_id")]      public int    ModId      { get; set; }
    [JsonPropertyName("name")]        public string Name       { get; set; } = string.Empty;
    [JsonPropertyName("domain_name")] public string DomainName { get; set; } = string.Empty;
    [JsonPropertyName("game_id")]     public int    GameId     { get; set; }
}
