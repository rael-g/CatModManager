using System;
using System.IO;
using CatModManager.Core.Services;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// The log has to survive the process. A power cut during a mount used to leave nothing behind but
/// whatever the user had managed to read off the status bar — and mount failures never even reached
/// the log, because they were returned as a result and not reported.
/// </summary>
public class PersistentLogTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "CMM_Log_" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_dir, "logs", "cmm.log");

    public PersistentLogTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void MessagesSurviveTheProcessThatWroteThem()
    {
        var log = new LogService(LogPath);
        log.Log("Mounting 28 mod(s)");

        Assert.Contains("Mounting 28 mod(s)", File.ReadAllText(LogPath));
    }

    /// <summary>
    /// The whole reason for this work: the message says *what* failed, the stack says *where*.
    /// Only the message was ever kept.
    /// </summary>
    [Fact]
    public void AnErrorRecordsTheStackTraceNotJustTheMessage()
    {
        var log = new LogService(LogPath);

        Exception captured;
        try { throw new FileNotFoundException("Could not find file '/games/Starfield/.Starfield.exe'"); }
        catch (Exception ex) { captured = ex; }

        log.LogError("Mount failed", captured);

        var text = File.ReadAllText(LogPath);
        Assert.Contains(".Starfield.exe", text);
        Assert.Contains(nameof(FileNotFoundException), text);
        Assert.Contains(nameof(AnErrorRecordsTheStackTraceNotJustTheMessage), text);
    }

    [Fact]
    public void InnerExceptionsAreRecordedToo()
    {
        var log = new LogService(LogPath);

        Exception captured;
        try
        {
            try { throw new UnauthorizedAccessException("access denied to backup"); }
            catch (Exception inner) { throw new IOException("Cannot persist state", inner); }
        }
        catch (Exception ex) { captured = ex; }

        log.LogError("Mount failed", captured);

        var text = File.ReadAllText(LogPath);
        Assert.Contains("Cannot persist state", text);
        Assert.Contains("access denied to backup", text);
    }

    /// <summary>A second run must not erase the evidence from the run that crashed.</summary>
    [Fact]
    public void ANewSessionAppendsRatherThanTruncating()
    {
        new LogService(LogPath).Log("from the session that crashed");
        new LogService(LogPath).Log("from the session after it");

        var text = File.ReadAllText(LogPath);
        Assert.Contains("from the session that crashed", text);
        Assert.Contains("from the session after it", text);
    }

    /// <summary>Tests and headless runs opt out, so they never touch the user's real log.</summary>
    [Fact]
    public void AnEmptyPathDisablesFileLoggingWithoutFailing()
    {
        var log = new LogService("");
        log.Log("goes nowhere");
        log.LogError("also nowhere", new Exception("boom"));

        Assert.False(Directory.Exists(Path.Combine(_dir, "logs")));
    }

    /// <summary>An unwritable path is not a reason to refuse to start.</summary>
    [Fact]
    public void AnUnwritableLogPathIsSurvivable()
    {
        var log = new LogService("/proc/cmm-cannot-write-here/cmm.log");

        log.Log("still works");
        log.LogError("still works", new Exception("boom"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
