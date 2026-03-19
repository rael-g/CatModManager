using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IProfileService
{
    Task SaveProfileAsync(Profile profile, string filePath);
    Task<Profile?> LoadProfileAsync(string filePath);
    Task<IEnumerable<string>> ListProfilesAsync(string directoryPath);
}



