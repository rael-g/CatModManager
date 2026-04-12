using Avalonia.Controls;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CatModManager.Ui.Views;

public partial class UpdateDialog : Window
{
    private readonly string _downloadUrl;

    public UpdateDialog() { InitializeComponent(); _downloadUrl = string.Empty; }

    public UpdateDialog(string latestTag, string downloadUrl)
    {
        InitializeComponent();
        _downloadUrl = downloadUrl;

        this.FindControl<TextBlock>("TitleText")!.Text = $"A new version is available: {latestTag}";
        this.FindControl<TextBlock>("BodyText")!.Text  = "Click Download to open the release page and get the latest version.";
        this.FindControl<Button>("DownloadBtn")!.Click += (_, _) =>
        {
            OpenUrl(_downloadUrl);
            Close();
        };
        this.FindControl<Button>("LaterBtn")!.Click += (_, _) => Close();
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else
                Process.Start("xdg-open", url);
        }
        catch { }
    }
}
