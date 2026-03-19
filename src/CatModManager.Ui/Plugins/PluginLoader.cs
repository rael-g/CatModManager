using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>
/// Scans the plugins directory, loads each plugin in an isolated AssemblyLoadContext,
/// and calls Initialize() on every discovered ICmmPlugin.
/// </summary>
public class PluginLoader
{
    private readonly ILogService _log;
    private readonly IPluginContext _context;
    private readonly List<ICmmPlugin> _loaded = new();

    public IReadOnlyList<ICmmPlugin> LoadedPlugins => _loaded;

    public PluginLoader(ILogService log, IPluginContext context)
    {
        _log = log;
        _context = context;
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

    private void TryLoadPlugin(string dllPath)
    {
        try
        {
            var alc = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dllPath), isCollectible: false);
            var assembly = alc.LoadFromAssemblyPath(dllPath);

            var pluginTypes = assembly.GetExportedTypes()
                .Where(t => typeof(ICmmPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in pluginTypes)
            {
                if (Activator.CreateInstance(type) is ICmmPlugin plugin)
                {
                    plugin.Initialize(_context);
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
