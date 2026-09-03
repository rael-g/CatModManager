using System;
using System.IO;
using System.Text;

namespace CatModManager.Core.Services;

/// <summary>
/// Logs to the UI, to stdout, and to a file that outlives the process.
///
/// The file matters: when something goes wrong mid-session — a power cut, a crash, a mount that
/// fails and leaves the game folder half-swapped — the in-memory log dies with the app, and the only
/// account of what happened is whatever the user managed to read off the screen. Diagnosing the
/// state of someone's game install after the fact needs a record on disk.
/// </summary>
public class LogService : ILogService
{
    /// <summary>Rotate at 5 MB, keeping one previous file. Enough to cover several sessions.</summary>
    private const long MaxLogBytes = 5 * 1024 * 1024;

    private readonly string? _logFilePath;
    private readonly object  _fileLock = new();

    public event Action<string>? OnLog;

    /// <param name="logFilePath">
    /// Where to append. Defaults to <c>&lt;LocalAppData&gt;/catmodmanager/logs/cmm.log</c>. Pass an
    /// empty string to disable file logging — tests do this so they don't write to the user's real
    /// log.
    /// </param>
    public LogService(string? logFilePath = null)
    {
        _logFilePath = logFilePath ?? DefaultLogPath();

        if (string.IsNullOrEmpty(_logFilePath))
        {
            _logFilePath = null;
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
            Rotate();
            AppendLine($"===== session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }
        catch
        {
            // A log we cannot write is not a reason to refuse to start.
            _logFilePath = null;
        }
    }

    private static string DefaultLogPath()
    {
        try
        {
            // Through CatPathService, not by spelling the location out again: this used to compute
            // <LocalAppData>/catmodmanager itself, so it kept writing to the real log no matter
            // where the rest of the application had been pointed.
            return Path.Combine(CatPathService.ResolveDataHome(), "logs", "cmm.log");
        }
        catch { return ""; }
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        Console.WriteLine(line);
        OnLog?.Invoke(line);
        AppendLine(line);
    }

    /// <summary>
    /// Reports a failure. The UI and stdout get the short form; the file additionally gets the
    /// exception type and stack trace, because "Could not find file 'x'" without a stack tells you
    /// what happened and nothing about where.
    /// </summary>
    public void LogError(string message, Exception? ex = null)
    {
        var summary = $"ERROR: {message}";
        if (ex != null) summary += $" | EX: {ex.Message}";

        var line = $"[{DateTime.Now:HH:mm:ss}] {summary}";
        Console.WriteLine(line);
        OnLog?.Invoke(line);

        if (ex == null) { AppendLine(line); return; }

        var detail = new StringBuilder(line);
        for (var current = ex; current != null; current = current.InnerException)
        {
            detail.AppendLine();
            detail.Append("    ").Append(current.GetType().FullName).Append(": ").Append(current.Message);
            if (!string.IsNullOrEmpty(current.StackTrace))
                detail.AppendLine().Append(current.StackTrace);
        }

        AppendLine(detail.ToString());
    }

    private void AppendLine(string line)
    {
        if (_logFilePath == null) return;

        lock (_fileLock)
        {
            try { File.AppendAllText(_logFilePath, line + Environment.NewLine); }
            catch { /* logging must never be the thing that breaks a session */ }
        }
    }

    /// <summary>Moves the current log aside once it grows past <see cref="MaxLogBytes"/>.</summary>
    private void Rotate()
    {
        if (_logFilePath == null || !File.Exists(_logFilePath)) return;
        if (new FileInfo(_logFilePath).Length < MaxLogBytes) return;

        try { File.Move(_logFilePath, _logFilePath + ".1", overwrite: true); }
        catch { /* keep appending to the current file rather than losing it */ }
    }
}
