using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.Plugins;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Tests.Core.Services;

public class ProfileCoordinatorTests
{
    private readonly ProfileCoordinator _coordinator;
    private readonly GameConfigViewModel _gameConfig;
    private readonly ModListViewModel _modList;
    private readonly AppSessionState _sessionState;
    private readonly ExternalToolsViewModel _tools;
    private bool _refreshCalled;
    private bool _syncCalled;

    public ProfileCoordinatorTests()
    {
        var logService = Substitute.For<ILogService>();
        var pathService = Substitute.For<ICatPathService>();
        var gameSupportService = Substitute.For<IGameSupportService>();
        var gameDiscovery = Substitute.For<IGameDiscoveryService>();
        
        _gameConfig = new GameConfigViewModel(gameSupportService, gameDiscovery, logService);
        _modList = new ModListViewModel();
        _sessionState = new AppSessionState();
        _tools = new ExternalToolsViewModel(
            Substitute.For<IProcessService>(),
            Substitute.For<IVfsOrchestrationService>(),
            logService);

        _coordinator = new ProfileCoordinator(
            Substitute.For<IProfileService>(),
            Substitute.For<IConfigService>(),
            logService,
            _sessionState,
            () => _gameConfig,
            () => _modList,
            () => _tools,
            () => _refreshCalled = true,
            () => _syncCalled = true
        );
    }

    /// <summary>
    /// Profile.ExternalTools existed and was serialised from the day the Tools tab was written, but
    /// neither side of this coordinator touched it — so a tool the user added lived in memory only
    /// and was gone on the next start, with no error anywhere to say why.
    /// </summary>
    [Fact]
    public void ExternalTools_SurviveASaveAndLoadRoundTrip()
    {
        _tools.LoadTools(
        [
            new ExternalTool
            {
                Name = "BodySlide",
                ExecutablePath = "wine",
                Arguments = "\"/games/Fallout 4/Data/Tools/BodySlide/BodySlide.exe\"",
                MountBeforeLaunch = true
            }
        ]);

        var saved = _coordinator.BuildCurrentProfile("Fallout");

        var tool = Assert.Single(saved.ExternalTools);
        Assert.Equal("BodySlide", tool.Name);
        Assert.Equal("wine", tool.ExecutablePath);
        Assert.True(tool.MountBeforeLaunch);

        // And it comes back — the arguments especially, since a command like "wine" is useless
        // without them.
        _tools.LoadTools([]);
        _coordinator.ApplyLoadedProfile(saved);

        var restored = Assert.Single(_tools.Tools);
        Assert.Equal("BodySlide", restored.Name);
        Assert.Contains("BodySlide.exe", restored.Arguments);
        Assert.True(restored.MountBeforeLaunch);
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
        Assert.Single(profile.Mods);
        Assert.Equal("Mod1", profile.Mods[0].Name);
    }

    [Fact]
    public void ApplyLoadedProfile_ShouldUpdateViewModelsAndNotify()
    {
        var profile = new Profile
        {
            Name = "LoadedProfile",
            Mods = new List<Mod> { new Mod("NewMod", "NewPath", 0) }
        };

        bool eventFired = false;
        _sessionState.ProfileChanged += name => eventFired = (name == "LoadedProfile");

        _coordinator.ApplyLoadedProfile(profile);

        // The folders are not here any more: they come from the game, through ApplyLoadedGame.
        Assert.Single(_modList.AllMods);
        Assert.Equal("NewMod", _modList.AllMods[0].Name);
        Assert.True(_refreshCalled);
        Assert.True(_syncCalled);
        Assert.True(eventFired);
    }
}
