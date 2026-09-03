using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Services;

namespace CatModManager.Tests.Plugins.SaveManager;

public class SaveBackupTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IPluginLogger _log;

    public SaveBackupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_SaveBackup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _log = Substitute.For<IPluginLogger>();
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    private string MakeSaves(string name, params (string File, string Content)[] files)
    {
        string dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        foreach (var (file, content) in files)
            File.WriteAllText(Path.Combine(dir, file), content);
        return dir;
    }

    [Fact]
    public async Task Save_ThenLoad_BringsBackTheOlderSaves()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "chapter one"));
        var service = new SaveBackupService(_tempDir, _log);

        await service.CreateAsync("game", saves, "before the ending");

        File.WriteAllText(Path.Combine(saves, "progress.dat"), "chapter two");

        var slot = service.ListSlots("game").Single(s => s.Label == "before the ending");
        await service.LoadAsync(slot, saves, "game");

        Assert.Equal("chapter one", File.ReadAllText(Path.Combine(saves, "progress.dat")));
    }

    /// <summary>
    /// The whole point of the feature: a slot the user named is theirs until they delete it. The
    /// previous implementation capped every backup at 15 and pruned the oldest, so the save someone
    /// made before an ending would silently disappear after enough launches.
    /// </summary>
    [Fact]
    public async Task ManualSlots_AreNeverRecycled_HoweverManyThereAre()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "x"));
        var service = new SaveBackupService(_tempDir, _log);

        for (int i = 0; i < 25; i++)
            await service.CreateAsync("game", saves, $"slot {i}");

        var manual = service.ListSlots("game").Where(s => s.Kind == SaveSlotKind.Manual).ToList();

        Assert.Equal(25, manual.Count);
        Assert.Contains(manual, s => s.Label == "slot 0");
    }

    [Fact]
    public async Task AutoSlots_RingBufferAtFive_WithoutTouchingManualOnes()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "x"));
        var service = new SaveBackupService(_tempDir, _log);

        await service.CreateAsync("game", saves, "my own save");

        for (int i = 0; i < 12; i++)
            await service.CreateAsync("game", saves, $"auto {i}", SaveSlotKind.Auto);

        var slots = service.ListSlots("game");

        Assert.Equal(5, slots.Count(s => s.Kind == SaveSlotKind.Auto));
        Assert.Contains(slots, s => s.Label == "my own save");
    }

    [Fact]
    public async Task Load_KeepsWhatItIsAboutToOverwrite_AsItsOwnSlot()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "current run"));
        var service = new SaveBackupService(_tempDir, _log);

        await service.CreateAsync("game", saves, "checkpoint");
        File.WriteAllText(Path.Combine(saves, "progress.dat"), "later run");

        var slot = service.ListSlots("game").Single(s => s.Label == "checkpoint");
        await service.LoadAsync(slot, saves, "game");

        var preLoad = service.ListSlots("game").SingleOrDefault(s => s.Kind == SaveSlotKind.PreLoad);
        Assert.NotNull(preLoad);

        // And it holds what was live a moment ago, not the thing that replaced it.
        using var archive = ZipFile.OpenRead(preLoad!.FilePath);
        using var reader = new StreamReader(archive.GetEntry("progress.dat")!.Open());
        Assert.Equal("later run", reader.ReadToEnd());
    }

    /// <summary>
    /// Loading the wrong slot is a mistake worth undoing, so several pre-load snapshots are kept —
    /// but they are made without asking, so they cannot pile up forever either.
    /// </summary>
    [Fact]
    public async Task PreLoadSlots_KeepTheLastFive_SoAMisclickIsStillRecoverable()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "run 0"));
        var service = new SaveBackupService(_tempDir, _log);

        await service.CreateAsync("game", saves, "checkpoint");
        var checkpoint = service.ListSlots("game").Single(s => s.Kind == SaveSlotKind.Manual);

        for (int i = 1; i <= 9; i++)
        {
            File.WriteAllText(Path.Combine(saves, "progress.dat"), $"run {i}");
            await service.LoadAsync(checkpoint, saves, "game");
        }

        var preLoad = service.ListSlots("game").Where(s => s.Kind == SaveSlotKind.PreLoad).ToList();
        Assert.Equal(5, preLoad.Count);

        // The five kept are the most recent ones — the newest holds the state from just before the
        // last load, which is what someone undoing a misclick reaches for first.
        using var archive = ZipFile.OpenRead(preLoad[0].FilePath);
        using var reader = new StreamReader(archive.GetEntry("progress.dat")!.Open());
        Assert.Equal("run 9", reader.ReadToEnd());
    }

    [Fact]
    public async Task TwoSlotsInTheSameSecond_BothSurvive()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "x"));
        var service = new SaveBackupService(_tempDir, _log);

        Assert.NotNull(await service.CreateAsync("game", saves, "same name"));
        Assert.NotNull(await service.CreateAsync("game", saves, "same name"));

        Assert.Equal(2, service.ListSlots("game").Count);
    }

    /// <summary>
    /// The failure that used to cost the saves outright: the old code emptied the folder and only
    /// then extracted, so a bad archive left nothing behind. Loading must be all or nothing.
    /// </summary>
    [Fact]
    public async Task Load_FromACorruptSlot_LeavesTheLiveSavesUntouched()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "do not lose me"));
        var service = new SaveBackupService(_tempDir, _log);

        await service.CreateAsync("game", saves, "checkpoint");
        var slot = service.ListSlots("game").Single();

        // Truncate the archive body, leaving a file that opens but cannot be read through.
        var bytes = File.ReadAllBytes(slot.FilePath);
        File.WriteAllBytes(slot.FilePath, bytes.Take(bytes.Length / 2).ToArray());

        await Assert.ThrowsAnyAsync<Exception>(() => service.LoadAsync(slot, saves, "game"));

        Assert.True(Directory.Exists(saves));
        Assert.Equal("do not lose me", File.ReadAllText(Path.Combine(saves, "progress.dat")));
    }

    /// <summary>
    /// A slot is only named once it is complete, so an interrupted write cannot be mistaken for a
    /// usable save — and cannot displace a real one when the ring buffer recycles.
    /// </summary>
    [Fact]
    public async Task AnInterruptedWrite_DoesNotAppearAsASlot()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "x"));
        var service = new SaveBackupService(_tempDir, _log);
        await service.CreateAsync("game", saves, "real");

        // What a half-finished write leaves behind.
        string folder = service.BackupFolderFor("game");
        File.WriteAllBytes(Path.Combine(folder, "torn.zip.cmm-writing"), [0x50, 0x4B, 0x03, 0x04]);

        Assert.Single(service.ListSlots("game"));
    }

    [Fact]
    public async Task Slots_AreOrderedByTheirOwnTimestamp_NotTheFilesystems()
    {
        string saves = MakeSaves("Saves", ("progress.dat", "x"));
        var service = new SaveBackupService(_tempDir, _log);

        await service.CreateAsync("game", saves, "first");
        await Task.Delay(1100);   // the name carries whole seconds
        await service.CreateAsync("game", saves, "second");

        var slots = service.ListSlots("game");
        Assert.Equal("second", slots[0].Label);
        Assert.Equal("first",  slots[1].Label);
    }
}
