using System.Security.Cryptography;
using System.Text;
using CatModManager.PluginSdk;

namespace CmmPlugin.SaveManager.Services;

/// <summary>
/// Timed snapshots of the live saves, into the automatic ring buffer.
///
/// Two things keep this from being a nuisance. It writes only when the saves actually changed since
/// the last snapshot, so idling in a menu — or leaving the option on with the game closed — costs
/// nothing and does not push useful history out of the five-slot buffer. And it writes as
/// <see cref="SaveSlotKind.Auto"/>, so it can never displace a slot the user made on purpose.
///
/// The change check also removes any need to detect whether the game is running: a game that is not
/// running is not writing saves, which is the same thing from here.
/// </summary>
public class AutoSaver : IDisposable
{
    private readonly SaveBackupService _backups;
    private readonly IPluginLogger     _log;

    private Timer?  _timer;
    private string? _gameId;
    private string? _saveFolder;
    private string? _lastFingerprint;
    private int     _ticking;   // 0/1; a slow save must not overlap the next tick

    public AutoSaver(SaveBackupService backups, IPluginLogger log)
    {
        _backups = backups;
        _log     = log;
    }

    public bool IsRunning => _timer != null;

    /// <summary>Raised after a snapshot is written, so a list showing the slots can catch up.</summary>
    public event Action? SlotWritten;

    public void Start(string gameId, string saveFolder, int intervalMinutes)
    {
        Stop();

        int minutes = Math.Max(GameSaveSettings.MinAutoSaveMinutes, intervalMinutes);
        _gameId     = gameId;
        _saveFolder = saveFolder;

        // Seeded with the current state so the first tick captures a change rather than a duplicate
        // of what the user could already have saved by hand a moment ago.
        _lastFingerprint = Fingerprint(saveFolder);

        var period = TimeSpan.FromMinutes(minutes);
        _timer = new Timer(_ => _ = TickAsync(), null, period, period);

        _log.Log($"[SaveManager] Auto-save on for '{gameId}' every {minutes} min.");
    }

    public void Stop()
    {
        if (_timer == null) return;

        _timer.Dispose();
        _timer = null;
        _log.Log($"[SaveManager] Auto-save off for '{_gameId}'.");
    }

    /// <summary>
    /// One snapshot, if anything changed. Public so it can be driven directly instead of waiting on
    /// a real timer. Returns whether a slot was written.
    /// </summary>
    public async Task<bool> TickAsync()
    {
        if (_gameId == null || _saveFolder == null) return false;

        // A save of a large folder can outlast the interval. Skipping is right: the next tick will
        // pick up whatever changed meanwhile, whereas queueing would fall further behind forever.
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return false;

        try
        {
            string? now = Fingerprint(_saveFolder);
            if (now == null || now == _lastFingerprint) return false;

            var path = await _backups.CreateAsync(_gameId, _saveFolder, "autosave", SaveSlotKind.Auto);
            if (path == null) return false;

            // Only after a successful write, so a failed snapshot is retried next tick instead of
            // being silently treated as done.
            _lastFingerprint = now;
            SlotWritten?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError("[SaveManager] Auto-save failed", ex);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    /// <summary>
    /// A cheap stand-in for "have the saves changed": every file's relative path, length and write
    /// time. Reads no file contents, so it stays fast on a folder the game may be writing to.
    /// </summary>
    private static string? Fingerprint(string saveFolder)
    {
        if (!Directory.Exists(saveFolder)) return null;

        var builder = new StringBuilder();
        foreach (string file in Directory.GetFiles(saveFolder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var info = new FileInfo(file);
                builder.Append(Path.GetRelativePath(saveFolder, file))
                       .Append('|').Append(info.Length)
                       .Append('|').Append(info.LastWriteTimeUtc.Ticks)
                       .Append('\n');
            }
            catch
            {
                // Vanished mid-scan. Skipping it makes the fingerprint differ from last time, which
                // is the honest answer: something changed.
            }
        }

        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public void Dispose() => Stop();
}
