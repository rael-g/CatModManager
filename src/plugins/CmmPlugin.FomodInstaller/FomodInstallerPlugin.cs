using CatModManager.PluginSdk;

namespace CmmPlugin.FomodInstaller;

public class FomodInstallerPlugin : ICmmPlugin
{
    public string Id          => "fomod-installer";
    public string DisplayName => "FOMOD Installer";
    public string Version     => typeof(FomodInstallerPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string Author      => "CatModManager";

    public void Initialize(IPluginContext ctx)
    {
        var installer = new FomodModInstaller(ctx.Log, ctx.ArchiveExtractor);
        ctx.Ui.RegisterModInstaller(installer);

        ctx.Log.Log($"[{DisplayName}] Initialized — FOMOD archives will show the installation wizard.");
    }
}
