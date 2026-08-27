using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Ui.Plugins;
using CatModManager.Ui.ViewModels;
using CatModManager.Tests.Support;
using CatModManager.PluginSdk;
using Avalonia.Headless.XUnit;
using Xunit;

namespace CatModManager.Tests.ViewModels;

/// <summary>
/// Installing a mod has to notify whoever owns persistence. It did not: a mod added through Add Mod
/// showed up in the list, mounted fine, and then vanished on the next start — still on disk, with
/// nothing in the profile pointing at it.
/// </summary>
public class InstallPersistsTests
{
    // AvaloniaFact, not Fact: the install path marshals list updates through
    // Dispatcher.UIThread, which never completes without a dispatcher running.
    [AvaloniaFact]
    public async Task FinishingAnInstallNotifiesTheOwner()
    {
        string modsFolder = Path.Combine(Path.GetTempPath(), "cmm-install-" + Path.GetRandomFileName());
        string installed  = Path.Combine(modsFolder, "MyMod");
        Directory.CreateDirectory(installed);

        try
        {
            var gameConfig = new GameConfigViewModel(new StubGameSupports(), new StubDiscovery(), new MockLogService())
            {
                ModsFolderPath = modsFolder
            };
            var modList = new ModListViewModel();

            var notified = new List<Mod>();

            var installer = new ModInstallationCoordinator(
                new StubModManagement(installed),
                new StubScanner(),
                new PhysicalFileService(),
                new MockLogService(),
                new AppSessionState(),
                uiExtensionHost: null,
                () => gameConfig,
                () => modList,
                (mod, _) => notified.Add(mod));

            await installer.InstallModAtMountPointAsync(Path.Combine(modsFolder, "MyMod.zip"), null);

            var mod = Assert.Single(notified);
            Assert.Equal("MyMod", mod.Name);
            Assert.Equal(installed, mod.ModRootPath);
            Assert.False(mod.IsInstalling);
        }
        finally
        {
            if (Directory.Exists(modsFolder)) Directory.Delete(modsFolder, recursive: true);
        }
    }

    private sealed class StubModManagement : IModManagementService
    {
        private readonly string _installed;
        public StubModManagement(string installed) => _installed = installed;

        public Task<string> InstallModAsync(string s, string t, string? o = null, IProgress<double>? p = null, CancellationToken ct = default)
            => Task.FromResult(_installed);
        public Task<string> InstallModFromMappingAsync(string a, string n, string t, Dictionary<string, string> m, string? o = null, IProgress<double>? p = null, CancellationToken ct = default)
            => Task.FromResult(_installed);
        public Task<string> InstallModToRootAsync(string a, string n, string t, IProgress<double>? p = null, CancellationToken ct = default)
            => Task.FromResult(_installed);
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
