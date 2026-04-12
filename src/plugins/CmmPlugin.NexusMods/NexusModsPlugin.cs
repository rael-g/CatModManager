using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

public class NexusModsPlugin : ICmmPlugin
{
    public string Id          => "nexus-mods";
    public string DisplayName => "Nexus Mods Integration";
    public string Version     => typeof(NexusModsPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string Author      => "CMM";

    private NexusApiService?          _api;
    private NexusDownloadService?     _downloadService;
    private NexusModTrackingService?  _trackingService;
    private IPluginContext?           _context;
    private string                    _settingsDir = "";
    private string?                   _currentProfileName;
    private NexusDatabase?            _nexusDb;
    private readonly System.Collections.Generic.List<NxmLinkEvent> _pendingNxmLinks = new();
    private System.Threading.CancellationTokenSource? _saveCts;
    private Action<string, CatModManager.PluginSdk.FomodPreset?>? _installCallback;

    public void Initialize(IPluginContext ctx)
    {
        _context = ctx;

        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "catmodmanager", "plugins", "nexusmods");
        Directory.CreateDirectory(_settingsDir);

        _nexusDb         = new NexusDatabase(_settingsDir);
        _api             = new NexusApiService(_nexusDb);
        _trackingService = new NexusModTrackingService(_nexusDb);
        _downloadService = new NexusDownloadService(_api, ctx.Log, _trackingService, _nexusDb);

        LoadDownloadsForProfile(ctx.State.CurrentProfileName);

        _downloadService.Downloads.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null)
                foreach (DownloadEntry e in args.NewItems)
                    e.PropertyChanged += OnEntryChanged;
            if (args.Action == NotifyCollectionChangedAction.Remove && args.OldItems != null)
                foreach (DownloadEntry e in args.OldItems)
                    e.PropertyChanged -= OnEntryChanged;
            DebounceSave(ctx.State.CurrentProfileName);
        };

        void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DownloadEntry.IsActive) or nameof(DownloadEntry.HasFailed))
            {
                // Save immediately on state change to prevent data loss
                SaveDownloadsForProfile(_currentProfileName);
            }
        }

        ctx.State.ProfileChanged += profileName =>
        {
            LoadDownloadsForProfile(profileName);
            // Process NXM links that arrived before the profile (and its DownloadsFolderPath) was ready.
            if (_pendingNxmLinks.Count > 0)
            {
                var pending = _pendingNxmLinks.ToArray();
                _pendingNxmLinks.Clear();
                foreach (var p in pending) OnNxmLink(p);
            }
        };

        ctx.State.ModInstalled += (IModInfo mod, string sourcePath) =>
        {
            if (_trackingService == null) return;

            // Re-track under the installed folder path, preserving the source archive path
            // so that "Reinstall (Nexus)" can locate the archive later.
            var trackEntry = _trackingService.GetEntryBySourcePath(sourcePath);
            if (trackEntry != null)
                _trackingService.Track(mod.RootPath, trackEntry.ModId, trackEntry.FileId,
                    trackEntry.Version, trackEntry.GameDomain, trackEntry.SourceArchivePath);

            // Enrich mod metadata from the download entry so the profile stores the real name/version/category.
            var download = _downloadService?.Downloads
                .FirstOrDefault(d => string.Equals(d.LocalPath, sourcePath, StringComparison.OrdinalIgnoreCase));
            if (download != null)
            {
                if (!string.IsNullOrEmpty(download.ModName))  mod.Name     = download.ModName;
                if (!string.IsNullOrEmpty(download.Version))  mod.Version  = download.Version;
                if (!string.IsNullOrEmpty(download.Category)) mod.Category = download.Category;
            }
        };

        ctx.Events.Subscribe<NxmLinkEvent>(OnNxmLink);

        _installCallback = InstallCallback;

        void InstallCallback(string archivePath, CatModManager.PluginSdk.FomodPreset? preset)
        {
            // Match on ModId + FileId: same file = update in place.
            // Different FileId = separate variant (e.g. different race body), install as new mod.
            var downloadEntry = _downloadService?.Downloads
                .FirstOrDefault(d => string.Equals(d.LocalPath, archivePath, StringComparison.OrdinalIgnoreCase));

            int modId  = downloadEntry?.ModId  ?? 0;
            int fileId = downloadEntry?.FileId ?? 0;

            if (modId > 0 && fileId > 0)
            {
                var existing = _trackingService?.GetEntryByModIdAndFileId(modId, fileId);
                if (existing != null && System.IO.Directory.Exists(existing.ModFolderPath))
                {
                    ctx.Log.Log($"[Nexus] Updating '{System.IO.Path.GetFileName(existing.ModFolderPath)}' (ModId {modId}, FileId {fileId}) in place.");
                    ctx.State.SetInstallFolderHint(existing.ModFolderPath);
                }
            }
            else
            {
                // Fallback: look up by the archive path recorded at download time
                var trackEntry = _trackingService?.GetEntryBySourcePath(archivePath);
                if (trackEntry != null)
                {
                    var existing = _trackingService?.GetEntryByModIdAndFileId(trackEntry.ModId, trackEntry.FileId);
                    if (existing != null && System.IO.Directory.Exists(existing.ModFolderPath))
                    {
                        ctx.Log.Log($"[Nexus] Reinstalling '{System.IO.Path.GetFileName(existing.ModFolderPath)}' (ModId {trackEntry.ModId}, FileId {trackEntry.FileId}) in place.");
                        ctx.State.SetInstallFolderHint(existing.ModFolderPath);
                    }
                }
            }
            ctx.State.RequestInstallMod(archivePath, preset);
        }
        ctx.Ui.RegisterInspectorTab(new NexusDownloadsTab(_downloadService, _api, InstallCallback, GetDownloadsFolder));
        ctx.Ui.RegisterSidebarAction(new NexusBrowseSidebarAction(_api, ctx.State, _downloadService, GetDownloadsFolder));
        ctx.Ui.RegisterModContextAction(new NexusCheckUpdateAction(_trackingService, _api, ctx.Log));
        ctx.Ui.RegisterModContextAction(new NexusReinstallAction(_trackingService, InstallCallback, ctx.Log));

        ctx.State.SetActiveDownloadCheck(() => _downloadService?.Downloads.Any(d => d.IsActive) ?? false);


        ctx.Log.Log($"[{DisplayName}] Initialized — Nexus Mods integration ready.");
    }

    public Task ShutdownAsync()
    {
        _saveCts?.Cancel();
        _downloadService?.Shutdown();
        SaveDownloadsForProfile(_currentProfileName);
        return Task.CompletedTask;
    }


    private static string NormalizeProfileName(string? profileName)
        => string.IsNullOrEmpty(profileName) ? "_global" : profileName;

    private void LoadDownloadsForProfile(string? profileName)
    {
        if (_downloadService == null) return;
        SaveDownloadsForProfile(_currentProfileName);
        _downloadService.LoadDownloads(NormalizeProfileName(profileName));
        _currentProfileName = profileName;
    }

    /// <summary>
    /// Coalesces rapid successive save requests (e.g. 100+ adds during collection install)
    /// into a single write 400ms after the last change.
    /// </summary>
    private void DebounceSave(string? profileName)
    {
        _saveCts?.Cancel();
        _saveCts = new System.Threading.CancellationTokenSource();
        var cts = _saveCts;
        _ = System.Threading.Tasks.Task.Delay(400, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) SaveDownloadsForProfile(profileName);
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    private void SaveDownloadsForProfile(string? profileName)
    {
        if (_downloadService == null) return;
        _downloadService.SaveDownloads(NormalizeProfileName(profileName));
    }

    private void OnNxmLink(NxmLinkEvent e)
    {
        if (_context == null || _downloadService == null) return;

        // If the profile hasn't loaded yet, DownloadsFolderPath is empty and we'd
        // fall back to temp. Defer until ProfileChanged fires with the real path.
        if (string.IsNullOrEmpty(_context.State.DownloadsFolderPath))
        {
            _context.Log.Log($"[NexusMods] NXM download deferred (profile loading): {e.NxmUri}");
            _pendingNxmLinks.Add(e);
            return;
        }

        try
        {
            var downloadsFolder = GetDownloadsFolder();

            // Collection link: nxm://{game}/collections/{slug}/revisions/{rev}?...
            var collectionLink = NxmCollectionLink.TryParse(e.NxmUri);
            if (collectionLink != null)
            {
                _downloadService.QueueCollectionDownloadFromNxm(collectionLink, downloadsFolder);
                _context.Log.Log($"[NexusMods] Collection NXM queued: {collectionLink.Slug} rev.{collectionLink.Revision}");
                return;
            }

            // Regular mod link: nxm://{game}/mods/{modId}/files/{fileId}?...
            var link    = NxmLink.Parse(e.NxmUri);
            var modName = $"Nexus Mod #{link.ModId}";
            _downloadService.QueueDownloadFromNxm(link, modName, downloadsFolder);
            _context.Log.Log($"[NexusMods] NXM download queued: {e.NxmUri}");
        }
        catch (Exception ex)
        {
            _context.Log.LogError($"[NexusMods] Failed to handle NXM link: {e.NxmUri}", ex);
        }
    }

    private string GetDownloadsFolder()
    {
        var defaultFolder = Path.Combine(Path.GetTempPath(), "CatNxmDownloads");
        if (_context == null) return defaultFolder;

        var dl = _context.State.DownloadsFolderPath;
        if (!string.IsNullOrEmpty(dl)) return dl;

        return defaultFolder;
    }
}

