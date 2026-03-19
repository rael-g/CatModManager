using System.Collections.Generic;

namespace CatModManager.Core.Services;

public interface IVfsStateService
{
    void RegisterMount(string originalPath, string backupPath);
    void UnregisterMount(string originalPath);
    void RecoverStaleMounts();
}
