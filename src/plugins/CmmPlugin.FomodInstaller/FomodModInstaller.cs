using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CatModManager.PluginSdk;
using CatModManager.PluginSdk;
using CmmPlugin.FomodInstaller.Models;
using CmmPlugin.FomodInstaller.Parser;
using CmmPlugin.FomodInstaller.Wizard;

namespace CmmPlugin.FomodInstaller;

public class FomodModInstaller : IModInstaller
{
    private readonly IPluginLogger _log;

    public FomodModInstaller(IPluginLogger log) => _log = log;

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

        _log.Log($"[FOMOD] Launching wizard for: {config.ModuleName}");

        InstallResult? result = null;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var mainWindow = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            var wizard = new FomodWizardWindow(config, _log);
            result = await wizard.ShowDialog<InstallResult?>(mainWindow)
                     ?? InstallResult.Failure("Installation cancelled by user.");
        });

        return result ?? InstallResult.Failure("Installation cancelled.");
    }
}
