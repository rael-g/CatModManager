using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.ViewModels;
using CatModManager.Core.Vfs;
using CatModManager.VirtualFileSystem;

namespace CatModManager.Tests;

public class BugReproductionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogService _logService;
    private readonly MockPathService _pathService;

    public BugReproductionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_Bugs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logService = new LogService();
        _pathService = new MockPathService { BaseDataPath = Path.Combine(_tempDir, "AppData") };
        Directory.CreateDirectory(_pathService.BaseDataPath);
        Directory.CreateDirectory(_pathService.ProfilesPath);
    }

    [Fact]
    public async Task Installation_Should_Use_Physical_Path_And_Prevent_Duplicates()
    {
        // SETUP
        var mockScanner = new MockModScanner();
        var mockProfileService = new MockProfileService();
        var mockDriverService = new MockDriverService();
        var mockModManagementService = new MockModManagementService();
        var mockProcessService = new MockProcessService();
        var db = new AppDatabase(_pathService);
        var stateService = new VfsStateService(db, _logService);
        var configService = new ConfigService(db);
        var gameSupportService = new GameSupportService(_pathService, _logService);

        var vm = new MainWindowViewModel(
            mockScanner, 
            mockProfileService, 
            mockDriverService, 
            mockModManagementService, 
            mockProcessService,
            new VfsOrchestrationService(
                new SimpleConflictResolver(_logService),
                new NullHardlinkStateStore(),
                new NoBaseSwapStrategy(),
                stateService,
                _logService,
                new NullRootSwapService()),
            new GameLaunchService(mockProcessService, _logService),
            new MockFileService(),
            _pathService,
            _logService,
            configService,
            gameSupportService,
            new GameDiscoveryService(gameSupportService),
            new NullRootSwapService(),
            new CatModManager.Ui.Plugins.AppSessionState(),
            new MockPluginLoader());

        string modsDir = Path.Combine(_tempDir, "Mods");
        Directory.CreateDirectory(modsDir);
        vm.GameConfig.ModsFolderPath = modsDir;
        
        string archivePath = Path.Combine(_tempDir, "AwesomeMod_v1.zip");
        string expectedInstallPath = Path.Combine(modsDir, "AwesomeMod_v1");
        mockModManagementService.ResultPath = expectedInstallPath;

        // ACT 1: Install first time
        await vm.AddModCommand.ExecuteAsync(archivePath);

        // ASSERT 1
        Assert.Single(vm.ModList.AllMods);
        var installed = vm.ModList.AllMods[0];
        Assert.Equal("AwesomeMod_v1", installed.Name);
        Assert.Equal(expectedInstallPath, installed.RootPath);
        Assert.NotEqual("default", Path.GetFileName(Path.GetDirectoryName(installed.RootPath)));

        // ACT 2: Re-install same mod (update simulation)
        mockScanner.NextResult = new Mod("Awesome Mod", expectedInstallPath, 1) { Version = "2.0.0", Category = "Visuals" };
        await vm.AddModCommand.ExecuteAsync(archivePath);

        // ASSERT 2: No duplicates, metadata updated
        Assert.Single(vm.ModList.AllMods);
        Assert.Equal("Awesome Mod", installed.Name);
        Assert.Equal("2.0.0", installed.Version);
        Assert.Equal("Visuals", installed.Category);
    }

    [Fact]
    public void VfsStateService_Should_Clear_Hidden_Attribute_On_Recovery()
    {
        string original = Path.Combine(_tempDir, "OriginalGameFolder");
        string backup = Path.Combine(_tempDir, "BackupFolder");
        Directory.CreateDirectory(backup);
        
        var di = new DirectoryInfo(backup);
        di.Attributes |= FileAttributes.Hidden;
        
        var service = new VfsStateService(new AppDatabase(_pathService), _logService);
        service.RegisterMount(original, backup);

        service.RecoverStaleMounts();

        Assert.True(Directory.Exists(original), "Original folder should be restored.");
        var restoredDi = new DirectoryInfo(original);
        Assert.False(restoredDi.Attributes.HasFlag(FileAttributes.Hidden), "Restored folder should NOT be hidden.");
    }

    [Fact]
    public async Task SimpleConflictResolver_Should_Not_Exit_Immediately_If_ForbiddenPath_Is_Root()
    {
        var resolver = new SimpleConflictResolver(_logService);
        string baseDir = Path.Combine(_tempDir, "BaseFolder");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "game.exe"), "content");

        resolver.ForbiddenPath = baseDir;

        var result = await Task.Run(() => resolver.ResolveConflicts(new List<Mod>(), baseDir));

        Assert.NotEmpty(result);
        Assert.True(result.ContainsKey("game.exe"), "Should have scanned the root folder even if it's the forbidden path.");
    }

    [Fact]
    public async Task SimpleConflictResolver_Should_Prevent_Infinite_Recursion_If_Mounted_Inside()
    {
        var resolver = new SimpleConflictResolver(_logService);
        string baseDir = Path.Combine(_tempDir, "GameRoot");
        string mountPoint = Path.Combine(baseDir, "Data"); 
        Directory.CreateDirectory(mountPoint);
        
        File.WriteAllText(Path.Combine(baseDir, "root.txt"), "root");
        File.WriteAllText(Path.Combine(mountPoint, "nested.txt"), "nested");

        resolver.ForbiddenPath = mountPoint;

        var result = await Task.Run(() => resolver.ResolveConflicts(new List<Mod>(), baseDir));

        Assert.True(result.ContainsKey("root.txt"));
        Assert.False(result.ContainsKey("Data\\nested.txt"), "Should NOT have scanned inside the forbidden mount point.");
    }

    [Fact(Timeout = 10000)]
    public async Task Shutdown_Should_Complete_Without_Deadlock_Simulation()
    {
        var mockScanner = new MockModScanner();
        var mockProfileService = new MockProfileService();
        var mockDriverService = new MockDriverService();
        var mockModManagementService = new MockModManagementService();
        var mockProcessService = new MockProcessService();
        var stateService = new VfsStateService(new AppDatabase(_pathService), _logService);
        var configService = new ConfigService(new AppDatabase(_pathService));
        var gameSupportService = new GameSupportService(_pathService, _logService);

        var vm = new MainWindowViewModel(
            mockScanner, 
            mockProfileService, 
            mockDriverService, 
            mockModManagementService, 
            mockProcessService,
            new VfsOrchestrationService(
                new SimpleConflictResolver(_logService),
                new NullHardlinkStateStore(),
                new NoBaseSwapStrategy(),
                stateService,
                _logService,
                new NullRootSwapService()),
            new GameLaunchService(mockProcessService, _logService),
            new MockFileService(),
            _pathService,
            _logService,
            configService,
            gameSupportService,
            new GameDiscoveryService(gameSupportService),
            new NullRootSwapService(),
            new CatModManager.Ui.Plugins.AppSessionState(),
            new MockPluginLoader());

        // ACT
        await Task.Run(() => vm.Shutdown());

        // ASSERT
        Assert.True(true); 
    }

    private class MockPathService : ICatPathService
    {
        public string BaseDataPath { get; set; } = "";
        public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
        public string DownloadsPath => Path.Combine(BaseDataPath, "downloads");
        public string GetProfilePath(string name) => Path.Combine(ProfilesPath, name + ".toml");
    }

    private class MockModScanner : IModScanner {
        public Mod? NextResult { get; set; }
        public Task<IEnumerable<Mod>> ScanDirectoryAsync(string p) => 
            Task.FromResult(NextResult != null ? new List<Mod> { NextResult }.AsEnumerable() : Enumerable.Empty<Mod>());
    }
    private class MockProfileService : IProfileService {
        public Task SaveProfileAsync(Profile p, string f) => Task.CompletedTask;
        public Task<Profile?> LoadProfileAsync(string f) => Task.FromResult<Profile?>(null);
        public Task<IEnumerable<string>> ListProfilesAsync(string d) => Task.FromResult(Enumerable.Empty<string>());
    }
    private class NullRootSwapService : IRootSwapService
    {
        public Task DeployAsync(IEnumerable<Mod> activeMods, string gameFolder) => Task.CompletedTask;
        public Task UndeployAsync(string gameFolder) => Task.CompletedTask;
        public Task UndeployModAsync(string modRootPath, string gameFolder) => Task.CompletedTask;
        public void RecoverStaleDeployments() { }
        public bool HasDeployedFiles(string gameFolder) => false;
    }
    private class MockDriverService : IDriverService {
        public bool IsDriverInstalled() => true;
    }
    private class MockProcessService : IProcessService {
        public Task<bool> StartProcessAsync(string f, string a, bool admin = false, bool waitForChildren = true) => Task.FromResult(true);
        public Task OpenFolderAsync(string p) => Task.CompletedTask;
    }
    private class MockModManagementService : IModManagementService {
        public string ResultPath { get; set; } = "";
        public Task<string> InstallModAsync(string s, string t, string? o = null, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult(ResultPath);
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m, string? o = null, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult(ResultPath);
        public Task<string> InstallModToRootAsync(string a, string n, string t, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult(ResultPath);
    }
    private class MockFileService : IFileService {
        public bool FileExists(string p) => true;
        public bool DirectoryExists(string p) => true;
        public void CreateDirectory(string p) { }
        public void CopyFile(string s, string d, bool o) { }
        public void CopyDirectory(string s, string d) { }
        public void DeleteFile(string p) { }
        public void DeleteDirectory(string p, bool r) { }
        public void MoveDirectory(string fromPath, string targetPath) { }

    }
    private sealed class NullHardlinkStateStore : IHardlinkStateStore
    {
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
