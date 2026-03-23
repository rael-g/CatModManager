using System;
using System.Collections.Generic;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Observable state bag that plugins read via IModManagerState.
/// Updated by MainWindowViewModel; decouples the adapter from ViewModel internals.
/// </summary>
public sealed class AppSessionState
{
    // ── Snapshot properties ───────────────────────────────────────────────────

    public IReadOnlyList<IModInfo> ActiveMods        { get; set; } = [];
    public string?                 DataFolderPath    { get; set; }
    public string?                 ModsFolderPath    { get; set; }
    public string?                 DownloadsFolderPath { get; set; }
    public string?                 GameExecutablePath { get; set; }
    public string?                 GameId            { get; set; }
    public string?                 NexusDomain       { get; set; }
    public string?                 CurrentProfileName { get; set; }
    public string?                 DataSubFolder     { get; set; }
    public bool                    RootSwapOnly      { get; set; }

    // ── Events fired by MainWindowViewModel ───────────────────────────────────

    public event Action<string>?           ProfileChanged;
    public event Action<IModInfo, string>? ModInstalled;

    /// <summary>Wired by MainWindowViewModel to execute AddModCommand on the UI thread.</summary>
    public Action<string, FomodPreset?>? RequestInstallModAction { get; set; }

    /// <summary>Wired by MainWindowViewModel to execute AddModToRootCommand on the UI thread.</summary>
    public Action<string>? RequestInstallModToRootAction { get; set; }

    /// <summary>
    /// Temporarily holds a FOMOD preset between RequestInstallMod and the actual install.
    /// Consumed (set to null) by MainWindowViewModel.AddMod.
    /// </summary>
    internal FomodPreset? PendingFomodPreset { get; private set; }

    internal FomodPreset? ConsumePendingPreset()
    {
        var p = PendingFomodPreset;
        PendingFomodPreset = null;
        return p;
    }

    // ── Notification helpers ───────────────────────────────────────────────────

    internal void NotifyProfileChanged(string name) => ProfileChanged?.Invoke(name);
    internal void NotifyModInstalled(IModInfo mod, string sourcePath) => ModInstalled?.Invoke(mod, sourcePath);

    internal void RequestInstall(string archivePath, FomodPreset? preset = null)
    {
        PendingFomodPreset = preset;
        RequestInstallModAction?.Invoke(archivePath, preset);
    }

    internal void RequestInstallToRoot(string archivePath)
        => RequestInstallModToRootAction?.Invoke(archivePath);
}
