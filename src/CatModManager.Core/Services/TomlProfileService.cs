using System;
using System.Collections.Generic;
using System.IO;
using CatModManager.PluginSdk;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using Nett;

namespace CatModManager.Core.Services;

public class TomlProfileService : IProfileService
{
    private readonly IFileService _fileService;

    public TomlProfileService(IFileService fileService)
    {
        _fileService = fileService;
    }

    public async Task SaveProfileAsync(Profile profile, string filePath)
    {
        var toml = Toml.WriteString(profile);
        // Using Task.Run for FileService interaction as IFileService isn't fully async yet
        await Task.Run(() =>
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !_fileService.DirectoryExists(dir))
                _fileService.CreateDirectory(dir);

            // Write to a temp file and swap it in, so a crash/kill mid-write can't
            // leave the profile truncated or blank.
            var tempPath = filePath + ".tmp";
            _fileService.WriteAllText(tempPath, toml);
            _fileService.CopyFile(tempPath, filePath, overwrite: true);
            _fileService.DeleteFile(tempPath);
        });
    }

    public async Task<Profile?> LoadProfileAsync(string filePath)
    {
        try
        {
            if (!_fileService.FileExists(filePath)) return null;

            var toml = await Task.Run(() => _fileService.ReadAllText(filePath));
            
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
            return new Profile { Name = Path.GetFileNameWithoutExtension(filePath) };
        }
    }

    public Task<IEnumerable<string>> ListProfilesAsync(string directoryPath)
    {
        try
        {
            if (!_fileService.DirectoryExists(directoryPath)) return Task.FromResult(Enumerable.Empty<string>());
            
            var files = _fileService.GetFiles(directoryPath, "*.toml")
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
