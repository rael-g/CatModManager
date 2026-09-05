using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Ui.ViewModels;

/// <summary>
/// Owns all profile list / CRUD state and commands, for one game at a time.
///
/// Communicates with MainWindowViewModel via events and callbacks:
///   - ProfileLoaded  → raised when a profile is loaded; caller applies the data.
///   - BuildSaveData  → callback that returns the current Profile to persist.
///   - IsVfsMounted   → callback used by DeleteProfile to guard against deletion while mounted.
///   - ConfirmDelete  → async callback set by the View to show a confirmation dialog.
///   - CurrentGameId  → which game new profiles belong to, and which ones the list shows.
/// </summary>
public partial class ProfileManagerViewModel : ViewModelBase
{
    private readonly IProfileService  _profileService;
    private readonly IConfigService   _configService;
    private readonly ILogService      _logService;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _suppressionCount;
    private ProfileSummary? _previousProfile;
    private bool _confirming;

    // ── Events & callbacks ────────────────────────────────────────────────────

    /// <summary>Raised after a profile is loaded. Subscriber applies mods / tools / mount points.</summary>
    public event Action<Profile>? ProfileLoaded;

    /// <summary>Called when saving — returns the Profile snapshot to persist.</summary>
    public Func<Profile>?        BuildSaveData;

    /// <summary>Returns whether the VFS is currently mounted (guard for delete).</summary>
    public Func<bool>?           IsVfsMounted;

    /// <summary>
    /// The game whose profiles are listed, and that a new profile joins. Null means the parked
    /// ones — profiles with no game at all.
    /// </summary>
    public Func<long?>?          CurrentGameId;

    /// <summary>Set by the View to show a confirmation dialog before deleting a profile.</summary>
    public Func<string, Task<bool>>? ConfirmDelete;

    /// <summary>
    /// Set by the View: asks for the new name, given the current one, and returns null if the user
    /// backed out. Renaming used to read a text box in the sidebar; that box is gone, and a menu
    /// entry has nowhere to type.
    /// </summary>
    public Func<string, Task<string?>>? RequestRename;

    /// <summary>
    /// Set by the View to confirm a profile switch. Receives the new profile name.
    /// Return false to cancel. Only invoked when CheckHasActiveDownloads (set by a plugin) returns true.
    /// </summary>
    public Func<string, Task<bool>>? ConfirmProfileChange;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    private ProfileSummary? _currentProfile;

    [ObservableProperty]
    private string? _profileDisplayName;

    [ObservableProperty]
    private ObservableCollection<ProfileSummary> _availableProfiles = new();

