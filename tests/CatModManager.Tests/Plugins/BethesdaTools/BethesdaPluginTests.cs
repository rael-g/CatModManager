using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Installers;
using CmmPlugin.BethesdaTools.Services;

namespace CatModManager.Tests.Plugins.BethesdaTools;

public class BethesdaPluginTests
{
    private readonly BethesdaDetector _detector = new(new PhysicalFileService());

    [Fact]
    public async Task BethesdaModInstaller_Install_StripsDataPrefix()
    {
        var mockState = new MockModManagerState { GameExecutablePath = "SkyrimSE.exe" };
        var mockExtractor = new MockArchiveExtractor();
        
        // Setup archive entries: some in Data/, some in root
        mockExtractor.FileList.Add("Data/test.esp");
        mockExtractor.FileList.Add("readme.txt");
        
        var installer = new BethesdaModInstaller(mockState, mockExtractor, _detector);

        // ACT
        var result = await installer.InstallAsync("mod.zip", new MockInstallContext());

        // ASSERT
        Assert.True(result.IsSuccess);
        // "Data/test.esp" should be mapped to "test.esp" because VFS mounts at Data/ for Bethesda games usually
        Assert.Equal("test.esp", result.FileMapping["Data/test.esp"]);
        Assert.Equal("readme.txt", result.FileMapping["readme.txt"]);
    }

    [Fact]
    public async Task BethesdaModInstaller_Install_DetectsWrapperFolder()
    {
        var mockState = new MockModManagerState { GameExecutablePath = "SkyrimSE.exe" };
        var mockExtractor = new MockArchiveExtractor();
        
        // Setup archive entries with a wrapper folder
        mockExtractor.FileList.Add("CoolMod_v1/Data/test.esp");
        mockExtractor.FileList.Add("CoolMod_v1/readme.txt");
        
        var installer = new BethesdaModInstaller(mockState, mockExtractor, _detector);

        // ACT
        var result = await installer.InstallAsync("mod.zip", new MockInstallContext());

        // ASSERT
        Assert.True(result.IsSuccess);
        // Should detect CoolMod_v1 as wrapper and strip it
        Assert.Equal("test.esp", result.FileMapping["CoolMod_v1/Data/test.esp"]);
        Assert.Equal("readme.txt", result.FileMapping["CoolMod_v1/readme.txt"]);
    }

    [Fact]
    public async Task BethesdaModInstaller_Install_IgnoresDirectoryEntries()
    {
        var mockState = new MockModManagerState { GameExecutablePath = "Starfield.exe" };
        var mockExtractor = new MockArchiveExtractor();

        // A real CharGenFix.zip: explicit folder entries, then the single payload file. Routing a
        // folder entry maps "Data" onto itself, and the mapping installer copies that whole subtree
        // with CopyDirectory — so the dll landed at both SFSE/Plugins and Data/SFSE/Plugins.
        mockExtractor.FileList.Add("Data/");
        mockExtractor.FileList.Add("Data/SFSE/");
        mockExtractor.FileList.Add("Data/SFSE/Plugins/");
        mockExtractor.FileList.Add("Data/SFSE/Plugins/sfee.dll");

        var installer = new BethesdaModInstaller(mockState, mockExtractor, _detector);

        var result = await installer.InstallAsync("mod.zip", new MockInstallContext());

        Assert.True(result.IsSuccess);
        var only = Assert.Single(result.FileMapping);
        Assert.Equal("Data/SFSE/Plugins/sfee.dll", only.Key);
        Assert.Equal("SFSE/Plugins/sfee.dll", only.Value);
    }

    [Fact]
    public async Task BethesdaModInstaller_Install_KeepsGameContentFolderAtTheTop()
    {
        var mockState = new MockModManagerState { GameExecutablePath = "Starfield.exe" };
        var mockExtractor = new MockArchiveExtractor();

        // Everything sits under SFSE/, so the "single top folder" rule would call it a wrapper and
        // strip it — leaving Plugins/x.dll, which SFSE never loads. A folder the game itself owns
        // is content, not packaging, however lonely it looks.
        mockExtractor.FileList.Add("SFSE/Plugins/versionlib.bin");
        mockExtractor.FileList.Add("SFSE/Plugins/other.bin");

        var installer = new BethesdaModInstaller(mockState, mockExtractor, _detector);

        var result = await installer.InstallAsync("mod.zip", new MockInstallContext());

        Assert.True(result.IsSuccess);
        Assert.Equal("SFSE/Plugins/versionlib.bin", result.FileMapping["SFSE/Plugins/versionlib.bin"]);
    }

    private class MockModManagerState : IModManagerState {
        public string? GameId => "skyrimse";
        public string? ModsFolderPath => "";
        public string? DataFolderPath => "";
        public string? DownloadsFolderPath => "";
        public string? GameExecutablePath { get; set; }
        public string? NexusDomain => "";
        public string? CurrentProfileName => "";
        public IReadOnlyList<IModInfo> ActiveMods => new List<IModInfo>();
        public event Action<string>? ProfileChanged { add { } remove { } }
        public event Action<IModInfo, string>? ModInstalled { add { } remove { } }
        public void SetInstallFolderHint(string p) { }
        public void SetActiveDownloadCheck(Func<bool> c) { }
        public void RequestInstallMod(string p) { }
        public void RequestInstallMod(string p, FomodPreset? pr) { }
    }

    private class MockArchiveExtractor : IArchiveExtractor {
        public List<string> FileList { get; } = new();
        public Task ExtractAsync(string a, string d, IProgress<double>? p, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public IEnumerable<string> GetFileList(string a) => FileList;
        public System.IO.Stream? OpenFileStream(string a, string e) => null;
    }

    private class MockInstallContext : IInstallContext {
        public FomodPreset? FomodPreset => null;
        public string DestinationFolder => "";
        public IPluginLogger Log => null!;
    }
}
