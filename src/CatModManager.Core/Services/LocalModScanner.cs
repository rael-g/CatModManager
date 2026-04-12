using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public class LocalModScanner : IModScanner
{
    private readonly IModParser _parser;
    private readonly IFileService _fileService;

    public LocalModScanner(IModParser parser, IFileService fileService)
    {
        _parser = parser;
        _fileService = fileService;
    }

    public async Task<IEnumerable<Mod>> ScanDirectoryAsync(string directoryPath)
    {
        if (!_fileService.DirectoryExists(directoryPath)) 
            return Enumerable.Empty<Mod>();

        var mods = new List<Mod>();
        
        try
        {
            await Task.Run(() =>
            {
                foreach (var dir in Directory.EnumerateDirectories(directoryPath))
                {
                    var mod = _parser.ParseModInfo(dir);
                    if (mod == null)
                    {
                        string name = Path.GetFileName(dir);
                        mod = new Mod(name, dir, mods.Count, false, "Uncategorized");
                    }
                    
                    // Check for incomplete installation marker
                    if (File.Exists(Path.Combine(dir, ".cmm_incomplete")))
                    {
                        mod.IsBroken = true;
                        mod.Name = "[INCOMPLETE] " + mod.Name;
                    }

                    mods.Add(mod);
                }

                foreach (var file in Directory.EnumerateFiles(directoryPath, "*.*")
                    .Where(f => f.EndsWith(".zip") || f.EndsWith(".7z")))
                {
                    var mod = _parser.ParseModInfo(file);
                    if (mod != null) mods.Add(mod);
                    else
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        mods.Add(new Mod(name, file, mods.Count, true, "Uncategorized"));
                    }
                }
            });
        }
        catch { }

        return mods;
    }
}
