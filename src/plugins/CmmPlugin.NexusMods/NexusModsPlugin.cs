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
            SaveDownloadsForProfile(ctx.State.CurrentProfileName);
        };

        void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DownloadEntry.IsActive) or nameof(DownloadEntry.HasFailed))
                SaveDownloadsForProfile(ctx.State.CurrentProfileName);
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

            // Re-track under the installed folder path so the NEXUS tab appears.
            var trackEntry = _trackingService.GetEntryBySourcePath(sourcePath);
            if (trackEntry != null)
                _trackingService.Track(mod.RootPath, trackEntry.ModId, trackEntry.FileId, trackEntry.Version, trackEntry.GameDomain);

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

        void InstallCallback(string archivePath) => ctx.State.RequestInstallMod(archivePath);

        ctx.Ui.RegisterInspectorTab(new NexusDownloadsTab(_downloadService, _api, InstallCallback, GetDownloadsFolder));
        ctx.Ui.RegisterInspectorTab(new NexusModInspectorTab(_trackingService, _api));


        ctx.Log.Log($"[{DisplayName}] Initialized — Nexus Mods integration ready.");
    }

    // -----------------------------------------------------------------------

    private static string NormalizeProfileName(string? profileName)
        => string.IsNullOrEmpty(profileName) ? "_global" : profileName;

    private void LoadDownloadsForProfile(string? profileName)
    {
        if (_downloadService == null) return;
        SaveDownloadsForProfile(_currentProfileName);
        _downloadService.LoadDownloads(NormalizeProfileName(profileName));
        _currentProfileName = profileName;
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