    /// <summary>
    /// What the rest of the application still asks for. Plugins and the session state care about the
    /// name on screen, not which row it came from.
    /// </summary>
    public string? CurrentProfileName => CurrentProfile?.Name;

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
        IConfigService  configService,
        ILogService     logService)
    {
        _profileService = profileService;
        _configService  = configService;
        _logService     = logService;
    }

    // ── Property changed handlers ─────────────────────────────────────────────

    partial void OnCurrentProfileChanged(ProfileSummary? value)
    {
        OnPropertyChanged(nameof(CurrentProfileName));

        if (IsAutoSaveSuppressed || _confirming) return;
        if (value != null && AvailableProfiles.Contains(value))
        {
            ProfileDisplayName = value.Name;
            _ = ConfirmAndLoadAsync(value);
        }
    }

    private async Task ConfirmAndLoadAsync(ProfileSummary profile)
    {
        if (ConfirmProfileChange != null)
        {
            bool ok = await ConfirmProfileChange(profile.Name);
            if (!ok)
            {
                _confirming = true;
                CurrentProfile     = _previousProfile;
                ProfileDisplayName = _previousProfile?.Name;
                _confirming = false;
                return;
            }
        }
        _previousProfile = profile;
        await LoadProfileAsync(profile.Id);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task NewProfile()
    {
        long? gameId = CurrentGameId?.Invoke();
        string newName = GetUniqueProfileName("NewProfile");
        long id;

        // Save a blank profile — intentionally NOT using BuildSaveData to avoid
        // copying the current profile's state into the new one.
        await _lock.WaitAsync();
        try
        {
            id = await _profileService.SaveProfileAsync(new Profile { Name = newName, GameId = gameId });
        }
        catch (Exception ex) { _logService.Log($"NEW PROFILE ERROR: {ex.Message}"); return; }
        finally { _lock.Release(); }

        await RefreshListAsync(id);
        await LoadProfileAsync(id);
    }

    /// <summary>
    /// Creates a profile holding what is on screen right now — the same mods, ticked the same way,
    /// in the same order.
    ///
    /// New Profile deliberately starts blank, and a mod installed since then reaches every profile
    /// of the game unticked, because enabling one behind the user's back changes how their game
    /// runs. Both are right, and both leave the same gap: trying a variation of a working setup
    /// meant re-ticking the list by hand, which for a few hundred mods is not something anyone
    /// does twice.
    ///
    /// Built from <see cref="BuildSaveData"/> rather than reread from the database, so unticking
    /// something and duplicating immediately copies what the user is looking at instead of the
    /// last state that happened to be written.
    /// </summary>
    [RelayCommand]
    public async Task DuplicateProfile()
    {
        if (CurrentProfile is not { } source) return;
        if (BuildSaveData?.Invoke() is not { } copy) return;

        // Id zero is what makes this an insert instead of an overwrite of the profile it came from.
        copy.Id     = 0;
        copy.Name   = GetUniqueProfileName($"{source.Name} copy");
        copy.GameId = CurrentGameId?.Invoke();

        long id;
        await _lock.WaitAsync();
        try
        {
            id = await _profileService.SaveProfileAsync(copy);
            _logService.Log($"Profile '{source.Name}' duplicated as '{copy.Name}'.");
        }
        catch (Exception ex) { _logService.Log($"DUPLICATE PROFILE ERROR: {ex.Message}"); return; }
        finally { _lock.Release(); }

        await RefreshListAsync(id);
        await LoadProfileAsync(id);
    }

    /// <summary>Picks a profile from the menu. Same thing the selector in the command bar does.</summary>
    [RelayCommand]
    public void SelectProfile(ProfileSummary? profile)
    {
        if (profile != null) CurrentProfile = profile;
    }

    [RelayCommand]
    public async Task DeleteProfile()
    {
        if (CurrentProfile is not { } profile) return;

        if (ConfirmDelete != null && !await ConfirmDelete(profile.Name)) return;

        if (IsVfsMounted?.Invoke() == true)
        {
            _logService.Log("ERROR: Cannot delete active profile while Safe Swap is active. Please unmount first.");
            return;
        }

        await _lock.WaitAsync();
        try
        {
            using (SuppressAutoSave())
            {
                CurrentProfile     = null;
                ProfileDisplayName = null;

                await _profileService.DeleteProfileAsync(profile.Id);
                _logService.Log($"Profile '{profile.Name}' deleted.");

                AvailableProfiles.Remove(profile);
            }
        }
        catch (Exception ex) { _logService.Log($"DELETE ERROR: {ex.Message}"); return; }
        finally { _lock.Release(); }

        await RefreshListAsync(null);
        if (AvailableProfiles.Count > 0) await LoadProfileAsync(AvailableProfiles[0].Id);
        else await NewProfile();
    }

    [RelayCommand]
    public async Task RenameProfile()
    {
        if (CurrentProfile is not { } profile) return;

        string? asked = RequestRename != null
            ? await RequestRename.Invoke(profile.Name)
            : ProfileDisplayName;
        if (string.IsNullOrWhiteSpace(asked) || asked == profile.Name) return;

        await _lock.WaitAsync();
        try
        {
            string newName = asked;

            // One rename, not "save under the new name and delete the old one". That sequence left
            // two profiles behind whenever the delete failed, and it also copied the *current* UI
            // state into the new name rather than renaming what was stored.
            await _profileService.RenameProfileAsync(profile.Id, newName);

            _logService.Log($"Profile renamed: '{profile.Name}' → '{newName}'");
            await RefreshListAsync(profile.Id);
        }
        catch (Exception ex) { _logService.Log($"RENAME ERROR: {ex.Message}"); }
        finally { _lock.Release(); }
    }

    [RelayCommand]
    public async Task SaveProfile()
    {
        await _lock.WaitAsync();
        try { await SaveProfileInternalAsync(); }
        finally { _lock.Release(); }
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    public void AutoSave()
    {
        if (IsAutoSaveSuppressed) return;
        if (CurrentProfile == null) return;
        _ = SaveProfile();
    }

    public async Task RefreshListAsync(long? selectId = null)
    {
        var profiles = await _profileService.ListProfilesAsync(CurrentGameId?.Invoke());

        using (SuppressAutoSave())
        {
            var keep = selectId ?? CurrentProfile?.Id;

            AvailableProfiles.Clear();
            foreach (var p in profiles) AvailableProfiles.Add(p);

            // Re-selected by id: the summaries are fresh records, so the old instance is not in the
            // list any more and leaving it selected would show a name that no longer exists.
            CurrentProfile     = AvailableProfiles.FirstOrDefault(p => p.Id == keep);
            ProfileDisplayName = CurrentProfile?.Name;
            _previousProfile   = CurrentProfile;
        }
    }

    /// <summary>Re-reads the profile that is open, discarding what is in the UI for it.</summary>
    public Task ReloadCurrentAsync()
        => CurrentProfile is { } p ? LoadProfileAsync(p.Id) : Task.CompletedTask;

    /// <summary>
    /// Opens the profiles of a game the user just switched to: the one asked for, else the first,
    /// and a fresh one when the game has none — a game with no profile at all is not something the
    /// user can do anything with.
    ///
    /// With no game selected, nothing is created. That is a fresh installation, where the answer is
    /// "add a game", not a profile parked over no folder that the user then has to notice and
    /// delete.
    /// </summary>
    public async Task OpenGameProfilesAsync(long? preferredProfileId)
    {
        await RefreshListAsync(preferredProfileId);

        if (CurrentProfile is { } profile) await LoadProfileAsync(profile.Id);
        else if (AvailableProfiles.Count > 0) await LoadProfileAsync(AvailableProfiles[0].Id);
        else if (CurrentGameId?.Invoke() != null) await NewProfile();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    public async Task LoadProfileAsync(long profileId)
    {
        await _lock.WaitAsync();
        try
        {
            var p = await _profileService.LoadProfileAsync(profileId);
            if (p == null)
            {
                _logService.Log($"LOAD ERROR: Profile not found: {profileId}");
                return;
            }

            using (SuppressAutoSave())
            {
                var summary = AvailableProfiles.FirstOrDefault(s => s.Id == profileId)
                              ?? new ProfileSummary(profileId, p.Name);
                CurrentProfile     = summary;
                ProfileDisplayName = p.Name;
                _previousProfile   = summary;
                _configService.Current.LastProfileId = profileId;
                _configService.Save();
            }

            ProfileLoaded?.Invoke(p);
            _logService.Log($"Profile '{p.Name}' loaded.");
        }
        catch (Exception ex) { _logService.Log($"LOAD ERROR: {ex.Message}"); }
        finally { _lock.Release(); }
    }

    private async Task SaveProfileInternalAsync()
    {
        if (CurrentProfile is not { } current) return;
        try
        {
            var profile = BuildSaveData?.Invoke() ?? new Profile { Name = current.Name };
            profile.Id     = current.Id;
            profile.Name   = current.Name;
            profile.GameId = CurrentGameId?.Invoke();
            await _profileService.SaveProfileAsync(profile);

            using (SuppressAutoSave())
            {
                _configService.Current.LastProfileId = current.Id;
                _configService.Save();
            }
        }
        catch (Exception ex) { _logService.Log($"SAVE ERROR: {ex.Message}"); }
    }

    private string GetUniqueProfileName(string baseName)
    {
        string name = baseName;
        int counter = 1;
        while (AvailableProfiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {counter++}";
        return name;
    }
}
