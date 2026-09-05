using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CmmPlugin.SaveManager.Services;
using CmmPlugin.SaveManager.Tabs;
using Xunit;

namespace CatModManager.Tests.Plugins.SaveManager;

/// <summary>
/// Deleting a save crashed the app. Removing the row made the virtualizing panel recycle its
/// container, clearing a container runs the item template again with nothing in it, and the template
/// built a row for that null. The file had already been deleted by then, so the crash arrived after
/// a delete that had actually worked.
/// </summary>
public class SaveListRowTemplateTests
{
    [AvaloniaFact]
    public void NoItemMeansNoRow()
    {
        var template = SaveManagerTabControl.RowTemplate(
            _ => throw new InvalidOperationException("a row was built for nothing"));

        // What Avalonia does to a container it is recycling.
        var built = template.Build(null);

        Assert.NotNull(built);
    }

    [AvaloniaFact]
    public void AnItemStillGetsItsRow()
    {
        var marker   = new TextBlock { Text = "row" };
        var template = SaveManagerTabControl.RowTemplate(_ => marker);

        var built = template.Build(new SaveSlot { Label = "before launch" });

        Assert.Same(marker, built);
    }
}
