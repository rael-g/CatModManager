using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;
using CatModManager.Ui.Plugins;
using CatModManager.Ui.Services;
using CatModManager.Ui.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace CatModManager.Ui.Views;

public partial class MainWindow : Window
{
    private Point _dragStartPoint;
    private Mod?  _dragCandidate;

    /// <summary>
    /// How far the pointer must travel before a press becomes a drag. Without a threshold every
    /// click on a row would begin a drag and selection would stop working.
    /// </summary>
    private const double DragThreshold = 4;

    private readonly DragReorderAnimator _dragAnimator = new();

    private bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();

        // Registered here, tunnelling, rather than as PointerPressed="..." in XAML. ListBoxItem
        // handles the bubbling PointerPressed to update selection and marks it handled, so a
        // bubbling handler on the ListBox is never reached — which is why drag-to-reorder had
        // never worked. Tunnelling sees the event on the way down, before the item consumes it.
        var modsList = this.FindControl<ListBox>("ModsListBox");
        if (modsList != null)
        {
            modsList.AddHandler(PointerPressedEvent,  OnPointerPressed,  RoutingStrategies.Tunnel);
            modsList.AddHandler(PointerMovedEvent,    OnPointerMoved,    RoutingStrategies.Tunnel);
            modsList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        }

        DataContextChanged += OnDataContextChanged;
        
        Closing += async (s, e) => {
            if (_isShuttingDown) return;
            if (DataContext is MainWindowViewModel vm)
            {
                e.Cancel = true;
                _isShuttingDown = true;
                
                // Show a status message if possible or just log
                vm.StatusMessage = "Shutting down safely...";
                
                await vm.Shutdown();
                
                // Now close for real
                Close();
            }
        };
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        vm.RequestClearFocus += () => this.FocusManager?.ClearFocus();

        GitHubUpdateChecker.UpdateAvailable += (tag, url) =>
            _ = new UpdateDialog(tag, url).ShowDialog(this);

        // The update check runs at startup before the window exists; show dialog if it already fired.
        if (GitHubUpdateChecker.PendingUpdate is { } pending)
            _ = new UpdateDialog(pending.Tag, pending.Url).ShowDialog(this);

        vm.ProfileManager.ConfirmDelete = async profileName =>
        {
            var dialog = new ConfirmDialog($"Delete profile \"{profileName}\"?", "This cannot be undone.");
            return await dialog.ShowDialog<bool>(this);
        };

        vm.ProfileManager.ConfirmProfileChange = async newProfileName =>
        {
            if (!vm.HasActiveDownloads) return true;
            var dialog = new ConfirmDialog(
                $"Switch to profile \"{newProfileName}\"?",
                "There are active downloads in progress. Switching profiles will interrupt them.");
            return await dialog.ShowDialog<bool>(this);
        };

        vm.ProfileManager.RequestRename = async currentName =>
            await TextInputDialog.ShowAsync(this, $"Rename profile \"{currentName}\"", currentName);

        // Adding a game is the same auto-detect dialog the Game menu offers, read as an installation
        // rather than as an edit to the one that is open. When the user backs out of the scan, the
        // file picker is the fallback — auto-detect finds Steam, GOG and Epic, and a game outside all
        // three is the ordinary case this application exists for.
        vm.GameManager.RequestNewGame = async () =>
        {
            var detected = await DetectGameAsync(vm);
            return detected ?? await PickGameExecutableAsync(vm);
        };

        // Straight into the settings once it is added: the folders were guessed from the executable,
        // and this is the one moment the user is thinking about that game and can correct them.
        vm.GameManager.GameAdded = async () => await GameSettingsDialog.ShowAsync(this, vm);

        vm.GameManager.ConfirmDelete = async (game, profileCount) =>
        {
            var dialog = new ConfirmDialog(
                $"Remove \"{game.DisplayName}\" from CatModManager?",
                $"Its {profileCount} profile(s) and its record of installed mods go with it.\n\n"
                + "Nothing on disk is deleted — the mods stay in their folder, and adding the game "
                + "back finds them again.");
            return await dialog.ShowDialog<bool>(this);
        };

