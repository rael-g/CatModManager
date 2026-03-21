using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CatModManager.Ui.Views;

/// <summary>
/// Modal dialog that asks the user whether to install a mod via the Virtual Filesystem (default)
/// or by copying files directly into the game folder (Base Folder install).
/// </summary>
public class InstallDestinationDialog : Window
{
    public bool ResultIsGameFolder { get; private set; }
    public bool WasCancelled       { get; private set; } = true;

    public InstallDestinationDialog()
    {
        Title                   = "Choose Installation Method";
        Width                   = 460;
        SizeToContent           = SizeToContent.Height;
        WindowStartupLocation   = WindowStartupLocation.CenterOwner;
        CanResize               = false;
        ShowInTaskbar           = false;

        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24) };

        panel.Children.Add(new TextBlock
        {
            Text         = "How do you want to install this mod?",
            FontSize     = 14,
            FontWeight   = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Virtual Filesystem (VFS) — Recommended.\n" +
                   "The mod is applied at game launch without touching the game files. " +
                   "Enable/disable at any time.\n\n" +
                   "Game Folder (Direct) — For ENB presets, ASI loaders, DLL mods.\n" +
                   "Files are copied physically into the game folder. " +
                   "Removing the mod from CMM will also delete those files.",
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground   = Brushes.Gray,
            LineHeight   = 20
        });

        var btnPanel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            Spacing             = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var vfsBtn = new Button
        {
            Content = "Virtual Filesystem (VFS)",
            Padding = new Thickness(12, 6)
        };
        vfsBtn.Click += (_, _) =>
        {
            WasCancelled      = false;
            ResultIsGameFolder = false;
            Close();
        };

        var gameFolderBtn = new Button
        {
            Content = "Game Folder (Direct)",
            Padding = new Thickness(12, 6)
        };
        gameFolderBtn.Click += (_, _) =>
        {
            WasCancelled      = false;
            ResultIsGameFolder = true;
            Close();
        };

        btnPanel.Children.Add(vfsBtn);
        btnPanel.Children.Add(gameFolderBtn);
        panel.Children.Add(btnPanel);

        Content = panel;
    }
}
