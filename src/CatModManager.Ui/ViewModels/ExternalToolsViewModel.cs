using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly IGlobalToolService       _globalToolService;

    /// <summary>The global half, kept aside so a game switch can rebuild the list without a query.</summary>
    private readonly System.Collections.Generic.List<ExternalTool> _globalTools = new();

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
        ILogService              logService,
        IGlobalToolService       globalToolService)
    {
        _processService    = processService;
        _vfsOrchestrator   = vfsOrchestrator;
        _logService        = logService;
        _globalToolService = globalToolService;
    }

    /// <summary>
    /// Shows the open game's tools, with the global ones after them.
    ///
    /// One list rather than two panes: from where the user stands a tool is a tool, and the only
    /// thing that differs is whether it survives switching game — which the checkbox in the editor
    /// says, in the place where it can be changed.
    /// </summary>
    public void LoadTools(System.Collections.Generic.IEnumerable<ExternalTool> gameTools)
    {
        Tools.Clear();
        foreach (var t in gameTools) { t.IsGlobal = false; Tools.Add(t); }
        foreach (var t in _globalTools) Tools.Add(t);
    }

    /// <summary>The open game's half of the list. The global ones are written by this view model itself.</summary>
    public System.Collections.Generic.List<ExternalTool> GetTools()
        => Tools.Where(t => !t.IsGlobal).ToList();

    /// <summary>
    /// Reads the global tools once, at startup, so that every later game switch is a list rebuild
    /// rather than another query.
    /// </summary>
    public async Task InitializeAsync()
    {
        _globalTools.Clear();
        _globalTools.AddRange(await _globalToolService.ListToolsAsync());
        foreach (var t in _globalTools) Tools.Add(t);
    }

    /// <summary>
    /// Persists both halves: the game's through <see cref="AutoSave"/>, the global ones directly.
    ///
    /// Both every time, because the editor's Global checkbox moves a tool from one to the other and
    /// there is no cheap way to know which direction it went — writing only the side that "changed"
    /// is how a tool ends up in both tables at once, or in neither.
    /// </summary>
    private void Save()
    {
        _globalTools.Clear();
        _globalTools.AddRange(Tools.Where(t => t.IsGlobal));

        AutoSave?.Invoke();
        _ = SaveGlobalsAsync();
    }

    private async Task SaveGlobalsAsync()
    {
        try { await _globalToolService.SaveToolsAsync(_globalTools.ToList()); }
        catch (Exception ex) { _logService.LogError("[Tools] Could not save the global tools", ex); }
    }

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
        Save();
    }

    /// <summary>A new, empty entry for the editor to fill in — the path to a file is only one of the ways a tool is named.</summary>
    public void AddBlankTool()
    {
        var tool = new ExternalTool { Name = "New tool" };
        Tools.Add(tool);
        SelectedTool = tool;
        Save();
    }

    /// <summary>Fills the command of the selected tool from the file dialog, naming it if it has no name yet.</summary>
    public void SetExecutable(string exePath)
    {
        if (SelectedTool == null || string.IsNullOrEmpty(exePath)) return;

        SelectedTool.ExecutablePath = exePath;
        if (string.IsNullOrWhiteSpace(SelectedTool.Name) || SelectedTool.Name == "New tool")
            SelectedTool.Name = Path.GetFileNameWithoutExtension(exePath);

        StatusMessage = "";
        Save();
    }

    /// <summary>The editor changed something. Clears any stale launch error along with saving.</summary>
    public void NotifyEdited()
    {
        StatusMessage = "";
        Save();
    }

    [RelayCommand]
    private void RemoveTool(ExternalTool? tool)
    {
        if (tool == null) return;
        Tools.Remove(tool);
        if (SelectedTool == tool) SelectedTool = null;
        Save();
    }

    [RelayCommand]
    private async Task OpenToolFolder(ExternalTool? tool)
    {
        if (tool == null || string.IsNullOrEmpty(tool.ExecutablePath)) return;
        var dir = Path.GetDirectoryName(tool.ExecutablePath) ?? "";
        await _processService.OpenFolderAsync(dir);
    }
}
