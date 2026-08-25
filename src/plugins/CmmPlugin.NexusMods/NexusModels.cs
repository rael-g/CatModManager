using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CmmPlugin.NexusMods;

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

    public string ResolvedCategory =>
        !string.IsNullOrEmpty(CategoryName) ? CategoryName
        : CategoryId > 0 ? $"Category {CategoryId}"
        : string.Empty;
}

public record NxmLink(
    string GameDomain,
    int ModId,
    int FileId,
    string? Key,
    string? Expires,
    int? UserId)
{
    /// <summary>
    /// Returns null if this is not a mod link (e.g. it's a collection link).
    /// </summary>
    public static NxmLink? TryParse(string uri)
    {
        var parsed = new Uri(uri);
        var segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 1 || !string.Equals(segments[0], "mods", StringComparison.OrdinalIgnoreCase))
            return null;

        var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);
        int? userId = int.TryParse(query["user_id"], out var uid) ? uid : null;
        return new NxmLink(
            parsed.Host,
            segments.Length > 1 ? int.Parse(segments[1]) : 0,
            segments.Length > 3 ? int.Parse(segments[3]) : 0,
            query["key"], query["expires"], userId);
    }

    public static NxmLink Parse(string uri) =>
        TryParse(uri) ?? throw new FormatException($"Not a mod nxm link: {uri}");
}

public record NxmCollectionLink(
    string GameDomain,
    string Slug,
    int    Revision,
    string? Key,
    string? Expires,
    int?   UserId)
{
    public static NxmCollectionLink? TryParse(string uri)
    {
        var parsed = new Uri(uri);
        var segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || !string.Equals(segments[0], "collections", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!int.TryParse(segments[3], out var revision)) return null;
        var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);
        int? userId = int.TryParse(query["user_id"], out var uid) ? uid : null;
        return new NxmCollectionLink(
            parsed.Host, segments[1], revision,
            query["key"], query["expires"], userId);
    }
}

public class NexusCollectionManifest
{
    [JsonPropertyName("info")] public NexusCollectionManifestInfo? Info { get; set; }
    [JsonPropertyName("mods")] public List<NexusCollectionModEntry> Mods { get; set; } = new();
}

public class NexusCollectionManifestInfo
{
    [JsonPropertyName("name")]       public string Name       { get; set; } = string.Empty;
    [JsonPropertyName("domainName")] public string DomainName { get; set; } = string.Empty;
    [JsonPropertyName("version")]    public string Version    { get; set; } = string.Empty;
}

public class NexusCollectionModEntry
{
    [JsonPropertyName("name")]     public string Name     { get; set; } = string.Empty;
    [JsonPropertyName("version")]  public string Version  { get; set; } = string.Empty;
    [JsonPropertyName("optional")] public bool   Optional { get; set; }
    /// <summary>Installation phase (0 = default). Higher phases install after lower phases complete.</summary>
    [JsonPropertyName("phase")]    public int    Phase    { get; set; }
    [JsonPropertyName("source")]   public NexusCollectionModSource? Source { get; set; }
    /// <summary>Pre-selected FOMOD choices for this mod. Present only if the curator saved installer options.</summary>
    [JsonPropertyName("choices")]  public NexusCollectionFomodChoices? Choices { get; set; }
}

public class NexusCollectionFomodChoices
{
    [JsonPropertyName("type")]    public string Type    { get; set; } = string.Empty;
    [JsonPropertyName("options")] public List<NexusCollectionFomodOption> Options { get; set; } = new();
}

public class NexusCollectionFomodOption
{
    [JsonPropertyName("name")]    public string Name    { get; set; } = string.Empty;
    [JsonPropertyName("choices")] public List<NexusCollectionFomodChoice> Choices { get; set; } = new();
}

public class NexusCollectionFomodChoice
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("idx")]  public int    Idx  { get; set; }
}

public class NexusCollectionModSource
{
    [JsonPropertyName("type")]       public string Type       { get; set; } = string.Empty;
    [JsonPropertyName("modId")]      public int    ModId      { get; set; }
    [JsonPropertyName("fileId")]     public long   FileId     { get; set; }
    [JsonPropertyName("gameDomain")] public string GameDomain { get; set; } = string.Empty;
    [JsonPropertyName("fileSize")]   public long   FileSize   { get; set; }
}

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

    /// <summary>
    /// The short-lived download token from the originating nxm:// link, if any. Kept on the entry
    /// so Retry can re-send it — retrying without it makes Nexus refuse free-user downloads with
    /// "premium required", which is why retrying a collection mod used to always fail.
    /// </summary>
    public string? NxmKey { get; set; }

    /// <summary>Expiry timestamp paired with <see cref="NxmKey"/>.</summary>
    public string? NxmExpires { get; set; }

    public CancellationTokenSource Cts { get; } = new();

    /// <summary>When non-null, the FOMOD installer will auto-apply these choices without showing the wizard.</summary>
    public CatModManager.PluginSdk.FomodPreset? FomodPreset { get; set; }
}

public class NexusCollectionGraphQlResponse
{
    [JsonPropertyName("data")] public NexusCollectionGraphQlData? Data { get; set; }
}

public class NexusCollectionGraphQlData
{
    [JsonPropertyName("collectionRevision")] public NexusCollectionRevisionData? CollectionRevision { get; set; }
}

public class NexusCollectionRevisionData
{
    [JsonPropertyName("modFiles")] public List<NexusCollectionModFile> ModFiles { get; set; } = new();
}

public class NexusCollectionModFile
{
    [JsonPropertyName("fileId")]   public long FileId   { get; set; }
    [JsonPropertyName("optional")] public bool Optional { get; set; }
    [JsonPropertyName("file")]     public NexusCollectionFileInfo? File { get; set; }
}

