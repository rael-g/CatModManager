using System;
using System.Collections.Generic;
using System.IO;
using CatModManager.PluginSdk;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using Nett;

namespace CatModManager.Core.Services;

/// <summary>
/// Reads and writes the old per-profile TOML files.
///
/// It no longer implements <see cref="IProfileService"/>: profiles live in cmm.db now, and this is
/// kept for one job only — <see cref="ProfileImporter"/> reading the files an existing installation
/// already has. It goes when the import does, along with <c>ICatPathService.GetProfilePath</c>.
///
/// The save path survives purely so the round-trip tests can still write a file to read back.
/// Nothing in the application calls it.
/// </summary>
public class TomlProfileService
{
    private readonly IFileService _fileService;

    public TomlProfileService(IFileService fileService)
    {
        _fileService = fileService;
    }

    public async Task SaveProfileAsync(LegacyTomlProfile profile, string filePath)
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

    public async Task<LegacyTomlProfile?> LoadProfileAsync(string filePath)
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

            var profile = Toml.ReadString<LegacyTomlProfile>(toml);
            
            if (profile != null && profile.Mods == null)
                profile.Mods = new List<Mod>();
                
            return profile;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TomlProfileService] LoadProfileAsync failed for '{filePath}': {ex.Message}");
            return new LegacyTomlProfile { Name = Path.GetFileNameWithoutExtension(filePath) };
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
