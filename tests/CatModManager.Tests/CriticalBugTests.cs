using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Vfs;
using CatModManager.VirtualFileSystem;

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
        var resolver = new SimpleConflictResolver(_logService, new SevenZipArchiveExtractor());
        // Raiz com o nome proibido ".CMM_base"
        string backupDir = Path.Combine(_tempDir, "Game.CMM_base"); 
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "test.txt"), "data");

        // ACT: Escanear essa pasta. Não deve retornar vazio.
        var result = resolver.ResolveConflicts(new List<Mod>(), backupDir, null, null);

        // ASSERT
        Assert.True(result.ContainsKey("test.txt"), "O scanner falhou em mapear arquivos da raiz porque o nome da pasta raiz contém 'CMM_base'.");
    }

    [Fact]
    public async Task Shutdown_Cleanup_MUST_Restore_Folders_Asynchronously()
    {
        var state = new VfsStateService(new AppDatabase(_pathService), _logService);
        var orchestrator = new VfsOrchestrationService(
            new SimpleConflictResolver(_logService, new SevenZipArchiveExtractor()),
            new NullHardlinkStateStore(),
            state,
            _logService,
            null);
        string original = Path.Combine(_tempDir, "GameFolder");
        string backup = Path.Combine(_tempDir, ".GameFolder.CMM_base");
        Directory.CreateDirectory(backup);
        
        state.RegisterMount(original, backup);

        // ACT: Cleanup de encerramento
        await orchestrator.ShutdownCleanupAsync();

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
    private sealed class NullHardlinkStateStore : IHardlinkStateStore
    {
        public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
        public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
        public void Clear(string? mountPoint) { }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
