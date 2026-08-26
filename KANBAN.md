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

- **Race condition em `NewProfile`/`RenameProfile`.** Testes em
  `tests/CatModManager.Tests/Regression/ProfileRegressionTests.cs` falham de forma intermitente
  (4-6 falhas variando entre execuções sem mudança de código). Não parece específico de Linux —
  provavelmente inicialização assíncrona do `MainWindowViewModel`/`ProfileManagerViewModel`
  competindo com o teste.

## Feito

- **nxm reabria uma instância nova do CMM a cada download.** Três defeitos somados, achados com o
  CMM rodando pela distrobox. (1) A posse do servidor IPC era decidida uma vez no startup: quem não
  pegava o lock (`/tmp/CatModManager_IPC_v1.lock`, compartilhado entre host e container) nunca mais
  tentava, então quando o dono saía ninguém ficava escutando e cada clique de nxm abria outra
  janela. Agora quem perde o lock fica reprocurando e assume quando ele vaga — validado matando o
  dono e vendo o link chegar na instância sobrevivente. (2) `LinuxNxmProtocolHandler.Register`
  gravava no `.desktop` o caminho que o container enxerga (`/run/host/home/...`), que não existe no
  host — e quem executa o `.desktop` é sempre o host, então re-registrar pela distrobox era um
  no-op. (3) `xdg-open` dentro do container não alcança sessão de desktop nenhuma, por isso as
  pastas não abriam; agora vai por `distrobox-host-exec`. A detecção ficou em
  `ContainerEnvironment` (PluginSdk), compartilhada pelos dois pontos.

- **Plugins do próprio Starfield apareciam na aba PLUGINS.** A lista fixa de masters implícitos em
  `BethesdaDetector` tinha 10 nomes, mas o jogo instalado traz 14 `.esm` oficiais — os 4 que
  faltavam (`BlueprintShips-SFBGS050`, `SFBGS00D`, `SFBGS047`, `SFBGS050`) viravam linhas com
  checkbox. O `Starfield.exe` embute a própria lista (`strings` mostra `SFBGS047.esm`,
  `SFBGS050.esm` etc.) e carrega esses arquivos independentemente do `Plugins.txt`, então o dano
  não é corromper a ordem: é oferecer um controle que não controla nada, e permitir arrastar um
  master do jogo para depois de um plugin comum (isso o motor rejeita). Trocado por derivação: o
  que está na pasta Data do jogo e nenhum mod gerenciado fornece pertence ao jogo. A lista fixa
  ficou só como piso para quando a Data não é legível. Nenhum `Plugins.txt` chegou a ser escrito.

- **[REFACTOR] Diálogos do app principal em código imperativo.** `GameDetectionDialog` (244 linhas
  de construção manual, incluindo um método de 178) virou `GameDetectionDialog.axaml` + 36 linhas
  de code-behind, com três `IValueConverter` no lugar dos `switch` de cor inline. Os dois diálogos
  embutidos no `MainWindow.axaml.cs` viraram janelas próprias (`MountPointPickerDialog`,
  `MountPointEditorDialog`), o que tirou 195 linhas do arquivo (630 → 435) e eliminou o sentinela
  `"cancelled"` — agora `null` significa cancelado, como no resto do código. O estado "mount point
  atual" virou uma classe de estilo (`Classes.current`) em vez de três ternários de brush.
  `DialogLoadTests` carrega cada diálogo em headless, porque com XAML um binding quebrado deixou de
  ser erro de compilação e só aparece ao abrir a janela.
  Os controles dos plugins continuam imperativos de propósito: carregar XAML de assembly externo é
  justamente o que se quis evitar.

- **Botão de Retry do Nexus não funcionava.** `RetryDownload` montava uma entrada nova só com
  `GameDomain`/`ModId`/`FileId` e chamava `QueueDownloadDirect`, descartando o `key`/`expires` do
  link `nxm://` original — e sem eles a Nexus recusa o download pra conta free como "premium
  required". Era a divergência entre os dois pipelines quase idênticos que escondia isso. Agora a
  chave vive na própria `DownloadEntry` (`NxmKey`/`NxmExpires`) e o retry reaproveita o objeto, o
  que também preserva o preset FOMOD de mods de collection. Falta confirmar com um mod real se a
  chave ainda é válida no momento do retry (ela expira) — se não for, a UI precisa reabrir a página
  em vez de falhar.

- **[REFACTOR] `NexusDownloadService` era uma god-class (773 linhas).** Decomposto em
  `NexusDownloadRepository` (SQLite), `NexusCollectionQueue` (a máquina de estados do fluxo
  página-a-página, agora testável e com 7 testes) e `NexusCollectionResolver` (GraphQL +
  `collection.json`). O que sobrou é o pipeline de transferência, com um único caminho `RunAsync`
  compartilhado por nxm://, direto e retry no lugar dos dois corpos de ~90 linhas duplicados.
  Os `Dispatcher.UIThread.Post` repetidos viraram `DownloadEntryExtensions` (`Begin`/`Fail`/
  `Complete`/`MarkCancelled`), que era onde alguns call sites esqueciam de limpar o `IsActive`.

- **[REFACTOR] Duas paletas de cores competindo.** Os 77 literais `Color.Parse("#...")` foram
  substituídos por `CmmPalette`, num projeto novo `CatModManager.Theme` que o app e os plugins
  referenciam (o `PluginSdk` continua sem Avalonia, de propósito). O `App.axaml` agora resolve
  todos os brushes via `{x:Static theme:CmmPalette.X}`, então XAML e code-behind não têm como
  divergir. As variantes duplicadas convergiram para a cor do tema (accent `#5865F2`/`#2563EB` →
  `#4E7FD5`; muted `#8E9297`/`#72767D`/`#757575` → `#80848E`; warning `#FAA81A`/`#FFA500` →
  `#FAA61A`) — a UI mudou de cor em alguns pontos, era esse o objetivo. Cores de marca de loja
  (Steam/GOG/Epic) ficaram como entradas separadas por não serem cores de tema.
  `PaletteConsistencyTests` falha se um literal novo aparecer fora da paleta.

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
