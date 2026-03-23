using System.Collections.ObjectModel;
using CatModManager.PluginSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CmmPlugin.SaveManager.Models;
using CmmPlugin.SaveManager.Services;

namespace CmmPlugin.SaveManager.Tabs;

public partial class SaveManagerTabViewModel : ObservableObject
{
    private readonly SaveDetector      _detector;
    private readonly SaveBackupService _backupService;
    private readonly IModManagerState  _state;
    private readonly IPluginLogger     _log;

    [ObservableProperty] private SaveGameDef? _currentGame;
    [ObservableProperty] private string?      _saveFolder;
    [ObservableProperty] private string       _status = "No compatible game detected.";

    public ObservableCollection<SaveBackup> Backups { get; } = [];

    public SaveManagerTabViewModel(SaveDetector detector, SaveBackupService backupService, IModManagerState state, IPluginLogger log)
    {
        _detector      = detector;
        _backupService = backupService;
        _state         = state;
        _log           = log;

        _state.ProfileChanged += _ => Refresh();
    }

    public void Refresh()
    {
        CurrentGame = _detector.Detect(_state.GameExecutablePath);

        if (CurrentGame == null)
        {
            SaveFolder = null;
            Status = "No compatible game detected.";
            Backups.Clear();
            return;
        }

        SaveFolder = SaveDetector.ResolveSaveFolder(CurrentGame);
        Status = SaveFolder != null
            ? $"{CurrentGame.DisplayName} — {SaveFolder}"
            : $"{CurrentGame.DisplayName} — save folder not found on disk";

        var list = _backupService.ListBackups(CurrentGame.GameId);
        Backups.Clear();
        foreach (var b in list) Backups.Add(b);
    }

    [RelayCommand]
    private async Task BackupNow()
    {
        if (CurrentGame == null || SaveFolder == null)
        {
            Status = "Cannot backup: game or save folder not detected.";
            return;
        }

        Status = "Creating backup...";
        var path = await _backupService.CreateBackupAsync(CurrentGame, SaveFolder, "manual");
        Status = path != null ? "Backup created." : "Backup failed — check logs.";
        Refresh();
    }

    public async Task Restore(SaveBackup backup)
    {
        if (SaveFolder == null) return;
        Status = "Restoring backup...";
        await _backupService.RestoreBackupAsync(backup, SaveFolder);
        Status = $"Restored: {backup.Label}";
        Refresh();
    }

    public void Delete(SaveBackup backup)
    {
        _backupService.DeleteBackup(backup);
        Backups.Remove(backup);
    }
}
