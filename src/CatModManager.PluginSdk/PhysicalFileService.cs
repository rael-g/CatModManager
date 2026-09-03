using System;
using System.Collections.Generic;
using System.IO;

namespace CatModManager.PluginSdk;

public class PhysicalFileService : IFileService
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void CopyFile(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
    
    /// <summary>
    /// Copies a directory tree, rebasing each entry with path arithmetic rather than string
    /// arithmetic.
    ///
    /// This used to be <c>path.Replace(source, destination)</c>, which silently drops the separator
    /// whenever <c>source</c> carries a trailing one — and a folder picker always hands one over.
    /// Copying "/home/u/Downloads/FasterMining/" into ".../cmm/mods (4)" then wrote
    /// ".../cmm/mods (4)SFSE" as a *sibling* of the destination instead of a child, scattering the
    /// whole source tree across the mods root as prefixed junk.
    ///
    /// Substring replacement was wrong for a second reason too: a source name that reappears deeper
    /// in the tree ("Data/Data/x") would be rewritten there as well.
    /// </summary>
    public void CopyDirectory(string source, string destination)
    {
        string root = Path.GetFullPath(source);
        string target = Path.GetFullPath(destination);

        string Rebase(string path) => Path.Combine(target, Path.GetRelativePath(root, path));

        Directory.CreateDirectory(target);

        foreach (string dirPath in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Rebase(dirPath));

        foreach (string filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            File.Copy(filePath, Rebase(filePath), true);
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
    public string[] ReadAllLines(string path) => File.ReadAllLines(path);
    public void WriteAllLines(string path, string[] contents) => File.WriteAllLines(path, contents);
    
    public string[] GetFiles(string path, string searchPattern, bool recursive = false) 
        => Directory.GetFiles(path, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public string[] GetDirectories(string path)
        => Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
}
