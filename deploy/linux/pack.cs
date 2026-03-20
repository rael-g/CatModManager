// Cat Mod Manager — Linux Packer
// Usage: dotnet run --file deploy/linux/pack.cs -- <version> [channel]
//        (or cd deploy/linux && dotnet run --file pack.cs -- <version>)
//
// Prerequisites:
//   • .NET 10 SDK
//   • Velopack CLI   (dotnet tool install -g vpk)
//   • libfuse3-dev   (sudo apt install libfuse3-dev  OR  sudo dnf install fuse3-devel)

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

// ── 2. Bundle libfuse3 ───────────────────────────────────────────────────────
Log("Bundling libfuse3...");

string? libfuse = FindLibfuse();
if (libfuse is null)
{
    Console.Error.WriteLine("ERROR: libfuse3.so.3 not found on this machine.");
    Console.Error.WriteLine("       Install it with:");
    Console.Error.WriteLine("         Debian/Ubuntu : sudo apt install libfuse3-dev");
    Console.Error.WriteLine("         Fedora/RHEL   : sudo dnf install fuse3-devel");
    Environment.Exit(1);
}

string real = ResolveSymlink(libfuse);
Console.WriteLine($"    Found: {real}");
File.Copy(real, Path.Combine(PublishDir, "libfuse3.so.3"), overwrite: true);

// ── 3. Pack with Velopack ────────────────────────────────────────────────────
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

static string? FindLibfuse()
{
    // 1. ldconfig cache
    try
    {
        var psi = new ProcessStartInfo("ldconfig", "-p")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true
        };
        using var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        var line = output
            .Split('\n')
            .FirstOrDefault(l => l.Contains("libfuse3.so.3 "));

        if (line is not null)
        {
            var parts = line.Split("=>");
            if (parts.Length > 1) return parts[1].Trim();
        }
    }
    catch { /* ldconfig not available */ }

    // 2. Well-known paths fallback
    string[] candidates =
    [
        "/usr/lib/x86_64-linux-gnu/libfuse3.so.3",
        "/usr/lib64/libfuse3.so.3",
        "/usr/lib/libfuse3.so.3"
    ];

    return candidates.FirstOrDefault(File.Exists);
}

static string ResolveSymlink(string path)
{
    while (true)
    {
        var info = new FileInfo(path);
        var target = info.LinkTarget;
        if (target is null) return path;
        path = Path.IsPathRooted(target) ? target : Path.Combine(info.DirectoryName!, target);
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
