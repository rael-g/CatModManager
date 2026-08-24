using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.BethesdaTools.Models;
using CmmPlugin.BethesdaTools.Services;

namespace CatModManager.Tests.Plugins.BethesdaTools;

/// <summary>
/// Regression tests for the bug that made the PLUGINS tab useless on Linux: the plugin used
/// %LOCALAPPDATA%, which resolves to ~/.local/share there, so plugins.txt was read and written at a
/// path no Proton-hosted game ever looks at.
/// </summary>
public class GamePathResolverTests
{
    private readonly IPluginLogger _log = Substitute.For<IPluginLogger>();
    private readonly FakeTree _tree = new();
    private readonly GamePathResolver _resolver;

    private static readonly BethesdaGame Starfield =
        new("Starfield", UsesStarFormat: true, MyGamesFolder: "Starfield");

    private const string ExePath =
        "/home/u/.steam/steam/steamapps/common/Starfield/Starfield.exe";
    private const string Prefix =
        "/home/u/.steam/steam/steamapps/compatdata/1716740/pfx";

    public GamePathResolverTests()
    {
        _resolver = new GamePathResolver(_tree, _log);
    }

    [Fact]
    public void GetPluginsTextPath_FindsFileInsideProtonPrefix()
    {
        _tree.AddFile($"{Prefix}/drive_c/users/steamuser/AppData/Local/Starfield/Plugins.txt");

        var result = _resolver.GetPluginsTextPath(Starfield, ExePath);

        Assert.Equal($"{Prefix}/drive_c/users/steamuser/AppData/Local/Starfield/Plugins.txt", result);
    }

    [Fact]
    public void GetPluginsTextPath_MatchesExistingFileCaseInsensitively()
    {
        // Wine prefixes sit on a case-sensitive filesystem but hold paths written by Windows code,
        // so the real file is often "plugins.txt" while we look for "Plugins.txt". Matching the
        // wrong case would silently create a second file the game ignores.
        _tree.AddFile($"{Prefix}/drive_c/users/steamuser/AppData/Local/Starfield/plugins.txt");

        var result = _resolver.GetPluginsTextPath(Starfield, ExePath);

        Assert.Equal($"{Prefix}/drive_c/users/steamuser/AppData/Local/Starfield/plugins.txt", result);
    }

    [Fact]
    public void GetPluginsTextPath_ReturnsCreatablePath_WhenFileDoesNotExistYet()
    {
        // Prefix exists (game has run once) but plugins.txt hasn't been created.
        _tree.AddDirectory($"{Prefix}/drive_c/users/steamuser/AppData/Local");

        var result = _resolver.GetPluginsTextPath(Starfield, ExePath);

        Assert.Equal($"{Prefix}/drive_c/users/steamuser/AppData/Local/Starfield/Plugins.txt", result);
    }

    [Fact]
    public void GetPluginsTextPath_ProbesEveryCompatdataPrefix()
    {
        // We don't know the AppId, so unrelated prefixes sitting next to the right one must not
        // shadow it.
        _tree.AddDirectory("/home/u/.steam/steam/steamapps/compatdata/228980/pfx/drive_c/users/steamuser");
        _tree.AddFile($"{Prefix}/drive_c/users/steamuser/AppData/Local/Starfield/Plugins.txt");

        var result = _resolver.GetPluginsTextPath(Starfield, ExePath);

        Assert.StartsWith(Prefix, result);
    }

    [Fact]
    public void GetPluginsTextPath_ReturnsNull_WhenNoPrefixExists()
    {
        // Game installed but never launched — better to report it than to write a file into the
        // host's ~/.local/share where nothing will read it.
        _tree.AddDirectory("/home/u/.steam/steam/steamapps/common/Starfield");

        var result = _resolver.GetPluginsTextPath(Starfield, ExePath);

        Assert.Null(result);
    }

    [Fact]
    public void GetMyGamesPath_ResolvesDocumentsInsidePrefix()
    {
        _tree.AddDirectory($"{Prefix}/drive_c/users/steamuser/Documents/My Games/Starfield");

        var result = _resolver.GetMyGamesPath(Starfield, ExePath);

        Assert.Equal($"{Prefix}/drive_c/users/steamuser/Documents/My Games/Starfield", result);
    }

    [Fact]
    public void GetUserDirectory_FallsBackToNonSteamUser_ForPlainWinePrefixes()
    {
        // Lutris/Heroic prefixes use the real login name instead of "steamuser".
        _tree.AddFile($"{Prefix}/drive_c/users/raelg/AppData/Local/Starfield/Plugins.txt");
        _tree.AddDirectory($"{Prefix}/drive_c/users/Public");

        var result = _resolver.GetPluginsTextPath(Starfield, ExePath);

        Assert.Equal($"{Prefix}/drive_c/users/raelg/AppData/Local/Starfield/Plugins.txt", result);
    }

    /// <summary>Minimal in-memory filesystem; only the members GamePathResolver touches do anything.</summary>
    private sealed class FakeTree : IFileService
    {
        private readonly HashSet<string> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _dirs = new(StringComparer.Ordinal);

        public void AddFile(string path)
        {
            _files.Add(path);
            AddDirectory(Path.GetDirectoryName(path)!);
        }

        public void AddDirectory(string path)
        {
            while (!string.IsNullOrEmpty(path) && path != "/")
            {
                _dirs.Add(path);
                path = Path.GetDirectoryName(path)!;
            }
        }

        public bool FileExists(string path) => _files.Contains(path);
        public bool DirectoryExists(string path) => _dirs.Contains(path);

        public string[] GetDirectories(string path) =>
            _dirs.Where(d => Path.GetDirectoryName(d) == path).ToArray();

        public string[] GetFiles(string path, string searchPattern, bool recursive = false) =>
            _files.Where(f => Path.GetDirectoryName(f) == path).ToArray();

        public void CreateDirectory(string path) => AddDirectory(path);
        public void CopyFile(string s, string d, bool o) => throw new NotSupportedException();
        public void CopyDirectory(string s, string d) => throw new NotSupportedException();
        public void DeleteFile(string path) => throw new NotSupportedException();
        public void DeleteDirectory(string path, bool r) => throw new NotSupportedException();
        public void MoveDirectory(string f, string t) => throw new NotSupportedException();
        public string ReadAllText(string path) => throw new NotSupportedException();
        public void WriteAllText(string path, string c) => throw new NotSupportedException();
        public string[] ReadAllLines(string path) => throw new NotSupportedException();
        public void WriteAllLines(string path, string[] c) => throw new NotSupportedException();
    }
}
