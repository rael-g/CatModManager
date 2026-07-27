namespace CmmPlugin.NexusMods;

/// <summary>
/// Thin static facade over the OS-specific <see cref="INxmProtocolHandler"/>, kept so
/// existing call sites don't need to thread a handler instance through the UI layer.
/// </summary>
public static class NxmProtocolService
{
    private static readonly INxmProtocolHandler Handler = NxmProtocolHandlerFactory.Create();

    public static bool IsRegistered() => Handler.IsRegistered();
    public static void Register(string exePath) => Handler.Register(exePath);
    public static void Unregister() => Handler.Unregister();
}
