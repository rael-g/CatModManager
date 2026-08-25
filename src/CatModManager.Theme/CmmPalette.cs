using Avalonia.Media;

namespace CatModManager.Theme;

/// <summary>
/// The single source of truth for CMM's colours.
///
/// App.axaml binds its resource keys to these fields with <c>x:Static</c>, and code that builds
/// controls by hand uses the <see cref="Brushes"/> below. Before this existed the app carried two
/// palettes that had drifted apart — App.axaml's named resources and ~77 hex literals scattered
/// through the code-behind — so changing the accent colour only repainted half the UI.
///
/// Lives in its own assembly rather than in the SDK because plugins draw their own controls and
/// must be able to match the host theme, while the SDK deliberately stays free of Avalonia.
/// </summary>
public static class CmmPalette
{
    // ── Surfaces, darkest to lightest ─────────────────────────────────────────

    /// <summary>Window background, and the recessed surface used for cards and headers.</summary>
    public static readonly Color AppBackground = Color.Parse("#1E1F22");

    /// <summary>Sidebar and secondary panels.</summary>
    public static readonly Color SidebarBg = Color.Parse("#2B2D31");

    /// <summary>Sidebar section headers.</summary>
    public static readonly Color SidebarHeaderBg = Color.Parse("#232428");

    /// <summary>Main content area.</summary>
    public static readonly Color ContentBg = Color.Parse("#313338");

    /// <summary>Raised surface: inputs, list rows, neutral buttons.</summary>
    public static readonly Color SurfaceBg = Color.Parse("#383A40");

    /// <summary>A raised surface that is selected or hovered.</summary>
    public static readonly Color SurfaceSelected = Color.Parse("#404249");

    // ── Borders ───────────────────────────────────────────────────────────────

    public static readonly Color Border = Color.Parse("#3F4147");

    /// <summary>Hairline divider, barely distinct from the background.</summary>
    public static readonly Color BorderSubtle = Color.Parse("#2E3035");

    // ── Text ──────────────────────────────────────────────────────────────────

    public static readonly Color TextPrimary = Color.Parse("#DBDEE1");

    /// <summary>Secondary text: still readable, clearly lower priority.</summary>
    public static readonly Color TextMuted = Color.Parse("#B5BAC1");

    /// <summary>Tertiary text: hints, timestamps, disabled labels.</summary>
    public static readonly Color TextSubtle = Color.Parse("#80848E");

    // ── Accent ────────────────────────────────────────────────────────────────

    public static readonly Color Accent = Color.Parse("#4E7FD5");
    public static readonly Color AccentHover = Color.Parse("#6895E0");

    /// <summary>Desaturated accent for the background of a highlighted-but-inactive row.</summary>
    public static readonly Color AccentMuted = Color.Parse("#3D4F6B");

    /// <summary>Translucent accent wash, for tinting a surface without hiding it.</summary>
    public static readonly Color AccentTint = Color.Parse("#1F4E7FD5");

    // ── Status ────────────────────────────────────────────────────────────────

    public static readonly Color StatusActive = Color.Parse("#3BA55D");
    public static readonly Color StatusDanger = Color.Parse("#ED4245");
    public static readonly Color StatusWarning = Color.Parse("#FAA61A");

    /// <summary>Dark wash behind a warning banner, so the warning border reads as the accent.</summary>
    public static readonly Color StatusWarningTint = Color.Parse("#2A2200");

    // ── Store brand colours ───────────────────────────────────────────────────
    //
    // Not theme colours: these identify a storefront on a badge and must stay recognisable
    // even if the rest of the palette is retuned.

    public static readonly Color StoreSteam = Color.Parse("#1B5E8A");
    public static readonly Color StoreGog = Color.Parse("#9B59B6");
    public static readonly Color StoreEpic = Color.Parse("#2563EB");

    /// <summary>
    /// Ready-made brushes for code that builds controls directly. Frozen and shared — building a
    /// new SolidColorBrush per control was both wasteful and how the literals spread.
    /// </summary>
    public static class Brushes
    {
        public static readonly IBrush AppBackground = New(CmmPalette.AppBackground);
        public static readonly IBrush SidebarBg = New(CmmPalette.SidebarBg);
        public static readonly IBrush SidebarHeaderBg = New(CmmPalette.SidebarHeaderBg);
        public static readonly IBrush ContentBg = New(CmmPalette.ContentBg);
        public static readonly IBrush SurfaceBg = New(CmmPalette.SurfaceBg);
        public static readonly IBrush SurfaceSelected = New(CmmPalette.SurfaceSelected);

        public static readonly IBrush Border = New(CmmPalette.Border);
        public static readonly IBrush BorderSubtle = New(CmmPalette.BorderSubtle);

        public static readonly IBrush TextPrimary = New(CmmPalette.TextPrimary);
        public static readonly IBrush TextMuted = New(CmmPalette.TextMuted);
        public static readonly IBrush TextSubtle = New(CmmPalette.TextSubtle);

        public static readonly IBrush Accent = New(CmmPalette.Accent);
        public static readonly IBrush AccentHover = New(CmmPalette.AccentHover);
        public static readonly IBrush AccentMuted = New(CmmPalette.AccentMuted);

        public static readonly IBrush StatusActive = New(CmmPalette.StatusActive);
        public static readonly IBrush StatusDanger = New(CmmPalette.StatusDanger);
        public static readonly IBrush StatusWarning = New(CmmPalette.StatusWarning);
        public static readonly IBrush StatusWarningTint = New(CmmPalette.StatusWarningTint);

        public static readonly IBrush StoreSteam = New(CmmPalette.StoreSteam);
        public static readonly IBrush StoreGog = New(CmmPalette.StoreGog);
        public static readonly IBrush StoreEpic = New(CmmPalette.StoreEpic);

        // Immutable so a shared brush can be handed to controls on any thread without one of them
        // being able to mutate the colour for everyone else.
        private static IBrush New(Color color) => new SolidColorBrush(color).ToImmutable();
    }
}
