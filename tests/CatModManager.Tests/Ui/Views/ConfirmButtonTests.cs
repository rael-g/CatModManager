using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using CatModManager.Theme;
using Xunit;

namespace CatModManager.Tests.Ui.Views;

/// <summary>
/// The click-to-confirm button, which is the only thing standing between a stray click and a
/// deleted save.
///
/// The version this replaced could be defeated by a double click: the first click armed it, and the
/// second — part of the same gesture, sent before the new label had been read — carried the action
/// out. A confirmation that is present in the code and absent in practice is worse than none, since
/// it is what justified putting Delete in a scrolling list in the first place.
/// </summary>
public class ConfirmButtonTests
{
    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    [AvaloniaFact]
    public void OneClickOnlyAsksTheQuestion()
    {
        int ran = 0;
        var button = CmmControls.ConfirmButton("Delete", "Sure?", () => ran++);

        Click(button);

        Assert.Equal(0, ran);
        Assert.Equal("Sure?", button.Content);
    }

    [AvaloniaFact]
    public void TheSecondClickActsOnceTheQuestionHasBeenOnScreen()
    {
        int ran = 0;
        var button = CmmControls.ConfirmButton("Delete", "Sure?", () => ran++, guard: TimeSpan.Zero);

        Click(button);
        Click(button);

        Assert.Equal(1, ran);
        Assert.Equal("Delete", button.Content);
    }

    /// <summary>The defect, pinned: two clicks of one gesture must not answer their own question.</summary>
    [AvaloniaFact]
    public void ADoubleClickDoesNotConfirmItself()
    {
        int ran = 0;
        var button = CmmControls.ConfirmButton("Delete", "Sure?", () => ran++);

        Click(button);
        Click(button);   // same gesture, no time passed

        Assert.Equal(0, ran);
        Assert.Equal("Sure?", button.Content);
    }

    /// <summary>
    /// A button left armed and forgotten must not be triggered later by a click aimed at whatever
    /// has since scrolled into its place.
    /// </summary>
    [AvaloniaFact]
    public void MovingAwayCancelsTheQuestion()
    {
        int ran = 0;
        var button = CmmControls.ConfirmButton("Delete", "Sure?", () => ran++);

        Click(button);
        button.RaiseEvent(new Avalonia.Input.PointerEventArgs(
            Avalonia.Input.InputElement.PointerExitedEvent, button, null!, null, default, 0,
            new Avalonia.Input.PointerPointProperties(), Avalonia.Input.KeyModifiers.None));

        Assert.Equal("Delete", button.Content);
        Assert.Equal(0, ran);
    }

    /// <summary>
    /// The save list hands its delete button danger red after building it. Disarming has to give
    /// that back, not the kit's default — otherwise the button quietly loses its colour the first
    /// time anyone thinks about pressing it.
    /// </summary>
    [AvaloniaFact]
    public void ACustomColourSurvivesBeingArmedAndDisarmed()
    {
        var button = CmmControls.ConfirmButton("✕", "Sure?", () => { }, guard: TimeSpan.Zero);
        button.Foreground = CmmPalette.Brushes.StatusDanger;

        Click(button);
        Assert.Equal("Sure?", button.Content);

        Click(button);   // answered, so the button goes back to how it was handed over

        Assert.Equal(CmmPalette.Brushes.StatusDanger, button.Foreground);
    }
}
