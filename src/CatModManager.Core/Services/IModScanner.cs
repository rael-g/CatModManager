using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IModScanner
{
    Task<IEnumerable<Mod>> ScanDirectoryAsync(string directoryPath);
}



