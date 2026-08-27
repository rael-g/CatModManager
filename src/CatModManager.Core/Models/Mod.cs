using System;
using System.IO;
using CatModManager.PluginSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nett;

namespace CatModManager.Core.Models;

/// <summary>
/// Data model for a Mod. 
/// Inherits from ObservableObject for UI binding, but uses manual properties
/// to ensure compatibility with Nett's reflection-based serialization.
/// </summary>
public class Mod : ObservableObject, IModInfo
{
    private string _name = string.Empty;
    public string Name 
    { 
        get => _name; 
        set => SetProperty(ref _name, value); 
    }

    private string _modRootPath = string.Empty;
    public string ModRootPath 
    { 
        get => _modRootPath; 
        set => SetProperty(ref _modRootPath, value); 
    }

    private int _priority;
    public int Priority 
    { 
        get => _priority; 
        set => SetProperty(ref _priority, value); 
    }

    private bool _isEnabled = true;
    public bool IsEnabled 
    { 
        get => _isEnabled; 
        set => SetProperty(ref _isEnabled, value); 
    }

    private bool _isArchive;
    public bool IsArchive 
    { 
        get => _isArchive; 
        set => SetProperty(ref _isArchive, value); 
    }

    [TomlIgnore]
    public bool IsDirectory => !IsArchive && Directory.Exists(ModRootPath);
    
    [TomlIgnore]
    public bool IsPhysicalArchive => IsArchive && File.Exists(ModRootPath);

    private string _category = "Uncategorized";
    public string Category 
    { 
        get => _category; 
        set => SetProperty(ref _category, value); 
    }

    private string _version = "1.0.0";
    public string Version 
    { 
        get => _version; 
        set => SetProperty(ref _version, value); 
    }

    private bool _isSeparator;
    public bool IsSeparator 
    { 
        get => _isSeparator; 
        set => SetProperty(ref _isSeparator, value); 
    }

    private string? _mountPointId;
    public string? MountPointId 
    { 
        get => _mountPointId; 
        set => SetProperty(ref _mountPointId, value); 
    }

    private string? _mountPointDisplayName;
    [TomlIgnore]
    public string? MountPointDisplayName 
    { 
        get => _mountPointDisplayName; 
        set => SetProperty(ref _mountPointDisplayName, value); 
    }

    private bool _isInstalling;
    [TomlIgnore]
    public bool IsInstalling 
    { 
        get => _isInstalling; 
        set => SetProperty(ref _isInstalling, value); 
    }

    private bool _isDragging;
    /// <summary>True while this row is being dragged, so the list can show which one is moving.</summary>
    [TomlIgnore]
    public bool IsDragging
    {
        get => _isDragging;
        set => SetProperty(ref _isDragging, value);
    }

    private bool _isBroken;
    [TomlIgnore]
    public bool IsBroken
    { 
        get => _isBroken; 
        set => SetProperty(ref _isBroken, value); 
    }

    private double _installProgress;
    [TomlIgnore]
    public double InstallProgress 
    { 
        get => _installProgress; 
        set => SetProperty(ref _installProgress, value); 
    }

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
            _installCts.Cancel();
        }
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
