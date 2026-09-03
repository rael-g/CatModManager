using System;
using Microsoft.Data.Sqlite;

namespace CatModManager.Core.Services;

/// <summary>
/// The three calls every query in this folder is made of. Shared so that the game and profile
/// services do not each keep their own copy of "make a command, bind the parameters, run it".
/// </summary>
internal static class Db
{
    public static void Execute(SqliteConnection conn, SqliteTransaction? tx, string sql,
                               params (string Name, object Value)[] parameters)
    {
        using var cmd = Prepare(conn, tx, sql, parameters);
        cmd.ExecuteNonQuery();
    }

    public static long ScalarLong(SqliteConnection conn, SqliteTransaction? tx, string sql,
                                  params (string Name, object Value)[] parameters)
    {
        using var cmd = Prepare(conn, tx, sql, parameters);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>The value, or null when the query found no row or the column was NULL.</summary>
    public static long? ScalarLongOrNull(SqliteConnection conn, SqliteTransaction? tx, string sql,
                                         params (string Name, object Value)[] parameters)
    {
        using var cmd = Prepare(conn, tx, sql, parameters);
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToInt64(result);
    }

    private static SqliteCommand Prepare(SqliteConnection conn, SqliteTransaction? tx, string sql,
                                         (string Name, object Value)[] parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
}
