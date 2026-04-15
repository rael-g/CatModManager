using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;
using CatModManager.Tests.Support;

namespace CatModManager.Tests.Core.Services;

public class VfsOrchestrationServiceTests
{
    private readonly MockLogService _logService = new();
    private readonly MockVfsStateService _stateService = new();
    private readonly MockConflictResolver _resolver = new();

    private VfsOrchestrationService CreateService(IEnumerable<IVfsLifecycleHook>? hooks = null)
    {
        return new VfsOrchestrationService(
            _resolver,
            new MockHardlinkStateStore(),
            _stateService,
            _logService,
            hooks?.ToList());
    }

    [Fact]
    public async Task MountAsync_ReturnsFailure_WhenNoGamePathProvided()
    {
        var service = CreateService();
        var result = await service.MountAsync(new MountOptions { GameFolderPath = "" });
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task MountAsync_CallsLifecycleHooks_BeforeMount()
    {
        var hook = new MockVfsHook();
        var service = CreateService(new[] { hook });
        
        await service.MountAsync(new MountOptions { 
            GameFolderPath = "C:\\Game", 
            ActiveMods = new List<Mod>(),
            MountPoints = new List<MountPointDef> { new MountPointDef("default", "Default", "") }
        });

        Assert.True(hook.BeforeMountCalled);
    }

    [Fact]
    public async Task UnmountAsync_CallsLifecycleHooks_AfterUnmount()
    {
        var hook = new MockVfsHook();
        var service = CreateService(new[] { hook });
        
        // Setup a valid mount point to ensure IsMounted becomes true
        await service.MountAsync(new MountOptions { 
            GameFolderPath = "C:\\Game", 
            ActiveMods = new List<Mod> { new Mod("T", "P", 1) },
            MountPoints = new List<MountPointDef> { new MountPointDef("default", "Default", "") }
        });

        await service.UnmountAsync();

        Assert.True(hook.AfterUnmountCalled);
    }

    [Fact]
    public async Task MountAsync_ReturnsFailure_IfAlreadyMounted()
    {
        var service = CreateService();
        var options = new MountOptions { 
            GameFolderPath = "C:\\Game",
            ActiveMods = new List<Mod> { new Mod("Test", "Path", 1) },
            MountPoints = new List<MountPointDef> { new MountPointDef("default", "Default", "") }
        };
        
        await service.MountAsync(options);
        Assert.True(service.IsMounted);

        var result = await service.MountAsync(options);
        Assert.False(result.IsSuccess);
    }

    // --- MOCKS ---

    private class MockVfsHook : IVfsLifecycleHook
    {
        public bool BeforeMountCalled { get; private set; }
        public bool AfterUnmountCalled { get; private set; }
        public Task OnBeforeMountAsync(MountInfo info) { BeforeMountCalled = true; return Task.CompletedTask; }
        public Task OnAfterUnmountAsync(string gameFolder) { AfterUnmountCalled = true; return Task.CompletedTask; }
    }

    private class MockVfsStateService : IVfsStateService
    {
        public void RegisterMount(string o, string b) { }
        public void UnregisterMount(string o) { }
        public void RecoverStaleMounts() { }
    }

    private class MockHardlinkStateStore : CatModManager.VirtualFileSystem.IHardlinkStateStore
    {
        public void Save(string mp, IReadOnlyList<CatModManager.VirtualFileSystem.HardlinkStateEntry> e) { }
        public IReadOnlyList<CatModManager.VirtualFileSystem.HardlinkStateEntry> Load(string? mp) => Array.Empty<CatModManager.VirtualFileSystem.HardlinkStateEntry>();
        public void Clear(string? mp) { }
    }

    private class MockConflictResolver : IConflictResolver
    {
        private readonly IArchiveExtractor _extractor = new SevenZipArchiveExtractor();
        public string? ForbiddenPath { get; set; }
        public IDictionary<string, IFileSource> ResolveConflicts(IEnumerable<Mod> mods, string? baseFolderPath, string? dataSubFolder = null, string? forbiddenPath = null) => new Dictionary<string, IFileSource>();
        public IReadOnlyList<ConflictReport> GetConflictReport(IEnumerable<Mod> activeMods) => Array.Empty<ConflictReport>();
    }
}
