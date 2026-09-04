using System.Collections.Generic;
using System.Threading.Tasks;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

/// <summary>Global tools in cmm.db. See <see cref="IGlobalToolService"/> for why they are not a game's.</summary>
public class SqliteGlobalToolService : IGlobalToolService
{
    private readonly AppDatabase _db;

    public SqliteGlobalToolService(AppDatabase db) => _db = db;

    public Task<List<ExternalTool>> ListToolsAsync() => Task.Run(() =>
    {
        using var conn = _db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name, executable_path, arguments, mount_before_launch
            FROM global_tools ORDER BY position
            """;

        var tools = new List<ExternalTool>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tools.Add(new ExternalTool
            {
                Name              = reader.GetString(0),
                ExecutablePath    = reader.GetString(1),
                Arguments         = reader.GetString(2),
                MountBeforeLaunch = reader.GetInt32(3) != 0,
                IsGlobal          = true,
            });

        return tools;
    });

    /// <summary>Delete-then-insert, the same way a game's tools are written.</summary>
    public Task SaveToolsAsync(IReadOnlyList<ExternalTool> tools) => Task.Run(() =>
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        Db.Execute(conn, tx, "DELETE FROM global_tools");

        for (int i = 0; i < tools.Count; i++)
        {
            var t = tools[i];
            Db.Execute(conn, tx, """
                INSERT INTO global_tools (position, name, executable_path, arguments,
                                          mount_before_launch)
                VALUES (@pos, @name, @exe, @args, @mount)
                """,
                ("@pos", i), ("@name", t.Name ?? ""), ("@exe", t.ExecutablePath ?? ""),
                ("@args", t.Arguments ?? ""), ("@mount", t.MountBeforeLaunch ? 1 : 0));
        }

        tx.Commit();
    });
}
