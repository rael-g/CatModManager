using System;
using System.IO;
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

    private readonly FakeDriver _driver = new();

    private VfsOrchestrationService CreateService(IEnumerable<IVfsLifecycleHook>? hooks = null)
    {
        return new VfsOrchestrationService(
            _resolver,
            new MockHardlinkStateStore(),
            _stateService,
            _logService,
            hooks?.ToList(),
            _ => _driver);
    }

    /// <summary>
    /// A game folder that is valid on whatever OS the suite runs on. These tests used to hardcode
    /// "C:\Game", which on Linux is a relative path to a directory that does not exist — so the
    /// real driver failed to mount and every assertion about IsMounted failed with it.
    /// </summary>
    private static string GameFolder => Path.Combine(Path.GetTempPath(), "CMM_VfsOrchestration");

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
            GameFolderPath = GameFolder, 
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
            GameFolderPath = GameFolder, 
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
            GameFolderPath = GameFolder,
            ActiveMods = new List<Mod> { new Mod("Test", "Path", 1) },
            MountPoints = new List<MountPointDef> { new MountPointDef("default", "Default", "") }
        };
        
        await service.MountAsync(options);
        Assert.True(service.IsMounted);

        var result = await service.MountAsync(options);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Mount points overlap by design: a "Game Root" with Path "" contains a "Data" with Path
    /// "Data", so a mod nesting its files under its own Data/ folder lands on exactly the same
    /// physical file as a mod on the Data mount point that does not. They used to be deployed by
    /// independent drivers, and the second one set the first one's hard link aside as if it were
    /// the game's original file — which is how a mod file ended up restored into the game folder,
    /// permanently, on unmount.
    /// </summary>
    [Fact]
    public async Task MountAsync_WhenTwoMountPointsClaimTheSameFile_OnlyTheHigherPriorityModDeploys()
    {
        string root = Path.Combine(Path.GetTempPath(), "CMM_Overlap_" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "Game");
        string modA = Path.Combine(root, "ModA");   // for the Data mount point
        string modB = Path.Combine(root, "ModB");   // for the Root mount point, nested under Data/

        Directory.CreateDirectory(Path.Combine(game, "Data"));
        Directory.CreateDirectory(Path.Combine(modA, "meshes"));
        Directory.CreateDirectory(Path.Combine(modB, "Data", "meshes"));
        File.WriteAllText(Path.Combine(modA, "meshes", "x.hkx"), "A");
        File.WriteAllText(Path.Combine(modB, "Data", "meshes", "x.hkx"), "B");

        try
        {
            // One driver per mount point, as in production — a shared one would report itself
            // already mounted and the second mount point would never be handed anything.
            var maps = new Dictionary<string, List<string>>();
            var service = new VfsOrchestrationService(
                new SimpleConflictResolver(_logService, new SevenZipArchiveExtractor()),
                new MockHardlinkStateStore(), _stateService, _logService, null,
                _ => new CapturingDriver(maps));

            await service.MountAsync(new MountOptions
            {
                GameFolderPath = game,
                MountPoints =
                [
                    new MountPointDef { Id = "data", Name = "Data",      Path = "Data" },
                    new MountPointDef { Id = "root", Name = "Game Root", Path = ""     },
                ],
                ActiveMods =
                [
                    new Mod("A", modA, 1) { MountPointId = "data" },
                    new Mod("B", modB, 5) { MountPointId = "root" },
                ]
            });

            // Both resolve to <game>/Data/meshes/x.hkx. B has the higher priority, so B keeps it
            // and A's copy is dropped before anything is linked.
            var dataMap = maps[Path.Combine(game, "Data")];
            var rootMap = maps[game];

            Assert.DoesNotContain(@"meshes\x.hkx", dataMap);
            Assert.Contains(@"Data\meshes\x.hkx", rootMap);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>Records what each mount point was actually handed, without touching the disk.</summary>
    private class CapturingDriver : CatModManager.VirtualFileSystem.IFileSystemDriver
    {
        private readonly Dictionary<string, List<string>> _maps;
        public CapturingDriver(Dictionary<string, List<string>> maps) => _maps = maps;
        public bool IsMounted { get; private set; }

        public void Mount(string mountPoint, CatModManager.VirtualFileSystem.IFileSystem fs)
        {
            var found = new List<string>();
            Walk(fs, "", found);
            _maps[mountPoint] = found;
            IsMounted = true;
        }

        private static void Walk(CatModManager.VirtualFileSystem.IFileSystem fs, string dir, List<string> into)
        {
            foreach (var name in fs.ReadDirectory(dir))
            {
                string rel = string.IsNullOrEmpty(dir) ? name : dir + Path.DirectorySeparatorChar + name;
                var info = fs.GetInfo(rel);
                if (info == null) continue;
                if (info.IsDirectory) Walk(fs, rel, into);
                else into.Add(rel.Replace('/', '\\'));
            }
        }

        public void Unmount() => IsMounted = false;
        public void Dispose() => Unmount();
    }

    // --- MOCKS ---

    /// <summary>
    /// Stands in for the real deployment driver so these tests exercise orchestration only —
    /// no FUSE mount, no hard links, no disk.
    /// </summary>
    private class FakeDriver : CatModManager.VirtualFileSystem.IFileSystemDriver
    {
        public bool IsMounted { get; private set; }
        public int MountCount { get; private set; }

        public void Mount(string mountPoint, CatModManager.VirtualFileSystem.IFileSystem fs)
        {
            MountCount++;
            IsMounted = true;
        }

        public void Unmount() => IsMounted = false;
        public void Dispose() => Unmount();
    }

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
