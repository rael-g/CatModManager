using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CatModManager.PluginSdk;

namespace CmmPlugin.NexusMods;

/// <summary>
/// Right-click context menu action: checks the installed file against the latest on Nexus.
/// Shows a dialog with version info and a Download button if an update is available.
/// </summary>
internal sealed class NexusCheckUpdateAction : IModContextAction
{
    private readonly NexusModTrackingService _tracking;
    private readonly NexusApiService         _api;
    private readonly IPluginLogger           _log;

    public string Label => "Check for Updates (Nexus)";

    public NexusCheckUpdateAction(NexusModTrackingService tracking, NexusApiService api, IPluginLogger log)
    {
        _tracking = tracking;
        _api      = api;
        _log      = log;
    }

    public bool IsVisible(IModInfo? mod)
        => mod != null && _tracking.IsTracked(mod.RootPath);

    public async Task<string?> ExecuteAsync(IModInfo mod)
    {
        var entry = _tracking.GetEntry(mod.RootPath);
        if (entry == null)
            return $"[Nexus] No tracking entry found for {mod.Name}.";

        _log.Log($"[Nexus] Checking for updates: {mod.Name}…");

        try
        {
            var filesResponse = await _api.GetFilesAsync(entry.GameDomain, entry.ModId);

            var mainFile = filesResponse.Files
                .Where(f => f.CategoryName.Contains("MAIN", StringComparison.OrdinalIgnoreCase) || f.IsPrimary)
                .OrderByDescending(f => f.UploadedTimestamp)
                .FirstOrDefault()
                ?? filesResponse.Files.OrderByDescending(f => f.UploadedTimestamp).FirstOrDefault();

            if (mainFile == null)
                return $"{mod.Name}: no files found on Nexus.";

            // Compare by FileId — more reliable than version strings which authors sometimes forget to update
            if (mainFile.FileId == entry.FileId)
                return $"{mod.Name} is up to date (v{mainFile.Version}).";

            string nexusUrl = $"https://www.nexusmods.com/{entry.GameDomain}/mods/{entry.ModId}";
            _log.Log($"[Nexus] Update available for {mod.Name}: v{entry.Version} → v{mainFile.Version}");

            await Dispatcher.UIThread.InvokeAsync(() => ShowUpdateDialog(mod.Name, entry.Version, mainFile.Version, nexusUrl));
            return null;
        }
        catch (Exception ex)
        {
            _log.Log($"[Nexus] Update check failed for {mod.Name}: {ex.Message}");
            return $"Update check failed: {ex.Message}";
        }
    }

    private static void ShowUpdateDialog(string modName, string currentVersion, string latestVersion, string nexusUrl)
    {
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow == null) return;

        var window = new Window
        {
            Title          = "Update Available",
            Width          = 420,
            Height         = 170,
            CanResize      = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background     = mainWindow.Background,
            Foreground     = mainWindow.Foreground,
        };

        var downloadBtn = new Button { Content = "Download", Padding = new Avalonia.Thickness(16, 6), FontWeight = FontWeight.SemiBold };
        var closeBtn    = new Button { Content = "Close",    Padding = new Avalonia.Thickness(16, 6) };

        downloadBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(nexusUrl) { UseShellExecute = true }); }
            catch { }
            window.Close();
        };
        closeBtn.Click += (_, _) => window.Close();

        var btnRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing             = 8,
            Margin              = new Avalonia.Thickness(0, 12, 0, 0)
        };
        btnRow.Children.Add(closeBtn);
        btnRow.Children.Add(downloadBtn);

        var body = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 6 };
        body.Children.Add(new TextBlock { Text = $"Update available for {modName}", FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = $"Installed: v{currentVersion}    →    Latest: v{latestVersion}", FontSize = 12, Opacity = 0.7 });
        body.Children.Add(btnRow);

        window.Content = body;
        window.ShowDialog(mainWindow);
    }
}

