using System.Collections.ObjectModel;
using Avalonia.Threading;
using CatModManager.PluginSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CmmPlugin.SaveManager.Models;
using CmmPlugin.SaveManager.Services;

namespace CmmPlugin.SaveManager.Tabs;

public partial class SaveManagerTabViewModel : ObservableObject
{
    private readonly SaveDetector        _detector;
    private readonly SaveBackupService   _backupService;
    private readonly SaveManagerSettings _settings;
    private readonly AutoSaver           _autoSaver;
    private readonly IModManagerState    _state;
    private readonly IPluginLogger       _log;

    [ObservableProperty] private SaveGameDef? _currentGame;
    [ObservableProperty] private string?      _saveFolder;
    [ObservableProperty] private string       _status = "";
    [ObservableProperty] private string       _newSlotLabel = "";
    [ObservableProperty] private bool         _autoSaveEnabled;
    [ObservableProperty] private int          _autoSaveMinutes = GameSaveSettings.DefaultAutoSaveMinutes;
    [ObservableProperty] private bool         _backupBeforeLaunch;

    /// <summary>Whether saving and loading are possible at all — i.e. we know where the saves are.</summary>
    public bool CanUseSlots => SaveFolder != null;

    public ObservableCollection<SaveSlot> Slots { get; } = [];

    public SaveManagerTabViewModel(
        SaveDetector        detector,
        SaveBackupService   backupService,
        SaveManagerSettings settings,
        AutoSaver           autoSaver,
        IModManagerState    state,
        IPluginLogger       log)
    {
        _detector      = detector;
        _backupService = backupService;
        _settings      = settings;
        _autoSaver     = autoSaver;
        _state         = state;
        _log           = log;

        _state.ProfileChanged += _ => Refresh();

        // Snapshots are written on a timer thread; the list they belong to is owned by the UI one.
        _autoSaver.SlotWritten += () => Dispatcher.UIThread.Post(ReloadSlots);
    }

    /// <summary>
    /// Which game's slots we are showing. Falls back to CMM's own game id so that a game with no
    /// shipped definition still gets its own folder once the user points at their saves by hand.
    /// </summary>
    private string? GameId => CurrentGame?.GameId ?? _state.GameId;

    public void Refresh()
    {
        CurrentGame = _detector.Detect(_state.GameExecutablePath, _state.DataFolderPath);

        string? gameId  = GameId;
        var     configured = _settings.For(gameId);

        // Reflect the stored settings without writing them back through the property setters.
        _suppressAutoSavePersist = true;
        AutoSaveEnabled    = configured.AutoSaveEnabled;
        AutoSaveMinutes    = configured.AutoSaveMinutes;
        BackupBeforeLaunch = configured.BackupBeforeLaunch;
        _suppressAutoSavePersist = false;

        // The user's own choice comes first: they can see the folder we found and know better.
        string? folder = configured.SaveFolder;
        bool    manual = folder != null;

        if (folder != null && !Directory.Exists(folder))
        {
            Status = $"The save folder you chose no longer exists: {folder}";
            SaveFolder = null;
            Slots.Clear();
            OnPropertyChanged(nameof(CanUseSlots));
            _autoSaver.Stop();
            return;
        }

        if (folder == null && CurrentGame != null)
            folder = _detector.ResolveSaveFolder(CurrentGame, _state.DataFolderPath, _state.GameExecutablePath);

        SaveFolder = folder;
        OnPropertyChanged(nameof(CanUseSlots));

        string name = CurrentGame?.DisplayName ?? gameId ?? "this game";

        Status = folder switch
        {
            null when CurrentGame == null => "No save folder known for this game yet — choose one below.",
            null                          => $"{name}: save folder not found on disk — choose one below.",
            _ when manual                 => $"{name} — {folder}  (chosen by you)",
            _                             => $"{name} — {folder}"
        };

        ReloadSlots();
        SyncAutoSaver();
    }

    private void ReloadSlots()
    {
        Slots.Clear();
        if (GameId == null) return;
        foreach (var s in _backupService.ListSlots(GameId)) Slots.Add(s);
    }

