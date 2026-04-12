using System.Collections.Generic;

namespace CatModManager.PluginSdk;

public class InstallResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// File mapping: key = archive-relative source path, value = destination path relative to mod root
    /// ("" = install directly to mod root). Keyed by source to support multiple entries with the same destination.
    /// </summary>
    public Dictionary<string, string> FileMapping { get; init; } = new();

    public static InstallResult Success(Dictionary<string, string> fileMapping) =>
        new() { IsSuccess = true, FileMapping = fileMapping };

    public static InstallResult Failure(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };
}
