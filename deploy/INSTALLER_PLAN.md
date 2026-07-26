# Plano de Instalador — Cat Mod Manager

> Criado em 2026-04-14.

Dois roteiros independentes:

- **Fase A (imediato)** — Refatorar o Inno Setup existente: remover WinFsp, adicionar plugins opcionais e todos os samples, com auto-detecção para zero manutenção futura.
- **Fase B (futuro)** — Migrar para Velopack: instalador nativo .NET com delta updates e bootstrapper customizado.

---

## Fase A — Refatoração do Inno Setup

### O que muda

#### Remoções
Toda a infraestrutura do WinFsp pode ser deletada:

| Item | Localização |
|------|------------|
| `#define WinFspMsi` | `CatModManager.iss:10–11` |
| Task `winfsp` (checkbox) | `[Tasks]` |
| Bundling do MSI | `[Files]` |
| `msiexec.exe` runner | `[Run]` |
| Função `WinFspNotInstalled` | `[Code]` |
| Download do MSI no pack.cs | `pack.cs:35–45` |
| Parâmetro `/DWinFspMsi` para ISCC | `pack.cs:49` |

#### Adições
- `[Components]` com um componente por plugin (gerado automaticamente pelo `pack.cs`)
- Samples: todos os 21 arquivos via wildcard — zero manutenção
- `plugins_generated.iss` — fragmento gerado em tempo de build, incluído pelo `.iss` principal

---

### Auto-detecção: como funciona

O Inno Setup é um formato estático — não consegue descobrir arquivos em tempo de compilação sozinho. A solução é usar o `pack.cs` (que já orquestra o build) como gerador:

```
pack.cs
  │
  ├── 1. dotnet publish UI
  ├── 2. dotnet publish cada plugin → publish\plugins\{PluginName}\
  ├── 3. escaneia src\plugins\*.csproj → lista de plugins
  ├── 4. gera plugins_generated.iss  ← NOVO
  └── 5. ISCC CatModManager.iss (que faz #include "plugins_generated.iss")
```

**Samples** — wildcard puro, nada a gerar:
```ini
; [Files] — pega todos os .toml presentes em samples/game_definitions/
Source: "..\..\samples\game_definitions\*"; \
  DestDir: "{localappdata}\catmodmanager\game_definitions"; \
  Flags: onlyifdoesntexist uninsneveruninstall
```
Adicionar um novo sample = criar o arquivo. Instalador pick up automaticamente.

**Plugins** — gerado pelo `pack.cs`. Exemplo do arquivo gerado para 5 plugins:
```ini
; AUTO-GERADO por pack.cs — não editar manualmente

[Components]
Name: "plugins\nexusmods";    Description: "NexusMods — download, checagem de atualizações e integração com o site";   Flags: disablenouninstallwarning
Name: "plugins\bethesdatools"; Description: "Bethesda Tools — gerenciador de load order para Skyrim/Fallout";            Flags: disablenouninstallwarning
Name: "plugins\reengine";     Description: "RE Engine — instalador especializado para jogos RE Engine (Capcom)";         Flags: disablenouninstallwarning
Name: "plugins\fomod";        Description: "FOMOD Installer — suporte ao formato de instalação FOMOD";                  Flags: disablenouninstallwarning
Name: "plugins\savemanager";  Description: "Save Manager — backup automático de saves";                                  Flags: disablenouninstallwarning

[Files]
Source: "publish\plugins\CmmPlugin.NexusMods\*";    DestDir: "{app}\plugins\CmmPlugin.NexusMods";    Components: plugins\nexusmods;    Flags: ignoreversion recursesubdirs
Source: "publish\plugins\CmmPlugin.BethesdaTools\*"; DestDir: "{app}\plugins\CmmPlugin.BethesdaTools"; Components: plugins\bethesdatools; Flags: ignoreversion recursesubdirs
Source: "publish\plugins\CmmPlugin.REEngine\*";     DestDir: "{app}\plugins\CmmPlugin.REEngine";     Components: plugins\reengine;     Flags: ignoreversion recursesubdirs
Source: "publish\plugins\CmmPlugin.FomodInstaller\*"; DestDir: "{app}\plugins\CmmPlugin.FomodInstaller"; Components: plugins\fomod;    Flags: ignoreversion recursesubdirs
Source: "publish\plugins\CmmPlugin.SaveManager\*"; DestDir: "{app}\plugins\CmmPlugin.SaveManager";   Components: plugins\savemanager;  Flags: ignoreversion recursesubdirs
```

