using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;
using CatModManager.Tests.Support;
using CatModManager.Ui.Plugins;
using CatModManager.Ui.ViewModels;
using Xunit;

namespace CatModManager.Tests.ViewModels;

/// <summary>
/// Extractions must not all run at once. Three downloads finishing together used to start three
/// extractions together, each holding a decompression dictionary that can run to hundreds of
/// megabytes, with nothing bounding how many more could pile on behind them.
/// </summary>
public class ConcurrentInstallLimitTests
{
    [AvaloniaFact]
    public async Task InstallsBeyondTheLimitWaitInsteadOfAllRunningAtOnce()
    {
        string modsFolder = Path.Combine(Path.GetTempPath(), "cmm-conc-" + Path.GetRandomFileName());
        Directory.CreateDirectory(modsFolder);

        try
        {
            var gameConfig = new GameConfigViewModel(new StubGameSupports(), new StubDiscovery(), new MockLogService())
            {
                ModsFolderPath = modsFolder
            };

            var management = new BlockingModManagement(modsFolder);
            var installer = new ModInstallationCoordinator(
                management, new StubScanner(), new PhysicalFileService(), new MockLogService(),
                new AppSessionState(), null, () => gameConfig, () => new ModListViewModel(),
                (_, _) => { });

            var installs = Enumerable.Range(0, 6)
                .Select(i => installer.InstallModAtMountPointAsync(Path.Combine(modsFolder, $"Mod{i}.zip"), null))
                .ToArray();

            // Long enough that every install that *can* start has started.
            await Task.Delay(600);
            int peak = management.Peak;

            management.ReleaseAll();
            await Task.WhenAll(installs);

            Assert.Equal(6, management.Started);

            // The limit itself is a tuning decision; that there *is* one is the invariant.
            Assert.True(peak < 6, $"All 6 extractions ran concurrently (peak {peak}) — the limit is gone.");
        }
        finally
        {
            if (Directory.Exists(modsFolder)) Directory.Delete(modsFolder, recursive: true);
        }
    }

    /// <summary>Blocks inside the install so overlap can be observed, then lets everything finish.</summary>
    private sealed class BlockingModManagement : IModManagementService
    {
        private readonly string _modsFolder;
        private readonly ManualResetEventSlim _gate = new(false);
        private int _running;

        public int Peak;
        public int Started;

        public BlockingModManagement(string modsFolder) => _modsFolder = modsFolder;

        public void ReleaseAll() => _gate.Set();

        private async Task<string> RunAsync(string name)
        {
            Interlocked.Increment(ref Started);
            int now = Interlocked.Increment(ref _running);
            InterlockedMax(ref Peak, now);

            await Task.Run(() => _gate.Wait(TimeSpan.FromSeconds(20)));

            Interlocked.Decrement(ref _running);

            string path = Path.Combine(_modsFolder, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            do { seen = Volatile.Read(ref target); }
            while (value > seen && Interlocked.CompareExchange(ref target, value, seen) != seen);
        }

        public Task<string> InstallModAsync(string s, string t, string? o = null, IProgress<double>? p = null, CancellationToken ct = default)
            => RunAsync(Path.GetFileNameWithoutExtension(s));
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m, string? o = null, IProgress<double>? p = null, CancellationToken ct = default)
            => RunAsync(n);
        public Task<string> InstallModToRootAsync(string a, string n, string t, IProgress<double>? p = null, CancellationToken ct = default)
            => RunAsync(n);
    }

    private sealed class StubScanner : IModScanner
    {
        public Task<IEnumerable<Mod>> ScanDirectoryAsync(string p) => Task.FromResult<IEnumerable<Mod>>([]);
    }

    private sealed class StubGameSupports : IGameSupportService
    {
        public IGameSupport Default => new GenericGameSupport();
        public void RefreshSupports() { }
        public IEnumerable<IGameSupport> GetAllSupports() => [Default];
        public IGameSupport GetSupportById(string? id) => Default;
        public IGameSupport DetectSupport(string? path) => Default;
    }

    private sealed class StubDiscovery : CatModManager.Core.Services.GameDiscovery.IGameDiscoveryService
    {
        public Task<IReadOnlyList<CatModManager.Core.Services.GameDiscovery.GameInstallation>> ScanAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CatModManager.Core.Services.GameDiscovery.GameInstallation>>([]);
    }
}
