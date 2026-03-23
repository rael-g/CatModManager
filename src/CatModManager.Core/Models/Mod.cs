using System.IO;
using CatModManager.PluginSdk;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatModManager.Core.Models;

public partial class Mod : ObservableObject, IModInfo
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _rootPath = string.Empty;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isArchive;

    [ObservableProperty]
    private string _category = "Uncategorized";

    [ObservableProperty]
    private string _version = "1.0.0";

    [ObservableProperty]
    private bool _isSeparator;

    /// <summary>True if this mod has a Root/ subfolder whose files will be deployed to the game root at mount time.</summary>
    /// <remarks>Computed at runtime; the setter is intentionally a no-op so TOML round-trips don't fail.</remarks>
    public bool HasRootFolder
    {
        get => !string.IsNullOrEmpty(RootPath) && Directory.Exists(Path.Combine(RootPath, "Root"));
        set { }
    }

    public Mod() { }

    public Mod(string name, string rootPath, int priority, bool isArchive = false, string category = "Uncategorized", string version = "1.0.0")
    {
        Name = name;
        RootPath = rootPath;
        Priority = priority;
        IsArchive = isArchive;
        Category = category;
        Version = version;
    }
}


