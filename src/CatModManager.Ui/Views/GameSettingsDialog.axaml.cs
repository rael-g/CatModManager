using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Ui.Views;

/// <summary>
/// Everything that describes one installation: where it is, which game mode applies, how it is
/// launched, and the folders mods can be mounted into.
///
/// This used to be the left panel of the main window, alongside the mod list. It is a dialog now
/// because it is not something anyone looks at while working — it is filled in once when the game
/// is added, and revisited rarely. The window's own sidebar is for the things that are used all the
/// time: which game, which profile, add a mod, mount, launch.
///
/// It edits the open game live, through the same view model the panel used, and every field saves
/// as it changes. That is why there is no Cancel — see the note in the XAML.
/// </summary>
public partial class GameSettingsDialog : Window
{
    public GameSettingsDialog() : this(null) { }

    public GameSettingsDialog(MainWindowViewModel? vm)
    {
        InitializeComponent();
        if (vm != null) DataContext = vm;

        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();

        // The name is applied on the way out rather than as it is typed: a rename per keystroke
        // would rewrite the row — and reshuffle the game list under the user — eight times while
        // they type "Skyrim SE".
        Closing += (_, _) => ApplyName();
    }

    /// <summary>Opens the settings for the game that is currently open.</summary>
    public static Task ShowAsync(Window owner, MainWindowViewModel vm)
        => new GameSettingsDialog(vm).ShowDialog(owner);

    private void ApplyName()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var typed = this.FindControl<TextBox>("GameNameBox")?.Text;
        if (!string.IsNullOrWhiteSpace(typed)) vm.GameManager.RenameGameCommand.Execute(typed);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ── Folder pickers ────────────────────────────────────────────────────────

    private async Task<IStorageFolder?> StartFolderAsync(string? preferred, string? fallback = null)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;
        foreach (var path in new[] { preferred, fallback })
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return await topLevel.StorageProvider.TryGetFolderFromPathAsync(path);
        }
        return null;
    }

    private async void SelectGame_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var startDir = !string.IsNullOrEmpty(vm.GameConfig.GameExecutablePath)
            ? Path.GetDirectoryName(vm.GameConfig.GameExecutablePath) : vm.GameConfig.BaseFolderPath;

        var files = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(startDir),
            FileTypeFilter = new[] { new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } } }
        });
        if (files.Count < 1) return;

        vm.GameConfig.GameExecutablePath = files[0].Path.LocalPath;

        // Picking the executable is what fills the three folders, so this is the moment the game
        // first knows where its mods are — and the moment they can appear in the list.
        await vm.AdoptGameFoldersAsync();
    }

    private async void SelectBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Base Game Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(vm.GameConfig.BaseFolderPath)
        });
        if (folders.Count >= 1)
            vm.GameConfig.BaseFolderPath = folders[0].Path.LocalPath;
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Mods Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(vm.GameConfig.ModsFolderPath, vm.GameConfig.BaseFolderPath)
        });
        if (folders.Count < 1) return;

        string? previous = vm.GameConfig.ModsFolderPath;
        string chosen = folders[0].Path.LocalPath;
        if (string.Equals(previous, chosen, StringComparison.Ordinal)) return;

        vm.GameConfig.ModsFolderPath = chosen;

        // Pointing somewhere else invalidates every path in the list at once, so the scan is shown
        // before it is applied. No file is touched either way — only the list changes.
        var result = await vm.ScanModsFolderAsync(chosen);
        if (result is not { } scan) { vm.GameConfig.ModsFolderPath = previous ?? ""; return; }

        if (scan.Added.Count == 0 && scan.Removed.Count == 0)
        {
            vm.ApplyModFolderScan(scan);
            return;
        }

        bool confirmed = await new ConfirmDialog(
            "Adopt this mods folder?",
            $"{scan.Removed.Count} mod(s) will be dropped from the list because they are not in this "
            + $"folder, and {scan.Added.Count} found there will be added, disabled.\n\n"
            + "No files are moved or deleted — only the list changes.")
            .ShowDialog<bool>(this);

        if (confirmed)
            vm.ApplyModFolderScan(scan);
        else
            vm.GameConfig.ModsFolderPath = previous ?? "";   // cancelling leaves nothing changed
    }

    private async void SelectDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Downloads Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(vm.GameConfig.DownloadsFolderPath, vm.GameConfig.BaseFolderPath)
        });
        if (folders.Count >= 1)
            vm.GameConfig.DownloadsFolderPath = folders[0].Path.LocalPath;
    }

    // ── Mount points ──────────────────────────────────────────────────────────

    private async void AddMountPoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var result = await MountPointEditorDialog.ShowAsync(this, "", "", vm.GameConfig.BaseFolderPath);
        if (result.HasValue)
            vm.GameConfig.AddUserMountPoint(result.Value.Name, result.Value.Path);
    }

    private async void EditMountPoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button btn || btn.Tag is not MountPointDef mp) return;

        var result = await MountPointEditorDialog.ShowAsync(this, mp.Name, mp.Path, vm.GameConfig.BaseFolderPath);
        if (!result.HasValue) return;

        if (mp.IsGameDefined)
        {
            // Game-defined: store a path override (the name stays the game definition's).
            vm.GameConfig.OverrideGameDefinedMountPointPath(mp.Id, mp.Name, result.Value.Path);
        }
        else
        {
            mp.Name = result.Value.Name;
            mp.Path = result.Value.Path;
            vm.GameConfig.NotifyMountPointsChanged();
            vm.GameConfig.SaveGame?.Invoke();
        }
    }

    private void OpenMountPointFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button btn || btn.Tag is not MountPointDef mp) return;

        _ = vm.OpenFolder(mp.ResolveAbsolute(vm.GameConfig.BaseFolderPath));
    }
}
