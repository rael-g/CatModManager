using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Support;

/// <summary>
/// Profiles in a dictionary, keyed the way the real service keys them: by id.
///
/// One class instead of the four near-identical stubs that used to live inside the test files that
/// needed one — every interface change meant editing four copies, which is how they had drifted:
/// two returned null from Load unconditionally, one wrote empty files to disk to satisfy a
/// File.Exists the ViewModel no longer does.
/// </summary>
public sealed class FakeProfileService : IProfileService
{
    private readonly Dictionary<long, Profile> _profiles = new();
    private long _nextId = 1;

    /// <summary>Makes every call throw, for the tests that check the ViewModel survives it.</summary>
    public bool ShouldFail { get; set; }

    public int SaveCount { get; private set; }

    public IReadOnlyCollection<string> StoredNames => _profiles.Values.Select(p => p.Name).ToList();

    public bool Contains(string name) => _profiles.Values.Any(p => p.Name == name);

    public Profile? ByName(string name) => _profiles.Values.FirstOrDefault(p => p.Name == name);

    public Task<long> SaveProfileAsync(Profile profile)
    {
        if (ShouldFail) return Task.FromException<long>(new Exception("forced"));
        SaveCount++;
        if (profile.Id == 0) profile.Id = _nextId++;
        _profiles[profile.Id] = profile;
        return Task.FromResult(profile.Id);
    }

    public Task<Profile?> LoadProfileAsync(long profileId)
    {
        if (ShouldFail) return Task.FromException<Profile?>(new Exception("forced"));
        return Task.FromResult(_profiles.TryGetValue(profileId, out var p) ? p : null);
    }

    public Task<IReadOnlyList<ProfileSummary>> ListProfilesAsync(long? gameId)
        => Task.FromResult(Summaries(_profiles.Values.Where(p => p.GameId == gameId)));

    public Task<IReadOnlyList<ProfileSummary>> ListAllProfilesAsync()
        => Task.FromResult(Summaries(_profiles.Values));

    public Task DeleteProfileAsync(long profileId)
    {
        _profiles.Remove(profileId);
        return Task.CompletedTask;
    }

    public Task RenameProfileAsync(long profileId, string newName)
    {
        if (_profiles.TryGetValue(profileId, out var profile)) profile.Name = newName;
        return Task.CompletedTask;
    }

    private static IReadOnlyList<ProfileSummary> Summaries(IEnumerable<Profile> profiles)
        => profiles.OrderBy(p => p.Name, StringComparer.Ordinal)
                   .Select(p => new ProfileSummary(p.Id, p.Name))
                   .ToList();
}
