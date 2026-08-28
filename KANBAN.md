# Kanban

Lista de issues conhecidos, anotados durante a validação de suporte a Linux, pra fazer depois.

## To Do

- **Suíte não foi validada depois dos commits de 27/08/2026.** Os 12 commits (de `perf(vfs): resolver
  conflitos...` até `docs(kanban): ...`) foram feitos com a suíte passando 320/322 na última leitura
  boa, mas **não** houve execução completa depois de fatiar os hunks. Três arquivos foram divididos
  por hunk em vez de por arquivo — `HardlinkDriver.cs` (rollback vs. diagnóstico),
  `BugReproductionTests.cs` e `MainWindowViewModelTests.cs` — e é exatamente aí que dá pra quebrar um
  commit intermediário sem perceber. **Primeira coisa a fazer:** rodar `dotnet test` e, se der
  problema, conferir também se cada commit compila isolado (`git rebase --exec "dotnet build"`).

- **102 linhas de lixo em `active_mounts` no `cmm.db` do usuário.** Restos de quando os testes
  rodavam contra o banco real (já corrigido). Apontam pra `/tmp/<guid>` inexistente, então são
  inertes, mas precisam de limpeza com confirmação explícita. `hardlink_entries` pode conter uma
  montagem **ativa** e não deve ser tocado junto.

- **Aba Ferramentas não expõe `Arguments` nem `MountBeforeLaunch`.** Os dois existem em
  `ExternalTool`, são persistidos no perfil e são usados em `ExternalToolsViewModel.LaunchTool`, mas
  não há campo na UI pra editá-los — só dá pra ligar mexendo no TOML do perfil na mão. O README já
  promete "with optional VFS auto-mount before launch", o que hoje é verdade só no código. Ou expor
  os dois campos, ou tirar a promessa do README.

- **Preservar downloads Nexus ao trocar de perfil.** Trocar de perfil no meio de um download do
  Nexus pode cancelar/perder o progresso. Em `src/plugins/CmmPlugin.NexusMods/NexusModsPlugin.cs`,
  `LoadDownloadsForProfile()` troca toda a coleção `Downloads` pelo conjunto do novo perfil (via
  `NexusDownloadService.LoadDownloads`), sem tratar entradas com `IsActive == true`. Precisa manter
  downloads ativos vivos até terminarem (globais, não amarrados a perfil) ou migrá-los pro histórico
  do novo perfil sem interromper a stream HTTP. Adicionar teste de regressão.

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

- **Launch via plataforma (Steam primeiro), por configuração e não por arquitetura.** Hoje o Launch
  só sabe abrir um `.exe`. No Linux quase todo jogo roda por Proton, então abrir o exe direto não
  serve — mas o Launch não é inútil lá: basta apontar o executável pra própria Steam e passar
  `-applaunch <appid>` nos args.

  *Verificado (27/08/2026):* `Process.Start` com `UseShellExecute = true` e um nome puro (sem barra)
  resolve pelo PATH no Linux — testado com `echo`. É exatamente o que `ProcessService.cs:31` já faz,
  então o lançamento em si não precisa de mudança nenhuma.

  *Descartado:* injetar as opções de inicialização da Steam em `localconfig.vdf`. É texto puro e o
  caminho existe (`UserLocalConfigStore/Software/Valve/Steam/apps/<appid>/LaunchOptions`), mas a
  Steam mantém o arquivo em memória e o reescreve ao sair — qualquer edição com ela aberta é
  perdida, e "com a Steam aberta" é justamente quando alguém aperta Launch. Além de ser o config do
  usuário, com controles e cloud dentro. `-applaunch` passa argumentos sem tocar nele.

  *Descartado também:* uma abstração `IGameLauncher` com registro de launchers, detecção por loja
  (ler `appmanifest_*.acf`, manifests do Heroic/Epic, `pga.db` do Lutris) e um `LaunchMode` gravado
  no perfil. Funciona, mas custa uma semana, cada loja nova é mais código, e é *mais opaca* pro
  usuário do que ver o comando literal num campo de texto. Só vale se um dia precisarmos de algo que
  dois campos de texto não expressem.

  Plano, nesta ordem:
  1. Tirar o `IsReadOnly="True"` do campo de executável (`MainWindow.axaml:201`) — é o único motivo
     de hoje não dar pra digitar `steam` ali. O file picker continua, como atalho.
  2. Botão "Configurar pra Steam": preenche executável e args a partir do `SteamAppId` que a
     definição do jogo já tem. No Linux escreve `steam`; no Windows, o caminho do `Steam.exe` lido do
     registro (`HKCU\Software\Valve\Steam\SteamExe`), porque lá a Steam não fica no PATH. Quem
     conhece a diferença de plataforma é o botão; o campo continua sendo texto puro.
  3. `OnGameExecutablePathChanged` chama `DetectSupport(value)` (`GameConfigViewModel.cs:127`), que
     com `steam` no campo não detecta nada. Passar a detectar pela pasta base, que é a informação
     certa de qualquer forma.
  4. `WaitForGameDirectoryProcesses` (`ProcessService.cs:52`) deriva a pasta do jogo do caminho do
     executável; com `steam` vira o diretório de trabalho e ela gasta 30s achando nada, fazendo os
     hooks de pós-saída dispararem cedo. Ancorar na pasta do jogo, que o perfil já tem. Vale pra
     qualquer launcher — `-applaunch` faz o processo lançado ser a Steam, nunca o jogo.

  Heroic e Lutris depois: mais botões preenchendo o mesmo par de campos, sem arquitetura nova.