Quando um novo plugin for adicionado ao `src/plugins/`, basta rodar `pack.cs` — o arquivo gerado aparecerá automaticamente no instalador com checkbox.

---

### Convenção de publish dos plugins

Para que os plugins fiquem em subpastas separadas (necessário para os checkboxes funcionarem), o `pack.cs` precisa publicar cada plugin individualmente:

```csharp
// Para cada plugin encontrado em src/plugins/:
Run("dotnet", $"publish \"{pluginCsproj}\" -c Release -r win-x64 --self-contained false " +
              $"-o publish\\plugins\\{pluginName}");
```

`--self-contained false` porque os plugins rodam no processo do CMM, que já carrega o runtime.

**Dependências compartilhadas**: se dois plugins trouxerem a mesma DLL, `ignoreversion` no `[Files]` resolve — o Inno sobrescreve silenciosamente. Para evitar conflitos de DLL no diretório do app, considerar publicar plugins com `--no-dependencies` e deixar as dependências somente no diretório raiz do app.

---

### Estrutura de arquivos resultante

```
deploy/windows/
  CatModManager.iss          ← script estático (sem WinFsp, com #include)
  plugins_generated.iss      ← gerado pelo pack.cs, no .gitignore
  pack.cs                    ← orquestrador (atualizado)
  dist/
    CatModManagerSetup-x.y.z.exe
```

`.gitignore` a adicionar:
```
deploy/windows/plugins_generated.iss
deploy/windows/dist/
deploy/windows/publish/
```

---

### Tela do instalador resultante

```
┌─────────────────────────────────────────────┐
│  Cat Mod Manager — Setup                    │
├─────────────────────────────────────────────┤
│  Selecione os componentes a instalar:       │
│                                             │
│  [■] Cat Mod Manager (obrigatório)          │  ← sem checkbox (é o app base)
│                                             │
│  Plugins opcionais:                         │
│  [■] NexusMods                              │
│  [■] Bethesda Tools                         │
│  [■] RE Engine                              │
│  [■] FOMOD Installer                        │
│  [■] Save Manager                           │
│                                             │
│  Atalhos:                                   │
│  [ ] Criar atalho na área de trabalho       │
└─────────────────────────────────────────────┘
```

O CMM base não pode ser desmarcado (`fixed` flag no componente). Todos os plugins vêm marcados por padrão (`checkablealone` + marcados).

---

### Checklist de implementação (Fase A)

- [ ] Criar convenção de pasta `publish\plugins\{PluginName}\` no `pack.cs`
- [ ] Adicionar step de publish por plugin em `pack.cs` (loop sobre `src\plugins\*.csproj`)
- [ ] Adicionar geração de `plugins_generated.iss` em `pack.cs`
- [ ] Remover tudo do WinFsp de `pack.cs`
- [ ] Atualizar `CatModManager.iss`:
  - [ ] Remover `#define WinFspMsi`, task `winfsp`, `[Code]`
  - [ ] Adicionar `#include "plugins_generated.iss"`
  - [ ] Adicionar componente base fixo (CMM) em `[Components]`
  - [ ] Trocar 3 samples por wildcard `*`
- [ ] Adicionar `plugins_generated.iss` ao `.gitignore`
- [ ] Testar build completo e fluxo de instalação

---

---

## Fase B — Migração para Velopack (futuro)

### Por que Velopack

| Critério | Inno Setup | Velopack |
|----------|-----------|---------|
| Integração .NET | Externo | NuGet package nativo |
| Delta updates | Não | Sim (built-in) |
| Auto-update em runtime | Manual | `UpdateManager` nativo |
| Checkboxes de componentes | Nativo | Requer bootstrapper |
| Complexidade do toolchain | Baixa | Média |
| CI/CD | Script externo | `vpk` CLI + GitHub Actions action |

