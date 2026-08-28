using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.PluginSdk;
using CatModManager.Tests.Support;
using CatModManager.Ui.Plugins;
using CatModManager.Ui.ViewModels;
using CatModManager.VirtualFileSystem;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Launch mounts for you, so it has to unmount for you too — but only what it mounted, and only once
/// the game has genuinely finished.
///
/// None of this existed: Launch mounted and nothing ever unmounted, and the launch result was
/// discarded so a launch that never produced a game still reported "Game running."
/// </summary>
public class UnmountAfterPlayingTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "CMM_Unmount_" + Guid.NewGuid().ToString("N"));

    private readonly FakeVfs _vfs = new();
    private readonly FakeLauncher _launcher = new();

    public UnmountAfterPlayingTests() => Directory.CreateDirectory(_tempDir);

    private MainWindowViewModel CreateViewModel()
    {
        string appData = Path.Combine(_tempDir, "AppData");
        Directory.CreateDirectory(appData);
        var paths = new StubPaths { BaseDataPath = appData };
        Directory.CreateDirectory(paths.ProfilesPath);

        return new MainWindowViewModel(
            new StubScanner(), new StubProfiles(), new StubManagement(), new StubProcesses(),
            _vfs, _launcher, new StubFiles(), paths, new MockLogService(),
            new StubConfig(), new StubSupports(),
            new GameDiscoveryService(new StubSupports(), Enumerable.Empty<IGameScanner>()),
            new AppSessionState(), new MockPluginLoader());
    }

    private static async Task<MainWindowViewModel> Ready(MainWindowViewModel vm)
    {
        await vm.InitialLoadTask;
        return vm;
    }

    [AvaloniaFact]
    public async Task TheGameRunningAndClosingUnmountsWhatLaunchMounted()
    {
        var vm = await Ready(CreateViewModel());
        vm.GameConfig.GameExecutablePath = "steam";
        vm.GameConfig.BaseFolderPath = _tempDir;
        _launcher.GameObserved = true;

        await vm.LaunchGameCommand.ExecuteAsync(null);

        Assert.True(_vfs.Mounted.Contains(true), "Launch should have mounted before starting the game.");
        Assert.False(_vfs.IsMounted);
        Assert.Equal(1, _vfs.UnmountCount);
    }

    /// <summary>
    /// Steam reporting a missing licence looks exactly like this: the process starts, the game never
    /// does. A first Proton run still compiling shaders past the wait window looks the same from
    /// here — so the mount stays, rather than risking pulling files out from under a game that is
    /// merely slow.
    /// </summary>
    [AvaloniaFact]
    public async Task AGameThatIsNeverSeenRunningLeavesTheMountAlone()
    {
        var vm = await Ready(CreateViewModel());
        vm.GameConfig.GameExecutablePath = "steam";
        vm.GameConfig.BaseFolderPath = _tempDir;
        _launcher.GameObserved = false;

        await vm.LaunchGameCommand.ExecuteAsync(null);

        Assert.True(_vfs.IsMounted, "A launch that could not be confirmed must not unmount.");
        Assert.Equal(0, _vfs.UnmountCount);
        Assert.Contains("never seen", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A mount you made yourself is a decision of yours; the game ending does not revoke it.</summary>
    [AvaloniaFact]
    public async Task AMountMadeByHandSurvivesTheGameClosing()
    {
        var vm = await Ready(CreateViewModel());
        vm.GameConfig.GameExecutablePath = "steam";
        vm.GameConfig.BaseFolderPath = _tempDir;
        _launcher.GameObserved = true;

        await vm.Vfs.ToggleMountInternal();      // mounted by hand, before launching
        Assert.True(_vfs.IsMounted);
        _vfs.UnmountCount = 0;

        await vm.LaunchGameCommand.ExecuteAsync(null);

        Assert.True(_vfs.IsMounted, "Launch unmounted a mount it did not make.");
        Assert.Equal(0, _vfs.UnmountCount);
    }

    [AvaloniaFact]
    public async Task AFailedLaunchIsReportedInsteadOfClaimingTheGameIsRunning()
    {
        var vm = await Ready(CreateViewModel());
        vm.GameConfig.GameExecutablePath = "steam";
        vm.GameConfig.BaseFolderPath = _tempDir;
        _launcher.Failure = "Could not start game process.";

        await vm.LaunchGameCommand.ExecuteAsync(null);

        Assert.Contains("failed", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("running", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeLauncher : IGameLaunchService
    {
        public bool GameObserved { get; set; }
        public string? Failure { get; set; }

        public Task<OperationResult<bool>> LaunchGameAsync(
            string? exe, string? args, IGameSupport support, IEnumerable<Mod> mods, string? gameFolder = null)
            => Task.FromResult(Failure != null
                ? OperationResult<bool>.Failure(Failure)
                : OperationResult<bool>.Success(GameObserved));
    }

    private sealed class FakeVfs : IVfsOrchestrationService
    {
        public bool IsMounted { get; private set; }
        public List<bool> Mounted { get; } = new();
        public int UnmountCount { get; set; }

        public Task<OperationResult> MountAsync(MountOptions options)
        {
            IsMounted = true;
            Mounted.Add(true);
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> UnmountAsync()
        {
            IsMounted = false;
            UnmountCount++;
            return Task.FromResult(OperationResult.Success());
        }

        public void RecoverStaleMounts() { }
        public Task ShutdownCleanupAsync() { IsMounted = false; return Task.CompletedTask; }
    }

    private sealed class StubPaths : ICatPathService
    {
        public string BaseDataPath { get; set; } = "";
        public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
        public string DownloadsPath => Path.Combine(BaseDataPath, "downloads");
        public string GetProfilePath(string n) => Path.Combine(ProfilesPath, n + ".toml");
    }

    private sealed class StubFiles : StubFileService
    {
        public override bool DirectoryExists(string p) => true;
    }

    private sealed class StubConfig : IConfigService
    {
        public AppConfig Current { get; } = new();
        public void Save() { }
        public void Load() { }
    }

    private sealed class StubSupports : IGameSupportService
    {
        public IGameSupport Default => new GenericGameSupport();
        public void RefreshSupports() { }
        public IEnumerable<IGameSupport> GetAllSupports() => new[] { Default };
        public IGameSupport GetSupportById(string? id) => Default;
        public IGameSupport DetectSupport(string? path) => Default;
    }

    private sealed class StubScanner : IModScanner
    {
        public Task<IEnumerable<Mod>> ScanDirectoryAsync(string p) => Task.FromResult(Enumerable.Empty<Mod>());
    }

    private sealed class StubProfiles : IProfileService
    {
        public Task SaveProfileAsync(Profile p, string f) => Task.CompletedTask;
        public Task<Profile?> LoadProfileAsync(string f) => Task.FromResult<Profile?>(null);
        public Task<IEnumerable<string>> ListProfilesAsync(string d) => Task.FromResult(Enumerable.Empty<string>());
    }

    private sealed class StubProcesses : IProcessService
    {
        public Task<ProcessRunResult> StartProcessAsync(string f, string a, bool admin = false, bool wait = true, string? watch = null)
            => Task.FromResult(new ProcessRunResult(true, false));
        public Task OpenFolderAsync(string p) => Task.CompletedTask;
    }

    private sealed class StubManagement : IModManagementService
    {
        public Task<string> InstallModAsync(string s, string t, string? o = null, IProgress<double>? p = null, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m, string? o = null, IProgress<double>? p = null, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> InstallModToRootAsync(string a, string n, string t, IProgress<double>? p = null, CancellationToken ct = default) => Task.FromResult("");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }
}
