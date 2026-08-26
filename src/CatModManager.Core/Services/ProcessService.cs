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

    public ProcessService(ILogService logService, IProcessRunner? runner = null)
    {
        _logService = logService;
        _runner = runner ?? new DefaultProcessRunner();
    }

    public async Task<bool> StartProcessAsync(string filePath, string arguments, bool runAsAdmin, bool waitForChildren = true)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = runAsAdmin ? "runas" : ""
            };

            var success = await _runner.StartAsync(info);
            if (!success) return false;

            if (waitForChildren)
                await WaitForGameDirectoryProcesses(filePath);

            return true;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to start process: {filePath}", ex);
            return false;
        }
    }

    private async Task WaitForGameDirectoryProcesses(string launcherPath)
    {
        var gameDir = Path.GetDirectoryName(Path.GetFullPath(launcherPath));
        if (string.IsNullOrEmpty(gameDir)) return;

        var prefix = gameDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var userSession = Process.GetCurrentProcess().SessionId;

        // Poll for up to 30 seconds after the launcher exits.
        var deadline = DateTime.UtcNow.AddSeconds(30);
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
                return;
            }
        }
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
            _runner.StartAsync(info);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to open folder: {candidate}", ex);
        }
        return Task.CompletedTask;
    }
}
