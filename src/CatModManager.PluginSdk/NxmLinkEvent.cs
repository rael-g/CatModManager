namespace CatModManager.PluginSdk;

/// <summary>
/// Published via IEventBus when CMM receives an nxm:// protocol link —
/// either from a command-line argument (startup) or from a second instance via IPC pipe.
/// </summary>
public record NxmLinkEvent(string NxmUri);
