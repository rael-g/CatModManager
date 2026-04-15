using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Tests;

public class VfsLifecycleCoordinatorTests
{
    private readonly VfsLifecycleCoordinator _coordinator;
    private readonly MockVfsOrchestrator _mockOrchestrator;
    private readonly GameConfigViewModel _gameConfig;
    private readonly ModListViewModel _modList;
    private bool _syncCalled;

    public VfsLifecycleCoordinatorTests()
    {
        var logService = new LogService();
        var pathService = new MockPathService();
        var gameSupportService = new GameSupportService(pathService, logService);
        
        _mockOrchestrator = new MockVfsOrchestrator();
        _gameConfig = new GameConfigViewModel(gameSupportService, new MockGameDiscoveryService(), logService);
        _modList = new ModListViewModel();

        _coordinator = new VfsLifecycleCoordinator(
            _mockOrchestrator,
            logService,
            () => _gameConfig,
            () => _modList,
            () => _syncCalled = true
        );
    }

    [Fact]
    public async Task ToggleMount_Should_Fail_If_No_Game_Selected()
    {
        _gameConfig.BaseFolderPath = "C:\\Game";
        // ActiveGameSupport starts as Default (Generic) — but let's test null if possible or invalid state
        
        // Simulating failure by not having enough info
        var result = await _coordinator.ToggleMountInternal();
        
        Assert.False(result.IsSuccess);
        Assert.False(_coordinator.IsVfsMounted);
    }

    [Fact]
    public async Task ToggleMount_Should_Mount_Successfully()
    {
        _gameConfig.BaseFolderPath = "C:\\Game";
        _mockOrchestrator.NextResult = OperationResult.Success();

        var result = await _coordinator.ToggleMountInternal();

        Assert.True(result.IsSuccess);
        Assert.True(_coordinator.IsVfsMounted);
        Assert.True(_syncCalled);
        Assert.Contains("Mounted", _coordinator.StatusMessage);
    }

    [Fact]
    public async Task ToggleMount_Should_Unmount_Successfully()
    {
        // SETUP: State is mounted
        _mockOrchestrator.NextResult = OperationResult.Success();
        await _coordinator.ToggleMountInternal();
        Assert.True(_coordinator.IsVfsMounted);

        // ACT: Toggle again
        _mockOrchestrator.NextResult = OperationResult.Success();
        var result = await _coordinator.ToggleMountInternal();

        Assert.True(result.IsSuccess);
        Assert.False(_coordinator.IsVfsMounted);
        Assert.Contains("Unmounted", _coordinator.StatusMessage);
    }

    private class MockVfsOrchestrator : IVfsOrchestrationService {
        public OperationResult NextResult { get; set; } = OperationResult.Success();
        public bool IsMounted { get; set; }
        public Task<OperationResult> MountAsync(MountOptions o) => Task.FromResult(NextResult);
        public Task<OperationResult> UnmountAsync() => Task.FromResult(NextResult);
        public void RecoverStaleMounts() { }
        public Task ShutdownCleanupAsync() => Task.CompletedTask;
    }

    private class MockPathService : ICatPathService {
        public string BaseDataPath => "";
        public string ProfilesPath => "";
        public string GameSupportsPath => "";
        public string ActiveMountsFile => "";
        public string DownloadsPath => "";
        public string GetProfilePath(string n) => "";
    }

    private class MockGameDiscoveryService : IGameDiscoveryService {
        public Task<IReadOnlyList<GameInstallation>> ScanAsync(CancellationToken ct = default) => 
            Task.FromResult<IReadOnlyList<GameInstallation>>(new List<GameInstallation>());
    }
}
