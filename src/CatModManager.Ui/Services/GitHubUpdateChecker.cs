using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CatModManager.Core.Services;

namespace CatModManager.Ui.Services;

public static class GitHubUpdateChecker
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    static GitHubUpdateChecker()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "CatModManager");
        _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    /// <summary>
    /// Fire-and-forget update check. Logs silently on any failure.
    /// If a newer version is found, logs a visible notice and returns the tag.
    /// </summary>
    public static void CheckInBackground(string owner, string repo, ILogService log)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                var release = await _http.GetFromJsonAsync<GhRelease>(url);

                if (release?.TagName is not { Length: > 0 } tag)
                {
                    log.Log("[Update] No releases found on GitHub.");
                    return;
                }

                // Strip leading 'v' before parsing
                var remoteStr = tag.TrimStart('v', 'V');
                if (!Version.TryParse(remoteStr, out var remote)) return;

                var local = GetLocalVersion();
                if (remote > local)
                    log.Log($"[Update] New version available: {tag} (current: {local}) — {release.HtmlUrl}");
                else
                    log.Log($"[Update] Up to date ({local}).");
            }
            catch (Exception ex)
            {
                // Fail silently — only the internal log, never the status bar
                log.Log($"[Update] Check failed: {ex.Message}");
            }
        });
    }

    private static Version GetLocalVersion()
    {
        var v = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version;
        return v ?? new Version(0, 0, 0);
    }

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")]  public string? TagName  { get; init; }
        [JsonPropertyName("html_url")]  public string? HtmlUrl  { get; init; }
        [JsonPropertyName("draft")]     public bool    Draft    { get; init; }
        [JsonPropertyName("prerelease")]public bool    Prerelease{ get; init; }
    }
}
