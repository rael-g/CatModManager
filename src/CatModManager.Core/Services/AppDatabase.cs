using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Lilmihe;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

/// <summary>
/// Singleton that owns cmm.db. All core services share this connection factory.
/// </summary>
public class AppDatabase
{
    private readonly string _dbPath;

    public AppDatabase(ICatPathService pathService)
    {
        _dbPath = Path.Combine(pathService.BaseDataPath, "cmm.db");
        Directory.CreateDirectory(pathService.BaseDataPath);
        Migrate();
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Brings the database up to date, and throws if it cannot.
    ///
    /// This used to be a block of CREATE TABLE IF NOT EXISTS run on every startup. That creates new
    /// tables but never alters existing ones, so there was no way to add a column to an installation
    /// that had already run — the schema was frozen at whatever the first release shipped. Lilmihe
    /// applies the scripts in Migrations/ once each and records a checksum, which is what makes
    /// changing the schema possible at all.
    ///
    /// Failing loudly is the point: every service here assumes its tables exist, and carrying on
    /// with a half-migrated database turns one clear error at startup into a scattering of unrelated
    /// ones later.
    /// </summary>
    private void Migrate()
    {
        var scripts = LoadMigrations();

        Snapshot(scripts);

        // Foreign keys off, which is SQLite's own default — Microsoft.Data.Sqlite is what turns them
        // on. Rebuilding a table is drop-and-rename, and with the constraints live the rename
        // rewrites the REFERENCES clauses of every table pointing at it, quietly aiming the children
        // at the table that is on its way out. PRAGMA foreign_keys is ignored inside a transaction,
        // and MigrationHelper wraps each script in one, so this has to be settled here in the
        // connection string. Only the migration connection: Open() keeps them on.
        using var connection = new SqliteConnection($"Data Source={_dbPath};Foreign Keys=False");

        var result = new MigrationHelper(scripts, connection).Migrate()
            .GetAwaiter().GetResult();

        if (!result.Success)
        {
            throw new IOException(
                $"Could not migrate '{_dbPath}': {result.Message}" +
                (result.FailedFile != null ? $" (in {result.FailedFile})" : string.Empty),
                result.Error);
        }
    }

    /// <summary>
    /// Copies the database aside before anything is applied to it, once per migration.
    ///
    /// A transaction protects a script that fails. It does not protect a script that succeeds and
    /// was wrong — and 003 is the first one that moves data rather than only adding tables, so
    /// "it ran fine and the result is not what you wanted" became possible. The answer to that is a
    /// file, not a rollback.
    ///
    /// The name carries the script the snapshot precedes, so the copies do not overwrite each other
    /// and it is obvious which one to restore. It is a few hundred kilobytes.
    /// </summary>
    private void Snapshot(List<MigrationScript> scripts)
    {
        try
        {
            if (!File.Exists(_dbPath)) return;

            var applied = AppliedMigrationIds();
            var pending = scripts.Where(s => !applied.Contains(s.Name))
                                 .OrderBy(s => s.Name, StringComparer.Ordinal)
                                 .ToList();
            if (pending.Count == 0) return;

            var target = $"{_dbPath}.bak-before-{Path.GetFileNameWithoutExtension(pending[0].Name)}";
            if (File.Exists(target)) return;   // An earlier attempt already took this one.

            File.Copy(_dbPath, target);
        }
        catch
        {
            // Never the reason the application refuses to start. A missing snapshot is worse than
            // having one and better than a database nobody can open, and the migration itself is
            // still transactional.
        }
    }

    /// <summary>
    /// The ledger's contents, or nothing when there is no ledger — which is what a database from
    /// before migrations looks like, and means every script is pending.
    /// </summary>
    private HashSet<string> AppliedMigrationIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM Migrations";
            using var reader = command.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetString(0));
        }
        catch { /* no ledger yet */ }
        return ids;
    }

    /// <summary>
    /// The migration scripts embedded in this assembly, named by their file name so the ledger
    /// records "001_initial.sql" rather than the full resource id.
    ///
    /// Public so a test can apply a prefix of them and get a database as some earlier release left
    /// it. Seeding that state by hand is not equivalent: the ledger stores a checksum of each
    /// script, so a hand-written row is rejected — correctly — as an edited migration.
    /// </summary>
    public static List<MigrationScript> LoadMigrations()
    {
        var assembly = typeof(AppDatabase).Assembly;
        var prefix   = $"{assembly.GetName().Name}.Migrations.";

        var scripts = new List<MigrationScript>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix) || !name.EndsWith(".sql")) continue;

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            scripts.Add(new MigrationScript(name.Substring(prefix.Length), reader.ReadToEnd()));
        }

        return scripts;
    }
}
