using System;
using System.Diagnostics;
using System.IO;
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
        _mockRunner.Start(Arg.Any<ProcessStartInfo>()).Returns(new Process());
        var service = new ProcessService(_logService, _mockRunner);

        var result = await service.StartProcessAsync("test.exe", "", false, false);

        Assert.True(result);
        _mockRunner.Received(1).Start(Arg.Is<ProcessStartInfo>(i => i.FileName == "test.exe"));
    }

    [Fact]
    public async Task StartProcessAsync_ReturnsFalse_OnException()
    {
        _mockRunner.Start(Arg.Any<ProcessStartInfo>()).Returns(_ => throw new Exception("crash"));
        var service = new ProcessService(_logService, _mockRunner);

        var result = await service.StartProcessAsync("test.exe", "", false, false);

        Assert.False(result);
    }

    /// <summary>
    /// A launcher that outlives the game used to hang the whole call, because StartProcessAsync
    /// waited for it before looking for the game — and `distrobox-enter -n steam -- steam
    /// -applaunch` *is* the Steam session. The Launch button stayed disabled for the rest of the
    /// session. It must come back on the watch window alone.
    /// </summary>
    [Fact]
    public async Task StartProcessAsync_Returns_EvenWhenTheLauncherNeverExits()
    {
        _mockRunner.Start(Arg.Any<ProcessStartInfo>()).Returns(new Process());
        _mockRunner.GetProcesses().Returns([]);   // no game ever appears either
        var service = new ProcessService(_logService, _mockRunner, TimeSpan.FromMilliseconds(300));

        var call = service.StartProcessAsync(
            Path.Combine(Path.GetTempPath(), "nowhere", "game.exe"), "", false, waitForChildren: true);

        var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == call, "StartProcessAsync hung waiting on the launcher.");

        var result = await call;
        Assert.True(result.Started);
        Assert.False(result.GameObserved);
    }

    /// <summary>
    /// An external tool is handed over, not waited on. The call used to block until the user closed
    /// the tool, so the Tools tab showed "Launching BodySlide…" for as long as BodySlide was open.
    /// </summary>
    [Fact]
    public async Task StartProcessAsync_ForATool_ReturnsWithoutWaitingForItToClose()
    {
        _mockRunner.Start(Arg.Any<ProcessStartInfo>()).Returns(new Process());
        _mockRunner.WaitForExitAsync(Arg.Any<Process>()).Returns(new TaskCompletionSource().Task);

        // A watch window long enough that waiting on anything at all would blow the assertion.
        var service = new ProcessService(_logService, _mockRunner, TimeSpan.FromMinutes(5));

        var call = service.StartProcessAsync("wine", "game.exe", false, waitForChildren: false);
        var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(finished == call, "StartProcessAsync waited for the tool to exit.");
        Assert.True((await call).Started);
    }

    /// <summary>
    /// A command that is not installed has to be reported. It was not: with UseShellExecute the
    /// name went to the desktop's default handler, which produced a process and a success, so
    /// launching "wine" without wine installed logged nothing and claimed to have worked.
    /// </summary>
    [Fact]
    public async Task StartProcessAsync_DoesNotUseTheShell_SoAMissingCommandFails()
    {
        ProcessStartInfo? captured = null;
        _mockRunner.Start(Arg.Do<ProcessStartInfo>(i => captured = i)).Returns(new Process());
        var service = new ProcessService(_logService, _mockRunner);

        await service.StartProcessAsync("wine", "game.exe", runAsAdmin: false, waitForChildren: false);

        Assert.NotNull(captured);
        Assert.False(captured!.UseShellExecute);
    }

    [Fact]
    public async Task OpenFolderAsync_CallsRunner_WithExplorer()
    {
        var service = new ProcessService(_logService, _mockRunner);
        string current = Directory.GetCurrentDirectory();

        await service.OpenFolderAsync(current);

        _mockRunner.Received(1).Start(Arg.Is<ProcessStartInfo>(i => 
            i.FileName.Contains("explorer") || i.FileName.Contains("xdg-open") || i.FileName == current));
    }
}
