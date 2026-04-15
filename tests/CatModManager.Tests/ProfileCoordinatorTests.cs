using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.Plugins;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Tests;

public class ProfileCoordinatorTests
{
    private readonly ProfileCoordinator _coordinator;
    private readonly GameConfigViewModel _gameConfig;
    private readonly ModListViewModel _modList;
    private readonly AppSessionState _sessionState;
    private bool _refreshCalled;
    private bool _syncCalled;

    public ProfileCoordinatorTests()
    {
        var logService = new LogService();
        var pathService = new MockPathService();
        var gameSupportService = new GameSupportService(pathService, logService);
        var gameDiscoveryService = new MockGameDiscoveryService();
        
        _gameConfig = new GameConfigViewModel(gameSupportService, gameDiscoveryService, logService);
        _modList = new ModListViewModel();
        _sessionState = new AppSessionState();

        _coordinator = new ProfileCoordinator(
            new MockProfileService(),
            new MockConfigService(),
            logService,
            _sessionState,
            () => _gameConfig,
            () => _modList,
            () => _refreshCalled = true,
            () => _syncCalled = true
        );
    }

    [Fact]
    public void BuildCurrentProfile_ShouldMapAllFields()
    {
        _gameConfig.ModsFolderPath = "C:\\Mods";
        _gameConfig.BaseFolderPath = "C:\\Game";
        _gameConfig.LaunchArguments = "-test";
        _modList.AllMods.Add(new Mod("Mod1", "Path1", 1));

        var profile = _coordinator.BuildCurrentProfile("TestProfile");

        Assert.Equal("TestProfile", profile.Name);
        Assert.Equal("C:\\Mods", profile.ModsFolderPath);
        Assert.Equal("-test", profile.LaunchArguments);
        Assert.Single(profile.Mods);
        Assert.Equal("Mod1", profile.Mods[0].Name);
    }

    [Fact]
    public void ApplyLoadedProfile_ShouldUpdateViewModelsAndNotify()
    {
        var profile = new Profile
        {
            Name = "LoadedProfile",
            ModsFolderPath = "C:\\NewMods",
            Mods = new List<Mod> { new Mod("NewMod", "NewPath", 0) }
        };

        bool eventFired = false;
        _sessionState.ProfileChanged += name => eventFired = (name == "LoadedProfile");

        _coordinator.ApplyLoadedProfile(profile);

        Assert.Equal("C:\\NewMods", _gameConfig.ModsFolderPath);
        Assert.Single(_modList.AllMods);
        Assert.Equal("NewMod", _modList.AllMods[0].Name);
        Assert.True(_refreshCalled);
        Assert.True(_syncCalled);
        Assert.True(eventFired);
    }

    private class MockPathService : ICatPathService {
        public string BaseDataPath => "";
        public string ProfilesPath => "";
        public string GameSupportsPath => "";
        public string ActiveMountsFile => "";
        public string DownloadsPath => "";
        public string GetProfilePath(string n) => "";
    }

    private class MockProfileService : IProfileService {
        public Task SaveProfileAsync(Profile p, string f) => Task.CompletedTask;
        public Task<Profile?> LoadProfileAsync(string f) => Task.FromResult<Profile?>(null);
        public Task<IEnumerable<string>> ListProfilesAsync(string d) => Task.FromResult(Enumerable.Empty<string>());
    }

    private class MockConfigService : IConfigService {
        public AppConfig Current { get; } = new();
        public void Save() { }
        public void Load() { }
    }

    private class MockGameDiscoveryService : IGameDiscoveryService {
        public Task<IReadOnlyList<GameInstallation>> ScanAsync(CancellationToken ct = default) => 
            Task.FromResult<IReadOnlyList<GameInstallation>>(new List<GameInstallation>());
    }
}
