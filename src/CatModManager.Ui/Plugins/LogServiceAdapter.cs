using System;
using CatModManager.Core.Services;
using CatModManager.PluginSdk;

namespace CatModManager.Ui.Plugins;

/// <summary>Wraps ILogService (Core) as IPluginLogger (SDK) so plugins have no Core dependency.</summary>
internal sealed class LogServiceAdapter : IPluginLogger
{
    private readonly ILogService _inner;
    public LogServiceAdapter(ILogService inner) => _inner = inner;

    public void Log(string message)                      => _inner.Log(message);
    public void LogError(string message, Exception? ex)  => _inner.LogError(message, ex);
}
