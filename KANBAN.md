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

- **Scanner de GOG pro Linux.** `GogScanner` (`src/CatModManager.Core/Services/GameDiscovery/`) lê o
  registro do Windows e o GOG Galaxy não tem cliente Linux, então lá ele sempre retorna vazio. Jogos
  GOG no Linux vêm via Heroic ou instalação manual — precisaria ler os manifests do Heroic
  (`~/.config/heroic/gog_store/installed.json`) ou aceitar que GOG é só detecção manual no Linux.
  (O `SteamScanner` já foi corrigido e funciona no Linux.)

- **Muitos downloads simultâneos crasham o app.** `NexusDownloadService.cs:25` já limita a
  `SemaphoreSlim _concurrentDownloads = new(3, 3)` (máx. 3 downloads paralelos), então o crash não é
  falta de limite — é algo mais nas rotinas de download em si (concorrência de I/O na pasta de
  downloads, updates de UI fora da thread certa, exceção não tratada em algum dos `Task.Run`
  paralelos). Precisa reproduzir disparando vários downloads de uma vez e pegar o stack trace real.

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

- **Mods pesados falham o download.** Relatado pelo usuário durante testes reais no Linux; ainda sem
  diagnóstico (nenhum log/stack trace coletado ainda). Investigar se é timeout de HTTP, limite de
  memória/buffer no `NexusDownloadService`, ou algo específico de arquivos grandes na escrita em disco.

- **Validar aba PLUGINS com Starfield real.** O suporte Bethesda foi corrigido e testado com árvore
  Steam/Proton sintética em disco (resolução do prefixo, casing de `plugins.txt`, escrita do
  `StarfieldCustom.ini`), mas nunca rodou contra uma instalação real do jogo. Falta confirmar: se o
  Starfield de fato lê o `Plugins.txt` que escrevemos, se a ordem `.esm` antes de `.esp` está
  correta pro engine do Creation Engine 2, e se a lista de masters implícitos em
  `BethesdaDetector._known` bate com a versão atual do jogo (DLCs novos entram nessa lista).

- **Scanner de jogos Bethesda não popula `SteamAppId` no resolver de prefixo.**
  `GamePathResolver.EnumerateCandidatePrefixes` (`src/plugins/CmmPlugin.BethesdaTools/Services/`)
  varre *todos* os diretórios de `steamapps/compatdata` porque o plugin não recebe o App ID do jogo.
  Funciona (escolhe o prefixo que já tem a pasta do jogo), mas é O(n) em prefixos e no primeiro run
  — quando nenhum prefixo tem os dados ainda — cai no primeiro da lista, que pode ser o errado.
  `IModManagerState` já expõe `GameId`; expor também o `SteamAppId` da definição TOML resolveria.

- **[REFACTOR] Duas paletas de cores competindo.** `App.axaml` define uma paleta completa como
  recursos nomeados (`AppBackground`, `TextPrimary`, `StatusDanger`, `Accent`…), mas o code-behind
  tem **77 literais** `Color.Parse("#...")` espalhados por 6 arquivos, e **22 das 28 cores usadas
  lá não existem no tema**. Pior, são variantes divergentes da mesma cor: accent é `#4E7FD5` no tema
  mas `#5865F2` e `#2563EB` no código; texto apagado é `#80848E` no tema mas `#8E9297`, `#72767D` e
  `#757575` no código; warning tem `#FAA61A`, `#FAA81A` e `#FFA500`. Na prática o tema só controla
  metade da UI — mudar a cor de destaque não afeta a aba Nexus nem os diálogos. Concentrado em
  `NexusDownloadsTabControl` (22), `NexusBrowseWindow` (20), `GameDetectionDialog` (12),
  `MainWindow.axaml.cs` (11), `NexusModInspectorTab` (10). Fix: expor os brushes do tema e trocar
  os literais por referência a eles.

