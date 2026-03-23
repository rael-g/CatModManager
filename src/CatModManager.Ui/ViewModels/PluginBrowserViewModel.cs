using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatModManager.Ui.Models;
using CatModManager.Ui.Services;
using CatModManager.Ui.Plugins;

namespace CatModManager.Ui.ViewModels;

public partial class PluginBrowserViewModel : ObservableObject
{
    private readonly NuGetPluginService _nuget;
    private PluginLoader?       _pluginLoader;

    private int _currentSkip;
    private int _totalHits;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty] private string _searchText    = string.Empty;
    [ObservableProperty] private string _selectedSort  = "Relevance";
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = "Search for plugins or browse all CMM plugins below.";
    [ObservableProperty] private bool   _hasMore;

    public ObservableCollection<NuGetPackageEntry>   SearchResults   { get; } = new();
    public ObservableCollection<InstalledPluginItem> InstalledPlugins { get; } = new();

    public string[] SortOptions { get; } = ["Relevance", "Most Downloaded", "Newest"];

    partial void OnSearchTextChanged(string value)  => _ = RunSearchAsync(reset: true);
    partial void OnSelectedSortChanged(string value) => _ = RunSearchAsync(reset: true);

    public PluginBrowserViewModel(NuGetPluginService nuget)
    {
        _nuget = nuget;
    }

    /// <summary>Set after construction to break the circular DI chain (PluginLoader → PluginBrowserViewModel → PluginLoader).</summary>
    public void SetPluginLoader(PluginLoader loader) => _pluginLoader = loader;

    public void Initialize()
    {
        _ = RunSearchAsync(reset: true);
        RefreshInstalledPlugins();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task Search() => RunSearchAsync(reset: true);

    [RelayCommand]
    private Task LoadMore() => RunSearchAsync(reset: false);

    private async Task RunSearchAsync(bool reset)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (reset)
        {
            _currentSkip = 0;
            SearchResults.Clear();
            HasMore = false;
        }

        IsLoading = true;
        StatusMessage = "Searching…";

        try
        {
            string? sortBy = SelectedSort switch
            {
                "Most Downloaded" => "totalDownloads",
                "Newest"          => "lastEdited",
                _                 => null
            };

            var (results, total) = await _nuget.SearchAsync(
                SearchText, sortBy, take: 20, skip: _currentSkip, ct);

            if (ct.IsCancellationRequested) return;

            _totalHits    = total;
            _currentSkip += results.Count;

            foreach (var r in results)
                SearchResults.Add(r);

            HasMore = SearchResults.Count < _totalHits;
            StatusMessage = SearchResults.Count == 0
                ? "No plugins found."
                : $"{_totalHits} plugin{(_totalHits == 1 ? "" : "s")} found — showing {SearchResults.Count}";
        }
        catch (OperationCanceledException) { /* new search started */ }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Action(NuGetPackageEntry? entry)
    {
        if (entry == null) return;

        IsLoading = true;
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            if (entry.HasUpdate)
                await _nuget.UpdateAsync(entry, progress);
            else if (entry.IsInstalled)
                _nuget.Uninstall(entry);
            else
                await _nuget.InstallAsync(entry, progress);

            OnPropertyChanged(nameof(SearchResults));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Operation failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Installed tab ─────────────────────────────────────────────────────────

    [RelayCommand]
    public void RefreshInstalledPlugins()
    {
        InstalledPlugins.Clear();

        var manifest = _nuget.LoadManifest();
        var nugetIds = manifest.Installed
            .ToDictionary(p => p.PackageId, p => p.Version, StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in _pluginLoader?.LoadedPlugins ?? [])
        {
            string assemblyName = plugin.GetType().Assembly.GetName().Name ?? plugin.Id;
            bool isNuGet = nugetIds.ContainsKey(assemblyName);

            InstalledPlugins.Add(new InstalledPluginItem
            {
                DisplayName  = plugin.DisplayName,
                Version      = isNuGet ? nugetIds[assemblyName] : plugin.Version,
                Author       = plugin.Author,
                CanUninstall = isNuGet,
                PackageId    = isNuGet ? assemblyName : null
            });
        }
    }

    [RelayCommand]
    private void UninstallInstalled(InstalledPluginItem? item)
    {
        if (item?.PackageId == null) return;
        _nuget.UninstallById(item.PackageId);
        InstalledPlugins.Remove(item);
        StatusMessage = $"{item.DisplayName} uninstalled. Restart CMM to deactivate.";
    }
}
