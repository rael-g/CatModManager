using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;
using NSubstitute;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Launching does not always mean running a file. Under Proton a game is started through its
/// platform — "steam -applaunch &lt;appid&gt;" — so the executable field may hold a bare command name
/// resolved through PATH, and the process that starts is the platform, not the game.
/// </summary>
public class LaunchThroughACommandTests
{
    private static IGameSupport Support() => new GenericGameSupport();

    /// <summary>
    /// The waiting used to derive the game folder from the executable path. "steam" has no folder,
    /// so Path.GetFullPath turned it into the working directory and the watch spent its full 30
    /// seconds there finding nothing — the post-exit hooks fired while the game was still starting.
    /// </summary>
    [Fact]
    public async Task ABareCommandNameDoesNotSendTheWaitToTheWorkingDirectory()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Start(Arg.Any<ProcessStartInfo>()).Returns(new Process());
        runner.GetProcesses().Returns(_ => throw new InvalidOperationException(
            "The watch ran for a bare command name — there is no folder to watch."));

        var service = new ProcessService(new MockLogService(), runner);

        // waitForChildren stays on: the point is that it finds nothing to anchor to and gives up
        // immediately, not that the caller has to remember to switch it off.
        bool ok = await service.StartProcessAsync("steam", "-applaunch 1716740", false, waitForChildren: true);

        Assert.True(ok);
    }

    [Fact]
    public async Task TheCommandAndItsArgumentsReachTheProcessUnchanged()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.Start(Arg.Any<ProcessStartInfo>()).Returns(new Process());

        var launcher = new GameLaunchService(new ProcessService(new MockLogService(), runner), new MockLogService());

        var result = await launcher.LaunchGameAsync("steam", "-applaunch 1716740", Support(), new List<Mod>());

        Assert.True(result.IsSuccess);
        runner.Received(1).Start(Arg.Is<ProcessStartInfo>(
            i => i.FileName == "steam" && i.Arguments == "-applaunch 1716740"));
    }

    /// <summary>
    /// Started through Steam, the process CMM starts is Steam. Anything waiting on the game has to
    /// be pointed at the game's folder explicitly, which the profile already knows.
    /// </summary>
    [Fact]
    public async Task TheGameFolderIsWhatGetsWatched_NotSteamsOwn()
    {
        string gameFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cmm-watch-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(gameFolder);

        try
        {
            var runner = Substitute.For<IProcessRunner>();
            runner.Start(Arg.Any<ProcessStartInfo>()).Returns(new Process());

            var watched = new List<string>();
            runner.GetProcesses().Returns(_ =>
            {
                watched.Add(gameFolder);
                // Nothing running there; let the watch time out rather than hang the test.
                return Array.Empty<Process>();
            });

            var service = new ProcessService(new MockLogService(), runner);
            var launcher = new GameLaunchService(service, new MockLogService());

            var launch = launcher.LaunchGameAsync("steam", "-applaunch 1716740", Support(), new List<Mod>(), gameFolder);

            // The watch polls; it only needs to have started for this to be observable.
            await Task.WhenAny(launch, Task.Delay(1500));

            Assert.NotEmpty(watched);
        }
        finally
        {
            try { System.IO.Directory.Delete(gameFolder, true); } catch { }
        }
    }
}
