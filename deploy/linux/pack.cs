// Cat Mod Manager — Linux Packer
// Usage: dotnet run --file deploy/linux/pack.cs -- <version> [channel]
//        (or cd deploy/linux && dotnet run --file pack.cs -- <version>)
//
// Prerequisites:
//   • .NET 10 SDK
//   • Velopack CLI   (dotnet tool install -g vpk)

using System.Diagnostics;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --file pack.cs -- <version> [channel]");
    Environment.Exit(1);
}

string version = args[0];
string channel = args.Length > 1 ? args[1] : "stable";

// Always work relative to this script's own directory.
Directory.SetCurrentDirectory(ScriptDir());

string project    = Path.GetFullPath("../../src/CatModManager.Ui/CatModManager.Ui.csproj");
const string PublishDir = "publish";
const string OutputDir  = "releases";

// ── 1. Publish ───────────────────────────────────────────────────────────────
Log("Publishing (linux-x64, self-contained)...");
Run("dotnet", $"publish \"{project}\" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false -o {PublishDir}");

// ── 2. Pack with Velopack ────────────────────────────────────────────────────
Log($"Packing AppImage v{version} (channel: {channel})...");
Run("vpk", $"pack --packId CatModManager --packTitle \"Cat Mod Manager\" --packVersion {version} --packDir {PublishDir} --mainExe CatModManager.Ui --channel {channel} --outputDir {OutputDir}");

Console.WriteLine();
Console.WriteLine($"Done. AppImage written to {Path.GetFullPath(OutputDir)}");

// ── Helpers ──────────────────────────────────────────────────────────────────

static void Log(string msg) => Console.WriteLine($"==> {msg}");

static void Run(string exe, string arguments)
{
    var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = false };
    using var proc = Process.Start(psi) ?? throw new Exception($"Failed to start: {exe}");
    proc.WaitForExit();
    if (proc.ExitCode != 0)
    {
        Console.Error.WriteLine($"'{exe}' exited with code {proc.ExitCode}");
        Environment.Exit(proc.ExitCode);
    }
}

static string ScriptDir()
{
    const string scriptName = "pack.cs";
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "deploy", "linux", scriptName)))
            return Path.Combine(dir.FullName, "deploy", "linux");
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}
