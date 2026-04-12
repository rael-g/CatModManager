using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using Nett;

namespace CatModManager.Core.Services;

public class TomlProfileService : IProfileService
{
    public async Task SaveProfileAsync(Profile profile, string filePath)
    {
        var toml = Toml.WriteString(profile);
        await File.WriteAllTextAsync(filePath, toml);
    }

    public async Task<Profile?> LoadProfileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var toml = await File.ReadAllTextAsync(filePath);
            
            // WORKAROUND: Legacy or "poisoned" TOML files may contain 'CancelInstallCommand'
            // which causes a crash because Nett cannot map a table to an interface.
            // We strip these lines manually before parsing to ensure stability.
            if (toml.Contains("CancelInstallCommand"))
            {
                var lines = toml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                toml = string.Join(Environment.NewLine, lines.Where(l => !l.Trim().StartsWith("CancelInstallCommand")));
            }

            var profile = Toml.ReadString<Profile>(toml);
            
            if (profile != null && profile.Mods == null)
                profile.Mods = new List<Mod>();
                
            return profile;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TomlProfileService] LoadProfileAsync failed for '{filePath}': {ex.Message}");
            // Return a minimal profile to allow the app to start instead of crashing
            return new Profile { Name = Path.GetFileNameWithoutExtension(filePath) };
        }
    }

    public Task<IEnumerable<string>> ListProfilesAsync(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath)) return Task.FromResult(Enumerable.Empty<string>());
            
            var files = Directory.GetFiles(directoryPath, "*.toml")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n)
                .ToList();
                
            return Task.FromResult(files.AsEnumerable()!);
        }
        catch
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }
    }
}