Velopack não tem wizard com checkboxes nativamente — isso é resolvido com um **bootstrapper**: um pequeno executável .NET que age como instalador personalizado antes de acionar o Velopack.

---

### Arquitetura da solução

```
CatModManagerBootstrapper.exe   ← novo projeto .NET (console ou WinForms mínimo)
  │   Apresenta UI de seleção de componentes
  │   Extrai e instala o pacote Velopack base
  └── Copia DLLs dos plugins selecionados
      para {app}\plugins\{PluginName}\

CatModManager.exe (app principal)
  └── Velopack SDK integrado (App.axaml.cs)
      ├── Detecta e aplica atualizações delta
      └── UpdateManager para check em background
```

---

### Estrutura de arquivos

```
deploy/
  windows/
    CatModManager.iss              ← mantido até migração completa
    pack.cs                        ← mantido
    plugins_generated.iss          ← mantido
  velopack/
    bootstrapper/
      CmmBootstrapper.csproj       ← novo projeto
      MainWindow.xaml              ← UI simples de seleção
      MainWindow.xaml.cs
      Installer.cs                 ← lógica de instalação
    pack-velopack.cs               ← novo orquestrador (substitui pack.cs no futuro)
    releases/                      ← output do vpk (no .gitignore)
```

---

### Passo a passo da migração

#### Etapa 1 — Integrar Velopack SDK no app

```xml
<!-- CatModManager.Ui.csproj -->
<PackageReference Include="Velopack" Version="*" />
```

Em `Program.cs` (entry point):
```csharp
VelopackApp.Build()
    .WithFirstRun(v => { /* primeiro run após instalação */ })
    .Run();
```

Em `App.axaml.cs` (shutdown):
```csharp
// Checar atualizações em background ao iniciar
_ = CheckForUpdatesAsync();
```

Velopack intercepta automaticamente `--velopack-*` args de linha de comando para lifecycle hooks (install/uninstall/update/obsolete).

#### Etapa 2 — Criar o bootstrapper

Projeto `CmmBootstrapper` (WinForms ou Avalonia, mínimo):

```
┌────────────────────────────────────┐
│  Instalar Cat Mod Manager          │
├────────────────────────────────────┤
│  Pasta de instalação: [_________]  │
│                                    │
│  Componentes:                      │
│  [■] Cat Mod Manager  (base)       │
│  [■] NexusMods                     │
│  [■] Bethesda Tools                │
│  [■] RE Engine                     │
│  [■] FOMOD Installer               │
│  [■] Save Manager                  │
│                                    │
│            [ Instalar ]            │
└────────────────────────────────────┘
```

Lógica do bootstrapper:
1. Lê a lista de plugins disponíveis do arquivo `components.json` bundled (gerado pelo `pack-velopack.cs`)
2. Mostra os checkboxes
3. Ao confirmar:
   - Extrai o pacote Velopack base (`CatModManager-{version}-win-x64-full.nupkg`)
   - Para cada plugin selecionado, extrai o sub-pacote ou copia a pasta correspondente
   - Copia samples para `{localappdata}\catmodmanager\game_definitions\`
   - Registra `nxm://` e `.catprofile` via reg.exe (HKCU, sem UAC)
   - Cria atalho no Start Menu
4. Lança o app

`components.json` — gerado pelo `pack-velopack.cs`, bundled no bootstrapper:
```json
{
  "plugins": [
    { "id": "nexusmods",    "name": "NexusMods",       "description": "...", "default": true },
    { "id": "bethesda",     "name": "Bethesda Tools",  "description": "...", "default": true },
    { "id": "reengine",     "name": "RE Engine",        "description": "...", "default": true },
    { "id": "fomod",        "name": "FOMOD Installer",  "description": "...", "default": true },
    { "id": "savemanager",  "name": "Save Manager",     "description": "...", "default": true }
  ]
}
```

Também gerado automaticamente por `pack-velopack.cs` escaneando `src/plugins/`.

#### Etapa 3 — Criar `pack-velopack.cs`

