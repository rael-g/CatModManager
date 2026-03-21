using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatModManager.Ui.ViewModels;

public partial class GameDetectionDialogViewModel : ObservableObject
{
    private readonly IGameDiscoveryService _discovery;

    [ObservableProperty] private bool              _isScanning = true;
    [ObservableProperty] private string            _status     = "Scanning Steam, GOG and Epic…";
    [ObservableProperty] private GameInstallation? _selectedInstallation;
    [ObservableProperty] private IGameSupport?     _selectedGameMode;

    public ObservableCollection<GameInstallation> Installations      { get; } = new();
    public ObservableCollection<IGameSupport>     AvailableSupports  { get; } = new();

    public GameInstallation? Result      { get; private set; }
    public IGameSupport?     ResultMode  { get; private set; }

    public GameDetectionDialogViewModel(
        IGameDiscoveryService discovery,
        IEnumerable<IGameSupport> availableSupports)
    {
        _discovery = discovery;
        foreach (var s in availableSupports)
            AvailableSupports.Add(s);
    }

    public async Task ScanAsync(CancellationToken ct = default)
    {
        IsScanning = true;
        Status     = "Scanning Steam, GOG and Epic…";
        Installations.Clear();

        var found = await _discovery.ScanAsync(ct);
        foreach (var inst in found)
            Installations.Add(inst);

        IsScanning = false;
        Status = found.Count == 0
            ? "No games found. Check that Steam/GOG/Epic are installed."
            : $"{found.Count} game(s) found.";

        if (Installations.Count > 0)
            SelectedInstallation = Installations[0];
    }

    partial void OnSelectedInstallationChanged(GameInstallation? value)
    {
        // Pre-select the detected game mode, or generic if none.
        SelectedGameMode = value?.DetectedSupport
            ?? AvailableSupports[0]; // Generic is always index 0
    }

    public void Apply()
    {
        Result     = SelectedInstallation;
        ResultMode = SelectedGameMode;
    }
}
