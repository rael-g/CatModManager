using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CatModManager.PluginSdk;
using CmmPlugin.REEngine.Installers;

namespace CatModManager.Tests.Plugins;

public class PluginTests
{
    [Fact]
    public void ReEngineModInstaller_CanInstall_ReturnsTrue_ForValidArchiveAndGame()
    {
        var mockState = new MockModManagerState { GameExecutablePath = "re2.exe" };
        var mockExtractor = new MockArchiveExtractor();
        var installer = new ReEngineModInstaller(mockState, mockExtractor);

        // ACT
        bool can = installer.CanInstall("mod.zip");

        // ASSERT
        Assert.True(can);
    }

    [Fact]
    public async Task ReEngineModInstaller_Install_DetectsWrapperFolder()
    {
        var mockState = new MockModManagerState { GameExecutablePath = "re2.exe" };
        var mockExtractor = new MockArchiveExtractor();
        // Setup archive entries with a wrapper folder
        mockExtractor.FileList.Add("MyCoolMod/modinfo.ini");
        mockExtractor.FileList.Add("MyCoolMod/natives/stm/test.pak");
        
        var installer = new ReEngineModInstaller(mockState, mockExtractor);

        // ACT
        var result = await installer.InstallAsync("mod.zip", new MockInstallContext());

        // ASSERT
        Assert.True(result.IsSuccess);
        Assert.True(result.FileMapping.ContainsKey("MyCoolMod/"));
        Assert.Equal("", result.FileMapping["MyCoolMod/"]);
    }

    private class MockModManagerState : IModManagerState {
        public string? GameId => "re2";
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
        public IPluginLogger Log => new MockPluginLogger();
    }

    private class MockPluginLogger : IPluginLogger {
        public void Log(string m) { }
        public void LogError(string m, Exception? e = null) { }
    }
}
