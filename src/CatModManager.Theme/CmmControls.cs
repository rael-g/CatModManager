using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CatModManager.Theme;

/// <summary>How a button reads: what it does, not what it looks like.</summary>
public enum CmmButtonKind
{
    /// <summary>The one thing this panel is for. Filled with the accent colour.</summary>
    Primary,

    /// <summary>An ordinary action. Outlined, no fill.</summary>
    Outline,

    /// <summary>Destructive and hard to undo. Reads red before it is read at all.</summary>
    Danger,

    /// <summary>Present but not competing for attention — "cancel", "not now".</summary>
    Subtle,
}

/// <summary>
/// The controls the app and its plugins both build by hand.
///
/// It lives beside the palette rather than in the UI project because the plugins are where the
/// duplication was: <c>MakeButton</c> existed six times across three of them, each picking its own
/// padding, corner radius and cursor, and four plugins could not even reach the palette — so they
/// reached for <c>Brushes.Gray</c> and drifted away from the theme one control at a time.
///
/// XAML is not an option here on purpose: plugin controls are built imperatively so the host never
/// has to load XAML out of an external assembly. This is the shared vocabulary that gives them the
/// same result anyway.
/// </summary>
public static class CmmControls
{
    /// <summary>A button that matches the rest of the application.</summary>
    public static Button Button(string label, Action onClick, CmmButtonKind kind = CmmButtonKind.Outline)
    {
        var button = Styled(new Button { Content = label }, kind);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A button whose action is asynchronous. Disabled while it runs, so it cannot re-enter.</summary>
    public static Button Button(string label, Func<Task> onClick, CmmButtonKind kind = CmmButtonKind.Outline)
    {
        var button = Styled(new Button { Content = label }, kind);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await onClick(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    /// <summary>
    /// How long a freshly armed confirmation refuses to accept the second click.
    ///
    /// Without it a double click was a complete bypass: the first click armed the button and the
    /// second — arriving from the same gesture, before the user had read the new label — carried out
    /// the deletion. The confirmation was there in the code and absent in practice, which is the
    /// worst of the three possible states.
    ///
    /// Long enough to outlast a double click, short enough that nobody deliberately confirming ever
    /// notices it.
    /// </summary>
    private static readonly TimeSpan ArmingGuard = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// A destructive action that asks in place: the first click turns the button into a question,
    /// the second carries it out.
    ///
    /// This exists instead of a modal because a control living inside a tab has no window of its own
    /// to parent one to. Use it for actions whose consequence fits in a label; when the consequence
    /// needs a sentence to explain — deleting a game along with its profiles — a real dialog is the
    /// honest choice.
    ///
    /// It disarms when the pointer leaves, so a button left armed and forgotten cannot be triggered
    /// later by a click meant for something else.
    /// </summary>
    /// <param name="guard">
    /// Overrides <see cref="ArmingGuard"/>. Only tests pass this, so that the case of a genuine
    /// second click does not have to spend real time waiting — the same reason
    /// <c>ProcessService</c> takes its watch window as an argument.
    /// </param>
    public static Button ConfirmButton(string label, string confirmLabel, Func<Task> action,
                                       CmmButtonKind kind = CmmButtonKind.Outline,
                                       TimeSpan? guard = null)
    {
        var window = guard ?? ArmingGuard;
        var button = Styled(new Button { Content = label }, kind);

        bool     armed   = false;
        DateTime armedAt = DateTime.MinValue;

        // Read when the button first arms rather than here, so a caller that recolours the button
        // after building it gets that colour back on disarm — the delete button in the save list is
        // handed danger red, and capturing at construction would quietly repaint it on first use.
        IBrush? resting = null;

        void Disarm()
        {
            armed             = false;
            button.Content    = label;
            button.Foreground = resting;
        }

        button.Click += async (_, _) =>
        {
            if (!armed)
            {
                armed             = true;
                armedAt           = DateTime.UtcNow;
                resting         ??= button.Foreground;
                button.Content    = confirmLabel;
                button.Foreground = CmmPalette.Brushes.StatusDanger;
                return;
            }

            // The second half of a double click, not an answer to the question.
            if (DateTime.UtcNow - armedAt < window) return;

            Disarm();
            await action();
        };

        button.PointerExited += (_, _) => { if (armed) Disarm(); };
        return button;
    }

    /// <inheritdoc cref="ConfirmButton(string, string, Func{Task}, CmmButtonKind)"/>
    public static Button ConfirmButton(string label, string confirmLabel, Action action,
                                       CmmButtonKind kind = CmmButtonKind.Outline,
                                       TimeSpan? guard = null)
        => ConfirmButton(label, confirmLabel, () => { action(); return Task.CompletedTask; }, kind, guard);

    /// <summary>Secondary text: hints, timestamps, the line under a heading.</summary>
    public static TextBlock Muted(string text, double fontSize = 11) => new()
    {
        Text       = text,
        FontSize   = fontSize,
        Foreground = CmmPalette.Brushes.TextSubtle,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// What a panel shows when it has nothing to show. Centred and muted, so an empty list reads as
    /// empty rather than broken.
    /// </summary>
    public static TextBlock EmptyState(string message) => new()
    {
        Text                = message,
        FontSize            = 12,
        Foreground          = CmmPalette.Brushes.TextSubtle,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment   = VerticalAlignment.Center,
        TextWrapping        = TextWrapping.Wrap,
        Margin              = new Thickness(24),
    };

    private static Button Styled(Button button, CmmButtonKind kind)
    {
        button.Padding         = new Thickness(10, 5);
        button.FontSize        = 11;
        button.CornerRadius    = new CornerRadius(4);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Cursor          = new Cursor(StandardCursorType.Hand);
        button.BorderThickness = new Thickness(kind == CmmButtonKind.Outline ? 1 : 0);

        switch (kind)
        {
            case CmmButtonKind.Primary:
                button.Background = CmmPalette.Brushes.Accent;
                button.Foreground = CmmPalette.Brushes.TextOnAccent;
                break;

            case CmmButtonKind.Danger:
                button.Background = CmmPalette.Brushes.StatusDangerStrong;
                button.Foreground = CmmPalette.Brushes.TextOnAccent;
                break;

            case CmmButtonKind.Subtle:
                button.Background = CmmPalette.Brushes.SurfaceSelected;
                button.Foreground = CmmPalette.Brushes.TextPrimary;
                break;

            default:
                button.Background  = Brushes.Transparent;
                button.BorderBrush = CmmPalette.Brushes.Border;
                button.Foreground  = CmmPalette.Brushes.TextPrimary;
                break;
        }

        return button;
    }
}
