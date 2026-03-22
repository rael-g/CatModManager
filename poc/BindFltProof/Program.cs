using System;
using System.IO;
using System.Linq;
using BindFltProof;

// ┌─────────────────────────────────────────────────────────────────────────┐
// │  CONFIGURE AQUI antes de rodar                                           │
// └─────────────────────────────────────────────────────────────────────────┘

// Pasta raiz do jogo (NÃO será movida — bindflt não precisa de SafeSwap)
const string GameFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Lies of P";

// Arquivos de mod: caminho relativo no jogo → fonte absoluta no disco
// O PoC vai copiar esses arquivos para um diretório de staging temporário,
// depois fazer o overlay via bindflt. O jogo verá os arquivos do mod no lugar
// dos originais (se existirem) sem que nenhum arquivo do jogo seja tocado.
var ModFiles = new (string RelativePath, string SourcePath)[]
{
    (
        @"LiesofP\Content\Paks\~mods\pakchunk155-WindowsNoEditor.pak",
        @"C:\Program Files (x86)\Steam\steamapps\common\Lies of P\NxmDownloads\Kaine-207-1-2-1749317309\pakchunk155-WindowsNoEditor.pak"
    ),
};

// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("=== BindFlt Proof of Concept ===");
Console.WriteLine();

// ── Pre-flight ────────────────────────────────────────────────────────────────

if (!BindFlt.IsAvailable())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("[ERRO] bindflt.dll não encontrado.");
    Console.WriteLine("       Requer Windows 10 Build 18362 (versão 1903) ou superior.");
    Console.ResetColor();
    return 1;
}

Console.WriteLine("✓ bindflt.dll disponível.");

if (!Directory.Exists(GameFolder))
{
    Console.WriteLine($"[ERRO] Pasta do jogo não encontrada: {GameFolder}");
    return 1;
}

foreach (var (rel, src) in ModFiles)
{
    if (!File.Exists(src))
    {
        Console.WriteLine($"[ERRO] Fonte do mod não encontrada: {src}");
        return 1;
    }
}

// ── Staging directory ─────────────────────────────────────────────────────────
//
// bindflt não serve arquivos diretamente de múltiplos locais dispersos no disco.
// Ele faz overlay de UMA pasta (staging) sobre outra (game root).
// Por isso criamos um staging dir temporário com a estrutura de pastas do mod.
//
// Tamanho: apenas os arquivos do mod (não copiamos o jogo inteiro).
// Tempo:   O(n) onde n = nº de arquivos de mod — geralmente 1 pak.

var stagingDir = Path.Combine(Path.GetTempPath(), $"CMM_BindFlt_{Guid.NewGuid():N}");
Console.WriteLine($"→ Criando staging: {stagingDir}");
Directory.CreateDirectory(stagingDir);

