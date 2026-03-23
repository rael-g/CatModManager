# The Schrödinger Cat Mod Manager (CMM)

> *"Mods only exist when observed."*

**Cat Mod Manager** is a mod management solution for Windows and Linux, built with **.NET 10** and **Avalonia UI**. Its core mechanic is a **Safe Swap** system that overlays mods onto your game directory only during gameplay, keeping the original files 100% untouched.

---

## The "Safe Swap" Concept

Instead of physically copying or replacing game files, CMM uses a driver-level overlay:

- **Windows** — NTFS hard links are created at mount time. The game reads mod files through the link; original files are backed up with a dot-prefix. Unmounting removes all links and restores originals. No third-party kernel driver required.
- **Linux** — A read-only FUSE filesystem is mounted over the game directory. The game sees a merged view of base files and mod overrides. FUSE automatically unmounts if the process exits.

Both approaches leave no permanent changes to your game directory.

---

## Features

- **Profile Management** — Multiple mod configurations per game, switch with one click.
- **Priority-based Mod Loading** — Drag-and-drop reordering. Higher priority mods win conflicts.
- **FOMOD Installer** — Native FOMOD XML wizard support.
- **External Tools** — Register and launch tools (Bodyslide, Nemesis, LOOT, etc.) directly from CMM, with optional VFS auto-mount before launch.
- **Nexus Mods Integration** (plugin)
  - NXM link handler (`nxm://`) for one-click mod manager downloads.
  - In-app mod browser with full-text search.
  - Nexus Collections — paste a collection URL to queue all required mods for download.
  - Per-profile download history.
- **RE Engine / Capcom Games** — Automatic `.pak` mod detection and launcher integration.
- **Bethesda Games** — Plugin list management for Skyrim, Fallout, etc.
- **Game Auto-Detection** — Scans Steam, GOG, and Epic libraries.
- **Crash Recovery** — Hard link state persisted in SQLite; stale mounts cleaned up on next launch.

---

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- **Linux only:** FUSE (`sudo apt install fuse` on Debian/Ubuntu, `sudo pacman -S fuse2` on Arch)

### Build and Run

```bash
# Build
dotnet build CatModManager.slnx

# Run
dotnet run --project src/CatModManager.Ui/CatModManager.Ui.csproj

# Test
dotnet test CatModManager.slnx

# Single test class
dotnet test CatModManager.slnx --filter "FullyQualifiedName~SimpleConflictResolverTests"
```

---

## Architecture

```
CatModManager.Core          ← Business logic (models, services, VFS orchestration)
CatModManager.VirtualFileSystem ← Platform drivers (HardlinkDriver / FuseDriver)
CatModManager.PluginSdk     ← Public plugin API
CatModManager.Ui            ← Avalonia MVVM shell
src/plugins/
  CmmPlugin.NexusMods       ← Nexus Mods integration
  CmmPlugin.FomodInstaller  ← FOMOD XML wizard
  CmmPlugin.REEngine        ← RE Engine / Capcom games
  CmmPlugin.BethesdaTools   ← Bethesda plugin list
  CmmPlugin.SaveManager     ← Save backup (scaffold)
```

Dependency flow: `Ui` → `Core` ← `VirtualFileSystem`
Plugins depend only on `PluginSdk`; they are loaded at runtime from the `plugins/` output directory.

---

## Plugin Development

Implement `ICmmPlugin` from `CatModManager.PluginSdk`:

```csharp
public class MyPlugin : ICmmPlugin
{
    public string Id          => "my-plugin";
    public string DisplayName => "My Plugin";
    public string Version     => "1.0.0";
    public string Author      => "Me";

    public void Initialize(IPluginContext ctx)
    {
        ctx.Ui.RegisterInspectorTab(new MyTab());
        ctx.Ui.RegisterSidebarAction(new MyAction());
    }
}
```

Build your plugin as a class library referencing `CatModManager.PluginSdk.dll`. Drop the output DLL into the `plugins/` folder next to `CatModManager.dll`.
