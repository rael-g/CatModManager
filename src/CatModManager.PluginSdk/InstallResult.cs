using System.Collections.Generic;

namespace CatModManager.PluginSdk;

public class InstallResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Files to include, keyed by virtual path relative to the mod root.</summary>
    public Dictionary<string, string> FileMapping { get; init; } = new();

    public static InstallResult Success(Dictionary<string, string> fileMapping) =>
        new() { IsSuccess = true, FileMapping = fileMapping };

    public static InstallResult Failure(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };
}
