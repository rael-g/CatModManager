using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Vfs;
using CatModManager.VirtualFileSystem;

namespace CatModManager.Tests.Support;

// The doubles every view-model test needs, in one place.
//
// Each of these used to be a private nested class, re-declared in four or five test files. They had
// already drifted — one file's path service was the *real* one, which is how a test run came to
// write into the user's own database — and every interface change meant fixing the same class four
// times. A new test could not reach any of them at all, which is what finally forced this move.
//
// They are deliberately dumb: no behaviour beyond what an interface demands. A double that starts
// making decisions becomes a second implementation to keep correct.

public class MockPathService : ICatPathService
{
    public string BaseDataPath      { get; set; } = "";
    public string ProfilesPath      => Path.Combine(BaseDataPath, "profiles");
    public string GameSupportsPath  => Path.Combine(BaseDataPath, "game_definitions");
    public string ActiveMountsFile  => Path.Combine(BaseDataPath, "active_mounts.toml");
    public string DownloadsPath     => Path.Combine(BaseDataPath, "downloads");
    public string GetProfilePath(string name) => Path.Combine(ProfilesPath, name + ".toml");
}

public class MockModScanner : IModScanner
{
    public Task<IEnumerable<Mod>> ScanDirectoryAsync(string path) => Task.FromResult(Enumerable.Empty<Mod>());
}

public class MockProcessService : IProcessService
{
    public Task<ProcessRunResult> StartProcessAsync(
        string file, string args, bool admin = false, bool waitForChildren = true, string? watch = null)
        => Task.FromResult(new ProcessRunResult(true, false));

    public Task OpenFolderAsync(string path) => Task.CompletedTask;
}

public class MockModManagementService : IModManagementService
{
    /// <summary>
    /// Where an install claims to have put the mod. Left null, each call answers with the target it
    /// was given, which is what a test that does not care about the path wants. Set it to make every
    /// install land somewhere chosen — how the tests about a mod installed outside the mods folder
    /// arrange their subject.
    /// </summary>
    public string? ResultPath { get; set; }

    public Task<string> InstallModAsync(
        string source, string target, string? only = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.FromResult(ResultPath ?? "");

    public Task<string> InstallModFromMappingAsync(
        string archive, string name, string target, Dictionary<string, string> map, string? only = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.FromResult(ResultPath ?? target);

    public Task<string> InstallModToRootAsync(
        string archive, string name, string target,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.FromResult(ResultPath ?? target);
}

public class MockFileService : StubFileService
{
    /// <summary>Makes every path appear to exist, for tests about what happens when it does.</summary>
    public bool ForceExists { get; set; }

    public override bool FileExists(string path)      => ForceExists;
    public override bool DirectoryExists(string path) => ForceExists;
}

public class MockConfigService : IConfigService
{
    public AppConfig Current { get; } = new();
    public void Save() { }
    public void Load() { }
}

public class MockGameSupportService : IGameSupportService
{
    public IGameSupport Default => new GenericGameSupport();
    public void RefreshSupports() { }
    public IEnumerable<IGameSupport> GetAllSupports() => new[] { Default };
    public IGameSupport GetSupportById(string? id)    => Default;
    public IGameSupport DetectSupport(string? path)   => Default;
}

public class MockVfsStateService : IVfsStateService
{
    public void RegisterMount(string original, string backup) { }
    public void UnregisterMount(string original) { }
    public void RecoverStaleMounts() { }
}

public class MockVfsOrchestrationService : IVfsOrchestrationService
{
    public bool IsMounted { get; private set; }
    public void SetMounted(bool value) => IsMounted = value;

    public Task<OperationResult> MountAsync(MountOptions options)
    {
        IsMounted = true;
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> UnmountAsync()
    {
        IsMounted = false;
        return Task.FromResult(OperationResult.Success());
    }

    public void RecoverStaleMounts() { }

    public Task ShutdownCleanupAsync()
    {
        IsMounted = false;
        return Task.CompletedTask;
    }
}

public sealed class NullHardlinkStateStore : IHardlinkStateStore
{
    public void Save(string mountPoint, IReadOnlyList<HardlinkStateEntry> entries) { }
    public IReadOnlyList<HardlinkStateEntry> Load(string? mountPoint) => Array.Empty<HardlinkStateEntry>();
    public void Clear(string? mountPoint) { }
}