- **Args de lançamento são recurso de emulador, não de jogo.** `IGameSupport.GetLaunchArguments`
  existe e a única implementação (`CustomGameSupport.cs:74`) retorna `""` — nunca foi usado. A
  intenção original era abrir um jogo específico via emulador. Jogo de loja não recebe args, e com
  o Launch via Steam o campo passa a carregar `-applaunch <id>`. Decidir se o campo vira exclusivo
  de definições que o pedem (escondido nas demais) ou se continua genérico.

- **Enxugar a UI agora que existe o menu dropdown.** Antes do menu, cada comando novo era enfiado
  onde coubesse. Com o menu na barra de título, dá pra decidir o que *merece* estar visível.

  Critério proposto: a barra e a sidebar carregam só o que se usa **várias vezes por sessão**;
  o que é de configuração inicial ou uso raro fica só no menu. Duplicar não é errado por si — MOUNT
  e LAUNCH são os dois verbos principais e devem estar nos dois lugares — mas o resto está duplicado
  por acidente, não por escolha.

  Duplicatas que dá pra remover da tela (o menu já cobre):
  - Botão `⊕ AUTO DETECT` na sidebar = `Game ▸ Auto Detect Game…`.
  - Botão `BROWSE PLUGINS` no rodapé da sidebar = `Tools ▸ Browse Plugins…`.
  - Botão `✕` de apagar perfil na sidebar = `Profile ▸ Delete Profile`. Ainda por cima é destrutivo e
    fica encostado no campo de renomear.
  - `OPEN FOLDER` / `REMOVE MOD` na aba INFO do inspetor = `Mod ▸ …` **e** o menu de contexto da
    linha. Três cópias da mesma ação; o menu de contexto é o lugar natural.

  Repetição de código no XAML (819 linhas num arquivo só):
  - O padrão "rótulo + TextBox + ↗ + …" aparece **10 vezes** na sidebar, copiado e colado. Vira um
    `UserControl` com rótulo, caminho e visibilidade dos botões como propriedades.
  - O padrão "botão outline com ícone + rótulo em caixa alta" aparece 6 vezes escrevendo
    `Background`/`BorderBrush`/`BorderThickness` inline, **apesar de a classe `outline-btn` já
    existir** justamente pra isso.
  - Os dois `ItemsControl` de mount points (predefinidos vs. do usuário) têm templates quase
    idênticos. Já foi notado quando corrigimos o handler errado de um deles; a duplicação ficou.
  - `MainWindow.axaml` (819 linhas) devia virar sidebar / lista de mods / inspetor em `UserControl`s
    separados, com `MainWindow.axaml.cs` (545 linhas de handlers) indo junto.

  Inconsistência visual pendente: linha da lista com `Height="44"` fixo (`MainWindow.axaml:555`)
  contra `MinHeight="34"` usado em outros lugares.

  Nos testes a mesma coisa: `MockFileService`, `MockProcessService`, `MockGameSupportService` e
  `MockPathService`/`MockCatPathService` estão reescritos como classes aninhadas privadas em 4
  arquivos diferentes. Cada mudança de interface
  obriga a corrigir todas as cópias (aconteceu duas vezes só nesta sessão). Deviam estar em
  `tests/CatModManager.Tests/Support/`, onde `StubFileService`, `MockLogService` e agora
  `TempPathService` já vivem. O caso do path service não é só repetição: uma das quatro cópias era
  o serviço real, e escrevia no banco do usuário (ver item acima).

