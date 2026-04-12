using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CatModManager.Core.Models;
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

    private bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;

        // ... rest of constructor logic ...
        
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

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ListBox listBox && e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed)
        {
            var visualSource = e.Source as Visual;
            while (visualSource != null)
            {
                if (visualSource is CheckBox) return; 
                if (visualSource is ListBoxItem) break;
                visualSource = visualSource.GetVisualParent();
            }

            _dragStartPoint = e.GetPosition(listBox);
            var item = listBox.InputHitTest(e.GetPosition(listBox)) as Visual;
            while (item != null && !(item is ListBoxItem)) item = item.GetVisualParent();

            if (item is ListBoxItem listBoxItem && listBoxItem.Content is Mod mod)
            {
                var data = new DataObject();
                data.Set("ModItem", mod);
                DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("ModItem")) e.DragEffects = DragDropEffects.Move;
        else e.DragEffects = DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is ListBox listBox && e.Data.Get("ModItem") is Mod draggedMod)
        {
            var point = e.GetPosition(listBox);
            var targetElement = listBox.InputHitTest(point) as Visual;
            while (targetElement != null && !(targetElement is ListBoxItem)) targetElement = targetElement.GetVisualParent();

            if (targetElement is ListBoxItem targetItem && targetItem.Content is Mod targetMod)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    int oldIndex = vm.ModList.AllMods.IndexOf(draggedMod);
                    int newIndex = vm.ModList.AllMods.IndexOf(targetMod);
                    if (oldIndex != -1 && newIndex != -1 && oldIndex != newIndex) vm.ModList.MoveMod(oldIndex, newIndex);
                }
            }
        }
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

    private async void SelectGame_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var startDir = !string.IsNullOrEmpty(vm.GameConfig.GameExecutablePath)
            ? System.IO.Path.GetDirectoryName(vm.GameConfig.GameExecutablePath) : vm.GameConfig.BaseFolderPath;
        var files = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(startDir),
            FileTypeFilter = new[] { new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } } }
        });
        if (files.Count >= 1)
            vm.GameConfig.GameExecutablePath = files[0].Path.LocalPath;
    }

    private async void SelectBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Base Game Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(vm.GameConfig.BaseFolderPath)
        });
        if (folders.Count >= 1)
            vm.GameConfig.BaseFolderPath = folders[0].Path.LocalPath;
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Mods Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(vm.GameConfig.ModsFolderPath, vm.GameConfig.BaseFolderPath)
        });
        if (folders.Count >= 1)
        {
            // Just update the path — don't scan (that would clear the current mod list).
            // User can press Refresh to explicitly scan the new folder.
            vm.GameConfig.ModsFolderPath = folders[0].Path.LocalPath;
        }
    }

    private async void SelectDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Downloads Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(vm.GameConfig.DownloadsFolderPath, vm.GameConfig.BaseFolderPath)
        });
        if (folders.Count >= 1)
            vm.GameConfig.DownloadsFolderPath = folders[0].Path.LocalPath;
    }

    private async void SelectDataSubFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var dataSub = vm.GameConfig.DataSubFolder;
        var currentFull = !string.IsNullOrEmpty(dataSub)
            ? (System.IO.Path.IsPathRooted(dataSub)
                ? dataSub
                : (!string.IsNullOrEmpty(vm.GameConfig.BaseFolderPath)
                    ? System.IO.Path.Combine(vm.GameConfig.BaseFolderPath, dataSub)
                    : null))
            : null;
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Data Subfolder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(currentFull, vm.GameConfig.BaseFolderPath)
        });
        if (folders.Count >= 1)
        {
            var selected = folders[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(vm.GameConfig.BaseFolderPath) && selected.StartsWith(vm.GameConfig.BaseFolderPath, StringComparison.OrdinalIgnoreCase))
                vm.GameConfig.DataSubFolder = System.IO.Path.GetRelativePath(vm.GameConfig.BaseFolderPath, selected);
            else
                vm.GameConfig.DataSubFolder = selected;
        }
    }

    private async void AddMod_Click(object sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (!(DataContext is MainWindowViewModel vm)) return;

        var options = new FilePickerOpenOptions
        {
            Title = "Select Mod (Archive)", AllowMultiple = true,
            FileTypeFilter = new[] {
                new FilePickerFileType("Mod Archives") { Patterns = new[] { "*.zip", "*.7z" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        };
        var result = await topLevel!.StorageProvider.OpenFilePickerAsync(options);

        string[] paths;
        if (result.Count > 0)
            paths = result.Select(f => f.Path.LocalPath).ToArray();
        else
        {
            var folderResult = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Mod Folder" });
            paths = folderResult.Count > 0 ? new[] { folderResult[0].Path.LocalPath } : Array.Empty<string>();
        }

        if (paths.Length == 0) return;

        foreach (var path in paths)
            await vm.AddModCommand.ExecuteAsync(path);
    }

    /// <summary>
    /// Shows a dialog listing available mount points.
    /// Returns null/"" for the default (first) mount point, a mount point Id for others,
    /// or "cancelled" if the user dismissed.
    /// </summary>
    private async Task<string?> ShowMountPointPickerDialog(MainWindowViewModel vm, string? currentMountPointId = null)
    {
        var mountPoints = vm.GameConfig.EffectiveMountPoints;

        // If only one mount point, skip the dialog entirely — just install to it.
        if (mountPoints.Count <= 1)
            return mountPoints.Count == 1 ? mountPoints[0].Id : null;

        // Resolve "null" MountPointId to the first (default) mount point id for comparison.
        string effectiveCurrent = string.IsNullOrEmpty(currentMountPointId)
            ? (mountPoints.Count > 0 ? mountPoints[0].Id : "")
            : currentMountPointId;

        var bg       = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2B2D31"));
        var cardBg   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E2124"));
        var border   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#40444B"));
        var mutedFg  = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8E9297"));
        var accentBg = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5865F2"));
        var currentBg = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D4F6B"));

        var dialog = new Avalonia.Controls.Window
        {
            Title                 = "Change Mount Point",
            Width                 = 420,
            SizeToContent         = Avalonia.Controls.SizeToContent.Height,
            CanResize             = false,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Background            = bg
        };

        Avalonia.Controls.Button MakeCard(string title, string subtitle, bool isCurrent)
        {
            var titlePanel = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            titlePanel.Children.Add(new Avalonia.Controls.TextBlock
            {
                Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold,
                FontSize = 13, Foreground = Avalonia.Media.Brushes.White
            });
            if (isCurrent)
                titlePanel.Children.Add(new Avalonia.Controls.TextBlock
                {
                    Text = "● current", FontSize = 10, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#72A7E8"))
                });

            var subBlock = new Avalonia.Controls.TextBlock
            {
                Text = subtitle, FontSize = 11, Foreground = mutedFg,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 3, 0, 0)
            };
            var inner = new Avalonia.Controls.StackPanel { Spacing = 0 };
            inner.Children.Add(titlePanel);
            if (!string.IsNullOrEmpty(subtitle)) inner.Children.Add(subBlock);
            return new Avalonia.Controls.Button
            {
                Content = inner,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Padding = new Avalonia.Thickness(14, 10),
                Background = isCurrent ? currentBg : cardBg,
                BorderBrush = isCurrent ? accentBg : border,
                BorderThickness = new Avalonia.Thickness(isCurrent ? 2 : 1),
                CornerRadius = new Avalonia.CornerRadius(4),
                Foreground = Avalonia.Media.Brushes.White
            };
        }

        var cards = new Avalonia.Controls.StackPanel { Spacing = 8 };
        foreach (var mp in mountPoints)
        {
            bool isCurrent = string.Equals(mp.Id, effectiveCurrent, StringComparison.OrdinalIgnoreCase);
            var btn = MakeCard(mp.Name, mp.Path, isCurrent);
            var capturedId = mp.Id;
            btn.Click += (_, _) => dialog.Close(capturedId);
            cards.Children.Add(btn);
        }

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel", Padding = new Avalonia.Thickness(16, 7),
            Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0),
            Foreground = mutedFg, FontSize = 12,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        cancelBtn.Click += (_, _) => dialog.Close("cancelled");

        var footer = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };
        footer.Children.Add(cancelBtn);

        var root = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 8 };
        root.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Where would you like to install this mod?",
            FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = Avalonia.Media.Brushes.White, Margin = new Avalonia.Thickness(0, 0, 0, 4)
        });
        root.Children.Add(cards);
        root.Children.Add(footer);
        dialog.Content = root;

        return await dialog.ShowDialog<string?>(this);
    }

    private async void AddMountPoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var result = await ShowMountPointEditorDialog("", "");
        if (result.HasValue)
            vm.GameConfig.AddUserMountPoint(result.Value.Name, result.Value.Path);
    }

    private async void EditMountPoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button btn || btn.Tag is not CatModManager.Core.Models.MountPointDef mp) return;

        var result = await ShowMountPointEditorDialog(mp.Name, mp.Path);
        if (!result.HasValue) return;

        if (mp.IsGameDefined)
        {
            // Game-defined: store path override in UserMountPoints (name stays from TOML).
            vm.GameConfig.OverrideGameDefinedMountPointPath(mp.Id, mp.Name, result.Value.Path);
        }
        else
        {
            mp.Name = result.Value.Name;
            mp.Path = result.Value.Path;
            vm.GameConfig.NotifyMountPointsChanged();
            vm.GameConfig.AutoSave?.Invoke();
        }
    }

    private async void ChangeMountPoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var selectedMod = vm.ModList.SelectedMod;
        if (selectedMod == null) return;

        var mountPoints = vm.GameConfig.EffectiveMountPoints;
        if (mountPoints.Count <= 1) return; // nothing to change to

        string? chosen = await ShowMountPointPickerDialog(vm, selectedMod.MountPointId);
        if (chosen == "cancelled") return;

        // The first entry is the default (DataSubFolder); null MountPointId means "use default".
        var defaultId = vm.GameConfig.EffectiveMountPoints.Count > 0
            ? vm.GameConfig.EffectiveMountPoints[0].Id : null;
        selectedMod.MountPointId = (chosen == null || chosen == defaultId) ? null : chosen;
        vm.RefreshModMountPointDisplayNames();
        vm.NotifySelectedModMountPointChanged();
        vm.GameConfig.AutoSave?.Invoke();
    }

    private void OpenMountPointFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button btn || btn.Tag is not CatModManager.Core.Models.MountPointDef mp) return;

        var expanded = System.Environment.ExpandEnvironmentVariables(mp.Path);
        string absPath;
        if (System.IO.Path.IsPathRooted(expanded))
            absPath = expanded;
        else if (!string.IsNullOrEmpty(vm.GameConfig.BaseFolderPath))
            absPath = System.IO.Path.Combine(vm.GameConfig.BaseFolderPath, expanded);
        else
            absPath = expanded;

        // Walk up to nearest existing ancestor (explorer falls back to Documents on missing paths).
        var candidate = absPath;
        while (!string.IsNullOrEmpty(candidate) && !System.IO.Directory.Exists(candidate))
            candidate = System.IO.Path.GetDirectoryName(candidate) ?? "";
        if (!string.IsNullOrEmpty(candidate))
            System.Diagnostics.Process.Start("explorer.exe", candidate);
    }

    private async Task<(string Name, string Path)?> ShowMountPointEditorDialog(string initialName, string initialPath)
    {
        if (DataContext is not MainWindowViewModel vm) return null;

        bool isNew  = string.IsNullOrEmpty(initialName);
        var bg      = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2B2D31"));
        var border  = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#40444B"));
        var mutedFg = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8E9297"));

        var dialog = new Avalonia.Controls.Window
        {
            Title                 = isNew ? "Add Mount Point" : "Edit Mount Point",
            Width                 = 380,
            SizeToContent         = Avalonia.Controls.SizeToContent.Height,
            CanResize             = false,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Background            = bg
        };

        var nameBox = new Avalonia.Controls.TextBox { Text = initialName, Watermark = "e.g. PAK Mods", Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        var pathBox = new Avalonia.Controls.TextBox { Text = initialPath, IsReadOnly = true, Foreground = Avalonia.Media.Brushes.White, Margin = new Avalonia.Thickness(0, 0, 0, 4) };
        var browseBtn = new Avalonia.Controls.Button
        {
            Content = "Browse…", Padding = new Avalonia.Thickness(10, 5),
            Background = Avalonia.Media.Brushes.Transparent, BorderBrush = border,
            BorderThickness = new Avalonia.Thickness(1), Foreground = Avalonia.Media.Brushes.White,
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };
        browseBtn.Click += async (_, _) =>
        {
            var topLevel = GetTopLevel(dialog);
            IStorageFolder? startFolder = null;
            var currentPath = pathBox.Text;
            if (!string.IsNullOrEmpty(currentPath) && System.IO.Directory.Exists(currentPath))
                startFolder = await topLevel!.StorageProvider.TryGetFolderFromPathAsync(currentPath);
            else if (!string.IsNullOrEmpty(vm.GameConfig.BaseFolderPath) && System.IO.Directory.Exists(vm.GameConfig.BaseFolderPath))
                startFolder = await topLevel!.StorageProvider.TryGetFolderFromPathAsync(vm.GameConfig.BaseFolderPath);
            var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Mount Point Folder", AllowMultiple = false, SuggestedStartLocation = startFolder
            });
            if (folders.Count >= 1) pathBox.Text = folders[0].Path.LocalPath;
        };

        var okBtn = new Avalonia.Controls.Button
        {
            Content = isNew ? "Add" : "Save", Padding = new Avalonia.Thickness(16, 7),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5865F2")),
            BorderThickness = new Avalonia.Thickness(0), Foreground = Avalonia.Media.Brushes.White,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        okBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(nameBox.Text) && !string.IsNullOrWhiteSpace(pathBox.Text))
                dialog.Close((nameBox.Text.Trim(), pathBox.Text.Trim()));
            else
                dialog.Close(null);
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel", Padding = new Avalonia.Thickness(16, 7),
            Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0), Foreground = mutedFg
        };
        cancelBtn.Click += (_, _) => dialog.Close(null);

        var btnRow = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);

        var root = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 4 };
        root.Children.Add(new Avalonia.Controls.TextBlock { Text = isNew ? "New Mount Point" : "Edit Mount Point", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.White, Margin = new Avalonia.Thickness(0, 0, 0, 12) });
        root.Children.Add(new Avalonia.Controls.TextBlock { Text = "Name", FontSize = 10, Foreground = mutedFg });
        root.Children.Add(nameBox);
        root.Children.Add(new Avalonia.Controls.TextBlock { Text = "Path", FontSize = 10, Foreground = mutedFg });
        root.Children.Add(pathBox);
        root.Children.Add(browseBtn);
        root.Children.Add(btnRow);
        dialog.Content = root;

        return await dialog.ShowDialog<(string Name, string Path)?>(this);
    }

    private async void AddTool_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var files = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
            vm.Tools.AddToolFromPath(files[0].Path.LocalPath);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
