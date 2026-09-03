using CatModManager.PluginSdk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CatModManager.Core.Services;

public class ProcessService : IProcessService
{
    private readonly ILogService _logService;
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _gameWatchWindow;

    /// <param name="gameWatchWindow">
    /// How long to keep looking for the game after the launcher is started. Overridable so tests
    /// that exercise the window expiring do not have to spend the real 30 seconds doing it.
    /// </param>
    public ProcessService(ILogService logService, IProcessRunner? runner = null, TimeSpan? gameWatchWindow = null)
    {
        _logService = logService;
        _runner = runner ?? new DefaultProcessRunner();
        _gameWatchWindow = gameWatchWindow ?? TimeSpan.FromSeconds(30);
    }

    public async Task<ProcessRunResult> StartProcessAsync(string filePath, string arguments, bool runAsAdmin, bool waitForChildren = true, string? watchFolder = null)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = arguments,

                // Only when elevating, which needs the shell verb and is Windows-only. Otherwise
                // false, so that a command which does not exist fails here with an exception we can
                // report. With it true on Linux, .NET hands the name to the desktop's default
                // handler instead: launching "wine" without wine installed produced a process, a
                // success, and no log line at all.
                UseShellExecute = runAsAdmin,
                Verb = runAsAdmin ? "runas" : ""
            };

            var started = _runner.Start(info);
            if (started == null) return new ProcessRunResult(false, false);

            // An external tool is handed over and left alone. Waiting for it held the caller for as
            // long as the user kept the tool open, which is why "Launching BodySlide…" stayed on
            // screen for the entire session.
            if (!waitForChildren) return new ProcessRunResult(true, false, _runner.WaitForExitAsync(started));

            // A game launcher is different: it is a means, not the thing being run. `steam
            // -applaunch` hands off and exits in a moment, while `distrobox-enter -n steam -- steam
            // -applaunch` *is* the Steam session and lives as long as it does — so the game is
            // searched for independently of whether the launcher is still around.
            bool observed = await WaitForGameDirectoryProcesses(watchFolder ?? DirectoryOf(filePath));

            return new ProcessRunResult(true, observed);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to start process: {filePath}", ex);
            return new ProcessRunResult(false, false);
        }
    }

    /// <summary>
    /// The folder holding <paramref name="fileName"/>, or null when it is a bare command name
    /// resolved through PATH — "steam" is not a file in the working directory, and treating it as
    /// one made this watch the working directory and find nothing for its full 30 seconds.
    /// </summary>
    private static string? DirectoryOf(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        if (fileName.IndexOf(Path.DirectorySeparatorChar) < 0 &&
            fileName.IndexOf(Path.AltDirectorySeparatorChar) < 0)
            return null;

        try { return Path.GetDirectoryName(Path.GetFullPath(fileName)); }
        catch { return null; }
    }

    /// <summary>
    /// Waits for the game to exit. Returns whether it was ever seen at all — a launch that never
    /// produced a process in the game folder is not a session that ended, and callers that undo
    /// things on exit must not treat it as one.
    /// </summary>
    private async Task<bool> WaitForGameDirectoryProcesses(string? gameDir)
    {
        if (string.IsNullOrEmpty(gameDir)) return false;

        var prefix = gameDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var userSession = Process.GetCurrentProcess().SessionId;

        var deadline = DateTime.UtcNow + _gameWatchWindow;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100); // Faster polling for tests
            
            var children = new List<Process>();
            foreach (var proc in _runner.GetProcesses())
            {
                try
                {
                    if (proc.SessionId != userSession) continue;
                    var exe = _runner.GetMainModuleFileName(proc);
                    if (exe != null && exe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        children.Add(proc);
                }
                catch { }
            }

            if (children.Count > 0)
            {
                _logService.Log($"Detected {children.Count} game process(es); waiting for exit...");
                await Task.WhenAll(children.Select(p => _runner.WaitForExitAsync(p)));
                return true;
            }
        }

        _logService.Log($"No game process appeared under '{gameDir}' within the wait window.");
        return false;
    }

    public Task OpenFolderAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return Task.CompletedTask;
        var candidate = path;
        while (!string.IsNullOrEmpty(candidate) && !Directory.Exists(candidate))
            candidate = Path.GetDirectoryName(candidate) ?? "";
        if (string.IsNullOrEmpty(candidate)) return Task.CompletedTask;

        try
        {
            var info = new ProcessStartInfo { FileName = candidate, UseShellExecute = true };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                info.FileName = "explorer.exe";
                info.ArgumentList.Add(candidate);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                info.FileName = "xdg-open";
                info.ArgumentList.Add(candidate);

                // A distrobox container has no desktop session, so xdg-open there resolves to
                // nothing and the folder silently never opens. Hand the call to the host instead.
                if (ContainerEnvironment.IsInsideContainer)
                {
                    info.ArgumentList.Insert(0, info.FileName);
                    info.FileName = ContainerEnvironment.HostExecCommand;
                }
            }
            _runner.Start(info);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to open folder: {candidate}", ex);
        }
        return Task.CompletedTask;
    }
}
