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

        LoadPlugins(Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
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
        services.AddSingleton<IGameDiscoveryService, GameDiscoveryService>();

        services.AddSingleton<IDriverService, HardlinkDriverService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IModParser, TomlModParser>();
        services.AddSingleton<IModScanner, LocalModScanner>();
        services.AddSingleton<IProfileService, TomlProfileService>();
        services.AddSingleton<IModManagementService, ModManagementService>();
        services.AddSingleton<IVfsStateService, VfsStateService>();
        services.AddSingleton<IRootSwapService, RootSwapService>();

        services.AddSingleton<IConflictResolver, SimpleConflictResolver>();
        services.AddSingleton<IHardlinkStateStore>(sp => new SqliteHardlinkStateStore(sp.GetRequiredService<AppDatabase>()));
        services.AddSingleton<IFileSystemDriver>(sp => FileSystemFactory.CreateDriver(sp.GetRequiredService<IHardlinkStateStore>()));
        // ISafeSwapStrategy: NoBaseSwapStrategy (HardlinkDriver/Windows) or
        //                    PassthroughSwapStrategy (FuseDriver/Linux).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            services.AddSingleton<ISafeSwapStrategy, NoBaseSwapStrategy>();
        else
            services.AddSingleton<ISafeSwapStrategy, PassthroughSwapStrategy>();
        services.AddSingleton<IVirtualFileSystem, CatVirtualFileSystem>();

        services.AddSingleton<IVfsOrchestrationService>(sp => new VfsOrchestrationService(
            sp.GetRequiredService<IVirtualFileSystem>(),
            sp.GetRequiredService<IVfsStateService>(),
            sp.GetRequiredService<IDriverService>(),
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<IRootSwapService>(),
            sp.GetRequiredService<UiExtensionHost>().VfsHooks));
        services.AddSingleton<IGameLaunchService>(sp => new GameLaunchService(
            sp.GetRequiredService<IProcessService>(),
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<UiExtensionHost>().LaunchHooks));

        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<UiExtensionHost>();
        services.AddSingleton<IPluginRegistrar>(sp => sp.GetRequiredService<UiExtensionHost>());
        services.AddSingleton<IPluginLogger>(sp => new LogServiceAdapter(sp.GetRequiredService<ILogService>()));
        services.AddSingleton<CmmSettingsFactory>(sp =>
            new CmmSettingsFactory(sp.GetRequiredService<AppDatabase>()));
        services.AddSingleton<AppSessionState>();
        services.AddSingleton<IModManagerState>(sp =>
            new ModManagerStateAdapter(sp.GetRequiredService<AppSessionState>()));
        services.AddSingleton<PluginLoader>();

        services.AddSingleton<NuGetPluginService>();
        services.AddSingleton<PluginBrowserViewModel>();

        services.AddSingleton<MainWindowViewModel>();
    }
}
