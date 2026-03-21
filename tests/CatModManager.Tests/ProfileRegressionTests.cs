using CatModManager.Ui.ViewModels;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Core.Vfs;

namespace CatModManager.Tests;

public class ProfileRegressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockModScanner _mockScanner = new();
    private readonly MockProfileService _mockProfileService = new();
    private readonly MockDriverService _mockDriverService = new();
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
            _mockDriverService,
            _mockModManagementService,
            _mockProcessService,
            new VfsOrchestrationService(new NullVfs(), _stateService, _mockDriverService, _mockLog, new NullRootSwapService()),
            new GameLaunchService(_mockProcessService, _mockLog),
            new MockFileService(),
            _pathService,
            _mockLog,
            _configService,
            _gameSupportService,
            new GameDiscoveryService(_gameSupportService),
            new NullRootSwapService()
        );
    }

    [Fact]
    public async Task NewProfile_Should_Be_Saved_Immediately()
    {
        var vm = CreateVm();
        
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.True(_mockProfileService.SaveCount >= 1, "Profile should be saved immediately after creation.");
        Assert.Contains(vm.CurrentProfileName!, vm.AvailableProfiles);
    }

    [Fact]
    public async Task Profile_Selection_Should_Load_Data()
    {
        var vm = CreateVm();
        
        // Setup initial profiles
        await vm.NewProfileCommand.ExecuteAsync(null);
        string profileA = vm.CurrentProfileName!;
        vm.ModsFolderPath = "PathA";
        await vm.SaveProfileCommand.ExecuteAsync(profileA);

        await vm.NewProfileCommand.ExecuteAsync(null);
        string profileB = vm.CurrentProfileName!;
        vm.ModsFolderPath = "PathB";
        await vm.SaveProfileCommand.ExecuteAsync(profileB);

        // Switch USING THE COMMAND to avoid race condition of the property setter
        await vm.LoadProfileCommand.ExecuteAsync(profileA);

        Assert.Equal("PathA", vm.ModsFolderPath);
    }

    [Fact]
    public async Task NewProfile_Should_Avoid_Duplicate_Names()
    {
        var vm = CreateVm();
        vm.AvailableProfiles.Add("NewProfile");
        await vm.NewProfileCommand.ExecuteAsync(null);
        Assert.NotEqual("NewProfile", vm.CurrentProfileName);
        Assert.Contains("NewProfile", vm.CurrentProfileName);
    }

    // MOCKS
    private class MockCatPathService : ICatPathService {
        public string BaseDataPath { get; }
        public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
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
            _storage[path] = p;
            if (!Directory.Exists(Path.GetDirectoryName(path)!)) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ""); 
            return Task.CompletedTask; 
        }

        public Task<Profile?> LoadProfileAsync(string p) 
        {
            if (_storage.TryGetValue(p, out var profile)) return Task.FromResult<Profile?>(profile);
            return Task.FromResult<Profile?>(null);
        }

        public Task<IEnumerable<string>> ListProfilesAsync(string d) => Task.FromResult(_storage.Keys.Select(Path.GetFileNameWithoutExtension).AsEnumerable()!);
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

    private class MockModManagementService : IModManagementService {
        public Task<string> InstallModAsync(string s, string d) => Task.FromResult("");
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m) => Task.FromResult(t);
    }

    private class MockFileService : IFileService {
        public bool FileExists(string p) => true;
        public bool DirectoryExists(string p) => true;
        public void CreateDirectory(string p) { }
        public void CopyFile(string s, string d, bool o) { }
        public void CopyDirectory(string s, string d) { }
        public void DeleteFile(string p) { }
        public void DeleteDirectory(string p, bool r) { }
    }

    private class MockProcessService : IProcessService {
        public Task<bool> StartProcessAsync(string p, string a, bool admin) => Task.FromResult(true);
        public Task OpenFolderAsync(string p) => Task.CompletedTask;
    }

    private class MockLogService : ILogService {
        public event Action<string>? OnLog;
        public void Log(string m) => OnLog?.Invoke(m);
        public void LogError(string m, Exception? e) => OnLog?.Invoke(m);
    }
}
