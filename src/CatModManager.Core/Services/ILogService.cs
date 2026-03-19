using System;

namespace CatModManager.Core.Services;

public interface ILogService
{
    event Action<string>? OnLog;
    void Log(string message);
    void LogError(string message, Exception? ex = null);
}
