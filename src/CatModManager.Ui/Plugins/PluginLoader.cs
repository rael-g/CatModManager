using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;
using CatModManager.Ui.Services;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Scans the plugins directory, loads each plugin in an isolated AssemblyLoadContext,
/// and calls Initialize() on every discovered ICmmPlugin.
/// </summary>
public class PluginLoader
{
    private readonly ILogService        _log;
    private readonly IPluginLogger      _pluginLogger;
    private readonly IEventBus          _events;
    private readonly IPluginRegistrar   _ui;
    private readonly IModManagerState   _state;
    private readonly IArchiveExtractor  _extractor;
    private readonly ICatPathService    _pathService;

    private readonly List<ICmmPlugin> _loaded = new();

    public IReadOnlyList<ICmmPlugin> LoadedPlugins => _loaded;

    public PluginLoader(
        ILogService        log,
        IPluginLogger      pluginLogger,
        IEventBus          events,
        IPluginRegistrar   ui,
        IModManagerState   state,
        IArchiveExtractor  extractor,
        ICatPathService    pathService)
    {
        _log             = log;
        _pluginLogger    = pluginLogger;
        _events          = events;
        _ui              = ui;
        _state           = state;
        _extractor       = extractor;
        _pathService     = pathService;
    }

    public void LoadFrom(string pluginsDirectory)
    {
        if (!Directory.Exists(pluginsDirectory)) return;

        foreach (var dir in Directory.GetDirectories(pluginsDirectory))
        {
            var dlls = Directory.GetFiles(dir, "CmmPlugin.*.dll");
            foreach (var dll in dlls)
                TryLoadPlugin(dll);
        }
    }

    public virtual async Task ShutdownAllAsync()
    {
        foreach (var plugin in _loaded)
        {
            try { await plugin.ShutdownAsync(); }
            catch (Exception ex) { _log.LogError($"Plugin shutdown error: {plugin.DisplayName}", ex); }
        }
        _loaded.Clear();
    }

    private void TryLoadPlugin(string dllPath)
    {
        try
        {
            var alc      = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dllPath), isCollectible: false);
            var assembly = alc.LoadFromAssemblyPath(dllPath);

            var pluginTypes = assembly.GetExportedTypes()
                .Where(t => typeof(ICmmPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in pluginTypes)
            {
                if (Activator.CreateInstance(type) is ICmmPlugin plugin)
                {
                    var context = new PluginContext(_pluginLogger, _events, _ui, _state, _extractor, _pathService);

                    plugin.Initialize(context);
                    _loaded.Add(plugin);
                    _log.Log($"Plugin loaded: {plugin.DisplayName} v{plugin.Version} by {plugin.Author}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Failed to load plugin from {Path.GetFileName(dllPath)}", ex);
        }
    }
}
