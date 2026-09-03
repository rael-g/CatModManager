using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CatModManager.Ui.ViewModels;
using CatModManager.Ui.Views;
using CatModManager.Ui.Plugins;
using CatModManager.Ui.Services;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Core.Vfs;
using CatModManager.VirtualFileSystem;
using CatModManager.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CatModManager.Ui;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        // Before anything reads a profile, and synchronously: the first thing the main window does
        // is load LastProfileName, and an import still running at that point looks to the user like
        // every profile is gone.
        //
        // Task.Run around it, not just GetResult(): this runs on the UI thread, which has a
        // SynchronizationContext, and the import awaits code that does not use ConfigureAwait(false)
        // — so its continuation would be posted back to the thread already blocked in GetResult().
        // Starting off the context means there is nothing to post back to.
        Task.Run(() => Services.GetRequiredService<ProfileImporter>().ImportIfEmptyAsync())
            .GetAwaiter().GetResult();

        LoadPlugins(Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void LoadPlugins(IServiceProvider services)
    {
        var pathService = services.GetRequiredService<ICatPathService>();
        var uiHost      = services.GetRequiredService<UiExtensionHost>();
        var eventBus    = services.GetRequiredService<IEventBus>();

        var loader         = services.GetRequiredService<PluginLoader>();
        var pluginBrowserVm = services.GetRequiredService<PluginBrowserViewModel>();
        pluginBrowserVm.SetPluginLoader(loader);

        loader.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins"));
        loader.LoadFrom(Path.Combine(pathService.BaseDataPath, "plugins"));

        var vm = services.GetRequiredService<MainWindowViewModel>();
        foreach (var tab    in uiHost.InspectorTabs)           vm.PluginInspectorTabs.Add(tab);
        foreach (var action in uiHost.SidebarActionsObservable) vm.PluginSidebarActions.Add(action);

        string? pendingNxm = Program.ConsumePendingNxmArg();
        if (pendingNxm != null)
            eventBus.Publish(new CatModManager.PluginSdk.NxmLinkEvent(pendingNxm));

        Program.NxmReceived += nxm =>
        {
            eventBus.Publish(new CatModManager.PluginSdk.NxmLinkEvent(nxm));
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow?.Activate();
        };
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICatPathService, CatPathService>();
        services.AddSingleton<AppDatabase>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IFileService, PhysicalFileService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IGameSupportService, GameSupportService>();
        services.AddSingleton<IRegistryService, WindowsRegistryService>();
        services.AddSingleton<IGameScanner, SteamScanner>();
        services.AddSingleton<IGameScanner, GogScanner>();
        services.AddSingleton<IGameScanner, EpicScanner>();
        services.AddSingleton<IGameDiscoveryService, GameDiscoveryService>();

        services.AddSingleton<IArchiveExtractor, SevenZipArchiveExtractor>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IModParser, TomlModParser>();
        services.AddSingleton<IModScanner, LocalModScanner>();
        services.AddSingleton<IProfileService, SqliteProfileService>();
        services.AddSingleton<IGameService, SqliteGameService>();
        services.AddSingleton<TomlProfileService>();
        services.AddSingleton<ProfileImporter>();
        services.AddSingleton<IModManagementService, ModManagementService>();
        services.AddSingleton<IVfsStateService, VfsStateService>();

        services.AddSingleton<IConflictResolver, SimpleConflictResolver>();
        services.AddSingleton<IHardlinkStateStore>(sp => new SqliteHardlinkStateStore(sp.GetRequiredService<AppDatabase>()));

        services.AddSingleton<IVfsOrchestrationService>(sp => new VfsOrchestrationService(
            sp.GetRequiredService<IConflictResolver>(),
            sp.GetRequiredService<IHardlinkStateStore>(),
            sp.GetRequiredService<IVfsStateService>(),
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<UiExtensionHost>().VfsHooks));
        services.AddSingleton<IGameLaunchService>(sp => new GameLaunchService(
            sp.GetRequiredService<IProcessService>(),
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<UiExtensionHost>().LaunchHooks));

        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<UiExtensionHost>();
        services.AddSingleton<IPluginRegistrar>(sp => sp.GetRequiredService<UiExtensionHost>());
        services.AddSingleton<IPluginLogger>(sp => new LogServiceAdapter(sp.GetRequiredService<ILogService>()));
        services.AddSingleton<AppSessionState>();
        services.AddSingleton<IModManagerState>(sp =>
            new ModManagerStateAdapter(sp.GetRequiredService<AppSessionState>()));
        services.AddSingleton<PluginLoader>(sp => new PluginLoader(
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<IPluginLogger>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<IPluginRegistrar>(),
            sp.GetRequiredService<IModManagerState>(),
            sp.GetRequiredService<IArchiveExtractor>(),
            sp.GetRequiredService<ICatPathService>()));

        services.AddSingleton<NuGetPluginService>();
        services.AddSingleton<PluginBrowserViewModel>();

        services.AddSingleton<MainWindowViewModel>();
    }
}
