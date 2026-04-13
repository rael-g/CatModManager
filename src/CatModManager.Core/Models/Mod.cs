using System.IO;
using CatModManager.PluginSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nett;

namespace CatModManager.Core.Models;

public partial class Mod : ObservableObject, IModInfo
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _modRootPath = string.Empty;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isArchive;

    public bool IsDirectory => !IsArchive && Directory.Exists(ModRootPath);
    public bool IsPhysicalArchive => IsArchive && File.Exists(ModRootPath);

    [ObservableProperty]
    private string _category = "Uncategorized";

    [ObservableProperty]
    private string _version = "1.0.0";

    [ObservableProperty]
    private bool _isSeparator;

    [ObservableProperty]
    private string? _mountPointId;

    [ObservableProperty]
    [property: TomlIgnore]
    private string? _mountPointDisplayName;

    [ObservableProperty]
    [property: TomlIgnore]
    private bool _isInstalling;

    [ObservableProperty]
    [property: TomlIgnore]
    private bool _isBroken;

    [ObservableProperty]
    [property: TomlIgnore]
    private double _installProgress;

    private System.Threading.CancellationTokenSource? _installCts;

    public void SetInstallCancellationTokenSource(System.Threading.CancellationTokenSource cts)
    {
        _installCts = cts;
    }

    private IRelayCommand? _cancelInstallCommand;
    
    [TomlIgnore]
    public IRelayCommand CancelInstallCommand => _cancelInstallCommand ??= new RelayCommand(CancelInstall);

    public void CancelInstall()
    {
        if (_installCts != null)
        {
            System.Diagnostics.Debug.WriteLine($"[Mod] Cancelling install for {Name}");
            _installCts.Cancel();
        }
    }

    public bool HasRootSubfolder
    {
        get => !string.IsNullOrEmpty(ModRootPath) && Directory.Exists(Path.Combine(ModRootPath, "Root"));
        set { }
    }

    public Mod() { }

    public Mod(string name, string modRootPath, int priority, bool isArchive = false, string category = "Uncategorized", string version = "1.0.0")
    {
        Name = name;
        ModRootPath = modRootPath;
        Priority = priority;
        IsArchive = isArchive;
        Category = category;
        Version = version;
    }
}
