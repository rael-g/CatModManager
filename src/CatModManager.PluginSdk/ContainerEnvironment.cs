using System;
using System.IO;

namespace CatModManager.PluginSdk;

/// <summary>
/// Detects running inside a distrobox/toolbx container and bridges the two gaps that causes.
///
/// These containers share the user's home and /tmp with the host, so most of CMM behaves
/// identically either way. Two things do not: the container has no desktop session of its own
/// (so xdg-open goes nowhere), and it sees the host filesystem remounted under /run/host
/// (so paths it reports back to the host may be unusable there).
/// </summary>
public static class ContainerEnvironment
{
    /// <summary>Where distrobox and toolbx mount the host's root filesystem.</summary>
    private const string HostRoot = "/run/host";

    /// <summary>
    /// True when this process runs inside a distrobox/toolbx container. The host never has
    /// /run/host, and distrobox-host-exec is what lets us reach back out to the desktop session.
    /// </summary>
    public static bool IsInsideContainer { get; } =
        OperatingSystem.IsLinux()
        && Directory.Exists(HostRoot)
        && File.Exists(HostExecCommandPath);

    /// <summary>Command that runs its arguments on the host instead of in the container.</summary>
    public const string HostExecCommand = "distrobox-host-exec";

    private const string HostExecCommandPath = "/usr/bin/" + HostExecCommand;

    /// <summary>
    /// Rewrites a path this process sees into the path the host uses for the same file.
    /// Only /run/host-prefixed paths differ: the shared home and mounted drives appear at the
    /// same location on both sides. Returns the path unchanged when there is nothing to strip.
    /// </summary>
    public static string ToHostPath(string path) =>
        path.StartsWith(HostRoot + "/", StringComparison.Ordinal)
            ? path[HostRoot.Length..]
            : path;
}
