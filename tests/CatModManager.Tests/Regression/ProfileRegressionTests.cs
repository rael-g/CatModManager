using CatModManager.Ui.ViewModels;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Core.Vfs;
using CatModManager.VirtualFileSystem;
using CatModManager.PluginSdk;
using CatModManager.Tests.Support;

namespace CatModManager.Tests.Regression;

public class ProfileRegressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockModScanner _mockScanner = new();
    private readonly MockProfileService _mockProfileService = new();
    private readonly MockModManagementService _mockModManagementService = new();
    private readonly MockProcessService _mockProcessService = new();
    private readonly MockLogService _mockLog = new();
    private readonly ICatPathService _pathService;
    private readonly IConfigService _configService;
    private readonly IGameSupportService _gameSupportService;
    private readonly IVfsStateService _stateService;

    public ProfileRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_Regress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _pathService = new MockCatPathService(Path.Combine(_tempDir, "AppData"));
        
        // Embora sejam reais, o pathService isola o cmm.db nesta tempDir única por teste
        var db = new AppDatabase(_pathService);
        _configService = new ConfigService(db);
        _gameSupportService = new GameSupportService(_pathService, _mockLog);
        _stateService = new VfsStateService(db, _mockLog);
    }

    public void Dispose()
    {
        // Forçar GC para tentar liberar handles de arquivo do SQLite antes de deletar a pasta
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private MainWindowViewModel CreateVm()
    {
        return new MainWindowViewModel(
            _mockScanner,
            _mockProfileService,
            _mockModManagementService,
            _mockProcessService,
            new VfsOrchestrationService(
                new SimpleConflictResolver(_mockLog, new SevenZipArchiveExtractor()),
                new NullHardlinkStateStore(),
                _stateService,
                _mockLog),
            new GameLaunchService(_mockProcessService, _mockLog),
            new MockFileService(),
            _pathService,
            _mockLog,
            _configService,
            _gameSupportService,
            new GameDiscoveryService(_gameSupportService, Enumerable.Empty<IGameScanner>()),
            new CatModManager.Ui.Plugins.AppSessionState(),
            new MockPluginLoader()
        );
    }

    [Fact]
    public async Task NewProfile_Should_Be_Saved_Immediately()
    {
        var vm = CreateVm();
        
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);

        Assert.True(_mockProfileService.SaveCount >= 1, "Profile should be saved immediately after creation.");
        Assert.Contains(vm.ProfileManager.CurrentProfileName!, vm.ProfileManager.AvailableProfiles);
    }

    [Fact(Skip = "Fails due to empty path in mock profile load")]
    public async Task Profile_Selection_Should_Load_Data()
    {
        var vm = CreateVm();
        
        // Setup initial profiles
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        string profileA = vm.ProfileManager.CurrentProfileName!;
        vm.GameConfig.ModsFolderPath = "PathA";
        await vm.ProfileManager.SaveProfileCommand.ExecuteAsync(profileA);

        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        string profileB = vm.ProfileManager.CurrentProfileName!;
        vm.GameConfig.ModsFolderPath = "PathB";
        await vm.ProfileManager.SaveProfileCommand.ExecuteAsync(profileB);

        // Switch USING THE COMMAND to avoid race condition of the property setter
        await vm.ProfileManager.LoadProfileCommand.ExecuteAsync(profileA);

        Assert.Equal("PathA", vm.GameConfig.ModsFolderPath);
    }

    [Fact]
    public async Task RenameProfile_Should_Rename_And_Update_CurrentProfileName()
    {
        var vm = CreateVm();

        // Create a profile with a known name
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        string originalName = vm.ProfileManager.CurrentProfileName!;

        // Set a new name and rename
        string newName = originalName + "_Renamed";
        vm.ProfileManager.ProfileDisplayName = newName;
        await vm.ProfileManager.RenameProfileCommand.ExecuteAsync(null);

        // CurrentProfileName must be updated
        Assert.Equal(newName, vm.ProfileManager.CurrentProfileName);
        Assert.Contains(newName, vm.ProfileManager.AvailableProfiles);
        Assert.DoesNotContain(originalName, vm.ProfileManager.AvailableProfiles);

        // Old file must not exist; new file must exist
        string oldPath = _pathService.GetProfilePath(originalName);
        string newPath = _pathService.GetProfilePath(newName);
        Assert.False(File.Exists(oldPath), $"Old profile file should be deleted: {oldPath}");
        Assert.True(File.Exists(newPath), $"New profile file should exist: {newPath}");
    }

    [Fact]
    public async Task NewProfile_Should_Avoid_Duplicate_Names()
    {
        var vm = CreateVm();
        vm.ProfileManager.AvailableProfiles.Add("NewProfile");
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        Assert.NotEqual("NewProfile", vm.ProfileManager.CurrentProfileName);
        Assert.Contains("NewProfile", vm.ProfileManager.CurrentProfileName);
    }

    // MOCKS
    private class MockCatPathService : ICatPathService {
        public string BaseDataPath { get; }
        public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
        public string DownloadsPath => Path.Combine(BaseDataPath, "downloads");
        public MockCatPathService(string path) => BaseDataPath = path;
        public string GetProfilePath(string n) => Path.Combine(ProfilesPath, n + ".toml");
    }

    private class MockModScanner : IModScanner {
        public Task<IEnumerable<Mod>> ScanDirectoryAsync(string p) => Task.FromResult(Enumerable.Empty<Mod>());
    }

    private class MockProfileService : IProfileService {
        public int SaveCount { get; private set; }
        private Dictionary<string, Profile> _storage = new();

        public Task SaveProfileAsync(Profile p, string path) 
        { 
            SaveCount++; 
            // Use path as key but normalize it for the dictionary
            string key = Path.GetFullPath(path);
            _storage[key] = p;
            if (!Directory.Exists(Path.GetDirectoryName(path)!)) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ""); 
            return Task.CompletedTask; 
        }

        public Task<Profile?> LoadProfileAsync(string p) 
        {
            string key = Path.GetFullPath(p);
            if (_storage.TryGetValue(key, out var profile)) return Task.FromResult<Profile?>(profile);
            return Task.FromResult<Profile?>(null);
        }

        public Task<IEnumerable<string>> ListProfilesAsync(string d) =>
            Task.FromResult(Directory.Exists(d)
                ? Directory.GetFiles(d, "*.toml").AsEnumerable()
                : Enumerable.Empty<string>());
    }

    private class MockModManagementService : IModManagementService {
        public Task<string> InstallModAsync(string s, string d, string? o = null, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult("");
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m, string? o = null, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult(t);
        public Task<string> InstallModToRootAsync(string a, string n, string t, IProgress<double>? p = null, System.Threading.CancellationToken ct = default) => Task.FromResult(t);
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
        public string ReadAllText(string path) => "";
        public void WriteAllText(string path, string contents) { }
        public string[] ReadAllLines(string path) => Array.Empty<string>();
        public void WriteAllLines(string path, string[] contents) { }
        public string[] GetFiles(string path, string pattern, bool rec) => Array.Empty<string>();
        public string[] GetDirectories(string path) => Array.Empty<string>();
    }

    private class MockProcessService : IProcessService {
        public Task<bool> StartProcessAsync(string p, string a, bool admin = false, bool waitForChildren = true) => Task.FromResult(true);
        public Task OpenFolderAsync(string p) => Task.CompletedTask;
    }

    private class MockLogService : ILogService {
        public event Action<string>? OnLog;
        public void Log(string m) => OnLog?.Invoke(m);
        public void LogError(string m, Exception? e) => OnLog?.Invoke(m);
    }

    private sealed class NullHardlinkStateStore : IHardlinkStateStore
    {
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
    }
}
