using System;
using System.IO;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;
using CmmPlugin.BethesdaTools.Services;
using NSubstitute;
using CatModManager.Tests.Support;
using Xunit;

namespace CatModManager.Tests.Plugins.BethesdaTools;

/// <summary>
/// The configured executable is not always the game, and is not always even a path.
///
/// A profile can legitimately launch the game through a wrapper — a container, a script, a bare
/// command like <c>distrobox-enter</c> with the real work in the launch arguments. Both the game
/// detection and the Wine-prefix search used to treat that string as if it were the game's
/// executable sitting inside the Steam library, so a perfectly configured Starfield profile
/// reported itself as not being a Bethesda game at all. The install folder is the reliable anchor.
/// </summary>
public class DetectionWithoutAGameExecutableTests
{
    private const string GameFolder = "/mnt/games/SteamLibraryGreen/steamapps/common/Starfield";

    private static IFileService FolderContaining(string gameFolder, string exeName)
    {
        var fs = Substitute.For<IFileService>();
        fs.FileExists(Path.Combine(gameFolder, exeName)).Returns(true);
        return fs;
    }

    /// <summary>The exact configuration that failed: the launch command is not a path at all.</summary>
    [Fact]
    public void AWrapperCommandWithNoDirectoryStillIdentifiesTheGame()
    {
        var detector = new BethesdaDetector(FolderContaining(GameFolder, "Starfield.exe"));

        var game = detector.Detect("distrobox-enter", GameFolder);

        Assert.NotNull(game);
        Assert.Equal("Starfield", game!.GameFolder);
    }

    /// <summary>An executable living somewhere else entirely must not veto the install folder.</summary>
    [Fact]
    public void ALauncherOutsideTheGameFolderDoesNotDefeatDetection()
    {
        var detector = new BethesdaDetector(FolderContaining(GameFolder, "Starfield.exe"));

        Assert.NotNull(detector.Detect("/usr/bin/steam", GameFolder));
    }

    /// <summary>With no executable configured at all, the folder alone is enough.</summary>
    [Fact]
    public void TheInstallFolderAloneIsEnough()
    {
        var detector = new BethesdaDetector(FolderContaining(GameFolder, "Starfield.exe"));

        Assert.NotNull(detector.Detect(null, GameFolder));
    }

    /// <summary>Nothing to go on is still not a game — the tab must not claim a false positive.</summary>
    [Fact]
    public void AFolderWithNoKnownExecutableIsNotAGame()
    {
        var detector = new BethesdaDetector(Substitute.For<IFileService>());

        Assert.Null(detector.Detect("distrobox-enter", "/mnt/games/steamapps/common/Something"));
    }

    /// <summary>The executable is still the most direct evidence when it happens to be the game.</summary>
    [Fact]
    public void ARealGameExecutableStillWinsWithoutTouchingTheDisk()
    {
        var fs = Substitute.For<IFileService>();
        var detector = new BethesdaDetector(fs);

        var game = detector.Detect(Path.Combine(GameFolder, "Starfield.exe"), gameFolder: null);

        Assert.NotNull(game);
        fs.DidNotReceiveWithAnyArgs().FileExists(default!);
    }

    /// <summary>
    /// The prefix search walks up to "steamapps" to find compatdata. Anchored on a bare command
    /// there is nothing to walk, so it gave up before probing a single prefix.
    /// </summary>
    [UnixFact("Windows uses shell folders, so there is no prefix walk")]
    public void ThePrefixSearchAnchorsOnTheInstallFolderNotTheLaunchCommand()
    {
        const string steamApps = "/mnt/games/SteamLibraryGreen/steamapps";
        const string prefix    = steamApps + "/compatdata/1716740/pfx";

        var fs = Substitute.For<IFileService>();
        fs.GetDirectories(Path.Combine(steamApps, "compatdata"))
          .Returns(new[] { steamApps + "/compatdata/1716740" });
        fs.GetDirectories(prefix + "/drive_c/users").Returns(new[] { prefix + "/drive_c/users/steamuser" });
        fs.DirectoryExists(prefix + "/drive_c/users").Returns(true);

        var log = Substitute.For<IPluginLogger>();
        var resolver = new GamePathResolver(fs, log);
        var game = new BethesdaGame("Starfield", UsesStarFormat: true, Masters());

        var path = resolver.GetPluginsTextPath(game, "distrobox-enter", GameFolder);

        Assert.NotNull(path);
        Assert.Contains("compatdata/1716740/pfx", path!.Replace('\\', '/'));
    }

    private static System.Collections.Generic.IReadOnlySet<string> Masters() =>
        new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
