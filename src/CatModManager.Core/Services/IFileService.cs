using System.Threading.Tasks;

namespace CatModManager.Core.Services;

public interface IFileService
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void CopyFile(string source, string destination, bool overwrite);
    void CopyDirectory(string source, string destination);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
}
