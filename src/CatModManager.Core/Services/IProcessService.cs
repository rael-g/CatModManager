using System.Threading.Tasks;

namespace CatModManager.Core.Services;

/// <summary>
/// What became of a start request.
///
/// <see cref="Started"/> and <see cref="GameObserved"/> are genuinely different answers, and
/// conflating them is what let a failed launch look like a successful one: starting Steam succeeds
/// even when Steam then refuses to run the game, so the only evidence the game actually ran is
/// having seen its process.
/// </summary>
/// <param name="Started">The process was created.</param>
/// <param name="GameObserved">
/// A process was seen running under the watched folder, and waited on until it exited. False when
/// nothing was watched for, and also when the watch gave up without ever seeing one.
/// </param>
/// <param name="Exited">
/// Completes when the started process does, or null when there is nothing to wait on. The call
/// itself no longer waits — a tool is handed over and the caller freed — so this is how a caller
/// that does care about the tool closing, in order to undo a mount it made for it, finds out
/// without blocking on it.
/// </param>
public readonly record struct ProcessRunResult(bool Started, bool GameObserved, Task? Exited = null)
{
    public static implicit operator bool(ProcessRunResult r) => r.Started;
}

public interface IProcessService
{
    /// <param name="watchFolder">
    /// Where the game's processes live, for <paramref name="waitForChildren"/>. Given explicitly
    /// because the thing being started is not always the game: launching through Steam starts
    /// Steam, and deriving the folder from that would watch Steam's own install directory.
    /// Falls back to the folder containing <paramref name="fileName"/> when null.
    /// </param>
    Task<ProcessRunResult> StartProcessAsync(string fileName, string arguments, bool runAsAdmin = false, bool waitForChildren = true, string? watchFolder = null);
    Task OpenFolderAsync(string folderPath);
}
