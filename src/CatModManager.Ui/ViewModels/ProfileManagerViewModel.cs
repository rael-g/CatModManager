using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Ui.ViewModels;

/// <summary>
/// Owns all profile list / CRUD state and commands.
/// Communicates with MainWindowViewModel via events and callbacks:
///   - ProfileLoaded  → raised when a profile is loaded; caller applies the data.
///   - BuildSaveData  → callback that returns the current Profile to persist.
///   - IsVfsMounted   → callback used by DeleteProfile to guard against deletion while mounted.
///   - ConfirmDelete  → async callback set by the View to show a confirmation dialog.
/// </summary>
public partial class ProfileManagerViewModel : ViewModelBase
{
    private readonly IProfileService  _profileService;
    private readonly ICatPathService  _pathService;
    private readonly IFileService     _fileService;
    private readonly IConfigService   _configService;
    private readonly ILogService      _logService;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _suppressionCount;

    // ── Events & callbacks ────────────────────────────────────────────────────

    /// <summary>Raised after a profile file is loaded. Subscriber applies paths / mods.</summary>
    public event Action<Profile>? ProfileLoaded;

    /// <summary>Called when saving — returns the Profile snapshot to persist.</summary>
    public Func<Profile>?        BuildSaveData;

    /// <summary>Returns whether the VFS is currently mounted (guard for delete).</summary>
    public Func<bool>?           IsVfsMounted;

    /// <summary>Set by the View to show a confirmation dialog before deleting a profile.</summary>
    public Func<string, Task<bool>>? ConfirmDelete;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    private string? _currentProfileName;

    [ObservableProperty]
    private string? _profileDisplayName;

    [ObservableProperty]
    private ObservableCollection<string> _availableProfiles = new();

    // ── Suppression helpers (used by MainWindowViewModel too) ─────────────────

    public bool IsAutoSaveSuppressed => _suppressionCount > 0;

    public IDisposable SuppressAutoSave() => new Suppressor(this);

    private sealed class Suppressor : IDisposable
    {
        private readonly ProfileManagerViewModel _vm;
        public Suppressor(ProfileManagerViewModel vm) { _vm = vm; Interlocked.Increment(ref _vm._suppressionCount); }
        public void Dispose() => Interlocked.Decrement(ref _vm._suppressionCount);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ProfileManagerViewModel(
        IProfileService profileService,
        ICatPathService pathService,
        IFileService    fileService,
        IConfigService  configService,
        ILogService     logService)
    {
        _profileService = profileService;
        _pathService    = pathService;
        _fileService    = fileService;
        _configService  = configService;
        _logService     = logService;
    }

    // ── Property changed handlers ─────────────────────────────────────────────

