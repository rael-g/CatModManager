using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

/// <summary>A stored profile, as the lists that let the user pick one need it.</summary>
public record ProfileSummary(long Id, string Name);

/// <summary>
/// Stores profiles, by id.
///
/// It was by name until the game became something the user picks: with a game-first flow every game
/// wants a profile called "Default", so the name stopped being able to identify a profile on its
/// own. The id also makes a rename what it sounds like — one update to one row, instead of
/// repointing three child tables and hoping nothing observes the half-renamed state in between.
/// </summary>
public interface IProfileService
{
    /// <summary>Stores the profile, and returns its id — assigned here when it was zero.</summary>
    Task<long> SaveProfileAsync(Profile profile);

    /// <summary>The profile, or null when there is no such row.</summary>
    Task<Profile?> LoadProfileAsync(long profileId);

    /// <summary>
    /// The profiles of one game, ordered by name. <paramref name="gameId"/> null asks for the parked
    /// ones — profiles that never had a game folder set, which are still the user's and still have
    /// to be reachable.
    /// </summary>
    Task<IReadOnlyList<ProfileSummary>> ListProfilesAsync(long? gameId);

    /// <summary>Every profile in the database, whatever game it belongs to.</summary>
    Task<IReadOnlyList<ProfileSummary>> ListAllProfilesAsync();

    /// <summary>Removes the profile. An id that does not exist is not an error.</summary>
    Task DeleteProfileAsync(long profileId);

    /// <summary>Renames in place, leaving the id — and so every child row — alone.</summary>
    Task RenameProfileAsync(long profileId, string newName);

    /// <summary>
    /// Drops a mod from the game's inventory, and so from every profile of that game.
    ///
    /// Saving a profile cannot express this. That path prunes only what no other profile still
    /// refers to — correct for a mod the open profile merely stopped listing, and wrong for one the
    /// user uninstalled, because uninstalling deletes the folder from disk. The row used to survive
    /// on another profile's reference, pointing at nothing, and the next load handed it back to the
    /// profile it had just been deleted from.
    ///
    /// Removing the inventory row is enough: profile_entries cascades from it.
    /// </summary>
    Task UninstallModAsync(long gameId, string modRootPath);
}