public class NexusCollectionFileInfo
{
    [JsonPropertyName("name")]    public string Name    { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("mod")]     public NexusCollectionModInfo? Mod { get; set; }
}

public class NexusCollectionModInfo
{
    [JsonPropertyName("modId")] public int    ModId { get; set; }
    [JsonPropertyName("name")]  public string Name  { get; set; } = string.Empty;
    [JsonPropertyName("game")]  public NexusCollectionGame? Game { get; set; }
}

public class NexusCollectionGame
{
    [JsonPropertyName("domainName")] public string DomainName { get; set; } = string.Empty;
}

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
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("categories")]
    public List<NexusCategory> Categories { get; set; } = new();
}

public enum BrowseSort { Trending, LatestAdded, LatestUpdated }

public class NexusBrowseCollection
{
    public int    Id           { get; set; }
    public string Slug         { get; set; } = string.Empty;
    public string Name         { get; set; } = string.Empty;
    public string Summary      { get; set; } = string.Empty;
    public string Author       { get; set; } = string.Empty;
    public string Category     { get; set; } = string.Empty;
    public long   Endorsements { get; set; }
    public long   Downloads    { get; set; }
    public int    Revision     { get; set; }
    public int    ModCount     { get; set; }
    public string GameDomain   { get; set; } = string.Empty;
    public int    TotalCount   { get; set; }
    /// <summary>Relative path from GraphQL, e.g. /v2/collections/{id}/revisions/{revId}/download_link. Requires API key.</summary>
    public string DownloadLink { get; set; } = string.Empty;
}

public class NexusCollectionsV2GraphQlResponse
{
    [JsonPropertyName("data")]   public NexusCollectionsV2GraphQlData?         Data   { get; set; }
    [JsonPropertyName("errors")] public List<System.Text.Json.JsonElement>? Errors { get; set; }
}

public class NexusCollectionsV2GraphQlData
{
    [JsonPropertyName("collectionsV2")] public NexusCollectionsV2Connection? CollectionsV2 { get; set; }
}

public class NexusCollectionsV2Connection
{
    [JsonPropertyName("nodes")]      public List<NexusCollectionV2Node> Nodes      { get; set; } = new();
    [JsonPropertyName("totalCount")] public int                         TotalCount { get; set; }
}

public class NexusCollectionV2Node
{
    [JsonPropertyName("id")]                      public int                         Id                      { get; set; }
    [JsonPropertyName("slug")]                    public string                      Slug                    { get; set; } = string.Empty;
    [JsonPropertyName("name")]                    public string                      Name                    { get; set; } = string.Empty;
    [JsonPropertyName("summary")]                 public string                      Summary                 { get; set; } = string.Empty;
    [JsonPropertyName("endorsements")]            public long                        Endorsements            { get; set; }
    [JsonPropertyName("totalDownloads")]          public long                        TotalDownloads          { get; set; }
    [JsonPropertyName("latestPublishedRevision")] public NexusCollectionRevisionInfo? LatestPublishedRevision { get; set; }
    [JsonPropertyName("user")]                    public NexusCollectionUserInfo?    User                    { get; set; }
    [JsonPropertyName("category")]                public NexusCollectionCategoryInfo? Category               { get; set; }
}

public class NexusCollectionRevisionInfo
{
    [JsonPropertyName("revision")]     public int    Revision     { get; set; }
    [JsonPropertyName("modCount")]     public int    ModCount     { get; set; }
    [JsonPropertyName("downloadLink")] public string DownloadLink { get; set; } = string.Empty;
}

public class NexusCollectionUserInfo
{
    [JsonPropertyName("name")]     public string Name     { get; set; } = string.Empty;
    [JsonPropertyName("memberId")] public int    MemberId { get; set; }
}

public class NexusCollectionCategoryInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class NexusBrowseMod
{
    public int    ModId            { get; set; }
    public string Name             { get; set; } = string.Empty;
    public string Summary          { get; set; } = string.Empty;
    public string Author           { get; set; } = string.Empty;
    public string CategoryName     { get; set; } = string.Empty;
    public long   DownloadCount    { get; set; }
    public long   EndorsementCount { get; set; }
    public string Version          { get; set; } = string.Empty;
    public string GameDomain       { get; set; } = string.Empty;
    public int    TotalCount       { get; set; }
}

public class NexusModsGraphQlResponse
{
    [JsonPropertyName("data")]   public NexusModsGraphQlData?   Data   { get; set; }
    [JsonPropertyName("errors")] public List<System.Text.Json.JsonElement>? Errors { get; set; }
}

public class NexusModsGraphQlData
{
    [JsonPropertyName("mods")] public NexusModsConnection? Mods { get; set; }
}

public class NexusModsConnection
{
    [JsonPropertyName("nodes")]      public List<NexusGraphQlMod> Nodes      { get; set; } = new();
    [JsonPropertyName("totalCount")] public int                   TotalCount { get; set; }
}

public class NexusGraphQlMod
{
    [JsonPropertyName("modId")]        public int    ModId        { get; set; }
    [JsonPropertyName("name")]         public string Name         { get; set; } = string.Empty;
    [JsonPropertyName("summary")]      public string Summary      { get; set; } = string.Empty;
    [JsonPropertyName("author")]       public string Author       { get; set; } = string.Empty;
    [JsonPropertyName("category")]     public string Category     { get; set; } = string.Empty;
    [JsonPropertyName("downloads")]    public long   Downloads    { get; set; }
    [JsonPropertyName("endorsements")] public long   Endorsements { get; set; }
    [JsonPropertyName("version")]      public string Version      { get; set; } = string.Empty;
    [JsonPropertyName("pictureUrl")]   public string PictureUrl   { get; set; } = string.Empty;
}

