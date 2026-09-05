using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

/// <summary>
/// Profiles in cmm.db, replacing the per-profile TOML file.
///
/// Saving is delete-then-insert inside one transaction rather than a diff. The lists are short —
/// hundreds of mods at the very most — and a diff would have to reason about rows moving position,
/// which is exactly the kind of bookkeeping that loses a mod. One transaction means a failed save
/// leaves the previous profile intact, which the temp-file-and-swap dance in the TOML service was
/// approximating.
/// </summary>
public class SqliteProfileService : IProfileService
{
    private readonly AppDatabase _db;

    public SqliteProfileService(AppDatabase db) => _db = db;

    public Task<long> SaveProfileAsync(Profile profile) => Task.Run(() => Save(profile));

    public Task<Profile?> LoadProfileAsync(long profileId) => Task.Run(() => Load(profileId));

    public Task<IReadOnlyList<ProfileSummary>> ListProfilesAsync(long? gameId) => Task.Run(() =>
        List(gameId == null ? "WHERE game_id IS NULL" : "WHERE game_id = @g",
             gameId == null ? Array.Empty<(string, object)>() : new[] { ("@g", (object)gameId.Value) }));

    public Task<IReadOnlyList<ProfileSummary>> ListAllProfilesAsync() => Task.Run(() =>
        List("", Array.Empty<(string, object)>()));

