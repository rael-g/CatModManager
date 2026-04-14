using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Ui.ViewModels;

public partial class VfsLifecycleCoordinator : ObservableObject
{
    private readonly IVfsOrchestrationService _vfsOrchestrator;
    private readonly ILogService              _logService;
    private readonly Func<GameConfigViewModel> _gameConfigProvider;
    private readonly Func<ModListViewModel>   _modListProvider;
    private readonly Action                   _syncSessionState;

    [ObservableProperty] private bool _isVfsMounted;
    [ObservableProperty] private string _statusMessage = "Ready";

    public string MountButtonText  => IsVfsMounted ? "Unmount" : "Mount";
    public string MountButtonIcon  => IsVfsMounted ? "◉" : "○";
    public IBrush MountButtonColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    public string SafeSwapStatusText  => IsVfsMounted ? "Safe Swap: Active" : "Safe Swap: Standby";
    public IBrush SafeSwapStatusColor => IsVfsMounted ? _mountedBrush : _unmountedBrush;

    private static readonly IBrush _mountedBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush _unmountedBrush = new SolidColorBrush(Color.Parse("#757575"));

    public VfsLifecycleCoordinator(
        IVfsOrchestrationService vfsOrchestrator,
        ILogService logService,
        Func<GameConfigViewModel> gameConfigProvider,
        Func<ModListViewModel> modListProvider,
        Action syncSessionState)
    {
        _vfsOrchestrator = vfsOrchestrator;
        _logService = logService;
        _gameConfigProvider = gameConfigProvider;
        _modListProvider = modListProvider;
        _syncSessionState = syncSessionState;
    }

    partial void OnIsVfsMountedChanged(bool value)
    {
        OnPropertyChanged(nameof(MountButtonText));
        OnPropertyChanged(nameof(MountButtonIcon));
        OnPropertyChanged(nameof(MountButtonColor));
        OnPropertyChanged(nameof(SafeSwapStatusText));
        OnPropertyChanged(nameof(SafeSwapStatusColor));
    }

    [RelayCommand]
    public async Task ToggleMount()
    {
        var res = await ToggleMountInternal();
        if (!res.IsSuccess) StatusMessage = res.ErrorMessage;
    }

    public async Task<OperationResult> ToggleMountInternal()
    {
        if (IsVfsMounted)
        {
            StatusMessage = "Unmounting...";
            var res = await _vfsOrchestrator.UnmountAsync();
            if (res.IsSuccess)
            {
                IsVfsMounted = false;
                StatusMessage = "Unmounted successfully.";
            }
            return res;
        }
        else
        {
            var config = _gameConfigProvider();
            var modList = _modListProvider();

            if (config.ActiveGameSupport == null) return OperationResult.Failure("No game selected.");
            if (string.IsNullOrEmpty(config.BaseFolderPath)) return OperationResult.Failure("Game folder not set.");

            StatusMessage = "Mounting Virtual File System...";
            _syncSessionState();
            
            var res = await _vfsOrchestrator.MountAsync(new MountOptions
            {
                GameFolderPath = config.BaseFolderPath,
                ActiveMods     = modList.AllMods.Where(m => m.IsEnabled && !m.IsBroken).ToList(),
                MountPoints    = config.EffectiveMountPoints.ToList()
            });
            
            if (res.IsSuccess)
            {
                IsVfsMounted = true;
                StatusMessage = "VFS Mounted & Safe Swap Active.";
            }
            return res;
        }
    }
}
