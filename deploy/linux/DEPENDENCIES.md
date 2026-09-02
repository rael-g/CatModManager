# Linux Dependencies

Levantado durante a primeira validação real do CMM em Linux (Ubuntu 24.04, via distrobox).
Usar como checklist na hora de montar o instalador/pacote Linux (`pack.cs`).

**Testando fora da distrobox**: o registro de `nxm://` só funciona de verdade quando o CMM roda no
mesmo mount namespace do resto do sistema (o jogo, o navegador) — dentro da distrobox, nada disso
é enxergado de fora. Para testar isso de verdade
sem instalar nada permanente no host, use `dev-host-install.sh` / `dev-host-uninstall.sh` nesta
pasta: publicam um build self-contained numa pasta isolada, instalando os pacotes pacman que faltarem
(dependências normais do sistema, ficam instalados). O uninstall só remove essa pasta — não mexe em
pacote nenhum.

## Runtime (precisam existir na máquina do usuário)

| Pacote (Ubuntu/Debian) | Para quê | Observação |
|---|---|---|
| ~~`libfuse2t64`~~, ~~`fuse3`~~ | **Não são mais necessários.** O driver FUSE foi aposentado e o Safe Swap usa hardlink em todas as plataformas, o que não exige pacote nenhum — só um filesystem que suporte hardlink (ext4, btrfs, xfs, NTFS) | Podem ser removidos do instalador |
| `xdg-utils` (`xdg-mime`) | Registro do protocolo `nxm://` (`LinuxNxmProtocolHandler`) | Sem isso o registro falha silenciosamente (best-effort) — botão "nxm" na UI fica sempre "não registrado" |
| `desktop-file-utils` (`update-desktop-database`) | Atualiza o cache de `.desktop` depois de registrar/desregistrar o `nxm://` | Best-effort; se faltar, o registro ainda funciona mas o cache do desktop environment pode demorar a refletir |
| ASP.NET Core + .NET runtime (self-contained no publish, então não é dependência externa) | — | Publicar com `--self-contained true` (já é o que `pack.cs` faz) evita depender do `dotnet` do sistema |
| Libs X11/GTK do Avalonia (`libx11-6`, `libice6`, `libsm6`, `libfontconfig1`) | Renderização da UI Avalonia | Normalmente já presentes em qualquer desktop Linux com ambiente gráfico; vale confirmar em distros minimalistas |

## Dev-only (não vão pro pacote final, só pra quem for compilar)

| Pacote | Para quê |
|---|---|
| `dotnet-sdk-10.0` | Build/publish |
| `git`, `git-lfs` | Clone do repo (assets binários via LFS) |
| `build-essential` | Compilação de dependências nativas transitivas |

## Fix real que fica no código (não é workaround)

Em [`src/CatModManager.Ui/Program.cs`](../../src/CatModManager.Ui/Program.cs), o mecanismo de
instância única (IPC via named pipe pra encaminhar `nxm://` pra uma janela já aberta) tinha uma falha
real no Linux: `NamedPipeServerStream` não recusa bind num pipe já em uso — ele apaga e recria por
baixo dos panos, então uma segunda instância podia "roubar" o pipe da primeira, deixando-a inalcançável
pro resto da execução. Corrigido com um lock de arquivo exclusivo (`FileShare.None`) que garante que só
uma instância por vez rode o servidor de IPC. Esse fix é definitivo, funciona em qualquer distro e deve
permanecer no código.

## ⚠️ Workarounds de ambiente de dev — NÃO fazem parte do produto

Durante a validação do `nxm://` neste ambiente (distrobox `dev` + navegador no host), foram criados
dois artefatos **fora do repo**, só pra viabilizar teste local. Eles não existem numa instalação real
e **não devem ser copiados/empacotados**:

- `~/.local/bin/cmm-nxm-launcher.sh` — script wrapper que faz `distrobox enter dev -- .../CatModManager "%u"`.
- `~/.local/share/applications/cmm-nxm-handler.desktop` — `.desktop` local apontando pro script acima.

**Por que existem**: neste setup, o CMM roda dentro da distrobox `dev` (não instalado nativamente no
host), então o handler de `nxm://` precisa entrar no container antes de executar o binário. Numa
instalação real (`pack.cs` linux, self-contained, direto no host), isso não é necessário —
`LinuxNxmProtocolHandler.Register()` (em
[`src/plugins/CmmPlugin.NexusMods/LinuxNxmProtocolHandler.cs`](../../src/plugins/CmmPlugin.NexusMods/LinuxNxmProtocolHandler.cs))
já gera o `.desktop` certo, com `Exec="{caminho-do-binário-instalado}" "%u"` — um único executável,
sem indireção nenhuma.

**Achado relevante que TEM que sobreviver ao release**: o parser de `Exec=` do GLib/`gio launch`
(usado pelo GNOME e por extensão pelo mecanismo padrão de abrir `nxm://` a partir do navegador) não
lida bem com uma linha `Exec=` de **múltiplos tokens antes do `%u`** (ex.: `distrobox enter dev --
/caminho/binário "%u"` — 5 tokens). Nesse ambiente de teste, isso fazia a conexão por named pipe
falhar silenciosamente toda vez que o link era clicado pelo navegador, mesmo funcionando perfeitamente
via terminal. A solução foi sempre ter **um único executável no `Exec=`** (script wrapper aqui; o
binário instalado direto no caso real). Como o `LinuxNxmProtocolHandler` real já gera `Exec="{exePath}"
"%u"` (um token + `%u`), ele já está no formato seguro — mas se alguém no futuro "simplificar" isso pra
incluir argumentos extras antes do `%u`, é bom lembrar desse quirk antes de reintroduzir o bug.

## Pendências conhecidas (ver tarefas anotadas na sessão)

- Sem scanner de Steam/GOG nativo pro Linux ainda — auto-detecção de jogos instalados não funciona lá (só via registro do Windows hoje).
- ~~`winfsp.net` é dead weight na publicação~~ — **resolvido.** Ele e o `Mono.Fuse.NETStandard` saíram do `.csproj` do `CatModManager.VirtualFileSystem` junto com a aposentadoria do driver FUSE. O `winfsp.net`, em particular, era referenciado sem nenhum código no repositório usá-lo: ia no binário à toa.
