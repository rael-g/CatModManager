using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;

namespace CatModManager.Tests.Core.Services;

public class ProcessServiceTests
{
    private readonly ILogService _logService;
    private readonly IProcessRunner _mockRunner;

    public ProcessServiceTests()
    {
        _logService = new MockLogService();
        _mockRunner = Substitute.For<IProcessRunner>();
    }

    [Fact]
    public async Task StartProcessAsync_ReturnsTrue_OnSuccess()
    {
        _mockRunner.StartAsync(Arg.Any<ProcessStartInfo>()).Returns(true);
        var service = new ProcessService(_logService, _mockRunner);

        var result = await service.StartProcessAsync("test.exe", "", false, false);

        Assert.True(result);
        await _mockRunner.Received(1).StartAsync(Arg.Is<ProcessStartInfo>(i => i.FileName == "test.exe"));
    }

    [Fact]
    public async Task StartProcessAsync_ReturnsFalse_OnException()
    {
        _mockRunner.StartAsync(Arg.Any<ProcessStartInfo>()).Returns(Task.FromException<bool>(new Exception("crash")));
        var service = new ProcessService(_logService, _mockRunner);

        var result = await service.StartProcessAsync("test.exe", "", false, false);

        Assert.False(result);
    }

    [Fact]
    public async Task OpenFolderAsync_CallsRunner_WithExplorer()
    {
        var service = new ProcessService(_logService, _mockRunner);
        string current = Directory.GetCurrentDirectory();

        await service.OpenFolderAsync(current);

        await _mockRunner.Received(1).StartAsync(Arg.Is<ProcessStartInfo>(i => 
            i.FileName.Contains("explorer") || i.FileName.Contains("xdg-open") || i.FileName == current));
    }
}
