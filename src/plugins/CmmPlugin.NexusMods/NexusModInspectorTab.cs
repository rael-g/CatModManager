using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CatModManager.PluginSdk;
using CatModManager.Theme;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Inspector tab that shows Nexus tracking information for a mod that is tracked.
/// </summary>
public class NexusModInspectorTab : IInspectorTab
{
    private readonly NexusModTrackingService _tracking;
    private readonly NexusApiService _api;

    public string TabId => "nexus-mod";
    public string TabLabel => "NEXUS";

    public NexusModInspectorTab(NexusModTrackingService tracking, NexusApiService api)
    {
        _tracking = tracking;
        _api = api;
    }

    public bool IsVisible(IModInfo? selectedMod)
    {
        return selectedMod != null && _tracking.IsTracked(selectedMod.ModRootPath);
    }

    public object CreateView(IModInfo? mod)
    {
        var entry = mod != null ? _tracking.GetEntry(mod.ModRootPath) : null;

        var updateStatus = new TextBlock
        {
            Text = string.Empty,
            FontSize = 11,
            Foreground = CmmPalette.Brushes.TextSubtle,
            TextWrapping = TextWrapping.Wrap
        };

        var modIdText = new TextBlock
        {
            Text = entry != null ? $"Mod ID: {entry.ModId}" : "Mod ID: —",
            FontSize = 12,
            Foreground = Brushes.White
        };

        var gameText = new TextBlock
        {
            Text = entry != null ? $"Game: {entry.GameDomain}" : "Game: —",
            FontSize = 12,
            Foreground = Brushes.White
        };

        var versionText = new TextBlock
        {
            Text = entry != null ? $"Tracked Version: {entry.Version}" : "Tracked Version: —",
            FontSize = 12,
            Foreground = Brushes.White
        };

        var openBtn = new Button
        {
            Content = "Open on Nexus",
            Padding = new Thickness(12, 6),
            Background = CmmPalette.Brushes.Accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };

        openBtn.Click += (_, _) =>
        {
            if (entry == null)
                return;

            try
            {
                var url = $"https://www.nexusmods.com/{entry.GameDomain}/mods/{entry.ModId}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                updateStatus.Text = $"Error opening browser: {ex.Message}";
                updateStatus.Foreground = CmmPalette.Brushes.StatusDanger;
            }
        };

        var checkBtn = new Button
        {
            Content = "Check for Updates",
            Padding = new Thickness(12, 6),
            Background = CmmPalette.Brushes.SurfaceSelected,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };

        checkBtn.Click += async (_, _) =>
        {
            if (entry == null)
            {
                updateStatus.Text = "No tracking entry found.";
                return;
            }

            try
            {
                checkBtn.IsEnabled = false;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    updateStatus.Text = "Checking for updates...";
                    updateStatus.Foreground = CmmPalette.Brushes.TextSubtle;
                });

                var filesResponse = await _api.GetFilesAsync(entry.GameDomain, entry.ModId);

                // Find the latest MAIN file
                var mainFile = filesResponse.Files
                    .Where(f => f.CategoryName.Contains("MAIN", StringComparison.OrdinalIgnoreCase)
                                || f.IsPrimary)
                    .OrderByDescending(f => f.UploadedTimestamp)
                    .FirstOrDefault()
                    ?? filesResponse.Files.OrderByDescending(f => f.UploadedTimestamp).FirstOrDefault();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (mainFile == null)
                    {
                        updateStatus.Text = "No files found on Nexus.";
                        updateStatus.Foreground = CmmPalette.Brushes.TextSubtle;
                    }
                    else if (string.Equals(mainFile.Version, entry.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        updateStatus.Text = $"Up to date! (v{entry.Version})";
                        updateStatus.Foreground = CmmPalette.Brushes.StatusActive;
                    }
                    else
                    {
                        updateStatus.Text = $"Update available: v{entry.Version} → v{mainFile.Version}";
                        updateStatus.Foreground = CmmPalette.Brushes.StatusWarning;
                    }

                    checkBtn.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    updateStatus.Text = $"Error: {ex.Message}";
                    updateStatus.Foreground = CmmPalette.Brushes.StatusDanger;
                    checkBtn.IsEnabled = true;
                });
            }
        };

        var innerStack = new StackPanel
        {
            Spacing = 12
        };
        innerStack.Children.Add(modIdText);
        innerStack.Children.Add(gameText);
        innerStack.Children.Add(versionText);
        innerStack.Children.Add(openBtn);
        innerStack.Children.Add(checkBtn);
        innerStack.Children.Add(updateStatus);

        var root = new Border
        {
            Padding = new Thickness(12),
            Background = CmmPalette.Brushes.ContentBg,
            Child = innerStack
        };

        return root;
    }
}

