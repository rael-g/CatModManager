using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Ui.ViewModels;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Core.Vfs;
using CatModManager.VirtualFileSystem;
using CatModManager.PluginSdk;
using CatModManager.Tests.Support;

namespace CatModManager.Tests.Ui.ViewModels;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockModScanner _mockScanner;
    private readonly Support.FakeProfileService _mockProfileService;
    private readonly MockFileService _mockFileService;
    private readonly MockProcessService _mockProcessService;
    private readonly MockModManagementService _mockModManagementService;
    private readonly ICatPathService _pathService;
    private readonly ILogService _logService;
    private readonly MockConfigService _mockConfigService;
    private readonly MockGameSupportService _mockGameSupportService;
    private readonly MockVfsStateService _mockStateService;
    private readonly MockVfsOrchestrationService _mockVfsService;

    public MainWindowViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_VmFinal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        
        _logService = new LogService("");
        
        string appData = Path.Combine(_tempDir, "AppData");
        Directory.CreateDirectory(appData);
        _pathService = new MockPathService { BaseDataPath = appData };
        Directory.CreateDirectory(_pathService.ProfilesPath);

        _mockConfigService = new MockConfigService();
        _mockGameSupportService = new MockGameSupportService();
        _mockStateService = new MockVfsStateService();

        _mockScanner = new MockModScanner();
        _mockProfileService = new Support.FakeProfileService();
        _mockFileService = new MockFileService();
        _mockProcessService = new MockProcessService();
        _mockModManagementService = new MockModManagementService();
        _mockVfsService = new MockVfsOrchestrationService();
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            _mockScanner, 
            _mockProfileService, 
            new Support.FakeGameService(),
            _mockModManagementService, 
            _mockProcessService,
            _mockVfsService,
            new GameLaunchService(_mockProcessService, _logService),
            _mockFileService,
            _pathService,
            _logService,
            _mockConfigService,
            _mockGameSupportService,
            new GameDiscoveryService(_mockGameSupportService, Enumerable.Empty<IGameScanner>()),
            new FakeGlobalToolService(),
            new CatModManager.Ui.Plugins.AppSessionState(),
            new MockPluginLoader());
    }

    [Fact]
    public async Task RemoveMod_RefusesToDeleteOutsideTheModsFolder()
    {
        var vm = CreateViewModel();
        await vm.InitialLoadTask;

        string mods = Path.Combine(Path.GetTempPath(), "CMM_RemoveGuard", "mods");
        vm.GameConfig.ModsFolderPath = mods;

        // An install interrupted partway leaves ModRootPath pointing at the source archive, which
        // lives in the downloads folder. Deleting it destroys the archive the user needs to retry.
        Assert.False(vm.IsInsideModsFolder(
            Path.Combine(Path.GetTempPath(), "CMM_RemoveGuard", "downloads", "SomeMod.7z")));

        // A sibling sharing a name prefix is not inside it either — plain string prefix matching
        // would say otherwise.
        Assert.False(vm.IsInsideModsFolder(
            Path.Combine(Path.GetTempPath(), "CMM_RemoveGuard", "mods_backup", "SomeMod")));

        Assert.True(vm.IsInsideModsFolder(Path.Combine(mods, "SomeMod")));
    }

    [Fact]
    public async Task Profile_Error_Handling_Coverage()
    {
        var vm = CreateViewModel();
        await vm.InitialLoadTask;

        // Something has to be open for saving and loading to be attempted at all. Startup no longer
        // conjures a profile when there is no game — a fresh install is meant to be empty.
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        vm.Logs.Clear();

        _mockFileService.ForceExists = true;
        _mockProfileService.ShouldFail = true;

        // ACT: Save fail
        await vm.ProfileManager.SaveProfileCommand.ExecuteAsync(null);
        Assert.True(await WaitForLog(vm, "SAVE ERROR"), "Log should contain SAVE ERROR");

        // ACT: Load fail
        await vm.ProfileManager.LoadProfileAsync(vm.ProfileManager.CurrentProfile!.Id);
        Assert.True(await WaitForLog(vm, "LOAD ERROR"), "Log should contain LOAD ERROR");
    }

    /// <summary>
    /// Polls the log for a line, on a snapshot each time.
    ///
    /// The loop used to call Any() straight on vm.Logs while background work was still appending to
    /// it, and threw "Collection was modified" — intermittently, roughly one full-suite run in four.
    /// A failure that says nothing about the behaviour under test is worse than no test at all,
    /// because it trains you to re-run instead of read.
    /// </summary>
    private static async Task<bool> WaitForLog(MainWindowViewModel vm, string fragment)
    {
        for (int i = 0; i < 20; i++)
        {
            if (vm.Logs.ToArray().Any(l => l.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    [Fact]
    public async Task Shutdown_Cleanup_Logic()
    {
        var vm = CreateViewModel();
        vm.GameConfig.BaseFolderPath = _tempDir;

        await vm.Shutdown();
        Assert.False(vm.Vfs.IsVfsMounted, "VFS should be marked as unmounted after shutdown.");
    }

    [Fact]
    public void MountButton_State_WhenUnmounted()
    {
        var vm = CreateViewModel();
        Assert.False(vm.Vfs.IsVfsMounted);
        Assert.Equal("Mount", vm.Vfs.MountButtonText);
    }

    [Fact]
    public async Task MountButton_State_WhenMounted()
    {
        var vm = CreateViewModel();
        vm.GameConfig.BaseFolderPath = _tempDir;

        // Simulate successful mount in mock
        _mockVfsService.SetMounted(true);

        // We trigger the command (even if it does nothing in mock, the UI state depends on IsMounted)
        await vm.ToggleMountCommand.ExecuteAsync(null);

        Assert.True(vm.Vfs.IsVfsMounted);
        Assert.Equal("Unmount", vm.Vfs.MountButtonText);
    }

    [Fact]
    public void DisplayedMods_AreSortedByPriority_WithTheConflictWinnerLast()
    {
        var vm = CreateViewModel();
        var mod1 = new Mod("Mod1", "Path1", 0);
        var mod2 = new Mod("Mod2", "Path2", 0);

        vm.ModList.AllMods.Add(mod1);
        vm.ModList.AllMods.Add(mod2);

        // The list reads bottom-wins, matching MO2 and the direction plugins load in. This test
        // used to assert the reverse — and did so by naming the rows, which passed only because
        // insertion order happened to line up with the old descending sort.
        var displayed = vm.ModList.DisplayedMods.ToList();
        Assert.Equal(displayed.OrderBy(m => m.Priority), displayed);
        Assert.Equal(displayed.Max(m => m.Priority), displayed.Last().Priority);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
