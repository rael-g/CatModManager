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
    private readonly MockProfileService _mockProfileService;
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
        
        _logService = new LogService();
        
        string appData = Path.Combine(_tempDir, "AppData");
        Directory.CreateDirectory(appData);
        _pathService = new MockPathService { BaseDataPath = appData };
        Directory.CreateDirectory(_pathService.ProfilesPath);

        _mockConfigService = new MockConfigService();
        _mockGameSupportService = new MockGameSupportService();
        _mockStateService = new MockVfsStateService();

        _mockScanner = new MockModScanner();
        _mockProfileService = new MockProfileService();
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
            new CatModManager.Ui.Plugins.AppSessionState(),
            new MockPluginLoader());
    }

    [Fact]
    public async Task Profile_Error_Handling_Coverage()
    {
        var vm = CreateViewModel();
        
        await Task.Delay(200);
        vm.Logs.Clear();

        _mockFileService.ForceExists = true;
        _mockProfileService.ShouldFail = true;

        // ACT: Save fail
        await vm.ProfileManager.SaveProfileCommand.ExecuteAsync("any");
        for (int i = 0; i < 20 && !vm.Logs.Any(l => l.Contains("SAVE ERROR", StringComparison.OrdinalIgnoreCase)); i++) await Task.Delay(100);
        Assert.True(vm.Logs.Any(l => l.Contains("SAVE ERROR", StringComparison.OrdinalIgnoreCase)), "Log should contain SAVE ERROR");

        // ACT: Load fail
        await vm.ProfileManager.LoadProfileCommand.ExecuteAsync("any");
        for (int i = 0; i < 20 && !vm.Logs.Any(l => l.Contains("LOAD ERROR", StringComparison.OrdinalIgnoreCase)); i++) await Task.Delay(100);
        Assert.True(vm.Logs.Any(l => l.Contains("LOAD ERROR", StringComparison.OrdinalIgnoreCase)), "Log should contain LOAD ERROR");
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
    public void DisplayedMods_Sorting_ByPriority()
    {
        var vm = CreateViewModel();
        var mod1 = new Mod("Mod1", "Path1", 0);
        var mod2 = new Mod("Mod2", "Path2", 0);
        
        vm.ModList.AllMods.Add(mod1);
        vm.ModList.AllMods.Add(mod2);

        var displayed = vm.ModList.DisplayedMods.ToList();
        Assert.Equal("Mod1", displayed[0].Name);
        Assert.Equal("Mod2", displayed[1].Name);
    }

    // MOCKS
    private class MockVfsOrchestrationService : IVfsOrchestrationService
    {
        public bool IsMounted { get; private set; }
        public void SetMounted(bool value) => IsMounted = value;

        public Task<OperationResult> MountAsync(MountOptions options) 
        {
            IsMounted = true;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> UnmountAsync() 
        {
            IsMounted = false;
            return Task.FromResult(OperationResult.Success());
        }

        public void RecoverStaleMounts() { }
        public Task ShutdownCleanupAsync() 
        {
            IsMounted = false;
            return Task.CompletedTask;
        }
    }

    private class MockPathService : ICatPathService {
        public string BaseDataPath { get; set; } = "";
        public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
        public string DownloadsPath => Path.Combine(BaseDataPath, "downloads");
        public string GetProfilePath(string n) => Path.Combine(ProfilesPath, n + ".toml");
    }

    private class MockModScanner : IModScanner {
        public Task<IEnumerable<Mod>> ScanDirectoryAsync(string p) => Task.FromResult(Enumerable.Empty<Mod>());
    }
    
    private sealed class NullHardlinkStateStore : IHardlinkStateStore
    {
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
    }

    private class MockProfileService : IProfileService {
        public bool ShouldFail { get; set; }
        public Task SaveProfileAsync(Profile p, string f) => ShouldFail ? Task.FromException(new Exception("forced")) : Task.CompletedTask;
        public Task<Profile?> LoadProfileAsync(string f) => ShouldFail ? Task.FromException<Profile?>(new Exception("forced")) : Task.FromResult<Profile?>(null);
        public Task<IEnumerable<string>> ListProfilesAsync(string d) => Task.FromResult(Enumerable.Empty<string>());
    }

    private class MockProcessService : IProcessService {
        public Task<bool> StartProcessAsync(string f, string a, bool admin = false, bool waitForChildren = true) => Task.FromResult(true);
        public Task OpenFolderAsync(string p) => Task.CompletedTask;
    }

    private class MockModManagementService : IModManagementService {
        public Task<string> InstallModAsync(string s, string t, string? o = null, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult("");
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m, string? o = null, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult(t);
        public Task<string> InstallModToRootAsync(string a, string n, string t, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult(t);
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
        public void MoveDirectory(string fromPath, string targetPath) { }
        public string ReadAllText(string path) => "";
        public void WriteAllText(string path, string contents) { }
        public string[] ReadAllLines(string path) => Array.Empty<string>();
        public void WriteAllLines(string path, string[] contents) { }
        public string[] GetFiles(string path, string pattern, bool rec) => Array.Empty<string>();
        public string[] GetDirectories(string path) => Array.Empty<string>();
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
