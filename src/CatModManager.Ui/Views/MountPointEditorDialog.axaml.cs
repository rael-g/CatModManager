using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CatModManager.Ui.Views;

/// <summary>
/// Creates or edits a user mount point. The path is picked with the folder browser rather than
/// typed, so it is always a real directory.
/// </summary>
public partial class MountPointEditorDialog : Window
{
    /// <summary>Folder the browser opens at when the mount point has no path yet.</summary>
    private string? _fallbackStartFolder;

    public MountPointEditorDialog()
    {
        InitializeComponent();
        CancelBtn.Click += (_, _) => Close(null);
        OkBtn.Click += (_, _) => Close(Result());
        BrowseBtn.Click += async (_, _) => await BrowseAsync();
    }

    /// <summary>
    /// Shows the editor and returns the entered name and path, or null when cancelled or when
    /// either field was left blank.
    /// </summary>
    public static Task<(string Name, string Path)?> ShowAsync(
        Window owner, string initialName, string initialPath, string? fallbackStartFolder)
    {
        bool isNew = string.IsNullOrEmpty(initialName);

        var dialog = new MountPointEditorDialog { _fallbackStartFolder = fallbackStartFolder };
        dialog.Title = isNew ? "Add Mount Point" : "Edit Mount Point";
        dialog.HeaderText.Text = isNew ? "New Mount Point" : "Edit Mount Point";
        dialog.OkBtn.Content = isNew ? "Add" : "Save";
        dialog.NameBox.Text = initialName;
        dialog.PathBox.Text = initialPath;

        return dialog.ShowDialog<(string Name, string Path)?>(owner);
    }

    private (string Name, string Path)? Result()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(PathBox.Text))
            return null;
        return (NameBox.Text.Trim(), PathBox.Text.Trim());
    }

    private async Task BrowseAsync()
    {
        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        // Start where the mount point already points, else next to the game.
        string? start = Directory.Exists(PathBox.Text) ? PathBox.Text
            : Directory.Exists(_fallbackStartFolder) ? _fallbackStartFolder
            : null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Mount Point Folder",
            AllowMultiple = false,
            SuggestedStartLocation = start == null ? null : await storage.TryGetFolderFromPathAsync(start)
        });

        if (folders.Count >= 1) PathBox.Text = folders[0].Path.LocalPath;
    }
}
