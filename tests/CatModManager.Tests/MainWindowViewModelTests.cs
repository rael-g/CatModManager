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

namespace CatModManager.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockModScanner _mockScanner;
    private readonly MockVfs _mockVfs;
    private readonly MockProfileService _mockProfileService;
    private readonly MockFileService _mockFileService;
    private readonly MockDriverService _mockDriverService;
    private readonly MockProcessService _mockProcessService;
    private readonly MockModManagementService _mockModManagementService;
    private readonly ICatPathService _pathService;
    private readonly ILogService _logService;
    private readonly MockConfigService _mockConfigService;
    private readonly MockGameSupportService _mockGameSupportService;
    private readonly MockVfsStateService _mockStateService;

    public MainWindowViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_VmFinal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        
        _logService = new LogService();
        
        string appData = Path.Combine(_tempDir, "AppData");
        Directory.CreateDirectory(appData);
        _pathService = new MockPathService { BaseDataPath = appData };
        Directory.CreateDirectory(_pathService.ProfilesPath);

        _mockConfigService = new MockConfigService();
        _mockGameSupportService = new MockGameSupportService();
        _mockStateService = new MockVfsStateService();

        _mockScanner = new MockModScanner();
        _mockVfs = new MockVfs();
        _mockProfileService = new MockProfileService();
        _mockFileService = new MockFileService();
        _mockDriverService = new MockDriverService();
        _mockProcessService = new MockProcessService();
        _mockModManagementService = new MockModManagementService();
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            _mockScanner, 
            _mockProfileService, 
            _mockDriverService, 
            _mockModManagementService, 
            _mockProcessService,
            new VfsOrchestrationService(_mockVfs, _mockStateService, _mockDriverService, _logService, new NullRootSwapService()),
            new GameLaunchService(_mockProcessService, _logService),
            _mockFileService,
            _pathService,
            _logService,
            _mockConfigService,
            _mockGameSupportService,
            new GameDiscoveryService(_mockGameSupportService),
            new NullRootSwapService());
    }

    [Fact]
    public async Task Profile_Error_Handling_Coverage()
    {
        var vm = CreateViewModel();
        
        await Task.Delay(200);
        vm.Logs.Clear();

        _mockFileService.ForceExists = true;
        _mockProfileService.ShouldFail = true;

        await vm.SaveProfileCommand.ExecuteAsync("any");
        
        // Poll for log arrival (max 1s)
        for (int i = 0; i < 10 && !vm.Logs.Any(l => l.Contains("SAVE ERROR", StringComparison.OrdinalIgnoreCase)); i++)
            await Task.Delay(100);

        Assert.True(vm.Logs.Any(l => l.Contains("SAVE ERROR", StringComparison.OrdinalIgnoreCase)), "Log should contain SAVE ERROR");

        await vm.LoadProfileCommand.ExecuteAsync("any");

        for (int i = 0; i < 10 && !vm.Logs.Any(l => l.Contains("LOAD ERROR", StringComparison.OrdinalIgnoreCase)); i++)
            await Task.Delay(100);

        Assert.True(vm.Logs.Any(l => l.Contains("LOAD ERROR", StringComparison.OrdinalIgnoreCase)), "Log should contain LOAD ERROR");
    }

    [Fact]
    public async Task Shutdown_Cleanup_Logic()
    {
        var vm = CreateViewModel();
        vm.BaseFolderPath = _tempDir;
        
        _mockVfs.IsMounted = true;

        vm.Shutdown();
        Assert.False(vm.IsVfsMounted, "VFS should be marked as unmounted after shutdown.");
    }

    [Fact]
    public void MountButton_State_WhenUnmounted()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsVfsMounted);
        Assert.Equal("Mount", vm.MountButtonText);
    }

    [Fact]
    public async Task MountButton_State_WhenMounted()
    {
        var vm = CreateViewModel();
        vm.BaseFolderPath = _tempDir;
        vm.DataSubFolder = "Data";
        
        await vm.ToggleMountCommand.ExecuteAsync(null);
        
        Assert.True(vm.IsVfsMounted);
        Assert.Equal("Unmount", vm.MountButtonText);
    }

    [Fact]
    public void DisplayedMods_Sorting_ByPriority()
    {
        var vm = CreateViewModel();
        var mod1 = new Mod("Mod1", "Path1", 10);
        var mod2 = new Mod("Mod2", "Path2", 20);
        
        vm.AllMods.Add(mod1);
        vm.AllMods.Add(mod2);

        var displayed = vm.DisplayedMods.ToList();
        Assert.Equal("Mod2", displayed[0].Name);
        Assert.Equal("Mod1", displayed[1].Name);
    }

    // MOCKS
    private class MockPathService : ICatPathService {
        public string BaseDataPath { get; set; } = "";
        public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
        public string GetProfilePath(string n) => Path.Combine(ProfilesPath, n + ".toml");
    }
    
    private class MockModScanner : IModScanner {
        public Task<IEnumerable<Mod>> ScanDirectoryAsync(string p) => Task.FromResult(Enumerable.Empty<Mod>());
    }
    
    private class NullRootSwapService : IRootSwapService
    {
        public Task DeployAsync(IEnumerable<Mod> activeMods, string gameFolder) => Task.CompletedTask;
        public Task UndeployAsync(string gameFolder) => Task.CompletedTask;
        public Task UndeployModAsync(string modRootPath, string gameFolder) => Task.CompletedTask;
        public void RecoverStaleDeployments() { }
        public bool HasDeployedFiles(string gameFolder) => false;
    }
    private class MockVfs : IVirtualFileSystem {
        public bool IsMounted { get; set; }
        public event EventHandler<string>? ErrorOccurred;
        public void Mount(string m, List<Mod> a, string? b, string? v, bool s) => IsMounted = true;
        public void Mount(string m, List<Mod> a) => IsMounted = true;
        public void Mount(string m, List<Mod> a, string? b) => IsMounted = true;
        public void Mount(string m, List<Mod> a, string? b, string? v) => IsMounted = true;
        public void Unmount() => IsMounted = false;
        public void Dispose() { }
    }

    private class MockProfileService : IProfileService {
        public bool ShouldFail { get; set; }
        public Task SaveProfileAsync(Profile p, string f) => ShouldFail ? throw new Exception("forced") : Task.CompletedTask;
        public Task<Profile?> LoadProfileAsync(string f) => ShouldFail ? throw new Exception("forced") : Task.FromResult<Profile?>(null);
        public Task<IEnumerable<string>> ListProfilesAsync(string d) => Task.FromResult(Enumerable.Empty<string>());
    }

    private class MockDriverService : IDriverService {
        public bool IsDriverInstalled() => true;
    }

    private class MockProcessService : IProcessService {
        public Task<bool> StartProcessAsync(string f, string a, bool admin) => Task.FromResult(true);
        public Task OpenFolderAsync(string p) => Task.CompletedTask;
    }

    private class MockModManagementService : IModManagementService {
        public Task<string> InstallModAsync(string s, string t) => Task.FromResult("");
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m) => Task.FromResult(t);
    }

    private class MockFileService : IFileService {
        public bool ForceExists { get; set; }
        public bool FileExists(string p) => ForceExists;
        public bool DirectoryExists(string p) => ForceExists;
        public void CreateDirectory(string p) { }
        public void CopyFile(string s, string d, bool o) { }
        public void CopyDirectory(string s, string d) { }
        public void DeleteFile(string p) { }
        public void DeleteDirectory(string p, bool r) { }
    }

    private class MockConfigService : IConfigService {
        public AppConfig Current { get; } = new();
        public void Save() { }
        public void Load() { }
    }

    private class MockGameSupportService : IGameSupportService {
        public IGameSupport Default => new GenericGameSupport();
        public void RefreshSupports() { }
        public IEnumerable<IGameSupport> GetAllSupports() => new[] { Default };
        public IGameSupport GetSupportById(string? id) => Default;
        public IGameSupport DetectSupport(string? path) => Default;
    }

    private class MockVfsStateService : IVfsStateService {
        public void RegisterMount(string o, string b) { }
        public void UnregisterMount(string o) { }
        public void RecoverStaleMounts() { }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
