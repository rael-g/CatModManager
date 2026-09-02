# Linux Dependencies

Written during the first real validation of CMM on Linux (Ubuntu 24.04, via distrobox). Use it as the
checklist when building the Linux installer/package (`pack.cs`).

**Testing outside distrobox**: registering `nxm://` only works properly when CMM runs in the same
mount namespace as the rest of the system (the game, the browser) — inside a distrobox, none of that
is visible from outside. To test it for real without permanently installing anything on the host, use
`dev-host-install.sh` / `dev-host-uninstall.sh` in this folder: they publish a self-contained build
into an isolated directory and install any missing pacman packages (ordinary system dependencies —
they stay installed). The uninstall script only removes that directory; it touches no packages.

## Runtime (must exist on the user's machine)

| Package (Ubuntu/Debian) | What for | Note |
|---|---|---|
| ~~`libfuse2t64`~~, ~~`fuse3`~~ | **No longer needed.** The FUSE driver was retired and the Safe Swap uses hard links on every platform, which requires no package at all — only a filesystem that supports hard links (ext4, btrfs, xfs, NTFS) | Can be dropped from the installer |
| `xdg-utils` (`xdg-mime`) | Registering the `nxm://` protocol (`LinuxNxmProtocolHandler`) | Without it registration fails silently (best-effort) — the "nxm" button in the UI stays permanently "not registered" |
| `desktop-file-utils` (`update-desktop-database`) | Refreshes the `.desktop` cache after registering/unregistering `nxm://` | Best-effort; if missing, registration still works but the desktop environment's cache may take a while to catch up |
| ASP.NET Core + .NET runtime (self-contained in the publish, so not an external dependency) | — | Publishing with `--self-contained true` (what `pack.cs` already does) avoids depending on the system `dotnet` |
| Avalonia's X11/GTK libraries (`libx11-6`, `libice6`, `libsm6`, `libfontconfig1`) | Rendering the Avalonia UI | Normally already present on any Linux desktop with a graphical environment; worth confirming on minimal distros |

## Dev-only (not shipped in the final package — only for building)

| Package | What for |
|---|---|
| `dotnet-sdk-10.0` | Build/publish |
| `git`, `git-lfs` | Cloning the repo (binary assets via LFS) |
| `build-essential` | Compiling transitive native dependencies |

## A real fix that stays in the code (not a workaround)

In [`src/CatModManager.Ui/Program.cs`](../../src/CatModManager.Ui/Program.cs), the single-instance
mechanism (IPC over a named pipe, forwarding `nxm://` to an already-open window) had a genuine bug on
Linux: `NamedPipeServerStream` does not refuse to bind to a pipe already in use — it silently deletes
and recreates it, so a second instance could "steal" the pipe from the first, leaving the first
unreachable for the rest of its run. Fixed with an exclusive file lock (`FileShare.None`) that
guarantees only one instance at a time runs the IPC server. This fix is permanent, works on any
distro, and should stay in the code.

## ⚠️ Dev-environment workarounds — NOT part of the product

While validating `nxm://` in this environment (distrobox `dev` + browser on the host), two artifacts
were created **outside the repo**, purely to make local testing possible. They do not exist in a real
installation and **must not be copied or packaged**:

- `~/.local/bin/cmm-nxm-launcher.sh` — a wrapper script that runs `distrobox enter dev -- .../CatModManager "%u"`.
- `~/.local/share/applications/cmm-nxm-handler.desktop` — a local `.desktop` pointing at that script.

**Why they exist**: in this setup CMM runs inside the distrobox `dev` (not installed natively on the
host), so the `nxm://` handler has to enter the container before executing the binary. In a real
installation (`pack.cs` linux, self-contained, straight onto the host) none of this is needed —
`LinuxNxmProtocolHandler.Register()` (in
[`src/plugins/CmmPlugin.NexusMods/LinuxNxmProtocolHandler.cs`](../../src/plugins/CmmPlugin.NexusMods/LinuxNxmProtocolHandler.cs))
already generates the right `.desktop`, with `Exec="{path-to-installed-binary}" "%u"` — a single
executable, no indirection.

**Finding that MUST survive the release**: the GLib / `gio launch` parser for `Exec=` (used by GNOME,
and by extension by the standard mechanism for opening `nxm://` from the browser) does not handle an
`Exec=` line with **multiple tokens before `%u`** (e.g. `distrobox enter dev -- /path/binary "%u"` —
5 tokens). In this test environment that made the named-pipe connection fail silently every time the
link was clicked in the browser, even though it worked perfectly from a terminal. The solution was to
always have **a single executable in `Exec=`** (the wrapper script here; the installed binary in the
real case). Since the real `LinuxNxmProtocolHandler` already generates `Exec="{exePath}" "%u"` (one
token + `%u`), it is already in the safe form — but if someone later "simplifies" this to include
extra arguments before `%u`, it is worth remembering this quirk before reintroducing the bug.

## Known gaps

- No native Steam/GOG scanner for Linux yet — auto-detection of installed games does not work there
  (today it only works via the Windows registry).
- ~~`winfsp.net` is dead weight in the publish~~ — **resolved.** It and `Mono.Fuse.NETStandard` were
  removed from `CatModManager.VirtualFileSystem.csproj` along with the retirement of the FUSE driver.
  `winfsp.net` in particular was referenced without a single line of code in the repository using it:
  it shipped in the binary for nothing.