        // Build plugin tabs whenever the collection changes or SelectedMod changes
        vm.PluginInspectorTabs.CollectionChanged += (_, _) => RebuildPluginTabs(vm);
        vm.ModList.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ModListViewModel.SelectedMod))
                UpdatePluginTabContents(vm);
        };

        // Sync multi-selection to SelectedMods
        var listBox = this.FindControl<ListBox>("ModsListBox");
        if (listBox != null)
        {
            listBox.SelectionChanged += (_, _) =>
                vm.ModList.SelectedMods = listBox.SelectedItems?.OfType<Mod>().ToList() ?? new System.Collections.Generic.List<Mod>();

            // Inject plugin-contributed context menu items before the menu opens
            if (listBox.ContextMenu != null)
            {
                listBox.ContextMenu.Opening += (_, _) => RebuildPluginContextMenuItems(vm, listBox.ContextMenu);
            }
        }

        // Initial build (in case plugins were loaded before the window)
        RebuildPluginTabs(vm);
    }

    private const string PluginContextMenuTag = "PluginContextAction";

    private void RebuildPluginContextMenuItems(MainWindowViewModel vm, ContextMenu menu)
    {
        // Remove previously injected plugin items and separators (both are Controls with the tag)
        var toRemove = menu.Items.OfType<Control>()
            .Where(i => i.Tag?.ToString() == PluginContextMenuTag)
            .ToList();
        foreach (var item in toRemove) menu.Items.Remove(item);

        var pluginActions = vm.PluginModContextActions;
        if (pluginActions.Count == 0) return;

        IModInfo? modInfo = vm.ModList.SelectedMod != null ? new ModInfoAdapter(vm.ModList.SelectedMod) : null;

        bool addedSeparator = false;
        foreach (var action in pluginActions)
        {
            if (modInfo == null || !action.IsVisible(modInfo)) continue;
            if (!addedSeparator)
            {
                var sep = new Separator { Tag = PluginContextMenuTag };
                menu.Items.Add(sep);
                addedSeparator = true;
            }
            var captured = action;
            var capturedMod = modInfo;
            var item = new MenuItem { Header = captured.Label, Tag = PluginContextMenuTag };
            item.Click += async (_, _) =>
            {
                try
                {
                    var msg = await captured.ExecuteAsync(capturedMod);
                    if (!string.IsNullOrEmpty(msg)) vm.StatusMessage = msg;
                }
                catch (Exception ex) { vm.StatusMessage = $"ERROR: {ex.Message}"; }
            };
            menu.Items.Add(item);
        }
    }

    private void RebuildPluginTabs(MainWindowViewModel vm)
    {
        var tc = this.FindControl<TabControl>("InspectorTabControl");
        if (tc == null) return;

        // Remove all plugin tabs (0=INFO, 1=FILES, 2=TOOLS are static — keep them)
        const int StaticTabCount = 3;
        while (tc.Items.Count > StaticTabCount)
            tc.Items.RemoveAt(tc.Items.Count - 1);

        foreach (var tab in vm.PluginInspectorTabs)
        {
            tc.Items.Add(new TabItem
            {
                Header  = tab.TabLabel,
                Content = tab.CreateView(vm.ModList.SelectedMod != null ? new ModInfoAdapter(vm.ModList.SelectedMod) : null)
            });
        }
    }

    private void UpdatePluginTabContents(MainWindowViewModel vm)
    {
        var tc = this.FindControl<TabControl>("InspectorTabControl");
        if (tc == null) return;

        var pluginTabItems = tc.Items.OfType<TabItem>().Skip(3).ToList();
        var pluginTabs     = vm.PluginInspectorTabs.ToList();
        IModInfo? modInfo  = vm.ModList.SelectedMod != null ? new ModInfoAdapter(vm.ModList.SelectedMod) : null;

        for (int i = 0; i < pluginTabItems.Count && i < pluginTabs.Count; i++)
            pluginTabItems[i].Content = pluginTabs[i].CreateView(modInfo);
    }

    /// <summary>Whether the mod list is currently armed for drag-to-reorder.</summary>
    private bool ReorderArmed =>
        DataContext is MainWindowViewModel vm && vm.ModList.IsReorderEnabled;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragCandidate = null;

        // Outside reorder mode a press is a selection, not the start of a load-order edit.
        if (!ReorderArmed) return;
        if (sender is not ListBox listBox) return;
        if (!e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed) return;

        // The checkbox is a control in its own right; dragging from it would mean enabling a mod
        // and moving it at once.
        var source = e.Source as Visual;
        while (source != null)
        {
            if (source is CheckBox) return;
            if (source is ListBoxItem) break;
            source = source.GetVisualParent();
        }

        _dragStartPoint = e.GetPosition(listBox);

        var item = listBox.InputHitTest(_dragStartPoint) as Visual;
        while (item != null && item is not ListBoxItem) item = item.GetVisualParent();

        // Only remember what could be dragged. Starting the drag here would swallow the click and
        // make rows unselectable.
        if (item is ListBoxItem listBoxItem && listBoxItem.Content is Mod mod)
            _dragCandidate = mod;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is not { } mod || !ReorderArmed) return;
        if (sender is not ListBox listBox) return;

        if (!e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = null;
            return;
        }

        var current = e.GetPosition(listBox);
        if (Math.Abs(current.X - _dragStartPoint.X) < DragThreshold &&
            Math.Abs(current.Y - _dragStartPoint.Y) < DragThreshold)
            return;

        _dragCandidate = null;

        var data = new DataObject();
        data.Set("ModItem", mod);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.ModList.BeginDragReorder(mod);
            _dragAnimator.Begin(listBox, vm.ModList, mod, current.Y);
        }

        // DoDragDrop blocks until the drop completes, so the end of the drag — however it ends,
        // including a cancel or a drop outside the list — is right here.
        _ = DragDrop.DoDragDrop(e, data, DragDropEffects.Move)
                    .ContinueWith(_ => Dispatcher.UIThread.Post(EndDrag));
    }

    private void EndDrag()
    {
        // Clear the transforms before the view model settles the list: EndDragReorder reapplies the
        // filter and sort, which can rebuild containers out from under an animation in flight.
        _dragAnimator.End();
        if (DataContext is MainWindowViewModel vm) vm.ModList.EndDragReorder();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => _dragCandidate = null;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!ReorderArmed || !e.Data.Contains("ModItem"))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;

        // Reorder as the pointer passes each row rather than waiting for the drop. Holding the
        // change back until release means no feedback during the drag at all: the list sits still
        // and only jumps once the button comes up.
        if (sender is ListBox listBox)
            _dragAnimator.Update(e.GetPosition(listBox).Y);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        // One last reconcile against the release point. The animator derives the order from the
        // absolute pointer position every time rather than from a per-event delta, so this both
        // settles the drop and repairs anything a coalesced event skipped over.
        if (ReorderArmed && sender is ListBox listBox)
            _dragAnimator.Update(e.GetPosition(listBox).Y);

        EndDrag();
    }

    private async void GameSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.GameManager.CurrentGame is not { Id: > 0 }) return;

        await GameSettingsDialog.ShowAsync(this, vm);
    }

    /// <summary>
    /// Runs the store scan and turns the pick into an unsaved game, or returns null when the user
    /// closed it without choosing.
    /// </summary>
    private async Task<Game?> DetectGameAsync(MainWindowViewModel vm)
    {
        var dialogVm = new GameDetectionDialogViewModel(
            vm.GameConfig.GameDiscoveryService, vm.GameConfig.AvailableGameSupports);
        await new GameDetectionDialog(dialogVm).ShowDialog(this);

        if (dialogVm.Result is not { } result) return null;

        var game = new Game
        {
            DisplayName        = result.DisplayName,
            GameExecutablePath = result.ExecutablePath,
            BaseDataPath       = result.GameFolder,
            GameSupportId      = dialogVm.ResultMode?.GameId ?? "generic",
        };
        GameFolderDefaults.Fill(game);
        return game;
    }

    /// <summary>
    /// The fallback for a game no store knows about: pick the executable, and everything else
    /// follows from it. This is the plain case the application is built around, not a lesser one.
    /// </summary>
    private async Task<Game?> PickGameExecutableAsync(MainWindowViewModel vm)
    {
        var files = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the game's executable",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } } }
        });
        if (files.Count < 1) return null;

        var game = new Game { GameExecutablePath = files[0].Path.LocalPath };
        game.GameSupportId = vm.GameConfig.DetectSupportId(game.GameExecutablePath);
        GameFolderDefaults.Fill(game);
        return game;
    }

    private async Task<IStorageFolder?> GetStartFolderAsync(string? preferredPath, string? fallbackPath = null)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;
        foreach (var path in new[] { preferredPath, fallbackPath })
        {
            if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                return await topLevel.StorageProvider.TryGetFolderFromPathAsync(path);
        }
        return null;
    }


    /// <summary>
    /// Archive and folder are separate menu entries rather than one picker falling back to the
    /// other. The fallback made cancelling the file dialog open a folder dialog, so backing out of
    /// "add a mod" took two dismissals and looked like the app had ignored the first one.
    /// </summary>
    private async void AddModFromArchive_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Mod Archive",
            AllowMultiple = true,
            SuggestedStartLocation = await GetStartFolderAsync(vm.GameConfig.DownloadsFolderPath),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Mod Archives") { Patterns = new[] { "*.zip", "*.7z", "*.rar", "*.tar" } },
                new FilePickerFileType("All Files")    { Patterns = new[] { "*.*" } }
            }
        });

        foreach (var file in files)
            await vm.AddModCommand.ExecuteAsync(file.Path.LocalPath);
    }

    private async void AddModFromFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Mod Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(vm.GameConfig.DownloadsFolderPath)
        });

        if (folders.Count >= 1)
            await vm.AddModCommand.ExecuteAsync(folders[0].Path.LocalPath);
    }

    private async void ChangeMountPoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var selectedMod = vm.ModList.SelectedMod;
        if (selectedMod == null) return;

        var mountPoints = vm.GameConfig.EffectiveMountPoints;
        if (mountPoints.Count <= 1) return; // nothing to change to

        string? chosen = await MountPointPickerDialog.ShowAsync(this, mountPoints, selectedMod.MountPointId);
        if (chosen == null) return;   // dismissed

        // The first entry is the default; null MountPointId means "use default".
        var defaultId = vm.GameConfig.EffectiveMountPoints.Count > 0
            ? vm.GameConfig.EffectiveMountPoints[0].Id : null;
        selectedMod.MountPointId = chosen == defaultId ? null : chosen;
        vm.RefreshModMountPointDisplayNames();
        vm.NotifySelectedModMountPointChanged();
        vm.ProfileManager.AutoSave();
    }

    private void OpenMountPointFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button btn || btn.Tag is not CatModManager.Core.Models.MountPointDef mp) return;

        _ = vm.OpenFolder(mp.ResolveAbsolute(vm.GameConfig.BaseFolderPath));
    }

    /// <summary>
    /// Opened from the code-behind rather than through a command: showing a window needs an owner,
    /// and the view model has no business knowing about one. Every other dialog here works the same.
    /// </summary>
    private async void BrowsePlugins_Click(object sender, RoutedEventArgs e)
    {
        if ((DataContext as MainWindowViewModel)?.PluginBrowser is not { } vm) return;

        await new PluginBrowserWindow(vm).ShowDialog(this);

        // Installing or removing a plugin only takes effect on restart, but the installed list is
        // shared state — re-read it so reopening the window does not show a stale snapshot.
        vm.RefreshInstalledPlugins();
    }

    /// <summary>
    /// Adds an empty tool for the editor to fill in, rather than opening a file dialog.
    ///
    /// A tool is not always a file: on Linux one usually has to be started through something else —
    /// "steam" with a rungameid argument, or a wine invocation. A picker can only name a file, which
    /// is why arguments and "mount first" used to be reachable only by hand-editing the profile.
    /// </summary>
    private void AddTool_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.Tools.AddBlankTool();
    }

    /// <summary>Fills the selected tool's command from a file dialog, for the tools that are files.</summary>
    private async void BrowseTool_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Tool Executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } },
                new FilePickerFileType("All Files")   { Patterns = new[] { "*.*"   } }
            }
        });
        if (files.Count >= 1)
            vm.Tools.SetExecutable(files[0].Path.LocalPath);
    }

    /// <summary>
    /// A field in the tool editor was left, or the checkbox toggled. The bindings already wrote the
    /// value into the tool — this is what persists it, and clears any stale launch error.
    /// </summary>
    private void ToolField_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.Tools.NotifyEdited();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
