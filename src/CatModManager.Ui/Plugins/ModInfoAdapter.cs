using CatModManager.Core.Models;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>Wraps Core's Mod as IModInfo so plugins have no Core dependency.</summary>
internal sealed class ModInfoAdapter : IModInfo
{
    private readonly Mod _mod;
    public ModInfoAdapter(Mod mod) => _mod = mod;

    public string Name     { get => _mod.Name;     set => _mod.Name     = value; }
    public string Version  { get => _mod.Version  ?? ""; set => _mod.Version  = value; }
    public string Category { get => _mod.Category ?? ""; set => _mod.Category = value; }
    public string ModRootPath => _mod.ModRootPath;
    public bool   IsEnabled => _mod.IsEnabled;
    public int    Priority  => _mod.Priority;
}