    partial void OnCurrentProfileNameChanged(string? value)
    {
        if (IsAutoSaveSuppressed) return;
        if (!string.IsNullOrEmpty(value) && AvailableProfiles.Contains(value))
        {
            ProfileDisplayName = value;
            _ = LoadProfileAsync(value);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task NewProfile()
    {
        string newName = GetUniqueProfileName("NewProfile");
        ProfileDisplayName = newName;
        await SaveProfileInternalAsync(newName);
        await RefreshListAsync(newName);
    }

    [RelayCommand]
    public async Task DeleteProfile()
    {
        if (string.IsNullOrEmpty(CurrentProfileName)) return;

        if (ConfirmDelete != null)
        {
            bool confirmed = await ConfirmDelete(CurrentProfileName);
            if (!confirmed) return;
        }

        if (IsVfsMounted?.Invoke() == true)
        {
            _logService.Log("ERROR: Cannot delete active profile while Safe Swap is active. Please unmount first.");
            return;
        }

        string profileToDelete = CurrentProfileName;
        string path = _pathService.GetProfilePath(profileToDelete);

        await _lock.WaitAsync();
        try
        {
            using (SuppressAutoSave())
            {
                CurrentProfileName = null;
                ProfileDisplayName = null;

                if (File.Exists(path))
                {
                    File.Delete(path);
                    _logService.Log($"Profile '{profileToDelete}' deleted.");
                }

                AvailableProfiles.Remove(profileToDelete);
                await RefreshListAsync();
            }
        }
        catch (Exception ex) { _logService.Log($"DELETE ERROR: {ex.Message}"); return; }
        finally { _lock.Release(); }

        if (AvailableProfiles.Count > 0)
            await LoadProfileAsync(AvailableProfiles[0]);
        else
            await NewProfile();
    }

    [RelayCommand]
    public async Task RenameProfile()
    {
        if (string.IsNullOrEmpty(CurrentProfileName) ||
            string.IsNullOrEmpty(ProfileDisplayName) ||
            CurrentProfileName == ProfileDisplayName) return;

        await _lock.WaitAsync();
        try
        {
            string oldPath = _pathService.GetProfilePath(CurrentProfileName);
            if (_fileService.FileExists(oldPath))
            {
                await SaveProfileInternalAsync(ProfileDisplayName);
                File.Delete(oldPath);
                string newName = ProfileDisplayName;
                _logService.Log($"Profile renamed: '{CurrentProfileName}' → '{newName}'");
                _configService.Current.LastProfileName = newName;
                _configService.Save();
                await RefreshListAsync(newName);
            }
        }
        catch (Exception ex) { _logService.Log($"RENAME ERROR: {ex.Message}"); }
        finally { _lock.Release(); }
    }

    [RelayCommand]
    public async Task SaveProfile(string? name)
    {
        await _lock.WaitAsync();
        try { await SaveProfileInternalAsync(name); }
        finally { _lock.Release(); }
    }

    [RelayCommand]
    public async Task LoadProfile(string? name) => await LoadProfileAsync(name);

    // ── Public helpers ────────────────────────────────────────────────────────

    public void AutoSave()
    {
        if (IsAutoSaveSuppressed) return;
        if (string.IsNullOrWhiteSpace(CurrentProfileName)) return;
        _ = SaveProfile(CurrentProfileName);
    }

    public async Task RefreshListAsync(string? selectAfter = null)
    {
        var profiles = await _profileService.ListProfilesAsync(_pathService.ProfilesPath);
        var names    = profiles.Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();

        AvailableProfiles.Clear();
        foreach (var p in names) if (p != null) AvailableProfiles.Add(p);

        if (!string.IsNullOrEmpty(selectAfter) && AvailableProfiles.Contains(selectAfter))
        {
            using (SuppressAutoSave())
            {
                CurrentProfileName = selectAfter;
                ProfileDisplayName = selectAfter;
            }
        }
    }

    public async Task LoadInitialProfile(string? lastProfileName)
    {
        await RefreshListAsync();
        if (!string.IsNullOrEmpty(lastProfileName))
            await LoadProfileAsync(lastProfileName);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task LoadProfileAsync(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        await _lock.WaitAsync();
        try
        {
            string filePath = _pathService.GetProfilePath(name);
            if (!_fileService.FileExists(filePath))
            {
                _logService.Log($"LOAD ERROR: Profile file not found: {name}");
                return;
            }
            var p = await _profileService.LoadProfileAsync(filePath);
            if (p != null)
            {
                using (SuppressAutoSave())
                {
                    CurrentProfileName = name;
                    ProfileDisplayName = name;
                    _configService.Current.LastProfileName = name;
                    _configService.Save();
                }
                ProfileLoaded?.Invoke(p);
                _logService.Log($"Profile '{name}' loaded.");
            }
        }
        catch (Exception ex) { _logService.Log($"LOAD ERROR: {ex.Message}"); }
        finally { _lock.Release(); }
    }

    private async Task SaveProfileInternalAsync(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var profile = BuildSaveData?.Invoke() ?? new Profile { Name = name };
            profile.Name = name;
string filePath = _pathService.GetProfilePath(name);
            await _profileService.SaveProfileAsync(profile, filePath);
            using (SuppressAutoSave())
            {
                _configService.Current.LastProfileName = name;
                _configService.Save();
            }
        }
        catch (Exception ex) { _logService.Log($"SAVE ERROR: {ex.Message}"); }
    }

    private string GetUniqueProfileName(string baseName)
    {
        string name = baseName;
        int counter = 1;
        while (AvailableProfiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            name = $"{baseName} {counter++}";
        return name;
    }
}
