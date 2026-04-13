using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;
using CatModManager.Ui.Plugins;

namespace CatModManager.Ui.ViewModels;

public partial class ModInstallationCoordinator : ObservableObject
{
    private readonly IModManagementService _modManagementService;
    private readonly IModScanner           _modScanner;
    private readonly IFileService          _fileService;
    private readonly ILogService           _logService;
    private readonly AppSessionState       _sessionState;
    private readonly UiExtensionHost?      _uiExtensionHost;
    private readonly Func<GameConfigViewModel> _gameConfigProvider;
    private readonly Func<ModListViewModel>   _modListProvider;
    private readonly Action<Mod, string>      _onModInstalled;

    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _statusMessage = "Ready";

    private readonly List<Task> _activeInstallTasks = new();

    public double TotalInstallProgress
    {
        get
        {
            var installing = _modListProvider().AllMods.Where(m => m.IsInstalling).ToList();
            if (installing.Count == 0) return 0;
            return installing.Average(m => m.InstallProgress);
        }
    }

    public bool IsTotalProgressIndeterminate => IsInstalling && TotalInstallProgress <= 0;

    public ModInstallationCoordinator(
        IModManagementService modManagementService,
        IModScanner modScanner,
        IFileService fileService,
        ILogService logService,
        AppSessionState sessionState,
        UiExtensionHost? uiExtensionHost,
        Func<GameConfigViewModel> gameConfigProvider,
        Func<ModListViewModel> modListProvider,
        Action<Mod, string> onModInstalled)
    {
        _modManagementService = modManagementService;
        _modScanner = modScanner;
        _fileService = fileService;
        _logService = logService;
        _sessionState = sessionState;
        _uiExtensionHost = uiExtensionHost;
        _gameConfigProvider = gameConfigProvider;
        _modListProvider = modListProvider;
        _onModInstalled = onModInstalled;
    }

