using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.Core.Services;
using CatModManager.Core.Models;
using CatModManager.PluginSdk;
using CatModManager.Tests.Support;

namespace CatModManager.Tests.Core.Services;

public class GameLaunchServiceTests
{
    private readonly IProcessService _processService;
    private readonly ILogService _logService;

    public GameLaunchServiceTests()
    {
        _processService = Substitute.For<IProcessService>();
        _processService.StartProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        _logService = new MockLogService();
    }

    [Fact]
    public async Task LaunchGameAsync_CallsProcessService_WithCorrectArgs()
    {
        var support = Substitute.For<IGameSupport>();
        support.GetLaunchArguments(Arg.Any<IEnumerable<Mod>>()).Returns("-modded");
        
        var service = new GameLaunchService(_processService, _logService);
        
        var result = await service.LaunchGameAsync("game.exe", "-extra", support, Enumerable.Empty<Mod>());

        Assert.True(result.IsSuccess);
        await _processService.Received(1).StartProcessAsync("game.exe", "-modded -extra", false);
    }

    [Fact]
    public async Task LaunchGameAsync_InvokesHooks()
    {
        var hook = Substitute.For<IGameLaunchHook>();
        var support = Substitute.For<IGameSupport>();
        
        var service = new GameLaunchService(_processService, _logService, new[] { hook });
        _processService.StartProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);

        await service.LaunchGameAsync("game.exe", "", support, Enumerable.Empty<Mod>());

        await hook.Received(1).OnBeforeLaunchAsync(Arg.Any<LaunchContext>());
        await hook.Received(1).OnAfterExitAsync(Arg.Any<LaunchContext>());
    }

    [Fact]
    public async Task LaunchGameAsync_ReturnsFailure_WhenProcessStartFails()
    {
        _processService.StartProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(false);
        var support = Substitute.For<IGameSupport>();
        
        var service = new GameLaunchService(_processService, _logService);
        var result = await service.LaunchGameAsync("game.exe", "", support, Enumerable.Empty<Mod>());

        Assert.False(result.IsSuccess);
        Assert.Contains("Could not start game process", result.ErrorMessage);
    }
}
