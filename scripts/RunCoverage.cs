// Cat Mod Manager — Coverage Runner
// Usage:  dotnet script scripts/RunCoverage.cs
//         (run from repo root or scripts/ folder)
//
// Prerequisites: reportgenerator global tool
//   dotnet tool install -g dotnet-reportgenerator-globaltool

using System.Diagnostics;

string rootDir  = FindRoot();
string tempDir  = Path.Combine(Path.GetTempPath(), "CMM_Coverage");
string resultsDir = Path.Combine(tempDir, "results");
string reportDir  = Path.Combine(tempDir, "report");

try
{
    Console.WriteLine(new string('=', 65));
    Console.WriteLine(" Cat Mod Manager - Coverage");
    Console.WriteLine(new string('=', 65));

    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    Directory.CreateDirectory(resultsDir);

    // 1. Rodar testes com coverlet
    Console.WriteLine("\n[1/2] Running tests...");
    Run("dotnet", $"test CatModManager.slnx --collect:\"XPlat Code Coverage\" --results-directory \"{resultsDir}\" --nologo -v quiet", rootDir);

    string? xml = Directory
        .GetFiles(resultsDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
        .FirstOrDefault();

    if (xml is null)
    {
        Console.Error.WriteLine("ERROR: No coverage file generated.");
        return 1;
    }

    // 2. Gerar resumo de texto
    Console.WriteLine("[2/2] Generating report...");
    Directory.CreateDirectory(reportDir);
    Run("reportgenerator", $"-reports:\"{xml}\" -targetdir:\"{reportDir}\" -reporttypes:\"TextSummary;HtmlSummary\" -assemblyfilters:\"+CatModManager.Core;+CatModManager.Ui;+CatModManager.VirtualFileSystem;+CatModManager.PluginSdk;+CmmPlugin.*\" -filefilters:\"-tests/**\"", rootDir);

    // 3. Exibir no terminal
    string summary = Path.Combine(reportDir, "Summary.txt");
    if (File.Exists(summary))
    {
        Console.WriteLine("\n" + new string('─', 65));
        Console.Write(File.ReadAllText(summary));
        Console.WriteLine(new string('─', 65));
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

return 0;

static void Run(string exe, string args, string workDir)
{
    var psi = new ProcessStartInfo(exe, args)
    {
        WorkingDirectory = workDir,
        UseShellExecute  = false,
    };

    using var p = Process.Start(psi) ?? throw new Exception($"Failed to start: {exe}");
    p.WaitForExit();

    if (p.ExitCode != 0)
        throw new Exception($"'{exe}' exited with code {p.ExitCode}");
}

static string FindRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "CatModManager.slnx")))
            return dir.FullName;
        dir = dir.Parent!;
    }
    return Directory.GetCurrentDirectory();
}
