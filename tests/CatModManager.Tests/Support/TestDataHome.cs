using System;
using System.IO;
using System.Runtime.CompilerServices;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Support;

/// <summary>
/// Points the whole suite at a throwaway data directory before a single test runs.
///
/// Patching call sites was not enough: the UI tests boot the application's real DI container, which
/// constructs <see cref="CatPathService"/> itself with no override to pass. The result was a suite
/// that wrote profiles into the developer's real data directory and — once AppDatabase started
/// migrating — altered the schema of their actual cmm.db.
///
/// A module initializer runs when the assembly is first touched, which is before xUnit has collected
/// anything, so there is no ordering to get wrong and no fixture for a test to forget to declare.
/// </summary>
internal static class TestDataHome
{
    /// <summary>Where this test run's data directory lives. One per process.</summary>
    internal static string Path { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Redirect()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "CMM_TestDataHome_" + Guid.NewGuid().ToString("N"));

        Environment.SetEnvironmentVariable(CatPathService.DataHomeVariable, Path);

        // Best effort: a run that is killed leaves the directory behind, which is a stale temp
        // folder rather than a problem. Failing to clean up must never fail the run.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
        };
    }
}
