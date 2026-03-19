using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CatModManager.Core.Models;
using CatModManager.PluginSdk;
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

    public MainWindow()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;

        // Global focus clearing when clicking outside input elements
        this.AddHandler(PointerPressedEvent, (s, e) => 
        {
            var visualSource = e.Source as Visual;
            bool overInput = false;
            while (visualSource != null)
            {
                if (visualSource is TextBox || visualSource is ComboBox || visualSource is ListBoxItem || visualSource is Button)
                {
                    overInput = true;
                    break;
                }
                visualSource = visualSource.GetVisualParent();
            }

            if (!overInput)
            {
                this.FocusManager?.ClearFocus();
            }
        }, RoutingStrategies.Tunnel);

        var listBox = this.FindControl<ListBox>("ModsListBox");
        if (listBox != null)
        {
            listBox.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            listBox.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
            listBox.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
        }

        Closing += (s, e) => {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Shutdown();
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

        // Build plugin tabs whenever the collection changes or SelectedMod changes
        vm.PluginInspectorTabs.CollectionChanged += (_, _) => RebuildPluginTabs(vm);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedMod))
                UpdatePluginTabContents(vm);
        };

        // Sync multi-selection to SelectedMods
        var listBox = this.FindControl<ListBox>("ModsListBox");
        if (listBox != null)
        {
            listBox.SelectionChanged += (_, _) =>
                vm.SelectedMods = listBox.SelectedItems?.OfType<Mod>().ToList() ?? new System.Collections.Generic.List<Mod>();
        }

        // Initial build (in case plugins were loaded before the window)
        RebuildPluginTabs(vm);
    }

    private void RebuildPluginTabs(MainWindowViewModel vm)
    {
        var tc = this.FindControl<TabControl>("InspectorTabControl");
        if (tc == null) return;

        // Remove all plugin tabs (index 0 = INFO tab, keep it)
        while (tc.Items.Count > 1)
            tc.Items.RemoveAt(tc.Items.Count - 1);

        foreach (var tab in vm.PluginInspectorTabs)
        {
            tc.Items.Add(new TabItem
            {
                Header  = tab.TabLabel,
                Content = tab.CreateView(vm.SelectedMod)
            });
        }
    }

    private void UpdatePluginTabContents(MainWindowViewModel vm)
    {
        var tc = this.FindControl<TabControl>("InspectorTabControl");
        if (tc == null) return;

        var pluginTabItems = tc.Items.OfType<TabItem>().Skip(1).ToList();
        var pluginTabs     = vm.PluginInspectorTabs.ToList();

        for (int i = 0; i < pluginTabItems.Count && i < pluginTabs.Count; i++)
            pluginTabItems[i].Content = pluginTabs[i].CreateView(vm.SelectedMod);
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
                    int oldIndex = vm.AllMods.IndexOf(draggedMod);
                    int newIndex = vm.AllMods.IndexOf(targetMod);
                    if (oldIndex != -1 && newIndex != -1 && oldIndex != newIndex) vm.MoveMod(oldIndex, newIndex);
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
        var startDir = !string.IsNullOrEmpty(vm.GameExecutablePath)
            ? System.IO.Path.GetDirectoryName(vm.GameExecutablePath) : vm.BaseFolderPath;
        var files = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(startDir),
            FileTypeFilter = new[] { new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } } }
        });
        if (files.Count >= 1)
            vm.GameExecutablePath = files[0].Path.LocalPath;
    }

    private async void SelectBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Base Game Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(vm.BaseFolderPath)
        });
        if (folders.Count >= 1)
            vm.BaseFolderPath = folders[0].Path.LocalPath;
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Mods Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(vm.ModsFolderPath, vm.BaseFolderPath)
        });
        if (folders.Count >= 1)
            await vm.ScanDirectoryCommand.ExecuteAsync(folders[0].Path.LocalPath);
    }

    private async void SelectDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Downloads Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(vm.DownloadsFolderPath, vm.BaseFolderPath)
        });
        if (folders.Count >= 1)
            vm.DownloadsFolderPath = folders[0].Path.LocalPath;
    }

    private async void SelectDataSubFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = GetTopLevel(this);
        var currentFull = !string.IsNullOrEmpty(vm.BaseFolderPath) && !string.IsNullOrEmpty(vm.DataSubFolder)
            ? System.IO.Path.Combine(vm.BaseFolderPath, vm.DataSubFolder) : null;
        var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Data Subfolder (inside game folder)",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartFolderAsync(currentFull, vm.BaseFolderPath)
        });
        if (folders.Count >= 1)
        {
            var selected = folders[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(vm.BaseFolderPath) && selected.StartsWith(vm.BaseFolderPath, StringComparison.OrdinalIgnoreCase))
                vm.DataSubFolder = System.IO.Path.GetRelativePath(vm.BaseFolderPath, selected);
            else
                vm.DataSubFolder = selected;
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
        if (result.Count > 0) foreach (var item in result) await vm.AddModCommand.ExecuteAsync(item.Path.LocalPath);
        else {
            var folderResult = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Mod Folder" });
            if (folderResult.Count > 0) await vm.AddModCommand.ExecuteAsync(folderResult[0].Path.LocalPath);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void FocusRename_Click(object? sender, RoutedEventArgs e)
    {
        var textBox = this.FindControl<TextBox>("RenameTextBox");
        textBox?.Focus();
    }
}
