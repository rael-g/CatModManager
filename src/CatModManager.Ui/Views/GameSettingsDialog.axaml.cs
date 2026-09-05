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
/// It is an editing session over the open game: saving is held off for as long as the dialog is up,
/// and the values are committed by Save or put back by Cancel.
///
/// It used to write each field as it changed, inheriting that from the panel. The cost showed up
/// twice as lost data — a launch line overwritten while the panel filled itself in, and a rename
/// that reshuffled the game list on every keystroke — and each was patched where it hurt, so the
/// name and the mods folder had already grown their own deferral and their own rollback. One commit
/// point replaces those, and the two suppression counters that existed to keep the live edit from
/// firing at the wrong moment.
/// </summary>
public partial class GameSettingsDialog : Window
{
    private readonly IDisposable?                _held;
    private readonly GameConfigViewModel.Snapshot? _opened;

    /// <summary>Cleared by Save. A window closed any other way — Cancel, Escape, the X — reverts.</summary>
    private bool _committed;

    public GameSettingsDialog() : this(null) { }

    public GameSettingsDialog(MainWindowViewModel? vm)
    {
        InitializeComponent();

        if (vm != null)
        {
            DataContext = vm;

            // Taken before the bindings can touch anything, and held for the whole session: the
            // field setters still call Save, and this is what makes those calls no-ops.
            _opened = vm.GameConfig.TakeSnapshot();
            _held   = vm.GameConfig.SuppressSaving();
        }

        this.FindControl<Button>("SaveBtn")!.Click   += async (_, _) => await CommitAsync();
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close();

        Closing += (_, _) => { if (!_committed) Revert(); };
    }

    /// <summary>Opens the settings for the game that is currently open.</summary>
    public static Task ShowAsync(Window owner, MainWindowViewModel vm)
        => new GameSettingsDialog(vm).ShowDialog(owner);

    /// <summary>
    /// Writes the session down: the fields, then the two changes that do more than set a value.
    ///
    /// The mods folder is handled before anything is persisted because the user can still refuse it,
    /// and refusing has to leave the folder as it was rather than half-applied.
    /// </summary>
    private async Task CommitAsync()
    {
        if (DataContext is not MainWindowViewModel vm || _opened is not { } opened) { Close(); return; }

        string? chosenModsFolder = vm.GameConfig.ModsFolderPath;
        bool modsFolderMoved = !string.Equals(chosenModsFolder, opened.ModsFolderPath, StringComparison.Ordinal);
        bool executableMoved = !string.Equals(vm.GameConfig.GameExecutablePath, opened.GameExecutablePath,
                                              StringComparison.Ordinal);

        ModReconcileResult? scan = null;
        if (modsFolderMoved)
        {
            // Pointing somewhere else invalidates every path in the list at once, so the scan is
            // shown before it is applied. No file is touched either way — only the list changes.
            scan = await vm.ScanModsFolderAsync(chosenModsFolder);
            if (scan is not { } proposed) { vm.GameConfig.ModsFolderPath = opened.ModsFolderPath; modsFolderMoved = false; }
            else if (proposed.Added.Count > 0 || proposed.Removed.Count > 0)
            {
                bool confirmed = await new ConfirmDialog(
                    "Adopt this mods folder?",
                    $"{proposed.Removed.Count} mod(s) will be dropped from the list because they are not in "
                    + $"this folder, and {proposed.Added.Count} found there will be added, disabled.\n\n"
                    + "No files are moved or deleted — only the list changes.")
                    .ShowDialog<bool>(this);

                if (!confirmed)
                {
                    // Only this field goes back. Refusing the folder is not refusing the edit.
                    vm.GameConfig.ModsFolderPath = opened.ModsFolderPath;
                    scan = null;
                    modsFolderMoved = false;
                }
            }
        }

        _committed = true;
        _held?.Dispose();

        ApplyName(vm);
        vm.GameConfig.SaveGame?.Invoke();

        if (scan is { } accepted) vm.ApplyModFolderScan(accepted);

        // Picking the executable is what fills the three folders, so this is the moment the game
        // first knows where its mods are — and the moment they can appear in the list.
        if (executableMoved && !modsFolderMoved) await vm.AdoptGameFoldersAsync();

        Close();
    }

    /// <summary>
    /// Undoes the session. Still under suppression, so putting the values back does not itself write
    /// anything — and the game on disk is untouched, because nothing was written in the first place.
    /// </summary>
    private void Revert()
    {
        if (DataContext is MainWindowViewModel vm && _opened is { } opened) vm.GameConfig.Restore(opened);
        _held?.Dispose();
    }

    /// <summary>
    /// The rename, applied once at the end rather than as it is typed — eight rewrites of the row,
    /// and eight reshuffles of the game list, while someone types "Skyrim SE".
    /// </summary>
    private void ApplyName(MainWindowViewModel vm)
    {
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

        // Only the field. Adopting the folders it implies is part of Save, so that cancelling after
        // browsing around leaves the game exactly as it was found.
        vm.GameConfig.GameExecutablePath = files[0].Path.LocalPath;
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

        // Just the field. The scan, its confirmation and its rollback used to live here — a Cancel
        // hand-built for one field, which is what a real one now does for all of them.
        vm.GameConfig.ModsFolderPath = folders[0].Path.LocalPath;
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
        }
    }

    private void OpenMountPointFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button btn || btn.Tag is not MountPointDef mp) return;

        _ = vm.OpenFolder(mp.ResolveAbsolute(vm.GameConfig.BaseFolderPath));
    }
}
