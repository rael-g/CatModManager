using System.Threading.Tasks;

namespace CatModManager.Core.Services;

public interface IProcessService
{
    Task<bool> StartProcessAsync(string fileName, string arguments, bool runAsAdmin = false, bool waitForChildren = true);
    Task OpenFolderAsync(string folderPath);
}
