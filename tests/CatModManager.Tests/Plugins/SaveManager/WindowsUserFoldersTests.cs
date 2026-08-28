using System;
using System.IO;
using CatModManager.PluginSdk;
using NSubstitute;
using Xunit;

namespace CatModManager.Tests.Plugins.SaveManager;

/// <summary>
/// Save folders are declared as Windows paths — <c>%APPDATA%\RE2</c>,
/// <c>%USERPROFILE%\Documents\My Games\Starfield\Saves</c> — in every game definition.
///
/// On Linux that path is wrong three times over: the variables are not in the environment, the
/// separator is not a separator, and the folder is not on the host at all — a game under Proton
/// writes inside its Wine prefix. Expanding it against the host environment gave back the literal
/// string, so the Save Manager reported "save folder not found" for every game on Linux, always.
/// </summary>
public class WindowsUserFoldersTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "CMM_WUF_" + Guid.NewGuid().ToString("N"));

    private readonly string _steamApps;
    private readonly string _gameFolder;

    public WindowsUserFoldersTests()
    {
        _steamApps  = Path.Combine(_root, "SteamLibrary", "steamapps");
        _gameFolder = Path.Combine(_steamApps, "common", "Starfield");
        Directory.CreateDirectory(_gameFolder);
    }

    /// <summary>Creates &lt;steamapps&gt;/compatdata/&lt;appId&gt;/pfx/... and returns the user directory.</summary>
    private string Prefix(string appId, string user = "steamuser")
    {
        string userDir = Path.Combine(_steamApps, "compatdata", appId, "pfx", "drive_c", "users", user);
        Directory.CreateDirectory(userDir);
        return userDir;
    }

    private WindowsUserFolders Resolver() =>
        new(new PhysicalFileService(), Substitute.For<IPluginLogger>());

    /// <summary>The real Starfield case, and the reason the tab was dead on Linux.</summary>
    [Fact]
    public void AUserProfilePathResolvesInsideTheWinePrefix()
    {
        string expected = Path.Combine(Prefix("1716740"), "Documents", "My Games", "Starfield", "Saves");
        Directory.CreateDirectory(expected);

        var actual = Resolver().Resolve(@"%USERPROFILE%\Documents\My Games\Starfield\Saves", _gameFolder);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AppDataMeansRoamingInsideThePrefix()
    {
        string expected = Path.Combine(Prefix("883710"), "AppData", "Roaming", "RE2");
        Directory.CreateDirectory(expected);

        Assert.Equal(expected, Resolver().Resolve(@"%APPDATA%\RE2", _gameFolder));
    }

    [Fact]
    public void LocalAppDataMeansLocalInsideThePrefix()
    {
        string expected = Path.Combine(Prefix("1086940"), "AppData", "Local", "Larian Studios", "Savegames");
        Directory.CreateDirectory(expected);

        Assert.Equal(expected, Resolver().Resolve(@"%LOCALAPPDATA%\Larian Studios\Savegames", _gameFolder));
    }

    /// <summary>
    /// A prefix is Windows-authored content on a case-sensitive filesystem, so the casing on disk
    /// is whatever the installer happened to write.
    /// </summary>
    [Fact]
    public void CasingOnDiskDoesNotHaveToMatchTheDefinition()
    {
        string expected = Path.Combine(Prefix("1716740"), "appdata", "roaming", "re2");
        Directory.CreateDirectory(expected);

        Assert.Equal(expected, Resolver().Resolve(@"%APPDATA%\RE2", _gameFolder));
    }

    /// <summary>
    /// Prefixes are keyed by Steam AppId, which the plugin does not have, so several get probed.
    /// Picking the first one would back up an unrelated game's folder — or nothing.
    /// </summary>
    [Fact]
    public void ThePrefixThatActuallyHasTheFolderIsTheOneChosen()
    {
        Prefix("0000001");   // decoy, sorts first, holds nothing
        string expected = Path.Combine(Prefix("1716740"), "AppData", "Roaming", "RE2");
        Directory.CreateDirectory(expected);

        Assert.Equal(expected, Resolver().Resolve(@"%APPDATA%\RE2", _gameFolder));
    }

    /// <summary>The Steam-ID subfolder FromSoftware games create.</summary>
    [Fact]
    public void ATrailingWildcardPicksTheNumericSubfolder()
    {
        string parent   = Path.Combine(Prefix("1245620"), "AppData", "Roaming", "EldenRing");
        string expected = Path.Combine(parent, "76561198000000000");
        Directory.CreateDirectory(expected);
        Directory.CreateDirectory(Path.Combine(parent, "Logs"));   // non-numeric sibling

        Assert.Equal(expected, Resolver().Resolve(@"%APPDATA%\EldenRing\*", _gameFolder));
    }

    /// <summary>
    /// The dangerous failure mode. Returning a plausible-but-absent path would make the tab claim a
    /// save folder it cannot read, and a restore would then unpack saves into a directory the game
    /// never looks at — silently, with the UI reporting success.
    /// </summary>
    [Fact]
    public void AMissingFolderIsReportedAsMissingNotInvented()
    {
        Prefix("1716740");   // a valid prefix, but the game has never written its saves

        Assert.Null(Resolver().Resolve(@"%APPDATA%\NeverPlayed", _gameFolder));
    }

    /// <summary>
    /// The prefix search walks up to "steamapps". Anchored on the launch command there is nothing
    /// to walk — and a bare command is exactly how a game started through a container is configured.
    /// </summary>
    [Fact]
    public void TheInstallFolderIsTheAnchorNotTheLaunchCommand()
    {
        string expected = Path.Combine(Prefix("1716740"), "Documents", "My Games", "Starfield", "Saves");
        Directory.CreateDirectory(expected);

        var actual = Resolver().Resolve(@"%USERPROFILE%\Documents\My Games\Starfield\Saves",
                                        _gameFolder, executablePath: "distrobox-enter");

        Assert.Equal(expected, actual);
    }

    /// <summary>With no prefix anywhere, there is no save folder — and no exception either.</summary>
    [Fact]
    public void NoPrefixMeansNoFolderRatherThanACrash()
    {
        Assert.Null(Resolver().Resolve(@"%APPDATA%\RE2", _gameFolder));
        Assert.Null(Resolver().Resolve(@"%APPDATA%\RE2", null));
        Assert.Null(Resolver().Resolve(null, _gameFolder));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }
}
