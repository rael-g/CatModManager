using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;

namespace CatModManager.Ui.ViewModels;

public partial class ModListViewModel : ViewModelBase
{
    // Callbacks wired by MainWindowViewModel
    public Action?           AutoSave         { get; set; }
    public Func<IDisposable>? SuppressAutoSave { get; set; }
    public Action?           SyncActiveMods   { get; set; }

    /// <summary>Raised when SelectedMod changes. Subscribed by ModInspectorViewModel and MainWindowViewModel.</summary>
    public event Action<Mod?>? SelectedModChanged;

    [ObservableProperty] private ObservableCollection<Mod> _allMods = new();
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private Mod? _selectedMod;

    public ObservableCollection<string> Categories    { get; } = new() { "All", "Uncategorized" };
    public ObservableCollection<Mod>    DisplayedMods { get; } = new();
    public System.Collections.Generic.List<Mod> SelectedMods { get; set; } = new();

    private bool _isRebuilding;

    public ModListViewModel()
    {
        AllMods.CollectionChanged += OnAllModsChanged;
    }

    // ── Property changed handlers ─────────────────────────────────────────────

    partial void OnSearchTextChanged(string? value)    => RebuildDisplayedMods();
    partial void OnSelectedCategoryChanged(string value) => RebuildDisplayedMods();

    partial void OnSelectedModChanged(Mod? value)
    {
        if (_isRebuilding) return;
        SelectedModChanged?.Invoke(value);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void RebuildDisplayedMods()
    {
        var savedMod = SelectedMod;
        _isRebuilding = true;
        try
        {
            DisplayedMods.Clear();
            var query = AllMods.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(m => m.Name.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase));
            if (SelectedCategory != "All")
                query = query.Where(m => m.Category == SelectedCategory);
            foreach (var mod in query.OrderByDescending(m => m.Priority))
                DisplayedMods.Add(mod);
        }
        finally { _isRebuilding = false; }

        if (savedMod != null && DisplayedMods.Contains(savedMod))
            SelectedMod = savedMod;
        OnPropertyChanged(nameof(DisplayedMods));
    }

    public void UpdateCategories()
    {
        foreach (var cat in AllMods.Select(m => m.Category).Distinct())
            if (!Categories.Contains(cat)) Categories.Add(cat);
    }

    public void UpdatePriorities()
    {
        for (int i = 0; i < AllMods.Count; i++)
            AllMods[i].Priority = AllMods.Count - 1 - i;
    }

    public void MoveMod(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= AllMods.Count || newIndex < 0 || newIndex >= AllMods.Count) return;
        AllMods.Move(oldIndex, newIndex);
        using (SuppressAutoSave?.Invoke() ?? NullDisposable.Instance) UpdatePriorities();
        RebuildDisplayedMods();
        AutoSave?.Invoke();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedMod == null) return;
        int index = AllMods.IndexOf(SelectedMod);
        if (index <= 0) return;
        AllMods.Move(index, index - 1);
        using (SuppressAutoSave?.Invoke() ?? NullDisposable.Instance) UpdatePriorities();
        RebuildDisplayedMods();
        AutoSave?.Invoke();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedMod == null) return;
        int index = AllMods.IndexOf(SelectedMod);
        if (index >= AllMods.Count - 1) return;
        AllMods.Move(index, index + 1);
        using (SuppressAutoSave?.Invoke() ?? NullDisposable.Instance) UpdatePriorities();
        RebuildDisplayedMods();
        AutoSave?.Invoke();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnAllModsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (Mod mod in e.NewItems) mod.PropertyChanged += OnModPropertyChanged;
        if (e.OldItems != null) foreach (Mod mod in e.OldItems) mod.PropertyChanged -= OnModPropertyChanged;
        SyncActiveMods?.Invoke();
        AutoSave?.Invoke();
        RebuildDisplayedMods();
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Mod.IsEnabled) or nameof(Mod.Priority) or nameof(Mod.Name) or nameof(Mod.Version) or nameof(Mod.Category))
        {
            if (e.PropertyName == nameof(Mod.IsEnabled)) SyncActiveMods?.Invoke();
            AutoSave?.Invoke();
            if (e.PropertyName == nameof(Mod.Category)) UpdateCategories();
            RebuildDisplayedMods();
        }
    }

    // Minimal IDisposable to satisfy 'using' when no suppressor is wired in tests.
    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
