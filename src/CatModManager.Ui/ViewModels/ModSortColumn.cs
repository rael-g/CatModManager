namespace CatModManager.Ui.ViewModels;

/// <summary>
/// Which column the mod list is ordered by.
///
/// Priority is not just another column: it <em>is</em> the load order, and it is the only one under
/// which a row's position carries meaning that dragging can edit. The others are ways of finding a
/// mod, not of ranking one.
/// </summary>
public enum ModSortColumn
{
    Priority,
    Name,
    Category
}
