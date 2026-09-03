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
    public Func<Task<OperationResult>>? RequestUnmount   { get; set; }
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

        // Only a mount this launch made gets undone afterwards. A mount you made yourself is a
        // decision of yours, and outliving the tool is the point of having made it — the same rule
        // the game's launch already follows.
        bool mountedForThisLaunch = false;

        if (tool.MountBeforeLaunch && EnsureMounted != null)
        {
            bool wasMounted = IsVfsMounted?.Invoke() ?? false;

            var result = await EnsureMounted();
            if (!result.IsSuccess)
            {
                StatusMessage = $"Mount failed: {result.ErrorMessage}";
                return;
            }

            mountedForThisLaunch = !wasMounted;
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

            // Not awaited: the tool is the user's now, and the wizard-style "Launching…" message
            // hanging around until they closed it was the whole complaint. The unmount rides along
            // in the background instead.
            if (mountedForThisLaunch && launch.Exited != null)
                _ = UnmountWhenClosed(tool, launch.Exited);

            return;
        }

        // Which of the two it is matters: "wine" not being installed and "/path/tool.exe" having
        // been moved are different problems, and the old wording only described the second.
        bool isCommand = !tool.ExecutablePath.Contains(Path.DirectorySeparatorChar)
                      && !tool.ExecutablePath.Contains(Path.AltDirectorySeparatorChar);

        StatusMessage = isCommand
            ? $"Could not launch {tool.Name}: the command '{tool.ExecutablePath}' was not found. Is it installed?"
            : $"Could not launch {tool.Name}: '{tool.ExecutablePath}' could not be started. Check that it still exists.";

        _logService.LogError($"[Tools] Launch failed for '{tool.Name}': {tool.ExecutablePath} {tool.Arguments}");
    }

    /// <summary>
    /// Undoes the mount this launch made, once the tool is gone.
    ///
    /// Deliberately tolerant: the user may have unmounted by hand, or launched the game in the
    /// meantime and still be playing. Undoing a mount that is no longer ours to undo would pull the
    /// files out from under whatever is using them, so the state is re-checked at the moment it
    /// matters rather than assumed from when the tool started.
    /// </summary>
    private async Task UnmountWhenClosed(ExternalTool tool, Task exited)
    {
        try
        {
            await exited;

            if (IsVfsMounted?.Invoke() != true) return;
            if (RequestUnmount == null) return;

            var result = await RequestUnmount();
            StatusMessage = result.IsSuccess
                ? ""
                : $"{tool.Name} closed, but unmounting failed: {result.ErrorMessage}";

            _logService.Log($"[Tools] '{tool.Name}' closed — unmounted.");
        }
        catch (Exception ex)
        {
            _logService.LogError($"[Tools] Failed to unmount after '{tool.Name}' closed", ex);
        }
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

    /// <summary>A new, empty entry for the editor to fill in — the path to a file is only one of the ways a tool is named.</summary>
    public void AddBlankTool()
    {
        var tool = new ExternalTool { Name = "New tool" };
        Tools.Add(tool);
        SelectedTool = tool;
        AutoSave?.Invoke();
    }

    /// <summary>Fills the command of the selected tool from the file dialog, naming it if it has no name yet.</summary>
    public void SetExecutable(string exePath)
    {
        if (SelectedTool == null || string.IsNullOrEmpty(exePath)) return;

        SelectedTool.ExecutablePath = exePath;
        if (string.IsNullOrWhiteSpace(SelectedTool.Name) || SelectedTool.Name == "New tool")
            SelectedTool.Name = Path.GetFileNameWithoutExtension(exePath);

        StatusMessage = "";
        AutoSave?.Invoke();
    }

    /// <summary>The editor changed something. Clears any stale launch error along with saving.</summary>
    public void NotifyEdited()
    {
        StatusMessage = "";
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
