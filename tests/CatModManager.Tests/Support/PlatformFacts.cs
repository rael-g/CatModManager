using System;
using Xunit;

namespace CatModManager.Tests.Support;

/// <summary>
/// A test that only means anything on Windows.
///
/// Use this instead of returning early from the body. An early return makes the test pass while
/// asserting nothing, so the run reports it as green and skipped-zero — the suite claims coverage
/// it does not have. This makes the runner say "skipped" out loud.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string because)
    {
        if (!OperatingSystem.IsWindows()) Skip = $"Windows only: {because}";
    }
}

/// <summary>
/// A test that only means anything away from Windows. Same reasoning as
/// <see cref="WindowsFactAttribute"/>.
/// </summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute(string because)
    {
        if (OperatingSystem.IsWindows()) Skip = $"Unix only: {because}";
    }
}
