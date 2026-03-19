using System.IO;

namespace CatModManager.Core.Services;

public class PhysicalFileService : IFileService
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void CopyFile(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void CopyDirectory(string sourceDir, string destinationDir)
    {
        if (!Directory.Exists(sourceDir)) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        foreach (string dirPath in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
        }

        foreach (string newPath in Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourceDir, destinationDir), true);
        }
    }
}