- **No Linux sob Proton, o CMM não consegue enxergar o processo do jogo.** Testado com Metaphor:
  ReFantazio (appid 2679460) em 27/08/2026, aberto por `steam -applaunch`. O botão LAUNCH ficou
  desabilitado ~30s, voltou ao normal com o jogo aberto, e ao fechar o jogo nada desmontou.

  O comportamento está *correto* conforme decidido (sem confirmação de execução, não desmonta) — o
  que falhou foi a detecção. `WaitForGameDirectoryProcesses` compara `process.MainModule.FileName`
  (`IProcessRunner.cs:34`) com a pasta do jogo. No Linux isso é o alvo de `/proc/pid/exe`, e sob
  Proton o processo é o wine do Proton, em `steamapps/common/Proton - Experimental/` — não a pasta
  do jogo. O caminho do `.exe` só aparece em `/proc/pid/cmdline`.

  *Não é limitação do modo Steam:* qualquer caminho que passe por Proton se comporta assim. No
  Windows, abrindo o exe direto, o processo **é** o exe dentro da pasta do jogo e o ciclo fecha.

  Ainda **não verificado empiricamente** — é dedução a partir do código, não leitura de `/proc`.
  Confirmar em 10s com o jogo aberto: `ls -l /proc/<pid>/exe` contra `cat /proc/<pid>/cmdline`.

  Fix provável: no Linux, casar também contra `cmdline`, não só contra a imagem do processo.
  Adiado por decisão do usuário — o auto-unmount funcionando no Windows já é suficiente por ora.

- **LAUNCH fica desabilitado só durante a espera, não durante o jogo.** Efeito colateral do item
  acima: o botão cinza por 30s e depois azul de novo com o jogo aberto passa a informação errada.
  Se a detecção passar a funcionar, ele fica cinza a sessão inteira — que é o certo. Reavaliar junto.

- **A janela de espera do jogo é de 30s e pode ser curta demais.** `ProcessService` desiste depois de
  30s sem ver processo na pasta do jogo. Uma primeira abertura via Proton (shaders, update, primeira
  execução do prefixo) passa disso. A consequência hoje é conservadora — o launch reporta que não
  conseguiu confirmar e **deixa montado** — então nada quebra, mas o auto-unmount simplesmente não
  acontece nesses casos. Medir quanto tempo o Starfield leva de verdade e ajustar, ou trocar o
  polling por algo baseado em evento.

- **SaveManager nunca foi validado de ponta a ponta no Linux.** A resolução da pasta de saves
  dentro do prefixo Wine foi corrigida e testada, mas backup e restore de verdade nunca rodaram lá.
  Falta confirmar que `SaveBackupService` grava e restaura corretamente, e o que acontece com um
  restore enquanto o jogo está aberto.

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

  **Agravado (26/08/2026):** não é só o app que trava — com vários downloads pesados o sistema
  inteiro congelou por pressão de memória, e o `systemd-oomd`/GNOME matou o CMM pra salvar a sessão.

  *Descartado:* o download em si **não** é o vazamento. `NexusApiService.DownloadToFileAsync`
  (`NexusApiService.cs:163`) já usa `HttpCompletionOption.ResponseHeadersRead` e copia em buffer de
  80 KB direto pro `FileStream`; nada do arquivo é materializado em memória.

  *Também descartado:* cheguei a atribuir o OOM a extrações concorrentes disparadas pelos downloads.
  **Está errado** — baixar não instala. `_installCallback` só é chamado por clique do usuário na aba
  de downloads (`NexusDownloadsTabControl.cs:676`); nada de auto-install ao concluir.

  **Causa encontrada (27/08/2026): vazamento de assinaturas na aba de downloads.**
  `NexusDownloadsTabControl.BuildCard` fazia `entry.PropertyChanged += ...` e nada desassinava.
  `RebuildCards` descarta e recria todos os cards a cada mudança da coleção **e** a cada transição
  ativo/concluído, mas a `DownloadEntry` sobrevive ao card — então cada reconstrução deixava mais um
  handler morto preso à entrada, cada um segurando viva a árvore visual inteira do card descartado e
  ainda executando a cada tick de progresso. Medido: 25 ciclos deixaram **51 handlers numa única
  entrada** onde deveria haver 1. Explica a ordem dos sintomas (UI trava primeiro, memória sobe
  depois) e por que piorava com vários downloads: mais entradas × mais transições.
  Corrigido rastreando as assinaturas e removendo-as antes de descartar os cards; teste em
  `tests/CatModManager.Tests/Plugins/NexusMods/DownloadCardSubscriptionTests.cs`, verificado por
  mutação (sem a desassinatura: 51 contra 1 esperado).

  **Ainda não fechado.** Repro instrumentada de 8 min com 3 downloads simultâneos (>900 MB, 3
  conclusões) mostrou RSS estável em 402–415 MB, subindo ~10 MB em cada reconstrução e voltando —
  sem crescimento monotônico. Mas 8 minutos não descarta vazamento lento. Fechar só depois de uma
  sessão longa de uso real com RSS amostrado; se voltar a crescer, aí sim `dotnet-counters` pra
  separar heap gerenciado de memória nativa.

