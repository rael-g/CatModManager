using System;
using System.IO;
using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Tests.Support;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Choosing a mod's mount point, and the one rule that decides whether it is ever deployed.
///
/// "Use the default" is stored as null, never as the default's own id: the mount only treats null
/// that way, so a mod carrying a concrete id that resolves to nothing matches no mount point at all
/// — installed, listed as enabled, and silently absent from the game folder. That is not a
/// hypothetical; it is the "Default" literal bug, and it shipped once already.
///
/// The rule used to live in MainWindow's click handler, where no test could reach it.
/// </summary>
public class MountPointAssignmentTests : IDisposable
{
    private readonly string             _tempDir;
    private readonly MainWindowViewModel _vm;

    public MountPointAssignmentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_MountAssign_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        string appData = Path.Combine(_tempDir, "AppData");
        Directory.CreateDirectory(appData);
        var pathService = new MockPathService { BaseDataPath = appData };
        Directory.CreateDirectory(pathService.ProfilesPath);

        var log        = new LogService("");
        var processes  = new MockProcessService();
        var supports   = new MockGameSupportService();

        _vm = new MainWindowViewModel(
            new MockModScanner(),
            new FakeProfileService(),
            new FakeGameService(),
            new MockModManagementService(),
            processes,
            new MockVfsOrchestrationService(),
            new GameLaunchService(processes, log),
            new MockFileService(),
            pathService,
            log,
            new MockConfigService(),
            supports,
            new GameDiscoveryService(supports, Enumerable.Empty<IGameScanner>()),
            new FakeGlobalToolService(),
            new CatModManager.Ui.Plugins.AppSessionState(),
            new MockPluginLoader());

        // Two mount points, no game-defined ones: the first is what "default" means here.
        _vm.GameConfig.UserMountPoints.Add(new MountPointDef { Id = "data",     Name = "Data",     Path = "Data"     });
        _vm.GameConfig.UserMountPoints.Add(new MountPointDef { Id = "override", Name = "Override", Path = "Override" });
    }

    private Mod SelectAMod()
    {
        var mod = new Mod { Name = "Test Mod", ModRootPath = Path.Combine(_tempDir, "mod") };
        _vm.ModList.AllMods.Add(mod);
        _vm.ModList.SelectedMod = mod;
        return mod;
    }

    [Fact]
    public void PickingTheDefaultStoresNullRatherThanItsId()
    {
        var mod = SelectAMod();

        _vm.AssignMountPointToSelectedMod("data");

        Assert.Null(mod.MountPointId);
    }

    [Fact]
    public void PickingAnyOtherMountPointStoresItsId()
    {
        var mod = SelectAMod();

        _vm.AssignMountPointToSelectedMod("override");

        Assert.Equal("override", mod.MountPointId);
    }

    /// <summary>
    /// Ids come from a TOML the user can edit, so the comparison must not depend on how the id was
    /// typed — a mount point declared "Data" and chosen as "data" is the same mount point.
    /// </summary>
    [Fact]
    public void TheDefaultIsRecognisedRegardlessOfCasing()
    {
        var mod = SelectAMod();

        _vm.AssignMountPointToSelectedMod("DATA");

        Assert.Null(mod.MountPointId);
    }

    [Fact]
    public void ChoosingAMountPointUpdatesTheNameShownOnTheRow()
    {
        var mod = SelectAMod();

        _vm.AssignMountPointToSelectedMod("override");

        Assert.Equal("Override", mod.MountPointDisplayName);
    }

    /// <summary>The default reads as "no mount point of its own", so the row shows nothing.</summary>
    [Fact]
    public void ChoosingTheDefaultClearsTheNameShownOnTheRow()
    {
        var mod = SelectAMod();
        _vm.AssignMountPointToSelectedMod("override");

        _vm.AssignMountPointToSelectedMod("data");

        Assert.Null(mod.MountPointDisplayName);
    }

    [Fact]
    public void WithNoModSelectedNothingHappens()
    {
        _vm.ModList.SelectedMod = null;

        _vm.AssignMountPointToSelectedMod("override");   // must not throw
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }
}