    /// <summary>Points this game's saves at a folder the user picked, and remembers it.</summary>
    public void SetSaveFolder(string folder)
    {
        string? gameId = GameId;
        if (gameId == null)
        {
            Status = "Select a game profile first — slots are stored per game.";
            return;
        }

        _settings.Update(gameId, s => s.SaveFolder = folder);
        _log.Log($"[SaveManager] Save folder for '{gameId}' set to: {folder}");
        Refresh();
    }

    public void ClearSaveFolderOverride()
    {
        _settings.Update(GameId, s => s.SaveFolder = null);
        Refresh();
    }

    // ── Auto-save ────────────────────────────────────────────────────────────

    /// <summary>
    /// Set while Refresh pushes stored settings into the properties, so loading a game's
    /// configuration does not look like the user having just changed it.
    /// </summary>
    private bool _suppressAutoSavePersist;

    partial void OnAutoSaveEnabledChanged(bool value)    => PersistAutoSaveSettings();
    partial void OnAutoSaveMinutesChanged(int value)     => PersistAutoSaveSettings();
    partial void OnBackupBeforeLaunchChanged(bool value) => PersistAutoSaveSettings();

    private void PersistAutoSaveSettings()
    {
        if (_suppressAutoSavePersist) return;

        int minutes = Math.Max(GameSaveSettings.MinAutoSaveMinutes, AutoSaveMinutes);

        _settings.Update(GameId, s =>
        {
            s.AutoSaveEnabled    = AutoSaveEnabled;
            s.AutoSaveMinutes    = minutes;
            s.BackupBeforeLaunch = BackupBeforeLaunch;
        });

        SyncAutoSaver();
    }

    /// <summary>Starts, restarts or stops the timer to match what is configured right now.</summary>
    private void SyncAutoSaver()
    {
        if (AutoSaveEnabled && SaveFolder != null && GameId != null)
        {
            _autoSaver.Start(GameId, SaveFolder, Math.Max(GameSaveSettings.MinAutoSaveMinutes, AutoSaveMinutes));
            AutoSaveStatus = $"Auto-saving every {Math.Max(GameSaveSettings.MinAutoSaveMinutes, AutoSaveMinutes)} min when the saves change.";
        }
        else
        {
            _autoSaver.Stop();
            AutoSaveStatus = AutoSaveEnabled
                ? "Auto-save is on, but the save folder is not set."
                : "";
        }
    }

    [ObservableProperty] private string _autoSaveStatus = "";

    [RelayCommand]
    private async Task Save()
    {
        if (SaveFolder == null || GameId == null)
        {
            Status = "Cannot save: the save folder is not set.";
            return;
        }

        string label = string.IsNullOrWhiteSpace(NewSlotLabel)
            ? DateTime.Now.ToString("MMM d, HH:mm")
            : NewSlotLabel.Trim();

        Status = "Saving…";
        var path = await _backupService.CreateAsync(GameId, SaveFolder, label);

        if (path != null)
        {
            NewSlotLabel = "";
            Status       = $"Saved: {label}";
        }
        else
        {
            Status = "Save failed — check the log.";
        }

        ReloadSlots();
    }

    /// <summary>
    /// Overwrites the live saves with a slot. Errors are surfaced rather than thrown at the UI: the
    /// one message the user must never miss is the one about their saves.
    /// </summary>
    public async Task Load(SaveSlot slot)
    {
        if (SaveFolder == null || GameId == null) return;

        Status = $"Loading '{slot.Label}'…";
        try
        {
            await _backupService.LoadAsync(slot, SaveFolder, GameId);
            Status = $"Loaded: {slot.Label}. Your previous saves were kept as a 'before load' slot.";
        }
        catch (Exception ex)
        {
            _log.LogError($"[SaveManager] Load failed for '{slot.Label}'", ex);
            Status = $"Load failed, your saves were not changed: {ex.Message}";
        }

        ReloadSlots();
    }

    public void Delete(SaveSlot slot)
    {
        try
        {
            _backupService.Delete(slot);
            Slots.Remove(slot);
            Status = $"Deleted: {slot.Label}";
        }
        catch (Exception ex)
        {
            _log.LogError($"[SaveManager] Could not delete '{slot.Label}'", ex);
            Status = $"Could not delete: {ex.Message}";
        }
    }
}
