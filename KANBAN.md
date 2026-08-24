# Kanban

Lista de issues conhecidos, anotados durante a validação de suporte a Linux, pra fazer depois.

## To Do

- **[GRAVE] Sem recuperação de mount FUSE órfão após crash no Linux.** Se o processo do CMM morre
  (crash, `kill`, etc.) enquanto um mount FUSE está ativo, o kernel mantém a entrada de mount
  registrada mas **desconectada** ("Transport endpoint is not connected" ao acessar) — a pasta do
  jogo parece ter sumido/vazio até alguém rodar `fusermount -uz` manualmente. Os arquivos reais nunca
  são afetados (confirmado 2x nesta sessão: KOTOR e Max Payne), mas o susto é grande e não tem
  recuperação automática. `VfsStateService.RecoverStaleMounts()` (`src/CatModManager.Core/Services/
  VfsStateService.cs`), que roda na inicialização pra limpar sessões anteriores, só conhece o esquema
  de hard links do Windows (restaura por `backup_path`) — não existe equivalente pra detectar/desmontar
  um FUSE órfão no Linux. Fix: no startup (ou no `RecoverStaleMounts` do Linux), verificar
  `/proc/mounts` por entradas `fuse.CatModManager` que apontem pra pastas de jogos conhecidos e, se
  encontradas, tentar `fusermount -uz` nelas antes de abrir a UI.

- **Preservar downloads Nexus ao trocar de perfil.** Trocar de perfil no meio de um download do
  Nexus pode cancelar/perder o progresso. Em `src/plugins/CmmPlugin.NexusMods/NexusModsPlugin.cs`,
  `LoadDownloadsForProfile()` troca toda a coleção `Downloads` pelo conjunto do novo perfil (via
  `NexusDownloadService.LoadDownloads`), sem tratar entradas com `IsActive == true`. Precisa manter
  downloads ativos vivos até terminarem (globais, não amarrados a perfil) ou migrá-los pro histórico
  do novo perfil sem interromper a stream HTTP. Adicionar teste de regressão.

- **Corrigir botão de Retry no download do Nexus.** O botão "↺ Retry" na aba de downloads não
  funciona. `NexusDownloadService.cs:410` `RetryDownload()` re-enfileira só com
  `GameDomain`/`ModId`/`FileId`, sem `key`/`expires`. Suspeita: `GetDownloadLinksAsync`
  (`NexusApiService.cs`) pode exigir `key`+`expires` vindos de um clique fresco no site pra contas
  free, falhando (404/403) sem eles. Investigar com um mod real que já falhou. Se for limitação
  free vs premium, tratar explicitamente na UI em vez de falhar silenciosamente.

- **Scanners de Steam/GOG pro Linux.** `SteamScanner`/`GogScanner`
  (`src/CatModManager.Core/Services/GameDiscovery/`) só funcionam no Windows (via registro) — no
  Linux sempre retornam vazio, então auto-detecção de jogos instalados não existe lá ainda.

- **Install Mod manual precisa suportar `.rar`.** No file picker de "Install Mod"
  (`src/CatModManager.Ui/Views/MainWindow.axaml.cs:325-327`), o filtro `FileTypeFilter` só lista
  `*.zip, *.7z` — `.rar` só aparece se o usuário mudar pra "All Files". Além disso, mesmo
  selecionando um `.rar`, o `SevenZipArchiveExtractor` (`src/CatModManager.Core/Services/
  SevenZipArchiveExtractor.cs`) usa `SharpCompress`, que tem suporte limitado a RAR (principalmente
  RAR5) por causa de restrições de licença do formato — precisa verificar se extrai de verdade ou só
  falha silenciosamente, e adicionar `*.rar` ao filtro do picker.

- **Muitos downloads simultâneos crasham o app.** `NexusDownloadService.cs:25` já limita a
  `SemaphoreSlim _concurrentDownloads = new(3, 3)` (máx. 3 downloads paralelos), então o crash não é
  falta de limite — é algo mais nas rotinas de download em si (concorrência de I/O na pasta de
  downloads, updates de UI fora da thread certa, exceção não tratada em algum dos `Task.Run`
  paralelos). Precisa reproduzir disparando vários downloads de uma vez e pegar o stack trace real.

