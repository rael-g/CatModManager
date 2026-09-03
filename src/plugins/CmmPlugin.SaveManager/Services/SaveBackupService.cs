using System.IO.Compression;
using CatModManager.PluginSdk;

namespace CmmPlugin.SaveManager.Services;

/// <summary>Why a slot exists, which decides whether it may ever be deleted automatically.</summary>
public enum SaveSlotKind
{
    /// <summary>The user pressed Save. Never removed except by the user.</summary>
    Manual,

    /// <summary>Taken automatically just before a load overwrote the live saves. Ring-buffered.</summary>
    PreLoad,

    /// <summary>Taken on a timer. Ring-buffered.</summary>
    Auto
}

public class SaveSlot
{
    public string       FilePath  { get; init; } = "";
    public string       Label     { get; init; } = "";
    public SaveSlotKind Kind      { get; init; }
    public DateTime     CreatedAt { get; init; }
    public long         SizeBytes { get; init; }

    public string Display => Kind switch
    {
        SaveSlotKind.PreLoad => $"{Label}  (auto, before load)",
        SaveSlotKind.Auto    => $"{Label}  (auto)",
        _                    => Label
    };
}

/// <summary>
/// Save slots, in the emulator sense: the user presses Save before a boss or an ending, plays on,
/// and presses Load to come back. Not a backup schedule.
///
/// Everything here is written so that an interruption at any point leaves the live saves intact.
/// That constraint drives the two non-obvious mechanics below — writing through a temporary name,
/// and loading by swapping directories rather than emptying one.
/// </summary>
public class SaveBackupService
{
    private readonly string        _backupsRoot;
    private readonly IPluginLogger _log;

    /// <summary>
    /// How many slots of each automatic kind to keep, oldest recycled first — a ring buffer per
    /// kind, so timed snapshots and pre-load snapshots cannot consume each other's room.
    ///
    /// Manual slots are deliberately uncapped: the user made each one on purpose, at a moment they
    /// chose, and deleting one to stay under a limit would throw away the exact thing they came here
    /// to keep. Automatic ones are the opposite — nobody asked for them, they accumulate on their
    /// own, and five is enough to undo a load the user regrets a few loads later.
    /// </summary>
    private const int MaxAutomaticSlots = 5;

    private const string TempSuffix    = ".cmm-writing";
    private const string StagingSuffix = ".cmm-loading";
    private const string OutgoingSuffix = ".cmm-previous";

    public SaveBackupService(string appDataPath, IPluginLogger log)
    {
        _backupsRoot = Path.Combine(appDataPath, "save_backups");
        _log         = log;
    }

    public string BackupFolderFor(string gameId) => Path.Combine(_backupsRoot, gameId);

    // ── Saving ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a new slot, or null if it could not be written. Never leaves a partial file under a
    /// name the rest of the app will treat as a slot.
    /// </summary>
    public async Task<string?> CreateAsync(string gameId, string saveFolder, string label, SaveSlotKind kind = SaveSlotKind.Manual)
    {
        if (!Directory.Exists(saveFolder))
        {
            _log.LogError($"[SaveManager] Cannot save: '{saveFolder}' does not exist.");
            return null;
        }

        string dest = BackupFolderFor(gameId);
        Directory.CreateDirectory(dest);

        string finalPath = Uniquify(Path.Combine(dest, BuildFileName(label, kind)));
        string tempPath  = finalPath + TempSuffix;

        try
        {
            // Written under a name ListSlots ignores, then renamed once it is known to be complete.
            // Writing straight to the final name meant a crash mid-zip left a truncated archive that
            // looked like a slot, listed like a slot, and would restore like one — right up to the
            // point of failing after the live saves had already been cleared.
            await Task.Run(() =>
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                ZipFile.CreateFromDirectory(saveFolder, tempPath, CompressionLevel.Fastest, includeBaseDirectory: false);
                VerifyArchive(tempPath);
                File.Move(tempPath, finalPath);
            });

            _log.Log($"[SaveManager] Saved: {Path.GetFileName(finalPath)}");
            if (kind != SaveSlotKind.Manual) Recycle(gameId, kind);
            return finalPath;
        }
        catch (Exception ex)
        {
            _log.LogError($"[SaveManager] Save failed for {gameId}", ex);
            TryDelete(tempPath);
            return null;
        }
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the live saves with <paramref name="slot"/>.
    ///
    /// The old shape of this was <c>Directory.Delete(saveFolder)</c> followed by an extract, which
    /// has a window — the whole extract — where the saves exist in neither place. A corrupt archive,
    /// a full disk, or the app dying in between destroyed them outright.
    ///
    /// Instead the replacement is fully built and verified in a sibling directory first, and only
    /// then swapped in by renaming. Sibling, so the renames are on one filesystem and therefore
    /// atomic; the user's saves and our backups can live on different disks, but this pair cannot.
    /// </summary>
    public async Task LoadAsync(SaveSlot slot, string saveFolder, string gameId)
    {
        VerifyArchive(slot.FilePath);   // before anything is touched

        // A snapshot of what is about to be overwritten. If this cannot be written we stop, because
        // proceeding would mean the user's current progress has no way back.
        if (Directory.Exists(saveFolder) && Directory.EnumerateFileSystemEntries(saveFolder).Any())
        {
            string? safety = await CreateAsync(gameId, saveFolder, "before load", SaveSlotKind.PreLoad);
            if (safety == null)
                throw new IOException(
                    "Refusing to load: the current saves could not be backed up first, so this " +
                    "would be irreversible. Check the log and free space in the backups folder.");
        }

        string staging  = saveFolder + StagingSuffix;
        string outgoing = saveFolder + OutgoingSuffix;

        await Task.Run(() =>
        {
            TryDeleteDirectory(staging);
            TryDeleteDirectory(outgoing);

            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(slot.FilePath, staging);

            bool hadSaves = Directory.Exists(saveFolder);
            if (hadSaves) Directory.Move(saveFolder, outgoing);

            try
            {
                Directory.Move(staging, saveFolder);
            }
            catch
            {
                // Put the originals back before surfacing the failure. Without this the saves are
                // sitting under a name the game does not know about.
                if (hadSaves && !Directory.Exists(saveFolder)) Directory.Move(outgoing, saveFolder);
                throw;
            }

            TryDeleteDirectory(outgoing);
        });

        _log.Log($"[SaveManager] Loaded '{slot.Label}' → {saveFolder}");
    }