    public async Task InstallModAtMountPointAsync(string sourcePath, string? mountPointId)
    {
        var config = _gameConfigProvider();
        var modList = _modListProvider();

        if (string.IsNullOrEmpty(config.ModsFolderPath))
        {
            StatusMessage = "Error: Mods folder not set in Game Config.";
            return;
        }

        string targetBaseDir = config.ModsFolderPath;
        var mountPoint = config.EffectiveMountPoints.FirstOrDefault(mp => mp.Id == mountPointId) 
                         ?? config.EffectiveMountPoints.FirstOrDefault();
        
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var existingMod = modList.AllMods.FirstOrDefault(m => 
            string.Equals(Path.GetFileName(m.ModRootPath), baseName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Name, baseName, StringComparison.OrdinalIgnoreCase));

        Mod targetMod;
        bool isUpdate = existingMod != null;

        if (isUpdate)
        {
            targetMod = existingMod!;
            targetMod.IsInstalling = true;
            targetMod.InstallProgress = 0;
        }
        else
        {
            targetMod = new Mod(baseName, sourcePath, modList.AllMods.Count + 1)
            {
                IsInstalling = true,
                InstallProgress = 0,
                MountPointId = mountPoint?.Id ?? "Default",
                Category = "Uncategorized"
            };

            _sessionState.NotifyModInstalled(targetMod, sourcePath);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                modList.AllMods.Insert(0, targetMod);
                modList.RebuildDisplayedMods();
            });
        }

        var cts = new CancellationTokenSource();
        targetMod.SetInstallCancellationTokenSource(cts);

        StatusMessage = $"Installing {targetMod.Name}...";
        IsInstalling = true;

        try
        {
            var progress = new Progress<double>(p => {
                targetMod.InstallProgress = p;
                OnPropertyChanged(nameof(TotalInstallProgress));
            });
            string installedPath = string.Empty;

            var chosen = _uiExtensionHost?.ModInstallers.FirstOrDefault(i => i.CanInstall(sourcePath));
            
            Task<string> installTask;
            if (chosen != null)
            {
                var ctx = new SimpleInstallContext(config.ModsFolderPath, new LogServiceAdapter(_logService), _sessionState.ConsumePendingPreset());
                var installResult = await chosen.InstallAsync(sourcePath, ctx);
                if (installResult == null || !installResult.IsSuccess)
                {
                    if (!isUpdate) modList.AllMods.Remove(targetMod);
                    targetMod.IsInstalling = false;
                    modList.RebuildDisplayedMods();
                    StatusMessage = installResult?.ErrorMessage ?? "Install cancelled.";
                    return;
                }
                installTask = _modManagementService.InstallModFromMappingAsync(
                    sourcePath, baseName, targetBaseDir, installResult.FileMapping, isUpdate ? targetMod.ModRootPath : null, progress, cts.Token);
            }
            else
            {
                installTask = _modManagementService.InstallModAsync(sourcePath, targetBaseDir, isUpdate ? targetMod.ModRootPath : null, progress, cts.Token);
            }

            lock (_activeInstallTasks) _activeInstallTasks.Add(installTask);
            try { installedPath = await installTask; }
            catch (OperationCanceledException) { installedPath = string.Empty; }
            catch (Exception ex) { _logService.LogError("Install task failed", ex); installedPath = string.Empty; }
            finally { lock (_activeInstallTasks) _activeInstallTasks.Remove(installTask); }

            if (string.IsNullOrEmpty(installedPath))
            {
                if (!isUpdate)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                        modList.AllMods.Remove(targetMod);
                        modList.RebuildDisplayedMods();
                    });
                }
                targetMod.IsInstalling = false;
                StatusMessage = "Installation cancelled.";
                return;
            }

            targetMod.ModRootPath = installedPath;
            targetMod.IsArchive = false;
            
            _sessionState.NotifyModInstalled(targetMod, sourcePath);

            try
            {
                string sidecar = Path.Combine(installedPath, ".cmm_metadata.toml");
                if (_fileService.FileExists(sidecar))
                {
                    var meta = Nett.Toml.ReadFile<ModMetadata>(sidecar);
                    if (meta != null)
                    {
                        if (!string.IsNullOrEmpty(meta.Name) && string.Equals(targetMod.Name, baseName, StringComparison.OrdinalIgnoreCase))
                            targetMod.Name = meta.Name;
                        if (!string.IsNullOrEmpty(meta.Version) && meta.Version != "1.0.0")
                            targetMod.Version = meta.Version;
                        if (targetMod.Category == "Uncategorized" && !string.IsNullOrEmpty(meta.Category))
                            targetMod.Category = meta.Category;
                    }
                }
            }
            catch { }

            targetMod.IsInstalling = false;
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                modList.UpdatePriorities();
                modList.UpdateCategories();
                modList.RebuildDisplayedMods();
            });
            
            _onModInstalled(targetMod, sourcePath);
            StatusMessage = $"Mod '{targetMod.Name}' installed.";
        }
        catch (Exception ex)
        {
            _logService.LogError("Installation error", ex);
            StatusMessage = $"Error: {ex.Message}"; 
            if (!isUpdate)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                    modList.AllMods.Remove(targetMod);
                    modList.RebuildDisplayedMods();
                });
            }
            targetMod.IsInstalling = false;
        }
        finally 
        { 
            IsInstalling = modList.AllMods.Any(m => m.IsInstalling); 
            cts.Dispose();
        }
    }

    public void CancelAll()
    {
        var installingMods = _modListProvider().AllMods.Where(m => m.IsInstalling).ToList();
        foreach (var mod in installingMods) mod.CancelInstall();
    }

    public async Task WaitForTasks()
    {
        if (_activeInstallTasks.Count > 0)
            await Task.WhenAll(_activeInstallTasks.ToArray());
    }

    private sealed class SimpleInstallContext : IInstallContext
    {
        public string TargetFolder { get; }
        public string DestinationFolder => TargetFolder;
        public IPluginLogger Log { get; }
        public FomodPreset? FomodPreset { get; }
        public SimpleInstallContext(string target, IPluginLogger log, FomodPreset? preset) 
        { TargetFolder = target; Log = log; FomodPreset = preset; }
    }

    private class LogServiceAdapter(ILogService log) : IPluginLogger
    {
        public void Log(string msg) => log.Log(msg);
        public void LogError(string msg, Exception? ex = null) => log.LogError(msg, ex);
    }
}