- **Retry de download recomeça do zero e reabre a página do Nexus.** Para usuário não-premium, o link
  de CDN vem de um token nxm de uso único; ao falhar, o `RetryDownload` não tem como repetir a
  requisição e o fluxo volta a abrir a página. Duas melhorias independentes: (a) **resumo por HTTP
  Range** — guardar o parcial e mandar `Range: bytes=N-` em vez de recomeçar, o que navegador e MO2
  fazem; (b) **re-resolver o link** via API quando o token expirou, sem mandar o usuário clicar de
  novo no site. (a) só ajuda se o link ainda for válido; (b) é o que remove o reabrir da página.
  Confirmar se a API de não-premium permite re-emitir o link antes de prometer (b).

- **`.7z` ainda ~13x mais lento que o `7z` nativo, mesmo depois de corrigido o comportamento
  quadrático.** Com a passada única (ver "Feito"), o mod de 708 MB extrai em ~55s contra ~4s do CLI.
  A diferença restante é esperada: o LZMA2 do SharpCompress é gerenciado e single-thread, enquanto o
  CLI decodifica em várias threads. Se ~55s incomodar, a saída é usar o binário `7z` quando estiver
  no PATH e cair no SharpCompress só como fallback — mas isso adiciona dependência externa, então é
  decisão a tomar, não óbvia.

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

- **`CmmPlugin.REEngine` ancora no executável configurado, que nem sempre é o jogo.** Usa
  `ReEngineDetector.Detect(_state.GameExecutablePath)` sem a pasta de instalação. Um perfil que
  lança por wrapper (`distrobox-enter`, script, `steam -applaunch`) não é detectado. Mesmo ponto
  cego já corrigido em `BethesdaDetector`, `GamePathResolver` e `SaveDetector`.

- **`GamePathResolver` do BethesdaTools duplica a descoberta de prefixo Wine.** A lógica agora
  existe em `CatModManager.PluginSdk/WindowsUserFolders.cs`, escrita pro SaveManager e mais completa
  (mapeia `%APPDATA%`/`%LOCALAPPDATA%`/`%USERPROFILE%` e resolve casing segmento a segmento).
  Migrar o BethesdaTools pra ela e apagar a cópia. Não foi feito junto porque o código atual
  funciona e a troca merece sua própria verificação.

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

## Feito

