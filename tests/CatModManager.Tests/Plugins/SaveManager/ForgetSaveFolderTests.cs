using System;
using System.IO;
using CatModManager.PluginSdk;
using CmmPlugin.SaveManager.Services;
using CmmPlugin.SaveManager.Tabs;
using NSubstitute;
using Xunit;

namespace CatModManager.Tests.Plugins.SaveManager;

/// <summary>
/// "Forget my folder" is the only button on the saves tab that throws a setting away — a folder the
/// user may have hunted down inside a Wine prefix. It used to be called "Auto-detect", read like a
/// free lookup, and stayed clickable even with nothing to discard. This pins the state that decides
/// whether it is armed at all.
/// </summary>
public class ForgetSaveFolderTests : IDisposable
{
    private readonly string                  _dir;
    private readonly SaveManagerSettings     _settings;
    private readonly SaveManagerTabViewModel _vm;

    public ForgetSaveFolderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CMM_Saves_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var log = Substitute.For<IPluginLogger>();

        var state = Substitute.For<IModManagerState>();
        state.GameId.Returns("skyrimspecialedition");

        _settings = new SaveManagerSettings(_dir, log);
        var backups = new SaveBackupService(_dir, log);

        _vm = new SaveManagerTabViewModel(
            new SaveDetector(log, new WindowsUserFolders(new PhysicalFileService(), log)),
            backups, _settings, new AutoSaver(backups, log), state, log);
    }

    [Fact]
    public void WithNoFolderChosenThereIsNothingToForget()
    {
        _vm.Refresh();

        Assert.False(_vm.HasSaveFolderOverride);
    }

    [Fact]
    public void ChoosingAFolderIsWhatArmsIt()
    {
        _vm.SetSaveFolder(_dir);

        Assert.True(_vm.HasSaveFolderOverride);
    }

    [Fact]
    public void ForgettingDisarmsItAgain()
    {
        _vm.SetSaveFolder(_dir);

        _vm.ClearSaveFolderOverride();

        Assert.False(_vm.HasSaveFolderOverride);
        Assert.Null(_settings.For("skyrimspecialedition").SaveFolder);
    }

    /// <summary>
    /// A chosen folder that has since disappeared is still a choice on record — and the case where
    /// forgetting it is most obviously what the user wants. Reading this from the resolved folder
    /// instead of the stored setting would leave the button dead exactly then.
    /// </summary>
    [Fact]
    public void AChosenFolderThatVanishedIsStillSomethingToForget()
    {
        string gone = Path.Combine(_dir, "on-a-disk-that-is-not-here");
        Directory.CreateDirectory(gone);
        _vm.SetSaveFolder(gone);
        Directory.Delete(gone);

        _vm.Refresh();

        Assert.True(_vm.HasSaveFolderOverride);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
