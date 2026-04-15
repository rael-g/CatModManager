// Cat Mod Manager — Windows Packer
// Usage: dotnet run --file deploy\windows\pack.cs -- <version> [iscc-path]
//        (or cd deploy\windows && dotnet run --file pack.cs -- <version>)
//
// Prerequisites:
//   • .NET 10 SDK
//   • Inno Setup 6  (https://jrsoftware.org/isinfo.php)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --file pack.cs -- <version> [iscc-path]");
    Environment.Exit(1);
}

string version = args[0];
string iscc    = args.Length > 1 ? args[1] : FindIscc();

// Always work relative to this script's own directory
Directory.SetCurrentDirectory(ScriptDir());

string uiProject = Path.GetFullPath(@"..\..\src\CatModManager.Ui\CatModManager.Ui.csproj");
string pluginsDir = Path.GetFullPath(@"..\..\src\plugins");

// ── 1. Clean ─────────────────────────────────────────────────────────────────
if (Directory.Exists("publish")) Directory.Delete("publish", true);
Directory.CreateDirectory("publish");

// ── 2. Publish UI ───────────────────────────────────────────────────────────
Log("Publishing UI (win-x64, self-contained)...");
Run("dotnet", $"publish \"{uiProject}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish");

// ── 3. Publish Plugins ───────────────────────────────────────────────────────
Log("Scanning and publishing plugins...");
var plugins = new List<PluginInfo>();
if (Directory.Exists(pluginsDir))
{
    foreach (var dir in Directory.GetDirectories(pluginsDir))
    {
        var csproj = Directory.GetFiles(dir, "*.csproj").FirstOrDefault();
        if (csproj == null) continue;

        string pluginName = Path.GetFileNameWithoutExtension(csproj);
        Log($"  Publishing {pluginName}...");
        
        string outputDir = Path.Combine("publish", "plugins", pluginName);
        Run("dotnet", $"publish \"{csproj}\" -c Release -r win-x64 --self-contained false -o \"{outputDir}\"");
        
        plugins.Add(new PluginInfo(pluginName, outputDir));
    }
}

// ── 4. Generate plugins_generated.iss ───────────────────────────────────────
Log("Generating plugins_generated.iss...");
GeneratePluginsIss(plugins);

// ── 5. Compile Inno Setup installer ─────────────────────────────────────────
Log("Compiling installer (Inno Setup)...");
Run(iscc, $"/DAppVersion={version} CatModManager.iss");

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

static void GeneratePluginsIss(List<PluginInfo> plugins)
{
    var sb = new StringBuilder();
    sb.AppendLine("; AUTO-GERADO por pack.cs — não editar manualmente");
    sb.AppendLine();
    
    sb.AppendLine("[Components]");
    foreach (var p in plugins)
    {
        string id = p.Name.Replace("CmmPlugin.", "").ToLowerInvariant();
        // Clean description based on common plugin names
        string desc = p.Name.Replace("CmmPlugin.", "") + " Plugin";
        sb.AppendLine($"Name: \"plugins\\{id}\"; Description: \"{desc}\"; Flags: disablenouninstallwarning");
    }
    sb.AppendLine();

    sb.AppendLine("[Files]");
    foreach (var p in plugins)
    {
        string id = p.Name.Replace("CmmPlugin.", "").ToLowerInvariant();
        sb.AppendLine($"Source: \"publish\\plugins\\{p.Name}\\*\"; DestDir: \"{{app}}\\plugins\\{p.Name}\"; Components: plugins\\{id}; Flags: ignoreversion recursesubdirs");
    }

    File.WriteAllText("plugins_generated.iss", sb.ToString(), Encoding.UTF8);
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
    Environment.Exit(1);
    return null!;
}

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
    return Directory.GetCurrentDirectory();
}

record PluginInfo(string Name, string Path);
