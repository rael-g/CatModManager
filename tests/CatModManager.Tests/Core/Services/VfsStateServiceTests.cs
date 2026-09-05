using System;
using System.IO;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Safe Swap bookkeeping: which folders were moved aside, and putting them back after a crash.
///
/// This suite used to build its services on the real <see cref="CatPathService"/>, so it read and
/// wrote the developer's own database. Two consequences. Every run left a row behind — a hundred
/// accumulated. Worse, <see cref="VfsStateService.RecoverStaleMounts"/> acts on what it loads: it
/// moves and deletes directories. Pointed at the real state, a test run would have performed
/// recovery on a real, currently-mounted game folder, outside any sandbox.
/// </summary>
public class VfsStateServiceTests : IDisposable
{
    private readonly string          _tempDir;
    private readonly TempPathService _paths = new();
    private readonly ILogService     _logService = new LogService("");

    public VfsStateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_Vfs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private VfsStateService NewService() => new(new AppDatabase(_paths), _logService);

    /// <summary>A registered swap has to survive the process, or a crash loses the way back.</summary>
    [Fact]
    public void ARegisteredSwapIsStillThereForTheNextSession()
    {
        string original = Path.Combine(_tempDir, "Data");
        string backup   = Path.Combine(_tempDir, ".Data.bak");
        Directory.CreateDirectory(backup);

        NewService().RegisterMount(original, backup);

        // A fresh instance loads from the database, exactly as a restart after a crash would.
        NewService().RecoverStaleMounts();

        Assert.True(Directory.Exists(original), "The backed-up folder was never restored.");
        Assert.False(Directory.Exists(backup));
    }

    /// <summary>Recovery has to clear the entry, or every later run retries a swap already undone.</summary>
    [Fact]
    public void ARecoveredSwapIsForgotten()
    {
        string original = Path.Combine(_tempDir, "Data");
        string backup   = Path.Combine(_tempDir, ".Data.bak");
        Directory.CreateDirectory(backup);

        NewService().RegisterMount(original, backup);
        NewService().RecoverStaleMounts();

        // If the row survived, this second recovery would find a backup that no longer exists.
        Directory.CreateDirectory(backup);
        NewService().RecoverStaleMounts();

        Assert.True(Directory.Exists(backup), "The entry was not cleared — recovery ran a second time.");
    }

    [Fact]
    public void UnregisteringDropsTheEntry()
    {
        string original = Path.Combine(_tempDir, "Data");
        string backup   = Path.Combine(_tempDir, ".Data.bak");

        var service = NewService();
        service.RegisterMount(original, backup);
        service.UnregisterMount(original);

        Directory.CreateDirectory(backup);
        NewService().RecoverStaleMounts();

        Assert.True(Directory.Exists(backup), "A dropped entry was still acted on.");
    }

    /// <summary>
    /// The third case, which used to have no branch at all: no backup left to restore from. Nothing
    /// ran, the entry never reached the recovered list, and it stayed in the database forever — this
    /// is how a hundred dead rows piled up in a real user's database.
    /// </summary>
    [Fact]
    public void AnEntryWithNothingLeftToRestoreIsDropped()
    {
        string original = Path.Combine(_tempDir, "Data");
        string backup   = Path.Combine(_tempDir, ".Data.bak");

        // Neither exists: the game was uninstalled while the swap was still on the books.
        NewService().RegisterMount(original, backup);
        NewService().RecoverStaleMounts();

        // If the row had survived, this recovery would find the backup and move it into place.
        Directory.CreateDirectory(backup);
        NewService().RecoverStaleMounts();

        Assert.True(Directory.Exists(backup), "The dead entry survived and was acted on later.");
        Assert.False(Directory.Exists(original));
    }

    /// <summary>
    /// The guard on that new branch. An unmounted drive or a Flatpak that did not start makes valid
    /// paths vanish, and the entry is the only record that the swap still has to be undone — so a
    /// missing path is not on its own proof that the entry is garbage.
    /// </summary>
    [Fact]
    public void AnEntryOnAVolumeThatIsNotThereIsKept()
    {
        // Nothing along this path exists, the way an external drive looks while unplugged.
        string offline  = Path.Combine(_tempDir, "unplugged-drive", "Game");
        string original = Path.Combine(offline, "Data");
        string backup   = Path.Combine(offline, ".Data.bak");

        NewService().RegisterMount(original, backup);
        NewService().RecoverStaleMounts();

        // The drive comes back, and with it the backup that recovery has to put in place.
        Directory.CreateDirectory(backup);
        NewService().RecoverStaleMounts();

        Assert.True(Directory.Exists(original), "The entry was dropped while the volume was offline.");
        Assert.False(Directory.Exists(backup));
    }

    /// <summary>
    /// This class writes its database where it says it does. The suite-wide version of this guard
    /// lives in <see cref="TestsStayOutOfTheRealDataDirectory"/>; the one that used to be here
    /// compared against <c>new CatPathService()</c>, which now resolves to the sandbox too and made
    /// the assertion vacuous.
    /// </summary>
    [Fact]
    public void TheSuiteWritesOnlyInsideItsOwnSandbox()
    {
        NewService().RegisterMount(Path.Combine(_tempDir, "orig"), Path.Combine(_tempDir, "back"));

        Assert.True(File.Exists(Path.Combine(_paths.BaseDataPath, "cmm.db")),
                    "The sandbox database was not created where it was supposed to be.");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        _paths.Dispose();
    }
}
