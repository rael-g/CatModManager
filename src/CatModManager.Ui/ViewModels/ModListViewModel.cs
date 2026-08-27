using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Core.Models;

namespace CatModManager.Ui.ViewModels;

public partial class ModListViewModel : ObservableObject
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

    /// <summary>
    /// Whether dragging rows re-orders the load order. Off by default: position <em>is</em> priority
    /// here, so dragging is an edit to the load order, not a view preference, and it should take a
    /// deliberate act to arm it.
    /// </summary>
    [ObservableProperty] private bool _isReorderEnabled;

    /// <summary>
    /// Hides disabled mods. A filter, not a sort — it changes which rows exist on screen, so the
    /// row you drag past may not be the row directly below in the real load order.
    /// </summary>
    [ObservableProperty] private bool _showOnlyEnabled;

    partial void OnShowOnlyEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnabledFilterIndicator));
        RebuildDisplayedMods();
    }

    /// <summary>Marks the ✓ header while the list is filtered, so hidden rows are never a surprise.</summary>
    public string EnabledFilterIndicator => ShowOnlyEnabled ? "•" : string.Empty;

    [RelayCommand]
    public void ToggleEnabledFilter() => ShowOnlyEnabled = !ShowOnlyEnabled;

    /// <summary>Which column the list is ordered by. Priority is the load order itself.</summary>
    [ObservableProperty] private ModSortColumn _sortColumn = ModSortColumn.Priority;

    /// <summary>
    /// Ascending by default, which for Priority puts the lowest first — so the winner of a file
    /// conflict sits at the bottom. That is the layering order MO2 uses and the same direction
    /// plugins load in; having files and plugins disagree on screen is a reliable way to make
    /// someone edit the wrong end of the list.
    /// </summary>
    [ObservableProperty] private bool _sortAscending = true;

    private (ModSortColumn Column, bool Ascending)? _sortBeforeReorder;

    partial void OnIsReorderEnabledChanged(bool value)
    {
        if (value)
        {
            // Dragging only means something while the screen shows the priority order itself.
            // Sorted by name, a drop position maps to no particular priority at all.
            _sortBeforeReorder = (SortColumn, SortAscending);
            SortColumn    = ModSortColumn.Priority;
            SortAscending = true;
        }
        else if (_sortBeforeReorder is { } previous)
        {
            SortColumn    = previous.Column;
            SortAscending = previous.Ascending;
            _sortBeforeReorder = null;
        }
    }

    partial void OnSortColumnChanged(ModSortColumn value)
    {
        NotifySortIndicators();
        RebuildDisplayedMods();
    }

    partial void OnSortAscendingChanged(bool value)
    {
        NotifySortIndicators();
        RebuildDisplayedMods();
    }

    private void NotifySortIndicators()
    {
        OnPropertyChanged(nameof(PrioritySortIndicator));
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(CategorySortIndicator));
    }

    /// <summary>
    /// Handles a click on a column header: the same column flips direction, a different one becomes
    /// the sort. Ignored while reordering, since that mode owns the ordering.
    /// </summary>
    [RelayCommand]
    public void SortBy(ModSortColumn column)
    {
        if (IsReorderEnabled) return;

        if (SortColumn == column) SortAscending = !SortAscending;
        else { SortColumn = column; SortAscending = true; }
    }

    public string PrioritySortIndicator => IndicatorFor(ModSortColumn.Priority);
    public string NameSortIndicator     => IndicatorFor(ModSortColumn.Name);
    public string CategorySortIndicator => IndicatorFor(ModSortColumn.Category);

    private string IndicatorFor(ModSortColumn column) =>
        SortColumn != column ? string.Empty : SortAscending ? "▲" : "▼";

    public ObservableCollection<string> Categories    { get; } = new() { "All", "Uncategorized" };
    public ObservableCollection<Mod>    DisplayedMods { get; } = new();
    public System.Collections.Generic.List<Mod> SelectedMods { get; set; } = new();

    private bool _isRebuilding;
    private int  _updateSuppressCount;

    public ModListViewModel()
    {
        AllMods.CollectionChanged += OnAllModsChanged;
    }

    public IDisposable SuppressUpdates()
    {
        _updateSuppressCount++;
        return new UpdateSuppressor(this);
    }

    private void EndSuppress()
    {
        _updateSuppressCount = Math.Max(0, _updateSuppressCount - 1);
        if (_updateSuppressCount == 0)
        {
            UpdatePriorities();
            RebuildDisplayedMods();
        }
    }

    // ── Property changed handlers ─────────────────────────────────────────────

    partial void OnSearchTextChanged(string? value)    => RebuildDisplayedMods();
    partial void OnSelectedCategoryChanged(string value) => RebuildDisplayedMods();

    partial void OnSelectedModChanged(Mod? value)
    {
        if (_isRebuilding || _updateSuppressCount > 0) return;
        SelectedModChanged?.Invoke(value);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void RebuildDisplayedMods()
    {
        if (_updateSuppressCount > 0) return;

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
            if (ShowOnlyEnabled)
                query = query.Where(m => m.IsEnabled);
            // Priority always breaks ties, so mods sharing a name or category keep a stable,
            // meaningful order instead of shuffling between rebuilds.
            IOrderedEnumerable<Mod> ordered = SortColumn switch
            {
                ModSortColumn.Name => SortAscending
                    ? query.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    : query.OrderByDescending(m => m.Name, StringComparer.OrdinalIgnoreCase),

                ModSortColumn.Category => SortAscending
                    ? query.OrderBy(m => m.Category, StringComparer.OrdinalIgnoreCase)
                    : query.OrderByDescending(m => m.Category, StringComparer.OrdinalIgnoreCase),

                _ => SortAscending
                    ? query.OrderBy(m => m.Priority)
                    : query.OrderByDescending(m => m.Priority),
            };

            if (SortColumn != ModSortColumn.Priority)
                ordered = ordered.ThenBy(m => m.Priority);

            foreach (var mod in ordered)
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
        if (_updateSuppressCount > 0) return;

        // Index 0 = Highest Priority (Count - 1)
        // Last Index = Lowest Priority (0)
        for (int i = 0; i < AllMods.Count; i++)
            AllMods[i].Priority = AllMods.Count - 1 - i;
    }

    /// <summary>The mod currently being dragged, so its row can show it. Null when nothing is.</summary>
    public Mod? DraggingMod { get; private set; }

    /// <summary>
    /// Starts a drag: rows will reorder live as the pointer moves, and the load order is written
    /// once at the end instead of on every step. Without this a single drag across ten rows would
    /// save the profile ten times.
    /// </summary>
    public void BeginDragReorder(Mod mod)
    {
        DraggingMod = mod;
        mod.IsDragging = true;
    }

    /// <summary>Moves a row mid-drag. Same reordering as MoveMod, minus the save.</summary>
    public void DragOver(Mod target)
    {
        if (DraggingMod is not { } dragged || ReferenceEquals(dragged, target)) return;

        int from = AllMods.IndexOf(dragged);
        int to   = AllMods.IndexOf(target);
        if (from < 0 || to < 0) return;

        // The Move has to be inside the suppression too, not just UpdatePriorities: any change to
        // AllMods triggers an AutoSave of its own. A drag across ten rows would otherwise rewrite
        // the profile ten times, and again for every priority it touched on the way.
        using (SuppressAutoSave?.Invoke() ?? NullDisposable.Instance)
        {
            AllMods.Move(from, to);
            UpdatePriorities();
        }

        RebuildDisplayedMods();
    }

    /// <summary>Ends the drag and persists the load order it produced.</summary>
    public void EndDragReorder()
    {
        if (DraggingMod is { } dragged) dragged.IsDragging = false;
        DraggingMod = null;
        AutoSave?.Invoke();
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

    /// <summary>
    /// Moves the selection <paramref name="displayOffset"/> rows as drawn on screen.
    ///
    /// Deliberately expressed in displayed positions rather than AllMods indices. AllMods is stored
    /// highest-priority-first, so under an ascending sort it is the reverse of what the user sees,
    /// and "up" in AllMods terms would visibly move the row *down*. Translating through the row the
    /// user is actually moving past keeps the two directions from ever disagreeing.
    /// </summary>
    private void MoveSelectionOnScreen(int displayOffset)
    {
        if (SelectedMod == null) return;

        int from = DisplayedMods.IndexOf(SelectedMod);
        int to   = from + displayOffset;
        if (from < 0 || to < 0 || to >= DisplayedMods.Count) return;

        MoveMod(AllMods.IndexOf(SelectedMod), AllMods.IndexOf(DisplayedMods[to]));
    }

    [RelayCommand] private void MoveUp()   => MoveSelectionOnScreen(-1);
    [RelayCommand] private void MoveDown() => MoveSelectionOnScreen(+1);

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnAllModsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) 
        {
            foreach (Mod mod in e.NewItems) mod.PropertyChanged += OnModPropertyChanged;
            if (_updateSuppressCount == 0) UpdatePriorities();
        }
        if (e.OldItems != null) foreach (Mod mod in e.OldItems) mod.PropertyChanged -= OnModPropertyChanged;
        
        SyncActiveMods?.Invoke();
        AutoSave?.Invoke();
        if (_updateSuppressCount == 0) RebuildDisplayedMods();
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Mod.IsEnabled) or nameof(Mod.Priority) or nameof(Mod.Name) or nameof(Mod.Version) or nameof(Mod.Category))
        {
            if (e.PropertyName == nameof(Mod.IsEnabled)) SyncActiveMods?.Invoke();
            AutoSave?.Invoke();
            if (e.PropertyName == nameof(Mod.Category)) UpdateCategories();
            
            if (_updateSuppressCount == 0) RebuildDisplayedMods();
        }
    }

    private class UpdateSuppressor : IDisposable
    {
        private readonly ModListViewModel _vm;
        public UpdateSuppressor(ModListViewModel vm) => _vm = vm;
        public void Dispose() => _vm.DisposeUpdateSuppressor();
    }

    private void DisposeUpdateSuppressor() => EndSuppress();

    // Minimal IDisposable to satisfy 'using' when no suppressor is wired in tests.
    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
