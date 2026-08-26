using Avalonia;
using Avalonia.Threading;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using CatModManager.Core.Services;
using CatModManager.Ui.Services;

namespace CatModManager.Ui;

sealed class Program
{
    private const string PipeName = "CatModManager_IPC_v1";
    private static readonly string LockPath = Path.Combine(Path.GetTempPath(), "CatModManager_IPC_v1.lock");
    private static FileStream? _instanceLock;

    /// <summary>Fired on the UI thread when a new nxm:// URI arrives via the IPC pipe.</summary>
    public static event Action<string>? NxmReceived;

    private static string? _pendingNxmArg;

    /// <summary>Consumes the nxm:// argument captured at startup (returns it once, then null).</summary>
    public static string? ConsumePendingNxmArg()
    {
        var v = _pendingNxmArg;
        _pendingNxmArg = null;
        return v;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // Older registrations wrote Exec=… "%u", which hands us the URL wrapped in literal
        // quotes. Strip them so an app installed before that was fixed still works without
        // the user having to re-register the nxm:// handler.
        string? nxmArg = args
            .Select(a => a.Trim('"', '\''))
            .FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));

        // If another CMM instance is already running, forward the link and exit.
        // Retried with backoff because an existing instance launched moments ago
        // may not have its IPC pipe listening yet (e.g. two nxm:// clicks in a row).
        if (!string.IsNullOrEmpty(nxmArg))
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (TrySendToExistingInstance(nxmArg))
                    return;
                if (attempt < 5) Thread.Sleep(300);
            }
        }

        // We got here because no running instance answered, so this process owns the link.
        _pendingNxmArg = nxmArg;

        // Only the instance holding the exclusive lock runs the IPC pipe server.
        // NamedPipeServerStream on Linux silently unlinks and rebinds an existing
        // socket file at the same path instead of failing to bind, so without this
        // gate a second instance can steal the first one's pipe out from under it,
        // leaving the original instance unreachable for the rest of its lifetime.
        if (TryAcquireInstanceLock())
        {
            IpcLog($"acquired instance lock; serving IPC (nxm={nxmArg ?? "none"})");
            StartPipeServer();
        }
        else
        {
            IpcLog($"lock held by another instance; watching for takeover (nxm={nxmArg ?? "none"})");
            WatchForInstanceLock();
        }

        // Bootstrap services for emergency VFS cleanup before DI is ready
        var logger = new LogService();
        var paths  = new CatPathService();
        var db     = new AppDatabase(paths);
        var state  = new VfsStateService(db, logger);

        GitHubUpdateChecker.CheckInBackground("rael-g", "CatModManager", logger);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogError("Fatal startup error", ex);
            try { state.RecoverStaleMounts(); } catch { }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // ── Single-instance IPC ───────────────────────────────────────────────────

    private static bool TryAcquireInstanceLock()
    {
        try
        {
            _instanceLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps trying to become the IPC server for as long as someone else holds the lock.
    ///
    /// Without this, ownership was decided once at startup and never revisited: when the holder
    /// exited, every other running instance stayed silent forever, so each nxm:// click found
    /// nobody listening and opened yet another window. That is easy to hit with two CMMs around
    /// (a host one and one inside distrobox, which share /tmp and therefore share this lock).
    /// </summary>
    private static void WatchForInstanceLock()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(2000);
                if (!TryAcquireInstanceLock()) continue;
                StartPipeServer();
                return;
            }
        })
        {
            IsBackground = true,
            Name = "CMM IPC Takeover"
        };
        thread.Start();
    }

    /// <summary>
    /// Startup diagnostics for the nxm:// hand-off, written before DI (and therefore ILogService)
    /// exists. Goes to a file because a process launched by xdg-open has nowhere to print: its
    /// stdout is the desktop session's, which nobody reads. Every failure here used to be
    /// swallowed, which is why "clicking download opens a second CMM" stayed unexplained.
    /// </summary>
    private static readonly string IpcLogPath =
        Path.Combine(Path.GetTempPath(), "cmm-ipc.log");

    private static void IpcLog(string message)
    {
        try
        {
            File.AppendAllText(IpcLogPath,
                $"{DateTime.Now:HH:mm:ss} [{Environment.ProcessId}] {message}\n");
        }
        catch { /* diagnostics must never break startup */ }
    }

    private static bool TrySendToExistingInstance(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400); // 400 ms timeout — fast fail if no server
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message);
            IpcLog($"forwarded to existing instance: {message}");
            return true;
        }
        catch (Exception ex)
        {
            IpcLog($"forward failed ({ex.GetType().Name}: {ex.Message}); " +
                   $"tmp={Path.GetTempPath()} socket={File.Exists(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + PipeName))}");
            return false;
        }
    }

    private static void StartPipeServer()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string? msg = reader.ReadLine();

                    if (msg?.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        string captured = msg;
                        Dispatcher.UIThread.InvokeAsync(() => NxmReceived?.Invoke(captured));
                    }
                }
                catch { /* pipe broken / app exiting — restart loop */ }
            }
        })
        {
            IsBackground = true,
            Name = "CMM IPC Server"
        };
        thread.Start();
    }
}
