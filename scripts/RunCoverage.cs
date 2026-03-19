using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

string tempDir = Path.Combine(Path.GetTempPath(), "CMM_Coverage_Official");
string currentDir = Directory.GetCurrentDirectory();
string rootDir = currentDir.EndsWith("scripts") ? Path.GetDirectoryName(currentDir)! : currentDir;

try 
{
    Console.WriteLine("\n" + new string('=', 65));
    Console.WriteLine(" 🚀 Cat Mod Manager - STABLE COVERAGE ANALYSIS");
    Console.WriteLine(new string('=', 65));
    
    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    Directory.CreateDirectory(tempDir);

    string coverageFile = Path.Combine(tempDir, "coverage.xml");

    // 1. Usar a ferramenta global dotnet-coverage para rodar tudo de uma vez
    Console.WriteLine("\n[1/2] 🧪 Running tests with dotnet-coverage...");
    RunCommand("dotnet-coverage", $"collect \"dotnet test CatModManager.slnx\" -f cobertura -o \"{coverageFile}\"", rootDir);

    if (!File.Exists(coverageFile)) {
        Console.WriteLine("\n❌ ERROR: Coverage file was not generated.");
        return;
    }

    // 2. Gerar resumo via ReportGenerator
    Console.WriteLine("\n[2/2] 📊 Generating visual summary...");
    string outputDir = Path.Combine(tempDir, "Output");
    Directory.CreateDirectory(outputDir);

    RunCommand("reportgenerator", $"-reports:\"{coverageFile}\" -targetdir:\"{outputDir}\" -reporttypes:TextSummary -assemblyfilters:\"+CatModManager*\"", rootDir);
    
    // 3. Exibir o resultado
    string summaryFile = Path.Combine(outputDir, "Summary.txt");
    if (File.Exists(summaryFile))
    {
        Console.WriteLine("\n" + new string('━', 65));
        Console.WriteLine(File.ReadAllText(summaryFile));
        Console.WriteLine(new string('━', 65));
    }

} 
catch (Exception ex) { Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}"); }

void RunCommand(string Command, string args, string workingDir)
{
    var psi = new ProcessStartInfo {
        FileName = Command, Arguments = args, WorkingDirectory = workingDir,
        RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
    };
    using var p = Process.Start(psi);
    if (p != null) {
        while (!p.StandardOutput.EndOfStream) {
            string? line = p.StandardOutput.ReadLine();
            if (line != null && (line.Contains("Passed!") || line.Contains("Failed!") || line.Contains("Total tests:") || line.Contains("Code coverage results")))
                Console.WriteLine("      " + line.Trim());
        }
        p.WaitForExit();
    }
}