Substitui `pack.cs` quando a migração estiver completa. Fluxo:

```
1. dotnet publish UI (win-x64, self-contained)
2. dotnet publish cada plugin → publish\plugins\{Name}\
3. gerar components.json
4. vpk pack                          ← cria o .nupkg Velopack
5. dotnet publish bootstrapper
6. bundlar bootstrapper + .nupkg + plugins + samples → CmmSetup-x.y.z.exe
   (usando 7-zip SFX ou similar para o bundle final)
```

Para o bundle final (bootstrapper + payload), as opções são:
- **7-Zip SFX** — simples, sem dependências
- **WiX Bundle** — mais formal, suporta silent install
- **Inno Setup** — irônico, mas funciona: Inno empacota o bootstrapper e o payload, o bootstrapper faz o trabalho real

#### Etapa 4 — Auto-update em runtime

```csharp
// Em MainWindowViewModel ou App.axaml.cs
private async Task CheckForUpdatesAsync()
{
    using var mgr = new UpdateManager("https://github.com/seu-repo/releases");
    var newVersion = await mgr.CheckForUpdatesAsync();
    if (newVersion is null) return;

    // Notificar usuário na UI (banner discreto)
    // Ao confirmar:
    await mgr.DownloadUpdatesAsync(newVersion);
    mgr.ApplyUpdatesAndRestart(newVersion);
}
```

Delta updates: Velopack gera automaticamente arquivos `*-delta.nupkg` nos releases. O `UpdateManager` baixa apenas o delta quando disponível.

#### Etapa 5 — CI/CD com GitHub Actions

```yaml
# .github/workflows/release.yml
- name: Pack with Velopack
  run: |
    dotnet tool install -g vpk
    dotnet run --file deploy/velopack/pack-velopack.cs -- ${{ github.ref_name }}

- name: Upload release
  uses: velopack/velopack-action@v1   # action oficial
  with:
    github-token: ${{ secrets.GITHUB_TOKEN }}
    releases-dir: deploy/velopack/releases
```

---

### Checklist de implementação (Fase B)

#### Pré-requisitos
- [ ] Fase A concluída e estável
- [ ] Decidir UI do bootstrapper: WinForms (simples) ou Avalonia (consistente com o app)

#### Integração Velopack no app
- [ ] Adicionar `Velopack` NuGet ao `CatModManager.Ui.csproj`
- [ ] Adicionar `VelopackApp.Build().Run()` no `Program.cs`
- [ ] Implementar `CheckForUpdatesAsync` no app
- [ ] Adicionar banner de "atualização disponível" na UI
- [ ] Testar lifecycle hooks (install/uninstall/update/obsolete)

#### Bootstrapper
- [ ] Criar projeto `deploy/velopack/bootstrapper/CmmBootstrapper.csproj`
- [ ] Implementar UI de seleção de componentes
- [ ] Implementar leitura de `components.json`
- [ ] Implementar extração e cópia de arquivos
- [ ] Implementar registro de `nxm://`, `.catprofile` e atalhos
- [ ] Testar instalação limpa e reinstalação

#### Pack script
- [ ] Criar `deploy/velopack/pack-velopack.cs`
- [ ] Geração automática de `components.json` (mesmo mecanismo da Fase A)
- [ ] Integrar `vpk pack` no script
- [ ] Bundle final (bootstrapper + payload)
- [ ] Testar geração end-to-end

#### CI/CD
- [ ] Criar `.github/workflows/release.yml`
- [ ] Configurar GitHub token e permissions
- [ ] Testar release completo via Actions
- [ ] Configurar URL de update no `UpdateManager` (apontar para GitHub Releases)

---

## Decisão de quando migrar para a Fase B

A Fase B faz sentido quando:

1. **Delta updates importam** — releases frequentes onde baixar o app completo incomoda os usuários
2. **Auto-update em runtime é prioridade** — usuários esquecendo de atualizar
3. **CI/CD** — processo de release está automatizado e a Action do Velopack agrega valor

Enquanto o projeto estiver em fases iniciais, a Fase A (Inno refatorado) é suficiente e muito mais simples de manter.