- **Montar lia o conteúdo de todos os arquivos pra dentro da RAM (27/08/2026).** O usuário relatou
  mount passando de ~5s pra quase 1 minuto. Causa: `PhysicalFileSource`, construído uma vez por
  arquivo durante `SimpleConflictResolver.ScanRecursive`, fazia `File.ReadAllBytes` no construtor.
  Montar custava **uma leitura completa da lista de mods** — 2,08 GB em 207 arquivos no perfil
  Starfield real, em NTFS (ntfs3) — mais os mesmos 2 GB retidos em heap enquanto o mount durasse.
  Medido: criar 800 hard links naquele mesmo filesystem leva 0,7s, então o I/O de deploy nunca foi
  o gargalo. Piorou junto com a pasta de mods, não com o número de mods habilitados.

  Segundo bug no mesmo lugar: os `.ba2` da Starfield passam de 4 GB, acima do teto de 2 GB do
  `File.ReadAllBytes`, que então lançava — e o `catch { }` em volta do `foreach` do `ScanRecursive`
  engolia a exceção **junto com o resto da varredura daquele diretório**. Os arquivos base do jogo
  eram silenciosamente descartados do mapa.

  A leitura ansiosa existia por um motivo real: sob FUSE, reabrir por caminho um arquivo que ficou
  embaixo do mount faz o handler bloquear esperando por uma thread de handler pra servir o próprio
  `open()` aninhado — deadlock. Trocado por manter um descritor aberto (`File.OpenHandle`) apenas
  pros arquivos que ficam embaixo do alvo do mount, com leitura por offset via `RandomAccess`; o
  resto reabre por caminho normalmente. O `catch` também passou a ser por arquivo.
  Testes: `tests/CatModManager.Tests/Core/Services/ResolverDoesNotReadContentsTests.cs`, verificados
  por mutação (restaurar o `ReadAllBytes` derruba 2 dos 4).

- **Extração de `.7z` era quadrática no número de arquivos.** `SevenZipArchiveExtractor.ExtractAsync`
  chamava `entry.WriteToDirectory` por entrada, que é acesso aleatório. Num `.7z` *sólido* (o padrão)
  todos os arquivos dividem um único stream LZMA2, então buscar qualquer arquivo re-decodifica o
  stream desde o começo — custo O(arquivos × tamanho). Medido no mod FA STARQUEEN (708 MB, 126
  arquivos): passava de **10 minutos e desacelerando**, com 78% de CPU numa thread só e 0 byte
  escrito em 10s de amostragem, contra 4s do `7z` nativo no mesmo arquivo.

  Trocado por `archive.ExtractAllEntries()`, que faz uma passada única e decodifica o stream uma vez
  — a mesma estratégia do CLI. Resultado: **54,7s**, e a saída conferida com `diff -r` contra a
  extração do `7z` nativo: idêntica.

- **A race dos testes de perfil, diagnosticada.** Era mesmo a inicialização assíncrona competindo
  com o teste, e a causa é do app, não só do teste: o construtor do `MainWindowViewModel` disparava
  `LoadInitialProfile` em fire-and-forget (`_ = Task.Run(...)`), que termina em `RefreshListAsync` —
  `AvailableProfiles.Clear()` seguido de repopular a partir de um snapshot da pasta de perfis tirado
  *quando a carga começou*. Quem cria um perfil enquanto ela está em voo tem o perfil apagado da
  lista pelo `Clear`, porque o snapshot é anterior a ele. Vale pro usuário também: criar perfil logo
  ao abrir o app pode perder a entrada da lista.

  A task agora é exposta como `MainWindowViewModel.InitialLoadTask` e os três testes a aguardam.
  Verificado com 8 execuções seguidas da suíte cheia, todas verdes (antes falhava ~1 em 3).
  Descoberta de brinde: a remoção do `BethesdaModInstaller` mudou o escalonamento do xUnit e tornou
  a falha determinística, o que é o que permitiu diagnosticá-la.

- **Três testes que nunca passaram no Linux, e o motivo de cada um.** A suíte rodava com 3 falhas
  desde o começo; nenhuma era flakiness.
  Os dois `VfsOrchestrationServiceTests` usavam `GameFolderPath = "C:\\Game"` — no Linux isso é um
  caminho *relativo* para uma pasta inexistente, então o driver real falhava ao montar e toda
  asserção sobre `IsMounted` caía junto. Eles passaram a "funcionar" sozinhos quando o fallback pra
  hardlink entrou, mas por motivo errado: montavam um mapa de arquivos vazio sem tocar em disco.
  A causa de fundo é que o `VfsOrchestrationService` construía o driver por uma fábrica estática,
  o que tornava impossível testar a orquestração (ordem dos hooks, guarda de já-montado) sem
  encostar no filesystem real. Agora o driver é injetável e os testes usam um falso.
  O terceiro, `MountButton_Click_TogglesMount`, comparava `mountButton.Command` com
  `vm.ToggleMountCommand`, mas o XAML liga em `Vfs.ToggleMountCommand` — comandos diferentes, então
  a asserção era impossível de satisfazer. Ainda executava o comando, disparando uma tentativa de
  mount real sem asserir nada com isso. Virou uma checagem de identidade do binding, que é o que
  realmente pega alguém renomeando a propriedade (XAML só reclama em runtime).
  Os três foram verificados por mutação: removi a guarda de já-montado, a chamada do hook de
  after-unmount e quebrei o binding do botão — cada mutação derrubou o teste correspondente.
  Suíte: 192 passando, 0 falhas, 2 pulados.

