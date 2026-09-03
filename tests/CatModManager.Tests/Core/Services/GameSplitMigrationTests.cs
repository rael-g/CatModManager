using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CatModManager.Tests.Core.Services;

/// <summary>
/// Migration 003, which every existing installation runs exactly once and cannot run again.
///
/// The other migration tests check that the schema arrives. This one checks the regrouping: it seeds
/// a database in the 002 shape — profiles carrying their own private copy of the mod list — and
/// asserts what comes out the other side. The fixture mirrors the developer's real data, two
/// profiles sharing a game plus a parked one, because those are the shapes that actually exist.
/// </summary>
public class GameSplitMigrationTests : IDisposable
{
    private readonly string _dir;
    private readonly MockCatPathService _paths;

    public GameSplitMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CMM_Split_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _paths = new MockCatPathService(_dir);
        SeedAt002();
    }

    private string DbPath => Path.Combine(_dir, "cmm.db");

    /// <summary>
    /// Builds the database as 002 left it, and inserts the data a real installation would hold, so
    /// that the next AppDatabase applies only 003 — exactly where every existing user will be.
    ///
    /// The first two migrations are applied for real rather than transcribed here. A hand-written
    /// ledger row is rejected by the checksum check, and rightly so: a transcription that drifted
    /// from the real script would make this test pass against a schema nobody runs.
    /// </summary>
    private void SeedAt002()
    {
        using (var connection = new SqliteConnection($"Data Source={DbPath}"))
        {
            var upTo002 = AppDatabase.LoadMigrations()
                .Where(s => string.CompareOrdinal(s.Name, "003") < 0)
                .ToList();

            var result = new Lilmihe.MigrationHelper(upTo002, connection).Migrate()
                .GetAwaiter().GetResult();
            Assert.True(result.Success, result.Message);
        }
        SqliteConnection.ClearAllPools();

        using (var connection = new SqliteConnection($"Data Source={DbPath}"))
        {
            connection.Open();
            using var setup = connection.CreateCommand();
            setup.CommandText = """
                INSERT INTO profiles (name, base_data_path, mods_folder_path, game_executable_path, game_support_id, launch_arguments)
                VALUES ('Vanilla', '/games/Starfield', '/games/Starfield/cmm/mods', '/games/Starfield/sf.exe', 'starfield', '-w');
                INSERT INTO profiles (name, base_data_path, mods_folder_path, game_executable_path, game_support_id, launch_arguments)
                VALUES ('Heavy', '/games/Starfield', '/games/Starfield/cmm/mods', '/games/Starfield/sf.exe', 'starfield', '');
                INSERT INTO profiles (name, base_data_path) VALUES ('Skyrim', '/games/Skyrim');
                INSERT INTO profiles (name) VALUES ('Modded');

                INSERT INTO profile_mods (profile_name, position, priority, name, mod_root_path, is_enabled)
                VALUES ('Vanilla', 0, 0, 'SFSE', '/games/Starfield/cmm/mods/SFSE', 1);
                INSERT INTO profile_mods (profile_name, position, priority, name, mod_root_path, is_enabled, is_separator)
                VALUES ('Vanilla', 1, 1, 'UI stuff', '', 1, 1);

                INSERT INTO profile_mods (profile_name, position, priority, name, mod_root_path, is_enabled)
                VALUES ('Heavy', 0, 0, 'SFSE', '/games/Starfield/cmm/mods/SFSE', 1);
                INSERT INTO profile_mods (profile_name, position, priority, name, mod_root_path, is_enabled, mount_point_id)
                VALUES ('Heavy', 1, 1, 'FasterMining', '/games/Starfield/cmm/mods/FasterMining', 0, 'root');

                INSERT INTO profile_tools (profile_name, position, name, executable_path, arguments, mount_before_launch)
                VALUES ('Vanilla', 0, 'xEdit', 'wine', 'xEdit.exe', 0);
                INSERT INTO profile_tools (profile_name, position, name, executable_path, arguments, mount_before_launch)
                VALUES ('Vanilla', 1, 'SKSE', '/games/Starfield/skse.exe', '', 0);
                INSERT INTO profile_tools (profile_name, position, name, executable_path, arguments, mount_before_launch)
                VALUES ('Heavy', 0, 'xEdit', 'wine', 'xEdit.exe', 1);
                INSERT INTO profile_tools (profile_name, position, name, executable_path, arguments, mount_before_launch)
                VALUES ('Heavy', 1, 'Wrye Bash', 'wine', 'Bash.exe', 0);
                """;
            setup.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    private IProfileService Migrated() => new SqliteProfileService(new AppDatabase(_paths));

    /// <summary>
    /// The profile by the name it had as a file. Names stopped identifying a profile in 004 — they
    /// are unique per game now — but they are still what the fixture is written in terms of.
    /// </summary>
    private static async Task<Profile> Load(IProfileService service, string name)
    {
        var summary = (await service.ListAllProfilesAsync()).Single(p => p.Name == name);
        return (await service.LoadProfileAsync(summary.Id))!;
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private string[] Strings(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new System.Collections.Generic.List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values.ToArray();
    }

    [Fact]
    public async Task NoProfileIsLost()
    {
        Assert.Equal(new[] { "Heavy", "Modded", "Skyrim", "Vanilla" },
                     (await Migrated().ListAllProfilesAsync()).Select(p => p.Name).OrderBy(n => n));
    }

    /// <summary>Profiles that agreed on the game folder were the same installation all along.</summary>
    [Fact]
    public void ProfilesSharingAGameFolderCollapseIntoOneGame()
    {
        _ = Migrated();

        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM games"));
        Assert.Equal(1, Scalar(
            "SELECT COUNT(DISTINCT game_id) FROM profiles WHERE name IN ('Vanilla', 'Heavy')"));
    }

    /// <summary>
    /// SFSE was two rows, one per profile, describing one folder on disk. It has to become one.
    /// </summary>
    [Fact]
    public void TheDuplicatedModsBecomeOneInventory()
    {
        _ = Migrated();

        Assert.Equal(2, Scalar(
            """
            SELECT COUNT(*) FROM game_mods
            WHERE game_id = (SELECT game_id FROM profiles WHERE name = 'Vanilla')
            """));
    }

    /// <summary>
    /// The half of the old row that belongs to the profile: ticked or not, in what order, and where
    /// it mounts. Losing this would silently change how someone's game runs.
    /// </summary>
    [Fact]
    public async Task EachProfileKeepsItsOwnSelection()
    {
        var service = Migrated();

        var heavy = await Load(service, "Heavy");
        var mining = Assert.Single(heavy.Mods.Where(m => m.Name == "FasterMining"));
        Assert.False(mining.IsEnabled);
        Assert.Equal("root", mining.MountPointId);
        Assert.True(heavy.Mods.Single(m => m.Name == "SFSE").IsEnabled);

        // The launch line moved to the game in 005: Vanilla's "-w" belongs to the Starfield game
        // now, and the profile no longer carries one at all.
        Assert.Equal(1, Scalar(
            """
            SELECT COUNT(*) FROM games
            WHERE display_name = 'Starfield' AND launch_arguments = '-w'
            """));
    }

    /// <summary>
    /// The gain, stated as a test. FasterMining was installed under Heavy and Vanilla had never
    /// heard of it — before the split, switching to Vanilla looked like it had been thrown away.
    /// </summary>
    [Fact]
    public async Task AModFromTheOtherProfileNowShowsUpUnticked()
    {
        var vanilla = await Load(Migrated(), "Vanilla");

        var mining = Assert.Single(vanilla.Mods.Where(m => m.Name == "FasterMining"));
        Assert.False(mining.IsEnabled);
        Assert.True(vanilla.Mods.Single(m => m.Name == "SFSE").IsEnabled);
    }

    [Fact]
    public async Task SeparatorsSurviveWithoutBecomingInventory()
    {
        var vanilla = await Load(Migrated(), "Vanilla");

        var separator = Assert.Single(vanilla.Mods.Where(m => m.IsSeparator));
        Assert.Equal("UI stuff", separator.Name);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM game_mods WHERE mod_root_path = ''"));
    }

    /// <summary>
    /// A profile the migration cannot classify is the user's data, not noise. Modded is real —
    /// it is one line on the developer's own machine.
    /// </summary>
    [Fact]
    public async Task TheProfileWithNoGameIsParkedRatherThanDropped()
    {
        var modded = await Load(Migrated(), "Modded");

        Assert.Null(modded.GameId);
        Assert.Empty(modded.Mods);
    }

    // ── Migration 006 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Tools became the game's, and the two profiles held overlapping lists. The one they shared
    /// has to collapse and the ones only one of them had have to survive — dropping a tool the user
    /// configured is worse than keeping one they stopped using.
    /// </summary>
    [Fact]
    public void TheToolsOfEveryProfileEndUpOnTheGameWithoutDuplicates()
    {
        _ = Migrated();

        Assert.Equal(new[] { "SKSE", "Wrye Bash", "xEdit" }, Strings(
            """
            SELECT name FROM game_tools
            WHERE game_id = (SELECT game_id FROM profiles WHERE name = 'Vanilla')
            ORDER BY name
            """));

        // Renumbered from zero and contiguous. The old positions came from two separate lists and
        // both started at zero, so keeping them would have collided on the primary key — and the
        // shared xEdit, which one profile had first, still leads.
        Assert.Equal(new[] { "0:xEdit", "1", "2" }, Strings(
            """
            SELECT CASE WHEN position = 0 THEN '0:' || name ELSE CAST(position AS TEXT) END
            FROM game_tools ORDER BY game_id, position
            """));
    }

    /// <summary>
    /// The checkbox is opt-in, so the profile that ticked it decides. Taking the other answer would
    /// launch a tool over an unmounted game and show it an empty mod folder.
    /// </summary>
    [Fact]
    public void ATickedMountBeforeLaunchSurvivesTheCollapse()
    {
        _ = Migrated();

        Assert.Equal(1, Scalar("SELECT mount_before_launch FROM game_tools WHERE name = 'xEdit'"));
    }

    // ── Migration 004 ─────────────────────────────────────────────────────────

    /// <summary>
    /// The game needs something to be called before it can be a thing the user picks. The folder's
    /// own last segment is the closest thing to a name that already exists on their machine.
    /// </summary>
    [Fact]
    public void EachGameComesOutNamedAfterItsFolder()
    {
        _ = Migrated();

        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM games WHERE display_name = 'Starfield'"));
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM games WHERE display_name = 'Skyrim'"));
    }

    /// <summary>
    /// The rebuild is the risky half of 004: four tables dropped and recreated. Nothing may be lost
    /// on the way through, and every child row has to end up pointing at the profile it came from.
    /// </summary>
    [Fact]
    public async Task TheRebuildCarriesEveryProfileAndItsRows()
    {
        var service = Migrated();

        Assert.Equal(4, Scalar("SELECT COUNT(*) FROM profiles"));
        Assert.Equal(4, Scalar("SELECT COUNT(DISTINCT id) FROM profiles"));

        // No orphans: an entry whose profile_id matches nothing would be a mod nobody can see.
        Assert.Equal(0, Scalar(
            """
            SELECT COUNT(*) FROM profile_entries e
            WHERE NOT EXISTS (SELECT 1 FROM profiles p WHERE p.id = e.profile_id)
            """));

        var heavy = await Load(service, "Heavy");
        Assert.Equal(2, heavy.Mods.Count(m => !m.IsSeparator));
    }

    /// <summary>
    /// Why the rebuild was worth doing. Under the old key the second of these overwrote the first.
    /// </summary>
    [Fact]
    public async Task AfterTheMigrationTwoGamesCanShareAProfileName()
    {
        var service = Migrated();

        long starfield = Scalar("SELECT id FROM games WHERE display_name = 'Starfield'");
        long skyrim    = Scalar("SELECT id FROM games WHERE display_name = 'Skyrim'");

        await service.SaveProfileAsync(new Profile { Name = "Default", GameId = starfield });
        await service.SaveProfileAsync(new Profile { Name = "Default", GameId = skyrim });

        Assert.Contains(await service.ListProfilesAsync(starfield), p => p.Name == "Default");
        Assert.Contains(await service.ListProfilesAsync(skyrim),    p => p.Name == "Default");
    }

    /// <summary>
    /// A transaction protects a script that fails, not one that succeeds and was wrong. 003 is the
    /// first migration that moves data rather than only adding tables, so the file copy is the only
    /// thing standing between a bad regrouping and a user's seven profiles.
    /// </summary>
    [Fact]
    public void TheDatabaseIsCopiedAsideBeforeTheMigrationRuns()
    {
        _ = Migrated();

        var snapshot = Path.Combine(_dir, "cmm.db.bak-before-003_games");
        Assert.True(File.Exists(snapshot), "No snapshot was taken before the migration.");

        // The copy has to predate the migration, or it is a snapshot of the wrong thing.
        using var connection = new SqliteConnection($"Data Source={snapshot}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'profile_mods'";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    /// <summary>Nothing pending means nothing to snapshot — otherwise every start copies the file.</summary>
    [Fact]
    public void AStartWithNothingToApplyTakesNoSnapshot()
    {
        _ = Migrated();
        SqliteConnection.ClearAllPools();
        foreach (var stale in Directory.GetFiles(_dir, "cmm.db.bak-*")) File.Delete(stale);

        _ = Migrated();

        Assert.Empty(Directory.GetFiles(_dir, "cmm.db.bak-*"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