try
{
    // Copia arquivos do mod para o staging (mantendo estrutura de pastas)
    foreach (var (rel, src) in ModFiles)
    {
        var dest = Path.Combine(stagingDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: true);
        Console.WriteLine($"  + {rel}");
    }

    Console.WriteLine();
    Console.WriteLine($"  Game root : {GameFolder}");
    Console.WriteLine($"  Staging   : {stagingDir}");
    Console.WriteLine($"  Mods      : {ModFiles.Length} arquivo(s)");
    Console.WriteLine();

    // ── BfSetupFilter ─────────────────────────────────────────────────────────
    //
    // Flags usados:
    //   BINDFLT_FLAG_READ_ONLY_MAPPING     — staging é somente leitura (não deixa o jogo escrever no staging)
    //   BINDFLT_FLAG_MERGED_BIND_MAPPING   — overlay: staging SOBRE game root
    //                                        (arquivos no staging têm prioridade;
    //                                         arquivos ausentes do staging vêm do game root real)
    //   BINDFLT_FLAG_USE_CURRENT_SILO_MAPPING — escopo: processo atual e filhos (não global)
    //
    // Sem USE_CURRENT_SILO o mapeamento seria global e afetaria TODOS os processos do sistema.

    uint flags = BindFlt.BINDFLT_FLAG_READ_ONLY_MAPPING
               | BindFlt.BINDFLT_FLAG_MERGED_BIND_MAPPING
               | BindFlt.BINDFLT_FLAG_USE_CURRENT_SILO_MAPPING;

    Console.WriteLine("→ Instalando bindflt overlay...");

    int hr = BindFlt.BfSetupFilter(
        jobHandle:          IntPtr.Zero,
        flags:              flags,
        virtRootPath:       GameFolder,   // O que o jogo vê
        realRootPath:       stagingDir,   // Onde estão os arquivos de mod
        exceptionPaths:     null,
        exceptionPathCount: 0);

    if (hr != 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERRO] BfSetupFilter: HRESULT 0x{hr:X8}");
        Console.WriteLine();
        DiagnoseHResult(hr);
        Console.ResetColor();
        return 1;
    }

    Console.WriteLine("  Feito.");
    Console.WriteLine();

    // ── Verificação rápida ────────────────────────────────────────────────────
    //
    // Verifica se o arquivo do mod aparece no game root (via overlay).
    Console.WriteLine("→ Verificando overlay...");
    foreach (var (rel, _) in ModFiles)
    {
        var projectedPath = Path.Combine(GameFolder, rel);
        bool exists = File.Exists(projectedPath);
        Console.WriteLine($"  {(exists ? "✓" : "✗")} {rel}");

        if (exists)
        {
            var fi = new FileInfo(projectedPath);
            var stagingSrc = new FileInfo(Path.Combine(stagingDir, rel));
            bool sizeMatch = fi.Length == stagingSrc.Length;
            Console.WriteLine($"    Tamanho: {fi.Length:N0} bytes {(sizeMatch ? "(OK)" : "[DIVERGÊNCIA!]")}");
        }
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓ Overlay ativo. Inicie o jogo pela Steam e teste se o mod está ativo.");
    Console.ResetColor();
    Console.WriteLine("Pressione ENTER para remover o overlay.");
    Console.ReadLine();

    // ── BfRemoveMapping ───────────────────────────────────────────────────────

    Console.WriteLine("→ Removendo overlay...");
    hr = BindFlt.BfRemoveMapping(IntPtr.Zero, GameFolder);
    if (hr != 0)
        Console.WriteLine($"  [AVISO] BfRemoveMapping: 0x{hr:X8} (pode já ter sido removido)");
    else
        Console.WriteLine("  Feito.");
}
finally
{
    // Limpa staging mesmo se der erro
    Console.WriteLine("→ Limpando staging...");
    try { Directory.Delete(stagingDir, true); Console.WriteLine("  Feito."); }
    catch (Exception ex) { Console.WriteLine($"  [AVISO] {ex.Message}"); }
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("✓ Concluído. Pasta do jogo não foi modificada.");
Console.ResetColor();
return 0;

// ── Diagnóstico ───────────────────────────────────────────────────────────────

static void DiagnoseHResult(int hr)
{
    Console.WriteLine("Causas comuns:");
    switch ((uint)hr)
    {
        case 0x80070005: // E_ACCESSDENIED
            Console.WriteLine("  • E_ACCESSDENIED: Execute como Administrador.");
            Console.WriteLine("    (BfSetupFilter requer SeCreateSymbolicLinkPrivilege ou Admin)");
            break;

        case 0x80070057: // E_INVALIDARG
            Console.WriteLine("  • E_INVALIDARG: Caminho inválido ou flag incompatível.");
            Console.WriteLine("    Verifique que virtRootPath e realRootPath existem e terminam sem \\.");
            break;

        case 0x8007007B: // ERROR_INVALID_NAME
            Console.WriteLine("  • ERROR_INVALID_NAME: Caminho malformado.");
            break;

        case 0xC0000034: // STATUS_OBJECT_NAME_NOT_FOUND
            Console.WriteLine("  • STATUS_OBJECT_NAME_NOT_FOUND: bindflt.sys não carregado.");
            Console.WriteLine("    Verifique: sc query bindflt");
            break;

        default:
            Console.WriteLine($"  • HRESULT desconhecido: 0x{hr:X8}");
            Console.WriteLine("    Tente: sc query bindflt (para ver se o driver está ativo)");
            Console.WriteLine("    Tente: Get-WindowsOptionalFeature -Online | Where FeatureName -like '*bind*'");
            break;
    }
}
