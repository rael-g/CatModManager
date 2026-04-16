using System.Collections.Generic;
using System.IO;

namespace CatModManager.Core.Services;

public class PhysicalFileService : IFileService
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void CopyFile(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
    
    public void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dirPath.Replace(source, destination));

        foreach (string newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            File.Copy(newPath, newPath.Replace(source, destination), true);
    }

    public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }
    public void DeleteDirectory(string path, bool recursive) { if (Directory.Exists(path)) Directory.Delete(path, recursive); }
    public void MoveDirectory(string fromPath, string targetPath) 
    {
        if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
        Directory.Move(fromPath, targetPath);
    }

    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
    
    public string[] GetFiles(string path, string searchPattern, bool recursive = false) 
        => Directory.GetFiles(path, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
}
