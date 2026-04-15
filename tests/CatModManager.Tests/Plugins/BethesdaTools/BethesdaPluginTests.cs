using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Installers;

namespace CatModManager.Tests.Plugins.BethesdaTools;

public class BethesdaPluginTests
{
    [Fact]
    public async Task BethesdaModInstaller_Install_StripsDataPrefix()
    {
        var mockState = new MockModManagerState { GameExecutablePath = "SkyrimSE.exe" };
        var mockExtractor = new MockArchiveExtractor();
        
        // Setup archive entries: some in Data/, some in root
        mockExtractor.FileList.Add("Data/test.esp");
        mockExtractor.FileList.Add("readme.txt");
        
        var installer = new BethesdaModInstaller(mockState, mockExtractor);

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
        
        var installer = new BethesdaModInstaller(mockState, mockExtractor);

        // ACT
        var result = await installer.InstallAsync("mod.zip", new MockInstallContext());

        // ASSERT
        Assert.True(result.IsSuccess);
        // Should detect CoolMod_v1 as wrapper and strip it
        Assert.Equal("test.esp", result.FileMapping["CoolMod_v1/Data/test.esp"]);
        Assert.Equal("readme.txt", result.FileMapping["CoolMod_v1/readme.txt"]);
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
