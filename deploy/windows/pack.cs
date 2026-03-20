// Cat Mod Manager — Windows Packer
// Usage: dotnet run --file deploy\windows\pack.cs -- <version> [iscc-path]
//        (or cd deploy\windows && dotnet run --file pack.cs -- <version>)
//
// Prerequisites:
//   • .NET 10 SDK
//   • Inno Setup 6  (https://jrsoftware.org/isinfo.php)

using System.Diagnostics;
using System.Net.Http;

const string WinFspUrl = "https://github.com/winfsp/winfsp/releases/download/v2.1/winfsp-2.1.25156.msi";

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --file pack.cs -- <version> [iscc-path]");
    Environment.Exit(1);
}

string version = args[0];
string iscc    = args.Length > 1 ? args[1] : FindIscc();

// Always work relative to this script's own directory, regardless of where
// dotnet run was invoked from.
Directory.SetCurrentDirectory(ScriptDir());

string project   = Path.GetFullPath(@"..\..\src\CatModManager.Ui\CatModManager.Ui.csproj");
string winfspMsi = Path.Combine(Path.GetTempPath(), "winfsp-setup.msi");

// ── 1. Publish ───────────────────────────────────────────────────────────────
Log("Publishing (win-x64, self-contained)...");
Run("dotnet", $"publish \"{project}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish");

// ── 2. Download WinFsp MSI ───────────────────────────────────────────────────
if (!File.Exists(winfspMsi))
{
    Log("Downloading WinFsp MSI...");
    using var http = new HttpClient();
    var bytes = await http.GetByteArrayAsync(WinFspUrl);
    await File.WriteAllBytesAsync(winfspMsi, bytes);
}
else
{
    Log("WinFsp MSI already present, skipping download.");
}

// ── 3. Compile Inno Setup installer ─────────────────────────────────────────
Log("Compiling installer (Inno Setup)...");
Run(iscc, $"/DAppVersion={version} /DWinFspMsi={winfspMsi} CatModManager.iss");

Console.WriteLine();
Console.WriteLine($"Done.  dist\\CatModManagerSetup-{version}.exe");

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

static string FindIscc()
{
    string[] candidates =
    [
        @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        @"C:\Program Files\Inno Setup 6\ISCC.exe",
        @"C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
    ];
    var found = candidates.FirstOrDefault(File.Exists);
    if (found is not null) return found;

    Console.Error.WriteLine("ERROR: ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php");
    Console.Error.WriteLine("       Or pass the path explicitly: dotnet run --file pack.cs -- <version> \"C:\\path\\to\\ISCC.exe\"");
    Environment.Exit(1);
    return null!;
}

// Walks up from cwd until it finds this script file, returns that directory.
// Falls back to cwd if not found (e.g. running from the script's own dir).
static string ScriptDir()
{
    const string scriptName = "pack.cs";
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "deploy", "windows", scriptName)))
            return Path.Combine(dir.FullName, "deploy", "windows");
        dir = dir.Parent;
    }
    // Already in the script's directory
    return Directory.GetCurrentDirectory();
}