    public Task DeleteProfileAsync(long profileId) => Task.Run(() =>
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        // The declared ON DELETE CASCADE would cover this, but only because Microsoft.Data.Sqlite
        // happens to enable foreign keys — they are off in SQLite itself, and per-connection.
        // Deleting the children outright does not depend on which connection this runs on.
        Db.Execute(conn, tx, "DELETE FROM profile_entries WHERE profile_id = @p", ("@p", profileId));
        Db.Execute(conn, tx, "DELETE FROM profiles WHERE id = @p", ("@p", profileId));

        tx.Commit();
    });

    /// <summary>
    /// One statement, because the id is what the child rows point at. When the name was the key this
    /// took four, plus deferred constraints to survive the moment in between — and a rename that
    /// failed halfway left a profile whose mods belonged to a name that no longer existed.
    /// </summary>
    public Task RenameProfileAsync(long profileId, string newName) => Task.Run(() =>
    {
        using var conn = _db.Open();
        Db.Execute(conn, null, "UPDATE profiles SET name = @n WHERE id = @p",
                   ("@n", newName), ("@p", profileId));
    });

    /// <inheritdoc/>
    public Task UninstallModAsync(long gameId, string modRootPath) => Task.Run(() =>
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        // The entries by hand rather than through the declared cascade, for the reason
        // DeleteProfileAsync gives: foreign keys are off in SQLite and enabled per connection.
        Db.Execute(conn, tx, """
            DELETE FROM profile_entries
             WHERE game_mod_id IN (SELECT id FROM game_mods
                                    WHERE game_id = @g AND mod_root_path = @path)
            """, ("@g", gameId), ("@path", modRootPath));

        Db.Execute(conn, tx, "DELETE FROM game_mods WHERE game_id = @g AND mod_root_path = @path",
                   ("@g", gameId), ("@path", modRootPath));

        tx.Commit();
    });

    // ── Save ──────────────────────────────────────────────────────────────────

    private long Save(Profile profile)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        if (profile.Id == 0)
        {
            Db.Execute(conn, tx, "INSERT INTO profiles (game_id, name) VALUES (@g, @name)",
                ("@g", (object?)profile.GameId ?? DBNull.Value), ("@name", profile.Name));

            profile.Id = Db.ScalarLong(conn, tx, "SELECT last_insert_rowid()");
        }
        else
        {
            Db.Execute(conn, tx, "UPDATE profiles SET game_id = @g, name = @name WHERE id = @id",
                ("@id", profile.Id), ("@g", (object?)profile.GameId ?? DBNull.Value),
                ("@name", profile.Name));
        }

        Db.Execute(conn, tx, "DELETE FROM profile_entries WHERE profile_id = @p", ("@p", profile.Id));

        SaveInventory(conn, tx, profile);
        SaveEntries(conn, tx, profile);

        tx.Commit();
        return profile.Id;
    }

    /// <summary>
    /// Writes what this profile says is installed into the game's inventory.
    ///
    /// The inventory belongs to the game, but the profile is what knows about a mod the moment it is
    /// installed — so this is where a new mod first reaches the database. A parked profile has no
    /// game to hang one on, and nothing is written for it.
    /// </summary>
    private static void SaveInventory(SqliteConnection conn, SqliteTransaction tx, Profile profile)
    {
        if (profile.GameId is not { } gameId) return;

        var installed = (profile.Mods ?? new List<Mod>())
            .Where(m => !m.IsSeparator && !string.IsNullOrEmpty(m.ModRootPath))
            .GroupBy(m => m.ModRootPath).Select(g => g.Last())
            .ToList();

        foreach (var m in installed)
        {
            Db.Execute(conn, tx, """
                INSERT INTO game_mods (game_id, mod_root_path, name, category, version, is_archive)
                VALUES (@g, @path, @name, @cat, @ver, @archive)
                ON CONFLICT(game_id, mod_root_path) DO UPDATE SET
                    name = excluded.name, category = excluded.category,
                    version = excluded.version, is_archive = excluded.is_archive
                """,
                ("@g", gameId), ("@path", m.ModRootPath!), ("@name", m.Name ?? ""),
                ("@cat", m.Category ?? "Uncategorized"), ("@ver", m.Version ?? "1.0.0"),
                ("@archive", m.IsArchive ? 1 : 0));
        }

        // A mod that left the list was uninstalled, not unticked: RemoveMod deletes the folder from
        // disk, and unticking is what the checkbox is for. So the inventory has to shrink with it —
        // otherwise every profile of this game keeps showing an entry whose files are gone.
        PruneInventory(conn, tx, gameId, profile.Id,
                       installed.Select(m => m.ModRootPath!).ToList());
    }

    /// <summary>
    /// Drops inventory rows this profile no longer lists — but only the ones no other profile of the
    /// same game still refers to.
    ///
    /// The narrower rule is not caution, it is a bug that was caught on real data. Importing the
    /// developer's own profiles, NewProfile and Starfield shared a game, and NewProfile's three mods
    /// were not among Starfield's thirty-eight. Saving Starfield second deleted them from the
    /// inventory, and the cascade took NewProfile's entries with them: three enabled mods became
    /// zero, silently.
    ///
    /// What survives is a mod some other profile asked for whose files are gone. That shows up
    /// broken, which is what it is — and is what happened before the split too, when each profile
    /// kept its own list. Vanishing from a profile the user was not looking at is the worse of the
    /// two.
    /// </summary>
    private static void PruneInventory(SqliteConnection conn, SqliteTransaction tx, long gameId,
                                       long profileId, IReadOnlyList<string> keepPaths)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@self", profileId);

        // Placeholders rather than the paths concatenated into the SQL: SQLite has no array
        // parameter, and a mod folder with an apostrophe in its name would otherwise be a syntax
        // error at best.
        var placeholders = new List<string>();
        for (int i = 0; i < keepPaths.Count; i++)
        {
            placeholders.Add($"@p{i}");
            cmd.Parameters.AddWithValue($"@p{i}", keepPaths[i]);
        }

        var notListed = keepPaths.Count == 0
            ? ""
            : $" AND mod_root_path NOT IN ({string.Join(", ", placeholders)})";

        cmd.CommandText = $"""
            DELETE FROM game_mods
            WHERE game_id = @g{notListed}
              AND id NOT IN (SELECT game_mod_id FROM profile_entries
                             WHERE game_mod_id IS NOT NULL AND profile_id <> @self)
            """;
        cmd.ExecuteNonQuery();
    }

    private static void SaveEntries(SqliteConnection conn, SqliteTransaction tx, Profile profile)
    {
        var mods = profile.Mods ?? new List<Mod>();
        for (int i = 0; i < mods.Count; i++)
        {
            var m = mods[i];

            object gameModId = DBNull.Value;
            if (!m.IsSeparator && profile.GameId != null && !string.IsNullOrEmpty(m.ModRootPath))
            {
                gameModId = Db.ScalarLong(conn, tx,
                    "SELECT id FROM game_mods WHERE game_id = @g AND mod_root_path = @path",
                    ("@g", profile.GameId.Value), ("@path", m.ModRootPath));
            }
            // A non-separator with no game to belong to is dropped: a parked profile has no
            // inventory to hang mods on, and inventing one would tie them to a folder the user has
            // not chosen yet.
            else if (!m.IsSeparator) continue;

            Db.Execute(conn, tx, """
                INSERT INTO profile_entries (profile_id, position, game_mod_id, separator_name,
                                             is_enabled, priority, mount_point_id)
                VALUES (@p, @pos, @gm, @sep, @enabled, @prio, @mount)
                """,
                ("@p", profile.Id), ("@pos", i), ("@gm", gameModId),
                ("@sep", m.IsSeparator ? (object)(m.Name ?? "") : DBNull.Value),
                ("@enabled", m.IsEnabled ? 1 : 0), ("@prio", m.Priority),
                ("@mount", (object?)m.MountPointId ?? DBNull.Value));
        }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private Profile? Load(long profileId)
    {
        using var conn = _db.Open();

        Profile profile;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT game_id, name FROM profiles WHERE id = @p";
            cmd.Parameters.AddWithValue("@p", profileId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            profile = new Profile
            {
                Id     = profileId,
                GameId = reader.IsDBNull(0) ? null : reader.GetInt64(0),
                Name   = reader.GetString(1),
            };
        }

        LoadMods(conn, profile);

        return profile;
    }

    /// <summary>
    /// The list the user sees: this profile's own entries, in order, followed by anything installed
    /// for the game that this profile has never had an opinion about.
    ///
    /// That tail is the point of the split. A mod installed while profile A was open used to be
    /// invisible to profile B, which is why switching profiles looked like it had thrown the mods
    /// away. Now it shows up in B — unticked, because enabling a mod in a profile the user was not
    /// looking at is a change to how their game runs that they never asked for.
    /// </summary>
    private static void LoadMods(SqliteConnection conn, Profile profile)
    {
        var seen = new HashSet<long>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT e.game_mod_id, e.separator_name, e.is_enabled, e.priority, e.mount_point_id,
                       COALESCE(m.name, ''), COALESCE(m.mod_root_path, ''),
                       COALESCE(m.category, 'Uncategorized'), COALESCE(m.version, '1.0.0'),
                       COALESCE(m.is_archive, 0)
                FROM profile_entries e
                LEFT JOIN game_mods m ON m.id = e.game_mod_id
                WHERE e.profile_id = @p
                ORDER BY e.position
                """;
            cmd.Parameters.AddWithValue("@p", profile.Id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                bool isSeparator = reader.IsDBNull(0);
                if (!isSeparator) seen.Add(reader.GetInt64(0));

                profile.Mods.Add(new Mod
                {
                    IsSeparator  = isSeparator,
                    Name         = isSeparator ? (reader.IsDBNull(1) ? "" : reader.GetString(1))
                                               : reader.GetString(5),
                    ModRootPath  = reader.GetString(6),
                    IsEnabled    = reader.GetInt32(2) != 0,
                    Priority     = reader.GetInt32(3),
                    MountPointId = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Category     = reader.GetString(7),
                    Version      = reader.GetString(8),
                    IsArchive    = reader.GetInt32(9) != 0,
                });
            }
        }

        if (profile.GameId is not { } gameId) return;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, name, mod_root_path, category, version, is_archive
                FROM game_mods WHERE game_id = @g ORDER BY id
                """;
            cmd.Parameters.AddWithValue("@g", gameId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (seen.Contains(reader.GetInt64(0))) continue;

                profile.Mods.Add(new Mod
                {
                    Name        = reader.GetString(1),
                    ModRootPath = reader.GetString(2),
                    Category    = reader.GetString(3),
                    Version     = reader.GetString(4),
                    IsArchive   = reader.GetInt32(5) != 0,
                    IsEnabled   = false,
                    Priority    = profile.Mods.Count,
                });
            }
        }
    }

    private IReadOnlyList<ProfileSummary> List(string where, (string Name, object Value)[] parameters)
    {
        using var conn = _db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT id, name FROM profiles {where} ORDER BY name";
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);

        var profiles = new List<ProfileSummary>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) profiles.Add(new ProfileSummary(reader.GetInt64(0), reader.GetString(1)));
        return profiles;
    }
}
