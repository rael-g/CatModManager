using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Services;

namespace CatModManager.Tests.Plugins.SaveManager;

public class AutoSaverTests : IDisposable
{
    private readonly string            _tempDir;
    private readonly string            _saves;
    private readonly IPluginLogger     _log;
    private readonly SaveBackupService _backups;
    private readonly AutoSaver         _autoSaver;

    public AutoSaverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_AutoSave_" + Guid.NewGuid().ToString("N"));
        _saves   = Path.Combine(_tempDir, "Saves");
        Directory.CreateDirectory(_saves);
        File.WriteAllText(Path.Combine(_saves, "progress.dat"), "start");

        _log       = Substitute.For<IPluginLogger>();
        _backups   = new SaveBackupService(_tempDir, _log);
        _autoSaver = new AutoSaver(_backups, _log);
    }

    public void Dispose()
    {
        _autoSaver.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    /// <summary>An interval long enough that only the explicit ticks in each test ever fire.</summary>
    private void StartWatching() => _autoSaver.Start("game", _saves, 240);

    private void ChangeSaves(string content)
    {
        File.WriteAllText(Path.Combine(_saves, "progress.dat"), content);
        // The fingerprint reads write time, whose resolution is coarser than a test's.
        File.SetLastWriteTimeUtc(Path.Combine(_saves, "progress.dat"), DateTime.UtcNow.AddSeconds(Random.Shared.Next(1, 10_000)));
    }

    [Fact]
    public async Task Tick_WritesASnapshot_WhenTheSavesHaveChanged()
    {
        StartWatching();
        ChangeSaves("played a while");

        Assert.True(await _autoSaver.TickAsync());
        Assert.Single(_backups.ListSlots("game"), s => s.Kind == SaveSlotKind.Auto);
    }

    /// <summary>
    /// The mechanic that lets the option be left on permanently: an unchanged folder means the game
    /// is not writing saves — idle in a menu, or closed entirely — and a snapshot of it would be a
    /// duplicate that pushes real history out of the five-slot ring.
    /// </summary>
    [Fact]
    public async Task Tick_WritesNothing_WhenTheSavesAreUnchanged()
    {
        StartWatching();

        Assert.False(await _autoSaver.TickAsync());
        Assert.False(await _autoSaver.TickAsync());
        Assert.Empty(_backups.ListSlots("game"));
    }

    [Fact]
    public async Task Tick_WritesOnlyOnce_ForASingleChange()
    {
        StartWatching();
        ChangeSaves("one change");

        Assert.True(await _autoSaver.TickAsync());
        Assert.False(await _autoSaver.TickAsync());

        Assert.Single(_backups.ListSlots("game"));
    }

    [Fact]
    public async Task Snapshots_NeverDisplaceASaveTheUserMade()
    {
        await _backups.CreateAsync("game", _saves, "before the boss");
        StartWatching();

        for (int i = 0; i < 10; i++)
        {
            ChangeSaves($"run {i}");
            await _autoSaver.TickAsync();
        }

        var slots = _backups.ListSlots("game");
        Assert.Equal(5, slots.Count(s => s.Kind == SaveSlotKind.Auto));
        Assert.Contains(slots, s => s.Label == "before the boss");
    }

    [Fact]
    public void Stop_HaltsTheTimer()
    {
        StartWatching();
        Assert.True(_autoSaver.IsRunning);

        _autoSaver.Stop();
        Assert.False(_autoSaver.IsRunning);
    }

    /// <summary>
    /// The user could have saved by hand a moment before switching this on; starting by capturing
    /// the current state means the first tick records a change rather than a duplicate.
    /// </summary>
    [Fact]
    public async Task Starting_DoesNotImmediatelySnapshotWhatIsAlreadyThere()
    {
        StartWatching();
        Assert.False(await _autoSaver.TickAsync());
        Assert.Empty(_backups.ListSlots("game"));
    }
}
