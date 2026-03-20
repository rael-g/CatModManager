using System;

namespace CatModManager.PluginSdk;

/// <summary>Logging interface provided to plugins — no dependency on CatModManager.Core.</summary>
public interface IPluginLogger
{
    void Log(string message);
    void LogError(string message, Exception? ex = null);
}
