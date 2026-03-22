using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Windows.ProjFS;
using ProjFsProof;

// ┌─────────────────────────────────────────────────────────────────────────┐
// │  CONFIGURE AQUI antes de rodar                                          │
// └─────────────────────────────────────────────────────────────────────────┘

// Pasta raiz do jogo (SERÁ MOVIDA para .CMM_base durante o teste)
const string GameFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Lies of P";

// Arquivos de mod: caminho relativo no jogo → fonte absoluta no disco
// Mods do Lies of P vão em: LiesofP\Content\Paks\~mods\<arquivo.pak>
var ModFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [@"LiesofP\Content\Paks\~mods\pakchunk155-WindowsNoEditor.pak"] =
        @"C:\Program Files (x86)\Steam\steamapps\common\Lies of P\NxmDownloads\Kaine-207-1-2-1749317309\pakchunk155-WindowsNoEditor.pak",
};

// ─────────────────────────────────────────────────────────────────────────────

var backupFolder = GameFolder + ".CMM_base";

Console.WriteLine("=== ProjFS Proof of Concept ===");
Console.WriteLine();

if (!Directory.Exists(GameFolder))
{
    Console.WriteLine($"[ERRO] Pasta do jogo não encontrada: {GameFolder}");
    Console.WriteLine("Ajuste a variável GameFolder em Program.cs.");
    return 1;
}

if (Directory.Exists(backupFolder))
{
    Console.WriteLine($"[AVISO] Backup anterior encontrado: {backupFolder}");
    Console.WriteLine("Isso indica um crash anterior. Restaurando antes de continuar...");
    if (Directory.Exists(GameFolder)) Directory.Delete(GameFolder, true);
    Directory.Move(backupFolder, GameFolder);
    Console.WriteLine("Restaurado. Rode novamente.");
    return 1;
}

// ── SafeSwap ─────────────────────────────────────────────────────────────────

Console.WriteLine($"Jogo   : {GameFolder}");
Console.WriteLine($"Backup : {backupFolder}");
Console.WriteLine($"Mods   : {ModFiles.Count} arquivo(s) configurado(s)");
Console.WriteLine();
Console.WriteLine("→ SafeSwap: movendo pasta do jogo para backup...");
Directory.Move(GameFolder, backupFolder);
Directory.CreateDirectory(GameFolder);
Console.WriteLine("  Feito.");

// ── Índice de arquivos ────────────────────────────────────────────────────────

Console.WriteLine("→ Indexando arquivos base...");
var fileIndex  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var emptyDirs  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var file in Directory.EnumerateFiles(backupFolder, "*", SearchOption.AllDirectories))
    fileIndex[Path.GetRelativePath(backupFolder, file)] = file;

// Inclui diretórios que não têm arquivos (ex: NGX subdirs, ~mods vazio)
// O BuildDirIndex só adiciona dirs que têm arquivos — sem isso eles somem da projeção.
foreach (var dir in Directory.EnumerateDirectories(backupFolder, "*", SearchOption.AllDirectories))
{
    var rel = Path.GetRelativePath(backupFolder, dir);
    if (!Directory.EnumerateFileSystemEntries(dir).Any())
        emptyDirs.Add(rel);
}

Console.WriteLine($"  {fileIndex.Count} arquivos base, {emptyDirs.Count} diretório(s) vazio(s).");

// Remapeia fontes que estavam dentro da pasta do jogo (agora em .CMM_base)
foreach (var (rel, src) in ModFiles)
{
    string resolvedSrc = src;
    if (src.StartsWith(GameFolder, StringComparison.OrdinalIgnoreCase))
        resolvedSrc = backupFolder + src[GameFolder.Length..];

    if (!File.Exists(resolvedSrc)) { Console.WriteLine($"  [AVISO] Fonte não encontrada: {resolvedSrc}"); continue; }
    fileIndex[rel] = resolvedSrc;
    Console.WriteLine($"  + MOD: {rel}");
}
Console.WriteLine();

// ── ProjFS ────────────────────────────────────────────────────────────────────

Console.WriteLine("→ Iniciando ProjFS...");

var provider = new ModProvider(fileIndex, emptyDirs);

var instance = new VirtualizationInstance(
    GameFolder,
    poolThreadCount:         32,
    concurrentThreadCount:   32,
    enableNegativePathCache: false,
    notificationMappings:    Array.Empty<NotificationMapping>());

provider.SetInstance(instance);

// ProjFS exige marcar o diretório como virtualization root antes de StartVirtualizing
// (cria o reparse point no diretório — necessário mesmo que o dir esteja vazio)
var markResult = VirtualizationInstance.MarkDirectoryAsVirtualizationRoot(GameFolder, instance.VirtualizationInstanceId);
if (markResult != HResult.Ok)
{
    Console.WriteLine($"[ERRO] MarkDirectoryAsVirtualizationRoot: 0x{(int)(object)markResult:X8}");
    Restore(GameFolder, backupFolder);
    return 1;
}

var startResult = instance.StartVirtualizing(provider);

if (startResult != HResult.Ok)
{
    Console.WriteLine($"[ERRO] StartVirtualizing: 0x{(int)(object)startResult:X8}");
    Console.WriteLine();
    Console.WriteLine("Causas comuns:");
    Console.WriteLine("  • Feature 'Windows Projected File System' não habilitada.");
    Console.WriteLine("    PowerShell (admin): Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart");
    Console.WriteLine("  • Execute o PoC como Administrador.");
    Restore(GameFolder, backupFolder);
    return 1;
}

Console.WriteLine($"  ProjFS ativo.");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("✓ Inicie o jogo pelo launcher agora e teste se o mod está ativo.");
Console.ResetColor();
Console.WriteLine("Pressione ENTER para desmontar e restaurar.");
Console.ReadLine();

// ── Desmontar ─────────────────────────────────────────────────────────────────

Console.WriteLine("→ Parando ProjFS...");
instance.StopVirtualizing();

Console.WriteLine("→ Removendo arquivos hidratados...");
CleanProjectedFiles(GameFolder);
Restore(GameFolder, backupFolder);

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("✓ Restaurado.");
Console.ResetColor();
return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static void CleanProjectedFiles(string folder)
{
    try
    {
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            try { File.Delete(f); } catch { }

        foreach (var d in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories)
                                   .OrderByDescending(x => x.Length))
            try { Directory.Delete(d); } catch { }
    }
    catch (Exception ex) { Console.WriteLine($"  [AVISO] Limpeza parcial: {ex.Message}"); }
}

static void Restore(string gameFolder, string backupFolder)
{
    Console.WriteLine("→ Restaurando pasta do jogo...");
    Console.WriteLine("  (Feche o crash reporter do jogo se estiver aberto)");

    for (int attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            if (Directory.Exists(gameFolder)) Directory.Delete(gameFolder, true);
            Directory.Move(backupFolder, gameFolder);
            Console.WriteLine("  Feito.");
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            Console.WriteLine($"  Tentativa {attempt}/10 falhou ({ex.Message.Split('\n')[0].Trim()}), aguardando 2s...");
            System.Threading.Thread.Sleep(2000);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERRO CRÍTICO] Não foi possível restaurar: {ex.Message}");
            Console.WriteLine($"Restaure manualmente: mova '{backupFolder}' → '{gameFolder}'");
            Console.ResetColor();
        }
    }
}