    // ── Listing and removal ──────────────────────────────────────────────────

    public IReadOnlyList<SaveSlot> ListSlots(string gameId)
    {
        string folder = BackupFolderFor(gameId);
        if (!Directory.Exists(folder)) return [];

        var found = new List<(SaveSlot Slot, DateTime Written)>();
        foreach (string file in Directory.GetFiles(folder, "*.zip"))
        {
            try
            {
                var (label, kind, createdAt) = ParseFileName(Path.GetFileName(file));
                var info = new FileInfo(file);

                found.Add((new SaveSlot
                {
                    FilePath  = file,
                    Label     = label,
                    Kind      = kind,
                    // Read from the name, not from the filesystem: creation time is not recorded on
                    // every Linux filesystem, and ordering slots wrongly would recycle the wrong one.
                    CreatedAt = createdAt ?? info.LastWriteTime,
                    SizeBytes = info.Length
                }, info.LastWriteTime));
            }
            catch (Exception ex)
            {
                _log.LogError($"[SaveManager] Skipping unreadable slot '{Path.GetFileName(file)}'", ex);
            }
        }

        // Names only carry whole seconds, so slots made in the same second tie. Broken by write
        // time, which does have sub-second resolution — without it the order within a second is
        // whatever the directory happened to enumerate, and recycling would drop an arbitrary one.
        return found
            .OrderByDescending(f => f.Slot.CreatedAt)
            .ThenByDescending(f => f.Written)
            .Select(f => f.Slot)
            .ToList();
    }

    public void Delete(SaveSlot slot)
    {
        File.Delete(slot.FilePath);
        _log.Log($"[SaveManager] Deleted slot: {slot.Label}");
    }

    /// <summary>
    /// Keeps the newest <see cref="MaxAutomaticSlots"/> slots of <paramref name="kind"/> and drops
    /// the rest. Counted per kind, and never called for <see cref="SaveSlotKind.Manual"/>.
    /// </summary>
    private void Recycle(string gameId, SaveSlotKind kind)
    {
        if (kind == SaveSlotKind.Manual) return;

        var ofKind = ListSlots(gameId).Where(s => s.Kind == kind).ToList();
        foreach (var old in ofKind.Skip(MaxAutomaticSlots))
            TryDelete(old.FilePath);
    }

    // ── Naming ───────────────────────────────────────────────────────────────

    // <timestamp>__<kind>__<label>.zip — self-describing, so the folder still makes sense to
    // someone digging through it by hand without CMM, which is the situation this feature exists for.
    private const string Separator     = "__";
    private const string TimestampForm = "yyyy-MM-dd_HH-mm-ss";

    private static string BuildFileName(string label, SaveSlotKind kind)
    {
        string safe = Sanitize(string.IsNullOrWhiteSpace(label) ? "unnamed" : label.Trim());
        return $"{DateTime.Now.ToString(TimestampForm)}{Separator}{kind.ToString().ToLowerInvariant()}{Separator}{safe}.zip";
    }

    /// <summary>
    /// Names carry whole seconds, so two slots made in the same second would collide. That is a
    /// curiosity for manual saves and a real problem for the automatic ones: a pre-load snapshot
    /// failing to write aborts the load entirely, and two loads in quick succession is exactly when
    /// someone is undoing a mistake.
    /// </summary>
    private static string Uniquify(string path)
    {
        if (!File.Exists(path)) return path;

        string dir  = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);

        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({n}).zip");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static (string Label, SaveSlotKind Kind, DateTime? CreatedAt) ParseFileName(string fileName)
    {
        string stem  = Path.GetFileNameWithoutExtension(fileName);
        var    parts = stem.Split(Separator, 3);

        if (parts.Length < 3 ||
            !DateTime.TryParseExact(parts[0], TimestampForm, null,
                                    System.Globalization.DateTimeStyles.None, out var when))
            // Anything else — a file the user dropped in, or one from an older layout — is shown as
            // a manual slot under its own name rather than hidden. Hiding it would be the one
            // outcome nobody wants from a folder full of saves.
            return (stem, SaveSlotKind.Manual, null);

        var kind = Enum.TryParse<SaveSlotKind>(parts[1], ignoreCase: true, out var k) ? k : SaveSlotKind.Manual;
        return (parts[2], kind, when);
    }

    private static string Sanitize(string label)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['_']).ToArray();
        return string.Concat(label.Select(c => invalid.Contains(c) ? ' ' : c)).Trim();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads every entry to the end, which makes the zip's per-entry CRC check actually run.
    /// Opening the archive alone only reads the central directory, so a truncated body passes.
    /// </summary>
    private static void VerifyArchive(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var buffer = new byte[81920];

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            using var stream = entry.Open();
            while (stream.Read(buffer, 0, buffer.Length) > 0) { }
        }
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.LogError($"[SaveManager] Could not delete '{path}'", ex); }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
