using System.Collections.Generic;

namespace CatModManager.Core.Services;

public interface IGameSupportService
{
    IGameSupport Default { get; }
    void RefreshSupports();
    IEnumerable<IGameSupport> GetAllSupports();
    IGameSupport GetSupportById(string? id);
    IGameSupport DetectSupport(string? gameExecutablePath);
}
