# The Schrödinger Cat Mod Manager (CMM)

> *"Mods only exist when observed."*

A **universal** mod manager for Windows and Linux, built with **.NET 10** and **Avalonia UI**.

CMM overlays your mods onto the game directory only while you play, and removes
them cleanly when you stop. Your game installation is never modified.

---

## Why this exists

This project started with a broken game install.

Installing mods by hand into a game running under an emulator went wrong, there
was no disk space for a backup, and the only way out was a full re-download. The
obvious next step was to find a mod manager that handled emulated games — and
there wasn't one.

That absence is not an oversight. It follows from the two approaches mod managers
usually take.

One builds a virtual filesystem by injecting code into the game process. It gives
excellent isolation, but it has to be taught about each executable it hooks, so
every new title is an engineering task and support arrives some time after
release. An emulated game is out of reach entirely: the process is the
*emulator*, and hooking it does not give you the filesystem the emulated game
sees.

The other simply copies mod files into the game folder and leaves them there.
Nothing to teach, and it works anywhere — but there is no isolation. What's
installed is installed, and undoing it is your problem.

CMM takes a third position. It mounts a directory and gets out of the way.
It does not know, and does not need to know, what is going to read those files —
a native game, a Proton prefix, or an emulator are all the same to it.

**The practical consequence: supporting a new game costs nothing.** You point CMM
at a folder and it works on release day, without waiting for anyone to ship an
update.

---

## The "Safe Swap"

One backend on every platform: **hard links**, created at mount time. Originals
are set aside with a dot-prefix and restored on unmount. No kernel driver, no
admin rights, no file content ever copied.

A read-only FUSE overlay was used on Linux for a while, and it is retired. It
only ever worked on one platform, so it doubled the polish and testing for a
minority of setups — and it was not a harmless parallel path: a mount left
behind by a killed process stays registered but disconnected, and everything
underneath it then fails with `ENOTCONN`, taking the hard link fallback down
with it. The implementation stays in the repository history; it is no longer
built or shipped.

This is a *session*, not a deployment. It has a beginning and an end, and the
end returns the game to exactly where it started.

---

## Multiple mount points

A game is rarely one folder. Mods land in the `Data` directory, script extenders
and ENB land in the game *root*, and configuration and saves live somewhere under
your user profile entirely.

CMM treats this as the normal case. A profile can have any number of mount
points, each pointing at a relative path (resolved against the game folder), an
absolute path, or the game root itself.

**Mount points are added in the app, at any time, to any profile.** They are not
a property of a supported game — they are yours. Nothing needs to be declared in
advance, no file has to exist, and you do not have to wait for a game to be
"supported" before you can put mods somewhere unusual. Add a mount point for a
config folder in `AppData`, for a second data directory an emulator reads from,
for anything at all — and remove it when you're done.

A game definition can pre-fill a few of them so you don't have to type the
obvious ones. That is the only thing it does here.

This is what makes script extenders, ENB, and similar root-level components
ordinary. Their files have to sit next to the game executable, outside the `Data`
folder — which is why they are so often the one thing you're told to copy in by
hand, mod manager or not. Here the game root is just another mount point:

```toml
[[MountPoints]]
Id   = "data"
Name = "Data"
Path = "Data"          # relative → <game folder>/Data

[[MountPoints]]
Id   = "root"
Name = "Game Root"
Path = ""              # empty → the game folder itself
```

Root-level components install like any other mod — managed, profile-aware, and
removed cleanly on unmount.

---

## Community-maintainable game support

CMM is designed so that **the core and the game support are separate concerns**,
maintained by different people.

### Nothing is required

You do not need a definition file to play. Select the game folder and the
executable, and CMM has what it needs. Everything below is convenience.

### Game definitions are plain TOML

A definition is a text file. No code, no compilation, no pull request to this
repository. Drop it into your `game_definitions` folder and it is picked up.

```toml
GameId      = "skyrimspecialedition"
DisplayName = "The Elder Scrolls V: Skyrim Special Edition"

NexusDomain = "skyrimspecialedition"
SteamAppId  = 489830

# Used to recognise the folder during auto-detection.
RequiredFiles = ["SkyrimSE.exe", "Data"]

[[MountPoints]]
Id   = "data"
Name = "Data"
Path = "Data"

[[MountPoints]]
Id   = "root"
Name = "Game Root"
Path = ""

SaveFolderPattern = "%USERPROFILE%\\Documents\\My Games\\Skyrim Special Edition\\Saves"
```

Paths may be relative, absolute, or contain environment variables. Mount points
that come from the file are marked as game-defined and shown read-only, so a
definition can't be silently broken — but you can add as many of your own
alongside them as you like.

