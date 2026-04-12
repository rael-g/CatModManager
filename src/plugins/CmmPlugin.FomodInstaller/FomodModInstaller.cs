using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CatModManager.PluginSdk;
using CmmPlugin.FomodInstaller.Models;
using CmmPlugin.FomodInstaller.Parser;
using CmmPlugin.FomodInstaller.Wizard;

namespace CmmPlugin.FomodInstaller;

public class FomodModInstaller : IModInstaller
{
    private readonly IPluginLogger _log;

    public FomodModInstaller(IPluginLogger log) => _log = log;

    /// <summary>
    /// Prepends the archive wrapper prefix to every source key so paths match what
    /// ModManagementService will see when enumerating the extracted temp directory.
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, string> ApplyWrapperPrefix(
        System.Collections.Generic.Dictionary<string, string> mapping, string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return mapping;
        var result = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (src, dst) in mapping)
            result[prefix + src] = dst;
        return result;
    }

    public bool CanInstall(string archivePath) => FomodParser.IsFomod(archivePath);

    public async Task<InstallResult> InstallAsync(string archivePath, IInstallContext ctx)
    {
        FomodModuleConfig config;
        try
        {
            config = FomodParser.Parse(archivePath);
        }
        catch (Exception ex)
        {
            return InstallResult.Failure($"Failed to parse FOMOD config: {ex.Message}");
        }

        // If a preset was supplied (e.g. from a Nexus Collection), auto-apply without showing the wizard.
        if (ctx.FomodPreset != null)
        {
            _log.Log($"[FOMOD] Auto-installing '{config.ModuleName}' using collection preset.");
            var vm = new FomodWizardViewModel(config);
            vm.ApplyPreset(ctx.FomodPreset);
            return InstallResult.Success(ApplyWrapperPrefix(vm.BuildFileMapping(), config.WrapperPrefix));
        }

        _log.Log($"[FOMOD] Launching wizard for: {config.ModuleName}");

        InstallResult? result = null;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var mainWindow = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            var wizard = new FomodWizardWindow(config, _log, archivePath);
            result = await wizard.ShowDialog<InstallResult?>(mainWindow!)
                     ?? InstallResult.Failure("Installation cancelled by user.");
        });

        if (result != null && result.IsSuccess && !string.IsNullOrEmpty(config.WrapperPrefix))
            result = InstallResult.Success(ApplyWrapperPrefix(result.FileMapping, config.WrapperPrefix));

        return result ?? InstallResult.Failure("Installation cancelled.");
    }
}
