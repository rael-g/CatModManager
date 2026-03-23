using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Vfs;

namespace CatModManager.Tests;

public class CriticalBugTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogService _logService;
    private readonly MockPathService _pathService;

    public CriticalBugTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CMM_Critical_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logService = new LogService();
        _pathService = new MockPathService { BaseDataPath = _tempDir };
    }

    [Fact]
    public void SimpleConflictResolver_MUST_Scan_Root_Even_With_Forbidden_Name()
    {
        var resolver = new SimpleConflictResolver(_logService);
        // Raiz com o nome proibido ".CMM_base"
        string backupDir = Path.Combine(_tempDir, "Game.CMM_base"); 
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "test.txt"), "data");

        // ACT: Escanear essa pasta. Não deve retornar vazio.
        var result = resolver.ResolveConflicts(new List<Mod>(), backupDir);

        // ASSERT
        Assert.True(result.ContainsKey("test.txt"), "O scanner falhou em mapear arquivos da raiz porque o nome da pasta raiz contém 'CMM_base'.");
    }

    [Fact]
    public void Shutdown_Cleanup_MUST_Restore_Folders_Synchronously()
    {
        var mockVfs = new MockVfs();
        var state = new VfsStateService(new AppDatabase(_pathService), _logService);
        var mockDriver = new MockDriverService();
        var orchestrator = new VfsOrchestrationService(mockVfs, state, mockDriver, _logService, new NullRootSwapService());

        string original = Path.Combine(_tempDir, "GameFolder");
        string backup = Path.Combine(_tempDir, ".GameFolder.CMM_base");
        Directory.CreateDirectory(backup);
        
        state.RegisterMount(original, backup);
        mockVfs.SetMounted(true);

        // ACT: Cleanup de encerramento (Síncrono)
        orchestrator.ShutdownCleanup();

        // ASSERT: Deve ter restaurado
        Assert.True(Directory.Exists(original), "A pasta original não foi restaurada no ShutdownCleanup!");
        Assert.False(Directory.Exists(backup), "O backup ainda existe após o ShutdownCleanup!");
    }

    private class MockPathService : ICatPathService {
        public string BaseDataPath { get; set; } = "";
        public string ProfilesPath => Path.Combine(BaseDataPath, "profiles");
        public string GameSupportsPath => Path.Combine(BaseDataPath, "game_definitions");
        public string ActiveMountsFile => Path.Combine(BaseDataPath, "active_mounts.toml");
        public string DownloadsPath => Path.Combine(BaseDataPath, "downloads");
        public string GetProfilePath(string n) => Path.Combine(ProfilesPath, n + ".toml");
    }
    private class MockVfs : IVirtualFileSystem {
        public bool IsMounted          { get; private set; }
        public void SetMounted(bool val) => IsMounted = val;
        public event EventHandler<string>? ErrorOccurred;
        public void Mount(string m, List<Mod> a, string? d = null) => IsMounted = true;
        public void Unmount() => IsMounted = false;
        public void Dispose() { }
    }
    private class MockDriverService : IDriverService { public bool IsDriverInstalled() => true; }
    private class NullRootSwapService : IRootSwapService
    {
        public Task DeployAsync(IEnumerable<Mod> activeMods, string gameFolder) => Task.CompletedTask;
        public Task UndeployAsync(string gameFolder) => Task.CompletedTask;
        public Task UndeployModAsync(string modRootPath, string gameFolder) => Task.CompletedTask;
        public void RecoverStaleDeployments() { }
        public bool HasDeployedFiles(string gameFolder) => false;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
