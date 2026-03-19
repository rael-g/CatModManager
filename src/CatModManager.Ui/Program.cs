using Avalonia;
using Avalonia.Threading;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using CatModManager.Core.Services;
using Velopack;

namespace CatModManager.Ui;

sealed class Program
{
    private const string PipeName = "CatModManager_IPC_v1";

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
        string? nxmArg = args.FirstOrDefault(a =>
            a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));

        // If another CMM instance is already running, forward the link and exit
        if (TrySendToExistingInstance(nxmArg ?? string.Empty))
            return;

        // We are the primary instance
        StartPipeServer();
        if (nxmArg != null) _pendingNxmArg = nxmArg;

        // Bootstrap services for emergency VFS cleanup before DI is ready
        var logger = new LogService();
        var paths  = new CatPathService();
        var db     = new AppDatabase(paths);
        var state  = new VfsStateService(db, logger);

        try
        {
            VelopackApp.Build().Run();
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

    private static bool TrySendToExistingInstance(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400); // 400 ms timeout — fast fail if no server
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message);
            return true;
        }
        catch { return false; }
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
