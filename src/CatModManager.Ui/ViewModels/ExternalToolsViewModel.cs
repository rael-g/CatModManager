using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Ui.ViewModels;

public partial class ExternalToolsViewModel : ViewModelBase
{
    private readonly IProcessService          _processService;
    private readonly IVfsOrchestrationService _vfsOrchestrator;
    private readonly ILogService              _logService;

    // Callbacks wired by MainWindowViewModel
    public Func<bool>?                  IsVfsMounted     { get; set; }
    public Func<Task<OperationResult>>? EnsureMounted    { get; set; }
    public Action?                      AutoSave         { get; set; }
    public Func<string, Task>?          PickExecutable   { get; set; }

    public ObservableCollection<ExternalTool> Tools { get; } = new();

    [ObservableProperty] private ExternalTool? _selectedTool;
    [ObservableProperty] private string        _statusMessage = "";

    public ExternalToolsViewModel(
        IProcessService          processService,
        IVfsOrchestrationService vfsOrchestrator,
        ILogService              logService)
    {
        _processService  = processService;
        _vfsOrchestrator = vfsOrchestrator;
        _logService      = logService;
    }

    public void LoadTools(System.Collections.Generic.IEnumerable<ExternalTool> tools)
    {
        Tools.Clear();
        foreach (var t in tools) Tools.Add(t);
    }

    public System.Collections.Generic.List<ExternalTool> GetTools()
        => new(Tools);

    [RelayCommand]
    private async Task LaunchTool(ExternalTool? tool)
    {
        if (tool == null || string.IsNullOrEmpty(tool.ExecutablePath)) return;

        if (tool.MountBeforeLaunch && EnsureMounted != null)
        {
            var result = await EnsureMounted();
            if (!result.IsSuccess)
            {
                StatusMessage = $"Mount failed: {result.ErrorMessage}";
                return;
            }
        }

        StatusMessage = $"Launching {tool.Name}…";
        _logService.Log($"[Tools] Launching: {tool.ExecutablePath} {tool.Arguments}");

        // The result used to be discarded and the status blanked unconditionally, so a tool with a
        // stale path — moved, uninstalled, on an unmounted drive — produced a flash of "Launching…"
        // and then nothing at all. Indistinguishable from a tool that opened fine on another
        // workspace, which is the one case where seeing nothing is correct.
        var launch = await _processService.StartProcessAsync(tool.ExecutablePath, tool.Arguments, waitForChildren: false);

        if (launch.Started)
        {
            StatusMessage = "";
            return;
        }

        StatusMessage = $"Could not launch {tool.Name} — check that '{tool.ExecutablePath}' still exists.";
        _logService.LogError($"[Tools] Launch failed for '{tool.Name}': {tool.ExecutablePath}");
    }

    [RelayCommand]
    private async Task AddTool()
    {
        if (PickExecutable == null) return;
        await PickExecutable("exe");
    }

    /// <summary>Called from code-behind after the file dialog resolves.</summary>
    public void AddToolFromPath(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return;
        var tool = new ExternalTool
        {
            Name           = Path.GetFileNameWithoutExtension(exePath),
            ExecutablePath = exePath
        };
        Tools.Add(tool);
        SelectedTool = tool;
        AutoSave?.Invoke();
    }

    [RelayCommand]
    private void RemoveTool(ExternalTool? tool)
    {
        if (tool == null) return;
        Tools.Remove(tool);
        if (SelectedTool == tool) SelectedTool = null;
        AutoSave?.Invoke();
    }

    [RelayCommand]
    private async Task OpenToolFolder(ExternalTool? tool)
    {
        if (tool == null || string.IsNullOrEmpty(tool.ExecutablePath)) return;
        var dir = Path.GetDirectoryName(tool.ExecutablePath) ?? "";
        await _processService.OpenFolderAsync(dir);
    }
}