- **[GRAVE] Crash recovery de hardlink nunca rodava no Linux.** `RecoverStaleMounts` pedia o driver
  pela plataforma, e no Linux isso devolvia o `FuseDriver` — cujo `Unmount()` retorna na hora
  quando nada foi montado. Ou seja: o `HardlinkDriver`, único que persiste o que foi implantado
  (links + backups no `IHardlinkStateStore`), nunca era consultado. Depois de um crash com deploy
  por hardlink, os arquivos de mod e os backups com prefixo de ponto ficariam na pasta do jogo pra
  sempre. Não dava pra notar antes porque hardlink só rodava no Windows. Agora existe
  `CreateCrashRecoveryDriver`, que devolve o driver com estado em qualquer plataforma. Mounts FUSE
  órfãos continuam sendo tratados à parte, pelo `IVfsStateService` contra `/proc/mounts`.

- **Fallback de FUSE para hardlink em vez de lista fixa de filesystems.** A lista de filesystems
  recusados pelo `fusermount` não é legível de fora — só descoberta. Em vez de manter um palpite,
  o `FuseWithHardlinkFallbackDriver` trata a falha de mount como resposta: tenta o overlay e, se
  for recusado, implanta por hardlink. Um mount FUSE que falha não deixa estado pela metade, então
  não há nada pra desfazer. A lista conhecida continua sendo consultada antes, só pra pular uma
  tentativa que sabemos que vai falhar. A troca de estratégia sempre vai pro log, porque hardlink
  escreve na pasta do jogo e isso não pode ser silencioso.
  Efeito colateral: os dois `VfsOrchestrationServiceTests` que falhavam desde sempre passaram a
  passar — eles tentavam FUSE de verdade num diretório temporário e morriam; agora caem pro
  hardlink. Confirmado em 3 execuções seguidas.

- **Mensagem de erro de mount do FUSE culpava o `modprobe`.** O Mono.Fuse reporta qualquer falha
  como "try running /sbin/modprobe fuse as the root user", e a causa real vai pro stderr do
  processo `fusermount`, que ninguém lê. Agora a exceção nomeia o filesystem do alvo e, quando é um
  dos recusados, diz exatamente isso. A causa original fica preservada em `InnerException`.

- **Modo hardlink funcionando no Linux (destrava jogo em NTFS).** Montar o VFS num jogo instalado
  em partição NTFS falhava, e a causa não é do CMM: o `fusermount` da libfuse tem uma lista de
  filesystems sobre os quais se recusa a montar, e o ntfs3 está nela —
  `mounting over filesystem type 0x7366746e is forbidden` (`0x7366746e` = "ntfs" em ASCII).
  Provado com A/B: o mesmo FUSE monta em btrfs e é recusado em NTFS. Não há opção de mount que
  contorne, a decisão é do fusermount antes de o CMM ter voz.
  O `HardlinkDriver` já existia mas era Windows-only (`CreateHardLinkW`); agora usa `link(2)` no
  Linux, com `EXDEV` caindo pra cópia igual ao `ERROR_NOT_SAME_DEVICE` do Windows. Saiu do
  namespace `.Windows`. O `FileSystemFactory` deixou de escolher por sistema operacional e passou a
  escolher pelo filesystem do alvo (lendo `/proc/mounts`), preferindo FUSE quando disponível porque
  ele não toca na pasta do jogo. Validado na partição real: mount substitui o arquivo, cria backup
  com prefixo de ponto, o hardlink é real (edição na origem propaga), e o unmount restaura o
  original deixando a pasta limpa.
  Os 9 testes de hardlink, que eram pulados fora do Windows, agora rodam no Linux — os pulados
  caíram de 11 pra 2. Um deles só passava no Windows por usar `Data\mesh.bin` com barra invertida
  literal; e reforcei o teste principal pra provar que é hardlink e não cópia (editar a origem tem
  que aparecer no destino), porque um fallback silencioso pra `File.Copy` passaria em todas as
  outras asserções.

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
