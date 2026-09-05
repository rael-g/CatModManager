using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Ui.ViewModels;
using NSubstitute;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// Duplicating is the answer to a gap the other two commands leave open on purpose: New Profile
/// starts blank, and a mod installed later reaches every profile unticked. Neither is wrong, but
/// together they mean varying a working setup used to be a few hundred clicks.
/// </summary>
public class ProfileDuplicateTests
{
    private readonly Dictionary<long, Profile> _stored = new();
    private readonly ProfileManagerViewModel _vm;
    private long _nextId = 1;

    public ProfileDuplicateTests()
    {
        var profiles = Substitute.For<IProfileService>();

        profiles.SaveProfileAsync(Arg.Any<Profile>()).Returns(call =>
        {
            var p = call.Arg<Profile>();
            if (p.Id == 0) p.Id = _nextId++;
            // Copied, so a later edit to the live list cannot reach what is "on disk".
            _stored[p.Id] = new Profile
            {
                Id = p.Id, Name = p.Name, GameId = p.GameId,
                Mods = p.Mods.Select(m => new Mod
                {
                    Name = m.Name, ModRootPath = m.ModRootPath,
                    IsEnabled = m.IsEnabled, Priority = m.Priority
                }).ToList()
            };
            return Task.FromResult(p.Id);
        });

        profiles.LoadProfileAsync(Arg.Any<long>())
                .Returns(c => Task.FromResult(_stored.GetValueOrDefault(c.Arg<long>())));

        profiles.ListProfilesAsync(Arg.Any<long?>()).Returns(_ => Task.FromResult(
            (IReadOnlyList<ProfileSummary>)_stored.Values
                .Select(p => new ProfileSummary(p.Id, p.Name)).ToList()));

        var config = Substitute.For<IConfigService>();
        config.Current.Returns(new AppConfig());

        _vm = new ProfileManagerViewModel(profiles, config, Substitute.For<ILogService>())
        {
            CurrentGameId = () => 7
        };
    }

    private Profile Seed(string name, params (string Mod, bool Enabled)[] mods)
    {
        var p = new Profile
        {
            Name = name, GameId = 7,
            Mods = mods.Select((m, i) => new Mod
            {
                Name = m.Mod, ModRootPath = "/mods/" + m.Mod,
                IsEnabled = m.Enabled, Priority = i
            }).ToList()
        };
        p.Id = _nextId++;
        _stored[p.Id] = p;
        _vm.CurrentProfile = new ProfileSummary(p.Id, p.Name);
        _vm.BuildSaveData = () => new Profile { Name = p.Name, GameId = 7, Mods = p.Mods };
        return p;
    }

    /// <summary>The whole point: the ticks come along, which is what New Profile will not do.</summary>
    [Fact]
    public async Task TheCopyKeepsEveryModEnabledExactlyAsItWas()
    {
        Seed("Playthrough", ("Alpha", true), ("Beta", false), ("Gamma", true));

        await _vm.DuplicateProfile();

        var copy = _stored.Values.Single(p => p.Name != "Playthrough");
        Assert.Equal(new[] { true, false, true }, copy.Mods.Select(m => m.IsEnabled));
    }

    /// <summary>Load order is half of what a setup is; a copy that reshuffles it is not a copy.</summary>
    [Fact]
    public async Task TheCopyKeepsTheLoadOrder()
    {
        Seed("Playthrough", ("Alpha", true), ("Beta", true));

        await _vm.DuplicateProfile();

        var copy = _stored.Values.Single(p => p.Name != "Playthrough");
        Assert.Equal(new[] { "Alpha", "Beta" }, copy.Mods.Select(m => m.Name));
    }

    /// <summary>
    /// The copy has to be an insert. Reusing the source's id would silently overwrite the profile
    /// the user asked to keep — the failure would look like the duplicate simply never appeared.
    /// </summary>
    [Fact]
    public async Task TheSourceProfileSurvivesUntouched()
    {
        var source = Seed("Playthrough", ("Alpha", true));

        await _vm.DuplicateProfile();

        Assert.Equal(2, _stored.Count);
        Assert.True(_stored.ContainsKey(source.Id));
        Assert.Equal("Playthrough", _stored[source.Id].Name);
    }

    [Fact]
    public async Task TheCopyGetsItsOwnNameAndOpensAsTheCurrentProfile()
    {
        Seed("Playthrough", ("Alpha", true));

        await _vm.DuplicateProfile();

        Assert.Equal("Playthrough copy", _vm.CurrentProfile!.Name);
    }

    /// <summary>Nothing open, nothing to copy — and certainly not an empty profile invented here.</summary>
    [Fact]
    public async Task DuplicatingWithNoProfileOpenDoesNothing()
    {
        _vm.CurrentProfile = null;

        await _vm.DuplicateProfile();

        Assert.Empty(_stored);
    }
}
