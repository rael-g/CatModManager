using System.Collections.Generic;

namespace CatModManager.PluginSdk;

public interface IFileService
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void CopyFile(string source, string destination, bool overwrite);
    void CopyDirectory(string source, string destination);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    void MoveDirectory(string fromPath, string targetPath);
    
    // New methods for scanners and profile persistence
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    string[] ReadAllLines(string path);
    void WriteAllLines(string path, string[] contents);
    string[] GetFiles(string path, string searchPattern, bool recursive = false);
}
