using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>A single mod of a collection, resolved to everything needed to download it.</summary>
public record ResolvedCollectionMod(
    int ModId,
    int FileId,
    string Domain,
    string Name,
    string Version,
    FomodPreset? FomodPreset);

/// <summary>The outcome of resolving a collection revision.</summary>
/// <param name="Mods">Required mods in install order, or empty when resolution failed.</param>
/// <param name="HasManifest">
/// True when collection.json was available, meaning <paramref name="Mods"/> is in phase order and
/// carries FOMOD choices. False means the fallback GraphQL curator order was used.
/// </param>
public record ResolvedCollection(IReadOnlyList<ResolvedCollectionMod> Mods, bool HasManifest);

/// <summary>
/// Turns a nxm:// collection link into an ordered mod list.
///
/// Two sources are combined:
///  1. GraphQL (v2, no API key) — modId + fileId + current names for every mod.
///  2. collection.json inside the collection archive (v1, needs the NXM key) — phase ordering and
///     FOMOD choices. Optional: if it can't be fetched or parsed, GraphQL curator order is used.
///
/// Extracted from NexusDownloadService: this is pure API/JSON work with no UI or transfer state,
/// which makes it the one part of the collection flow that can be reasoned about on its own.
/// </summary>
public class NexusCollectionResolver
{
    private readonly NexusApiService _api;
    private readonly IPluginLogger _log;

    public NexusCollectionResolver(NexusApiService api, IPluginLogger log)
    {
        _api = api;
        _log = log;
    }

    /// <param name="onProgress">Receives user-facing status messages as each step starts.</param>
    public async Task<ResolvedCollection> ResolveAsync(
        NxmCollectionLink link, Action<string> onProgress, CancellationToken ct)
    {
        onProgress("Resolving collection via Nexus API…");

        var gql = await _api.QueryCollectionRevisionAsync(link.Slug, link.Revision, ct);
        var modFiles = gql?.Data?.CollectionRevision?.ModFiles;
        if (modFiles == null || modFiles.Count == 0)
            return new ResolvedCollection(Array.Empty<ResolvedCollectionMod>(), HasManifest: false);

        onProgress("Fetching collection manifest…");
        var manifest = await TryFetchManifestAsync(link, ct);

        var mods = manifest != null
            ? FromManifest(manifest, modFiles, link.GameDomain)
            : FromGraphQl(modFiles, link.GameDomain);

        return new ResolvedCollection(mods, manifest != null);
    }

    /// <summary>
    /// Downloads the collection archive and pulls collection.json out of it. Best-effort: every
    /// failure path returns null so the caller falls back to GraphQL ordering.
    /// </summary>
    private async Task<NexusCollectionManifest?> TryFetchManifestAsync(
        NxmCollectionLink link, CancellationToken ct)
    {
        try
        {
            var archiveUrl = await _api.GetCollectionArchiveUrlAsync(
                link.Slug, link.Revision, link.Key, link.Expires, ct);
            if (string.IsNullOrEmpty(archiveUrl)) return null;

            var zipBytes = await _api.GetBytesAsync(archiveUrl, ct: ct);
            if (zipBytes.Length == 0) return null;

            using var ms  = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var zipEntry  = zip.GetEntry("collection.json");
            if (zipEntry == null) return null;

            using var stream = zipEntry.Open();
            return await JsonSerializer.DeserializeAsync<NexusCollectionManifest>(stream, cancellationToken: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Log($"[NexusMods] Could not read collection.json: {ex.Message}");
            return null;
        }
    }

    /// <summary>Required mods in phase order, with names refreshed from GraphQL where possible.</summary>
    private static List<ResolvedCollectionMod> FromManifest(
        NexusCollectionManifest manifest,
        List<NexusCollectionModFile> graphQlFiles,
        string fallbackDomain)
    {
        var byIds = graphQlFiles
            .Where(f => f.File?.Mod != null && f.File.Mod.ModId != 0 && f.FileId != 0)
            .ToDictionary(f => ((int)f.File!.Mod!.ModId, (int)f.FileId));

        return manifest.Mods
            .Where(m => !m.Optional &&
                        string.Equals(m.Source?.Type, "nexus", StringComparison.OrdinalIgnoreCase) &&
                        m.Source!.ModId != 0 && m.Source.FileId != 0)
            .OrderBy(m => m.Phase)
            .Select(m =>
            {
                var src = m.Source!;
                // Prefer the GraphQL name — it's always current, the manifest snapshot may be stale.
                string name = byIds.TryGetValue((src.ModId, (int)src.FileId), out var g)
                    ? (g.File?.Mod?.Name ?? m.Name)
                    : m.Name;

                return new ResolvedCollectionMod(
                    src.ModId,
                    (int)src.FileId,
                    string.IsNullOrEmpty(src.GameDomain) ? fallbackDomain : src.GameDomain,
                    name,
                    m.Version,
                    ToFomodPreset(m.Choices));
            })
            .ToList();
    }

    /// <summary>Fallback ordering when no manifest is available — GraphQL lists mods in curator order.</summary>
    private static List<ResolvedCollectionMod> FromGraphQl(
        List<NexusCollectionModFile> graphQlFiles, string fallbackDomain)
    {
        var mods = new List<ResolvedCollectionMod>();
        foreach (var f in graphQlFiles.Where(f => !f.Optional))
        {
            var mod = f.File?.Mod;
            if (mod == null || mod.ModId == 0 || f.FileId == 0) continue;

            mods.Add(new ResolvedCollectionMod(
                mod.ModId,
                (int)f.FileId,
                mod.Game?.DomainName ?? fallbackDomain,
                mod.Name.Length > 0 ? mod.Name : $"Mod #{mod.ModId}",
                f.File?.Version ?? string.Empty,
                FomodPreset: null));
        }
        return mods;
    }

    /// <summary>Maps the manifest's FOMOD choices onto the SDK preset the installer understands.</summary>
    internal static FomodPreset? ToFomodPreset(NexusCollectionFomodChoices? choices)
    {
        if (choices == null || !string.Equals(choices.Type, "fomod", StringComparison.OrdinalIgnoreCase))
            return null;

        var preset = new FomodPreset();
        foreach (var option in choices.Options)
        {
            var group = new FomodPresetGroup { GroupName = option.Name };
            foreach (var choice in option.Choices)
            {
                group.SelectedNames.Add(choice.Name);
                group.SelectedIndices.Add(choice.Idx);
            }
            preset.Groups.Add(group);
        }
        return preset;
    }
}
