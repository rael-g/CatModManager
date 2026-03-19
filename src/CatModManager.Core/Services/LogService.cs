using System;

namespace CatModManager.Core.Services;

public class LogService : ILogService
{
    public event Action<string>? OnLog;

    public void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formattedMessage = $"[{timestamp}] {message}";
        
        Console.WriteLine(formattedMessage);
        OnLog?.Invoke(formattedMessage);
    }

    public void LogError(string message, Exception? ex = null)
    {
        var errorMsg = $"ERROR: {message}";
        if (ex != null) errorMsg += $" | EX: {ex.Message}";
        Log(errorMsg);
    }
}
