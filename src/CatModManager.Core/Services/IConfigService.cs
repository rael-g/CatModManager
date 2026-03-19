using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IConfigService
{
    AppConfig Current { get; }
    void Save();
    void Load();
}
