namespace CatModManager.Core.Models;

public class AppConfig
{
    /// <summary>
    /// What to reopen on the next start. Zero means nothing has been opened yet — after which
    /// <see cref="LastProfileName"/> gets one last look, for an installation upgrading from the days
    /// when the name was all there was.
    /// </summary>
    public long LastGameId { get; set; }

    public long LastProfileId { get; set; }

    /// <summary>Read once when there is no id yet, and rewritten from then on. Kept for that.</summary>
    public string LastProfileName { get; set; } = string.Empty;

    public string Theme { get; set; } = "Dark";
}