Over twenty definitions ship in [`samples/game_definitions/`](samples/game_definitions/)
— Skyrim, Elden Ring, Baldur's Gate 3, Cyberpunk 2077, the RE Engine catalogue,
Armored Core VI, Dragon's Dogma 2, and others.

### Plugins handle the rest

When a game needs behaviour rather than configuration, that goes in a plugin
against `CatModManager.PluginSdk`. Plugins are loaded at runtime from the
`plugins/` directory next to the executable — installing one is copying a DLL.

This split is deliberate. **If development of the core stops, game support does
not have to.** A new release can be covered by a TOML file written in five
minutes, and a genuinely unusual game by a plugin that never touches this
codebase.

---

## Features

**Core**

- **Profiles** — multiple mod configurations per game, switched with one click.
- **Priority-based conflict resolution** — drag-and-drop ordering; higher priority wins.
- **Multiple mount points** — any number, per profile, relative or absolute, added and removed at any time from the app.
- **Game auto-detection** — scans Steam, GOG, and Epic libraries.
- **Crash recovery** — hard-link state persisted in SQLite; stale mounts cleaned on next launch.
- **External tools** — register BodySlide, Nemesis, LOOT, xEdit and friends, and launch them from CMM with the VFS mounted first, so they see the same merged view the game will.
- **Launch integration** — launch the game directly, or through a platform: put `steam` in the executable field and `-applaunch <appid>` in the arguments to keep the overlay and achievements working.

**Bundled plugins**

| Plugin | What it does |
|---|---|
| `CmmPlugin.NexusMods` | `nxm://` one-click download handler, in-app mod browser with search, Nexus Collections (paste a URL to queue every required mod), per-profile download history |
| `CmmPlugin.FomodInstaller` | Native FOMOD XML installer wizard |
| `CmmPlugin.REEngine` | RE Engine / Capcom `.pak` detection and launcher integration |
| `CmmPlugin.BethesdaTools` | Plugin list (`plugins.txt`) management for Skyrim, Fallout, Starfield |
| `CmmPlugin.SaveManager` | Save backup (scaffold) |

---

## Installation

### Requirements

- **.NET 10.0** runtime (SDK if building from source)
- **Linux:** `xdg-utils`, `desktop-file-utils` — exact package names per
  distribution are in [deploy/linux/DEPENDENCIES.md](deploy/linux/DEPENDENCIES.md).
  A filesystem that supports hard links, which covers ext4, btrfs, xfs and NTFS.
- **Windows:** an **NTFS** volume for the game (hard links are an NTFS feature).
  No administrator rights needed.

### From source

```bash
git clone <repo-url> CatModManager
cd CatModManager

dotnet build CatModManager.slnx
dotnet run --project src/CatModManager.Ui/CatModManager.Ui.csproj
```

### Linux — install to the desktop

```bash
./deploy/linux/dev-host-install.sh     # installs .desktop entry + nxm:// handler
./deploy/linux/dev-host-uninstall.sh   # removes it
```

### Windows — installer

An Inno Setup script is provided at [deploy/windows/CatModManager.iss](deploy/windows/CatModManager.iss).

---

## Usage

1. **Add a game.** CMM scans your Steam, GOG, and Epic libraries on first run.
   If yours isn't found, point it at the folder yourself.
2. **Check the mount points.** A shipped definition configures them for you.
   Otherwise add them — usually `Data` and the root, or just the root.
3. **Install mods.** Drag archives in, or let the `nxm://` handler catch
   downloads from Nexus. FOMOD installers open their wizard automatically.
4. **Order them.** Drag to set priority. Later wins conflicts.
5. **Mount and play.** Press LAUNCH — CMM mounts, starts the game, and unmounts
   once it exits. Or mount by hand and leave it, if you'd rather.

Anything CMM mounted for you, CMM unmounts. A mount you made by hand is yours and
stays.

---

## Architecture

```
CatModManager.Core               ← models, services, VFS orchestration
CatModManager.VirtualFileSystem  ← deployment driver (HardlinkDriver)
CatModManager.PluginSdk          ← the public plugin API
CatModManager.Ui                 ← Avalonia MVVM shell
src/plugins/                     ← the bundled plugins listed above
```

Dependencies flow `Ui → Core ← VirtualFileSystem`. Plugins depend on
`PluginSdk` alone, which is why they can be built and shipped independently of
this repository.

```bash
dotnet test CatModManager.slnx
dotnet test CatModManager.slnx --filter "FullyQualifiedName~SimpleConflictResolverTests"
```

---

## Writing a plugin

Implement `ICmmPlugin`:

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

Build as a class library referencing `CatModManager.PluginSdk.dll` and drop the
output into `plugins/`, next to `CatModManager.dll`.

---

## Status

Version 0.1.0 — early, and under active development. Expect rough edges, and
please report the ones you hit.

## License

MIT — see [LICENSE](LICENSE).
