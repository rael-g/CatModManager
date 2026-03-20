namespace CmmPlugin.Capcom.Models;

/// <summary>Metadata for a known RE Engine game title.</summary>
public record ReEngineGame(
    string GameId,
    string DisplayName,
    string ExecutableName,
    bool   HasReFrameworkSupport);
