using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Pure unit tests for VfsLifecycleCoordinator using NSubstitute for isolation.
/// </summary>
public class VfsLifecycleCoordinatorTests
{
    private readonly VfsLifecycleCoordinator _coordinator;
    private readonly IVfsOrchestrationService _orchestrator;
    private readonly GameConfigViewModel _gameConfig;
    private readonly ModListViewModel _modList;
    private bool _syncCalled;

    public VfsLifecycleCoordinatorTests()
    {
        var logService = Substitute.For<ILogService>();
        _orchestrator = Substitute.For<IVfsOrchestrationService>();
        
        var gameSupportService = Substitute.For<IGameSupportService>();
        var gameDiscovery = Substitute.For<IGameDiscoveryService>();
        
        _gameConfig = new GameConfigViewModel(gameSupportService, gameDiscovery, logService);
        _modList = new ModListViewModel();

        _coordinator = new VfsLifecycleCoordinator(
            _orchestrator,
            logService,
            () => _gameConfig,
            () => _modList,
            () => _syncCalled = true
        );
    }

    [Fact]
    public async Task ToggleMount_Should_Fail_If_No_Game_Selected()
    {
        // SETUP: No game support selected
        _gameConfig.ActiveGameSupport = null!; 
        _gameConfig.BaseFolderPath = "C:\\Game";

        // ACT
        var result = await _coordinator.ToggleMountInternal();
        
        // ASSERT
        Assert.False(result.IsSuccess);
        Assert.Equal("No game selected.", result.ErrorMessage);
        Assert.False(_coordinator.IsVfsMounted);
        
        // Ensure orchestrator was NEVER called
        await _orchestrator.DidNotReceive().MountAsync(Arg.Any<MountOptions>());
    }

    [Fact]
    public async Task ToggleMount_Should_Mount_Successfully_When_Valid()
    {
        // SETUP
        var mockGame = Substitute.For<IGameSupport>();
        mockGame.GameId.Returns("test-game");
        _gameConfig.ActiveGameSupport = mockGame;
        _gameConfig.BaseFolderPath = "C:\\Game";
        
        _orchestrator.MountAsync(Arg.Any<MountOptions>()).Returns(OperationResult.Success());

        // ACT
        var result = await _coordinator.ToggleMountInternal();

        // ASSERT
        Assert.True(result.IsSuccess);
        Assert.True(_coordinator.IsVfsMounted);
        Assert.True(_syncCalled);
        Assert.Contains("Mounted", _coordinator.StatusMessage);
        
        await _orchestrator.Received(1).MountAsync(Arg.Is<MountOptions>(o => o.GameFolderPath == "C:\\Game"));
    }

    [Fact]
    public async Task ToggleMount_Should_Unmount_Successfully_If_Already_Mounted()
    {
        // SETUP: Force mounted state
        var mockGame = Substitute.For<IGameSupport>();
        _gameConfig.ActiveGameSupport = mockGame;
        _gameConfig.BaseFolderPath = "C:\\Game";
        _orchestrator.MountAsync(Arg.Any<MountOptions>()).Returns(OperationResult.Success());
        
        await _coordinator.ToggleMountInternal();
        Assert.True(_coordinator.IsVfsMounted);

        _orchestrator.UnmountAsync().Returns(OperationResult.Success());

        // ACT: Toggle again to unmount
        var result = await _coordinator.ToggleMountInternal();

        // ASSERT
        Assert.True(result.IsSuccess);
        Assert.False(_coordinator.IsVfsMounted);
        Assert.Contains("Unmounted", _coordinator.StatusMessage);
        await _orchestrator.Received(1).UnmountAsync();
    }
}
