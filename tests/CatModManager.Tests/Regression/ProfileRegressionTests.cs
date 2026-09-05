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
    private readonly IProfileService _profileService;

    // A real one, on the same database as the profiles: profiles.game_id is a foreign key, so a
    // game that exists only in a fake is a game the insert refuses.
    private readonly IGameService _gameService;
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
        // O serviço real, não um dublê: estes testes são sobre criar, renomear e apagar perfis, e
        // um dublê de perfil só provaria que o dublê concorda consigo mesmo. Foi assim que o bug do
        // CopyDirectory passou despercebido.
        _profileService = new SqliteProfileService(db);
        _gameService    = new SqliteGameService(db);
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
            _profileService,
            _gameService,
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
            new FakeGlobalToolService(),
            new CatModManager.Ui.Plugins.AppSessionState(),
            new MockPluginLoader()
        );
    }

    private async Task<string[]> StoredNames()
        => (await _profileService.ListAllProfilesAsync()).Select(p => p.Name).ToArray();

    [Fact]
    public async Task NewProfile_Should_Be_Saved_Immediately()
    {
        var vm = CreateVm();

        // The constructor starts loading the profile list in the background. Creating a profile
        // before that finishes lets its RefreshListAsync clear the list and refill it from a
        // snapshot taken before our profile existed — leaving AvailableProfiles empty.
        await vm.InitialLoadTask;

        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);

        Assert.Contains(vm.ProfileManager.CurrentProfileName, await StoredNames());
        Assert.Contains(vm.ProfileManager.AvailableProfiles,
                        p => p.Name == vm.ProfileManager.CurrentProfileName);
    }

    [Fact]
    public async Task Profile_Selection_Should_Load_Data()
    {
        var vm = CreateVm();
        await vm.InitialLoadTask;

        // A game first: a profile with none is parked, and a parked profile has no inventory to
        // hang mods on — by design, since the folder they would belong to has not been chosen.
        long gameId = await _gameService.SaveGameAsync(new Game
        {
            DisplayName  = "Test game",
            BaseDataPath = "/games/Test",
        });
        await vm.GameManager.RefreshListAsync(gameId);

        // Two profiles, each with its own mod list — which is what a profile is, now that the
        // folders and the launch line belong to the game.
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        long profileA = vm.ProfileManager.CurrentProfile!.Id;
        vm.ModList.AllMods.Add(new Mod("OnlyInA", "/mods/OnlyInA", 0));
        await vm.ProfileManager.SaveProfileCommand.ExecuteAsync(null);

        // The second profile of the same game sees that mod too — it is installed for the game —
        // but unticked, because nobody asked for it here.
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        Assert.False(Assert.Single(vm.ModList.AllMods).IsEnabled);

        // Switch through the method rather than the property setter, whose load is fire-and-forget.
        await vm.ProfileManager.LoadProfileAsync(profileA);

        var carried = Assert.Single(vm.ModList.AllMods);
        Assert.Equal("OnlyInA", carried.Name);
        Assert.True(carried.IsEnabled, "Profile A's own mod came back unticked.");
    }

    [Fact]
    public async Task RenameProfile_Should_Rename_And_Update_CurrentProfileName()
    {
        var vm = CreateVm();
        await vm.InitialLoadTask;

        // Create a profile with a known name
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        string originalName = vm.ProfileManager.CurrentProfileName!;

        // Set a new name and rename
        string newName = originalName + "_Renamed";
        vm.ProfileManager.ProfileDisplayName = newName;
        await vm.ProfileManager.RenameProfileCommand.ExecuteAsync(null);

        // CurrentProfileName must be updated
        Assert.Equal(newName, vm.ProfileManager.CurrentProfileName);
        Assert.Contains(vm.ProfileManager.AvailableProfiles,     p => p.Name == newName);
        Assert.DoesNotContain(vm.ProfileManager.AvailableProfiles, p => p.Name == originalName);

        // And in storage, not just in the list: the rename used to be "save under the new name,
        // then delete the old", which left both behind whenever the delete failed.
        var stored = await StoredNames();
        Assert.Contains(newName, stored);
        Assert.DoesNotContain(originalName, stored);
    }

    [Fact]
    public async Task NewProfile_Should_Avoid_Duplicate_Names()
    {
        var vm = CreateVm();
        await vm.InitialLoadTask;

        vm.ProfileManager.AvailableProfiles.Add(new ProfileSummary(999, "NewProfile"));
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        Assert.NotEqual("NewProfile", vm.ProfileManager.CurrentProfileName);
        Assert.Contains("NewProfile", vm.ProfileManager.CurrentProfileName);
    }

    [Fact]
    public async Task DeleteProfile_Should_Not_Deadlock_And_Should_Select_Another()
    {
        var vm = CreateVm();
        await vm.InitialLoadTask;

        // Two profiles: deleting the only one has nothing to fall back to.
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        string survivor = vm.ProfileManager.CurrentProfileName!;
        await vm.ProfileManager.NewProfileCommand.ExecuteAsync(null);
        string doomed = vm.ProfileManager.CurrentProfileName!;

        Assert.Contains(vm.ProfileManager.AvailableProfiles, p => p.Name == doomed);
        Assert.Contains(doomed, await StoredNames());

        vm.ProfileManager.ConfirmDelete = _ => Task.FromResult(true);

        // The regression this guards: DeleteProfile used to block the calling thread waiting on the
        // confirmation dialog, so awaiting the command never returned. A timeout, not an await, is
        // what makes that failure show up as a failed test instead of a hung run.
        var delete = vm.ProfileManager.DeleteProfileCommand.ExecuteAsync(null);
        var finished = await Task.WhenAny(delete, Task.Delay(5000));
        Assert.True(finished == delete, "DeleteProfile deadlocked.");
        await delete;

        Assert.DoesNotContain(vm.ProfileManager.AvailableProfiles, p => p.Name == doomed);
        Assert.DoesNotContain(doomed, await StoredNames());

        // Something else is open — which one does not matter, only that the window is not left
        // pointing at a profile that no longer exists.
        Assert.NotNull(vm.ProfileManager.CurrentProfile);
        Assert.NotEqual(doomed, vm.ProfileManager.CurrentProfileName);
        Assert.Contains(survivor, await StoredNames());
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




    private class MockFileService : StubFileService {
        public override bool FileExists(string p) => true;
        public override bool DirectoryExists(string p) => true;
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