- **[REFACTOR] `NexusDownloadService` é uma god-class (773 linhas).** Acumula persistência
  (`LoadDownloads`/`SaveDownloads`), download HTTP, fila de collections, parsing de link `nxm://`,
  conversão de preset FOMOD e integração com o shell (`OpenFolder`). Não é só estética: os 3 bugs
  de download em aberto neste kanban moram todos nele. `QueueDownloadFromNxm` (128 linhas) e
  `QueueDownloadDirect` (100 linhas) compartilham um corpo de ~90 linhas quase idêntico, diferindo
  só em de onde vêm os identificadores — e é exatamente por essa divergência que o
  `RetryDownload` chama o `Direct` sem `key`/`expires`. Unificar esse pipeline provavelmente torna
  o bug do Retry tratável de verdade em vez de remendo.

- **[REFACTOR] UI construída em código imperativo em vez de XAML.** ~2.400 linhas de construção
  manual de controles Avalonia (`NexusDownloadsTabControl` 759, `NexusBrowseWindow` 746,
  `FomodWizardWindow` 270, `GameDetectionDialog` 244, `PluginsTabControl` 145), contra só 1.200
  linhas de `.axaml` no projeto inteiro. É a causa raiz da paleta duplicada acima e dos métodos
  gigantes (`GameDetectionDialog` tem um de 178 linhas). Para os controles de plugin há uma razão
  legítima (evitar carregar XAML de assembly externo), mas `GameDetectionDialog` e os diálogos
  dentro de `MainWindow.axaml.cs` estão no app principal e não têm essa desculpa.

- **Race condition em `NewProfile`/`RenameProfile`.** Testes em
  `tests/CatModManager.Tests/Regression/ProfileRegressionTests.cs` falham de forma intermitente
  (4-6 falhas variando entre execuções sem mudança de código). Não parece específico de Linux —
  provavelmente inicialização assíncrona do `MainWindowViewModel`/`ProfileManagerViewModel`
  competindo com o teste.

## Feito

- **[GRAVE] Mod instalado sem mount point virava `"Default"` e nunca era montado.** Em
  `ModInstallationCoordinator`, quando não havia mount point pra atribuir, o mod era salvo com a
  string literal `"Default"`. Mas `VfsOrchestrationService.MountPointMatches` só trata `null` como
  "use o default" — uma id desconhecida cai na comparação normal e não casa com **nada**. Como
  nenhum jogo define uma id `"Default"` (KOTOR usa `override`, Skyrim/Starfield usam `data`/`root`),
  o mod era instalado, aparecia habilitado na UI e simplesmente nunca chegava na pasta do jogo. Era
  pior do que o registrado aqui antes (a suspeita era "vai pra pasta errada"; na real não ia a lugar
  nenhum). Corrigido gravando `null`, mais migração no load de perfil que reseta qualquer
  `MountPointId` órfão — perfis já salvos continuariam quebrados só com o fix do installer.

- **Auto-detecção de jogos Steam no Linux.** `SteamScanner.Scan()` abria com
  `if (!OperatingSystem.IsWindows()) return Array.Empty<...>()`, mesmo o parsing de `appmanifest_*.acf`
  e `libraryfolders.vdf` sendo 100% agnóstico de plataforma — só a descoberta da raiz do Steam era
  Windows-only (registro + Program Files). Agora procura nas localizações conhecidas do Linux
  (`~/.steam/steam`, `~/.local/share/Steam`, Flatpak, etc.), resolvendo symlinks pra não listar a
  mesma biblioteca 3x. Validado na máquina real: achou as duas bibliotecas, incluindo a de
  `/mnt/games` via `libraryfolders.vdf`. Runtimes/Proton não poluem a lista porque o
  `GameDiscoveryService` já exige um `.exe` no topo da pasta.

- **`.rar` no Install Mod.** A parte do filtro do picker era real e foi corrigida (`*.rar`/`*.tar`
  adicionados, e o `LocalModScanner` também só enxergava `.zip`/`.7z`). Mas a suspeita de que o
  SharpCompress falharia silenciosamente em RAR estava **errada**: testado com um RAR4 real
  construído à mão e validado com `7z`, o `SevenZipArchiveExtractor` lista e extrai corretamente; e
  arquivo inválido lança `InvalidOperationException` bem visível, não falha em silêncio. Ressalva:
  o teste cobriu RAR4 stored — RAR5 e archives "solid" não foram verificados.

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
