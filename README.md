# 🐱 The Schrödinger Cat Mod Manager (CMM)

> *"Mods only exist when observed."*

**The Schrödinger Cat Mod Manager** is a professional-grade mod management solution for Windows and Linux, built with **.NET 10** and **Avalonia UI**. It features a high-performance **Virtual File System (VFS)** engine that ensures your original game directory remains 100% clean and untouched.

---

## 🔬 The "Safe Swap" Concept

CMM's defining feature is the **Safe Swap** system. Instead of physically moving or overwriting files in your game folder, it uses a kernel-level virtual overlay:

-   **Total Integrity:** Original game files are protected in a secure backup folder (`.CMM_base`) and never modified.
-   **Virtual Manifestation:** Mods only "exist" in the game directory while the game is launched through CMM.
-   **Zero Residue:** Once the game is closed, the directory instantly reverts to its pristine state, leaving no trace of uninstalled or corrupted mods.

---

## 🚀 Key Features

-   **Kernel-Level Virtualization:** Powered by WinFsp (Windows) and FUSE (Linux) for near-zero performance overhead.
- **Native Cross-Platform:** Full support for both Windows and Linux environments.
- **Universal Modding:** Designed as a generic manager with optional game-specific support via TOML-based **Game Definitions** and extensible **Plugins**.
- **"Atomic Lab" Interface:** A modern, dark-themed UI designed for speed and a minimalist user experience.
-   **Profile Management:** Create and switch between multiple mod configurations for the same game with a single click.
-   **Atomic Safety:** Robust protection mechanisms that prevent game file loss even during system crashes or power failures.

---

## 💻 Getting Started

### Prerequisites
-   .NET 10.0 SDK
-   **Windows:** [WinFsp](https://winfsp.dev/) installed.
-   **Linux:** FUSE installed.

### Build and Run
```bash
# Build the solution
dotnet build CatModManager.slnx

# Run the application
dotnet run --project src/CatModManager.Ui/CatModManager.Ui.csproj

# Run unit and integration tests
dotnet test CatModManager.slnx
```

---

*Manage your mods with the absolute certainty that your original game will never be touched.*
