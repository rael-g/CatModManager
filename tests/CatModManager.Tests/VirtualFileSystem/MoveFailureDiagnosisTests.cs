using System;
using System.IO;
using System.Linq;
using CatModManager.VirtualFileSystem;
using Xunit;

namespace CatModManager.Tests.VirtualFileSystem;

/// <summary>
/// When setting a game file aside fails, the report has to be worth reading — and it has to be safe.
///
/// Both halves were learned the hard way. A mount failed with nothing but
/// "Could not find file '.Starfield.exe'", naming a backup that was supposed not to exist yet, with
/// no indication of what was being moved where; and the first version of the diagnosis retried the
/// move to test whether the failure was stable, which meant writing to the game folder from inside
/// an error path.
/// </summary>
public class MoveFailureDiagnosisTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "CMM_Diag_" + Guid.NewGuid().ToString("N"));

    public MoveFailureDiagnosisTests() => Directory.CreateDirectory(_dir);

    private string Report(string source, string dest) =>
        (string)typeof(HardlinkDriver)
            .GetMethod("Diagnose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { source, dest })!;

    /// <summary>The whole point: reporting a failure must not change anything on disk.</summary>
    [Fact]
    public void DiagnosingAFailureTouchesNothing()
    {
        string source = Path.Combine(_dir, "Starfield.exe");
        string dest   = Path.Combine(_dir, ".Starfield.exe");
        File.WriteAllText(source, "the real game");

        var before = Directory.GetFileSystemEntries(_dir).OrderBy(x => x).ToArray();
        Report(source, dest);
        var after = Directory.GetFileSystemEntries(_dir).OrderBy(x => x).ToArray();

        Assert.Equal(before, after);
        Assert.Equal("the real game", File.ReadAllText(source));
        Assert.False(File.Exists(dest), "The diagnosis created the backup it was only supposed to describe.");
    }

    /// <summary>
    /// The sharpest version of "touches nothing". A backup already sitting at the destination is a
    /// previous mount's copy of a real game file, and it is the only copy. Retrying the move with
    /// overwrite destroys it — and moving back afterwards does not bring it back, so the loss reads
    /// as a clean directory to anyone checking only which names exist.
    /// </summary>
    [Fact]
    public void AnExistingBackupIsNotDestroyedByDiagnosingTheFailure()
    {
        string source = Path.Combine(_dir, "Starfield.exe");
        string dest   = Path.Combine(_dir, ".Starfield.exe");
        File.WriteAllText(source, "the mod loader");
        File.WriteAllText(dest, "THE ONLY COPY OF THE REAL GAME EXE");

        Report(source, dest);

        Assert.True(File.Exists(dest), "The diagnosis deleted an existing backup.");
        Assert.Equal("THE ONLY COPY OF THE REAL GAME EXE", File.ReadAllText(dest));
        Assert.Equal("the mod loader", File.ReadAllText(source));
    }

    /// <summary>
    /// The sibling listing used a "&lt;stem&gt;*" pattern, which matches no dot-prefixed name — so the
    /// backup files this driver creates were exactly the ones it could never show.
    /// </summary>
    [Fact]
    public void AnExistingDotPrefixedBackupIsVisibleInTheReport()
    {
        string source = Path.Combine(_dir, "Starfield.exe");
        string dest   = Path.Combine(_dir, ".Starfield.exe");
        File.WriteAllText(source, "game");
        File.WriteAllText(dest, "a backup left behind by an earlier mount");

        Assert.Contains(".Starfield.exe", Report(source, dest));
    }

    /// <summary>A directory in the way reads as "does not exist" to File.Exists, and still blocks a rename.</summary>
    [Fact]
    public void ADirectoryOccupyingTheBackupNameIsCalledOut()
    {
        string source = Path.Combine(_dir, "Starfield.exe");
        string dest   = Path.Combine(_dir, ".Starfield.exe");
        File.WriteAllText(source, "game");
        Directory.CreateDirectory(dest);

        Assert.Contains("DIRECTORY", Report(source, dest));
    }

    /// <summary>Invisible characters survive a hex dump and nothing else.</summary>
    [Fact]
    public void PathsAreReportedAsBytesSoOddCharactersShowUp()
    {
        string source = Path.Combine(_dir, "Star​field.exe");   // zero-width space
        string dest   = Path.Combine(_dir, ".Star​field.exe");
        File.WriteAllText(source, "game");

        var report = Report(source, dest);
        Assert.Contains("E2808B", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Diagnosing a failure must never become a second failure.</summary>
    [Fact]
    public void AnUnreadableSourceStillProducesAReport()
    {
        var report = Report(Path.Combine(_dir, "gone.exe"), Path.Combine(_dir, ".gone.exe"));
        Assert.False(string.IsNullOrWhiteSpace(report));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