- **Voltar pro primeiro mount point salva "Default" em vez do Id real.** Em
  `src/CatModManager.Ui/ViewModels/ModInstallationCoordinator.cs:102`, `MountPointId = mountPoint?.Id
  ?? "Default"` — se `mountPoint` vier `null` (ex.: ao tentar voltar pro mount point original depois
  de trocar), o mod fica com o literal `"Default"` em vez do Id real do primeiro mount point (ex.:
  `"override"` no KOTOR). Investigar se isso realmente redireciona pro primeiro mount point na hora
  de resolver o path de instalação, ou se joga o mod pra outro lugar (raiz do jogo, pasta errada,
  etc.) — ver como `MountPointId` é resolvido de volta pra um path físico.

- **Extração de `.7z` extremamente lenta, mesmo pra arquivo minúsculo.** O mesmo arquivo em `.zip`
  extrai rápido; em `.7z` demora muito. `SevenZipArchiveExtractor.cs` usa `SharpCompress`
  (`ArchiveFactory.Open` + `entry.WriteToDirectory`) — suspeita: decodificação LZMA de arquivos 7z
  "solid" no SharpCompress é conhecida por ser lenta/ineficiente (recompacta/redecodifica o bloco
  solid inteiro por entrada, ou falta buffering adequado no stream). Vale medir com profiling e
  considerar trocar a lib só pro caminho `.7z` se confirmado.

- **Decidir se Flatpak/Snap são viáveis, dado o design atual de FUSE.** Flatpak/Snap isolam mount
  namespace igual container — um mount FUSE criado pelo CMM sandboxado ficaria invisível pro jogo
  fora do sandbox, o mesmo problema que achamos rodando via distrobox nesta sessão. Contornos
  existem (`flatpak-spawn --host` pra montar fora do sandbox, permissões especiais de propagação),
  mas com custo de complexidade/segurança. Alternativa: usar hard links no Linux também (como já
  fazemos no Windows via `HardlinkDriver`), que não dependem de namespace de mount e funcionariam
  sandboxado sem gambiarra. Decidir isso antes de investir em empacotar Flatpak/Snap.

- **Race condition em `NewProfile`/`RenameProfile`.** Testes em
  `tests/CatModManager.Tests/Regression/ProfileRegressionTests.cs` falham de forma intermitente
  (4-6 falhas variando entre execuções sem mudança de código). Não parece específico de Linux —
  provavelmente inicialização assíncrona do `MainWindowViewModel`/`ProfileManagerViewModel`
  competindo com o teste.

## Feito

- **[VALIDADO PONTA A PONTA] Fluxo completo do Linux funciona: baixar, instalar, montar e desmontar
  mods, jogo carrega os mods de verdade.** Testado com KOTOR real (build self-contained no host, fora
  da distrobox). Bug que faltava: deadlock recursivo ao ler arquivos originais (não modificados)
  através do mount FUSE — `SimpleConflictResolver` criava `PhysicalFileSource` apontando pro caminho
  físico original, que é a própria pasta sendo montada; ler esse arquivo em runtime reabria o caminho
  e recursava de volta no mesmo mount FUSE, travando a thread (`dd`/Nautilus ficavam presos em D-state,
  não matável). Corrigido em `IFileSource.cs`: `PhysicalFileSource` agora lê o conteúdo inteiro pra
  memória no construtor (antes do mount acontecer), então nunca mais reabre o caminho em runtime —
  elimina a recursão de vez. Trade-off consciente: usa mais memória (todo arquivo base escaneado fica
  em RAM enquanto montado) — aceitável pro caso de uso atual, mas pode precisar de uma versão mais
  eficiente (streaming via file descriptor pré-aberto) se algum dia isso virar gargalo real com jogos
  de pastas Override muito grandes.

- **`Unmount()` mentia sucesso quando o `fusermount` real falhava.** Reproduzido com KOTOR real: o
  Nautilus segurou um handle na pasta montada, `fusermount -u` falhou com "Device or resource busy",
  mas `FuseNativeHost.RunFusermount()` (`src/CatModManager.VirtualFileSystem/Linux/FuseNativeHost.cs`)
  nunca checava o exit code — o app logava "Unmounted." e reportava sucesso mesmo com o mount ainda
  preso num estado zumbi (aparecia vazio via `ls`/Nautilus até alguém rodar `fusermount -uz` na mão).
  Dados no disco nunca foram afetados. Corrigido: agora tenta de novo (com espera) algumas vezes e cai
  pra unmount lazy (`-uz`) como garantia final, então o mount point sempre fica realmente desanexado
  antes do método retornar.
