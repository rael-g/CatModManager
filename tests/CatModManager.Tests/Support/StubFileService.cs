using System;
using CatModManager.PluginSdk;

namespace CatModManager.Tests.Support;

/// <summary>
/// Base for the hand-written IFileService fakes used across the test suite. Every member is a
/// harmless no-op by default so each test only overrides the handful of operations it actually
/// exercises. Previously four separate test classes each spelled out the full interface, which meant
/// adding a single member to IFileService broke all four for no reason.
/// </summary>
public abstract class StubFileService : IFileService
{
    public virtual bool FileExists(string path) => false;
    public virtual bool DirectoryExists(string path) => false;
    public virtual void CreateDirectory(string path) { }
    public virtual void CopyFile(string source, string destination, bool overwrite) { }
    public virtual void CopyDirectory(string source, string destination) { }
    public virtual void DeleteFile(string path) { }
    public virtual void DeleteDirectory(string path, bool recursive) { }
    public virtual void MoveDirectory(string fromPath, string targetPath) { }
    public virtual string ReadAllText(string path) => "";
    public virtual void WriteAllText(string path, string contents) { }
    public virtual string[] ReadAllLines(string path) => Array.Empty<string>();
    public virtual void WriteAllLines(string path, string[] contents) { }
    public virtual string[] GetFiles(string path, string searchPattern, bool recursive = false) => Array.Empty<string>();
    public virtual string[] GetDirectories(string path) => Array.Empty<string>();
}
