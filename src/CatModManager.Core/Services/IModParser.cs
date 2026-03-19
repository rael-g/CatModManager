using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

public interface IModParser
{
    Mod? ParseModInfo(string path);
}
