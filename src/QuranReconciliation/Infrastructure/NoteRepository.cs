using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class NoteRepository
{
    private readonly string _databasePath;

    internal NoteRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.ResearchDatabase;
    }

    internal ResearchNote? GetAyahNote(int surahNumber, int ayahNumber)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT body, updated_utc
        FROM ayah_notes
        WHERE surah_number=$surah AND ayah_number=$ayah;
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);
        command.Parameters.AddWithValue("$ayah", ayahNumber);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ResearchNote(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)));
    }

    internal ResearchNote? GetContextNote(long contextBlockId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT body, updated_utc
        FROM context_notes
        WHERE context_block_id=$id;
        """;
        command.Parameters.AddWithValue("$id", contextBlockId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ResearchNote(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)));
    }

    internal bool SaveAyahNote(
        int surahNumber,
        int ayahNumber,
        string body)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        string? prior = ReadAyahBody(
            connection,
            tx,
            surahNumber,
            ayahNumber);

        if (prior is not null &&
            string.Equals(prior, body, StringComparison.Ordinal))
        {
            tx.Commit();
            return false;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        if (prior is not null)
        {
            using var history = connection.CreateCommand();
            history.Transaction = tx;
            history.CommandText = """
            INSERT INTO ayah_note_history(
                surah_number, ayah_number, prior_body, changed_utc)
            VALUES($surah, $ayah, $body, $now);
            """;
            history.Parameters.AddWithValue("$surah", surahNumber);
            history.Parameters.AddWithValue("$ayah", ayahNumber);
            history.Parameters.AddWithValue("$body", prior);
            history.Parameters.AddWithValue("$now", now);
            history.ExecuteNonQuery();
        }

        using var save = connection.CreateCommand();
        save.Transaction = tx;
        save.CommandText = """
        INSERT INTO ayah_notes(
            surah_number, ayah_number, body, updated_utc)
        VALUES($surah, $ayah, $body, $now)
        ON CONFLICT(surah_number, ayah_number)
        DO UPDATE SET body=excluded.body, updated_utc=excluded.updated_utc;
        """;
        save.Parameters.AddWithValue("$surah", surahNumber);
        save.Parameters.AddWithValue("$ayah", ayahNumber);
        save.Parameters.AddWithValue("$body", body);
        save.Parameters.AddWithValue("$now", now);
        save.ExecuteNonQuery();

        ResearchActivityWriter.Append(
            connection,
            tx,
            prior is null
                ? "AyahNoteCreated"
                : "AyahNoteRevised",
            "AyahNote",
            null,
            surahNumber,
            ayahNumber,
            null,
            null,
            ayahNumber,
            ayahNumber,
            prior is null
                ? $"Created Ayah note · {surahNumber}:{ayahNumber}"
                : $"Revised Ayah note · {surahNumber}:{ayahNumber}",
            now,
            JsonSerializer.Serialize(
                new
                {
                    body
                }));

        tx.Commit();
        return true;
    }

    internal bool SaveContextNote(long contextBlockId, string body)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        EnsureContextExists(connection, tx, contextBlockId);

        string? prior = ReadContextBody(
            connection,
            tx,
            contextBlockId);

        if (prior is not null &&
            string.Equals(prior, body, StringComparison.Ordinal))
        {
            tx.Commit();
            return false;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        if (prior is not null)
        {
            using var history = connection.CreateCommand();
            history.Transaction = tx;
            history.CommandText = """
            INSERT INTO context_note_history(
                context_block_id, prior_body, changed_utc)
            VALUES($id, $body, $now);
            """;
            history.Parameters.AddWithValue("$id", contextBlockId);
            history.Parameters.AddWithValue("$body", prior);
            history.Parameters.AddWithValue("$now", now);
            history.ExecuteNonQuery();
        }

        using var save = connection.CreateCommand();
        save.Transaction = tx;
        save.CommandText = """
        INSERT INTO context_notes(
            context_block_id, body, updated_utc)
        VALUES($id, $body, $now)
        ON CONFLICT(context_block_id)
        DO UPDATE SET body=excluded.body, updated_utc=excluded.updated_utc;
        """;
        save.Parameters.AddWithValue("$id", contextBlockId);
        save.Parameters.AddWithValue("$body", body);
        save.Parameters.AddWithValue("$now", now);
        save.ExecuteNonQuery();

        int surahNumber;
        int startAyah;
        int endAyah;

        using (var target = connection.CreateCommand())
        {
            target.Transaction = tx;
            target.CommandText = """
            SELECT surah_number, start_ayah, end_ayah
            FROM context_blocks
            WHERE id=$id;
            """;
            target.Parameters.AddWithValue("$id", contextBlockId);

            using var reader = target.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException(
                    "Context block no longer exists.");
            }

            surahNumber = reader.GetInt32(0);
            startAyah = reader.GetInt32(1);
            endAyah = reader.GetInt32(2);
        }

        ResearchActivityWriter.Append(
            connection,
            tx,
            prior is null
                ? "ContextNoteCreated"
                : "ContextNoteRevised",
            "ContextNote",
            contextBlockId,
            surahNumber,
            null,
            contextBlockId,
            null,
            startAyah,
            endAyah,
            prior is null
                ? $"Created Context note · {startAyah}" +
                  (startAyah == endAyah ? string.Empty : $"–{endAyah}")
                : $"Revised Context note · {startAyah}" +
                  (startAyah == endAyah ? string.Empty : $"–{endAyah}"),
            now,
            JsonSerializer.Serialize(
                new
                {
                    body
                }));

        tx.Commit();
        return true;
    }

    internal IReadOnlyList<NoteRevision> GetAyahHistory(
        int surahNumber,
        int ayahNumber,
        int limit = 8)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, prior_body, changed_utc
        FROM ayah_note_history
        WHERE surah_number=$surah AND ayah_number=$ayah
        ORDER BY id DESC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);
        command.Parameters.AddWithValue("$ayah", ayahNumber);
        command.Parameters.AddWithValue("$limit", limit);

        return ReadHistory(command);
    }

    internal IReadOnlyList<NoteRevision> GetContextHistory(
        long contextBlockId,
        int limit = 8)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, prior_body, changed_utc
        FROM context_note_history
        WHERE context_block_id=$id
        ORDER BY id DESC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$id", contextBlockId);
        command.Parameters.AddWithValue("$limit", limit);

        return ReadHistory(command);
    }

    private SqliteConnection Open()
    {
        var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.GetFullPath(_databasePath),
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static string? ReadAyahBody(
        SqliteConnection connection,
        SqliteTransaction tx,
        int surahNumber,
        int ayahNumber)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        SELECT body
        FROM ayah_notes
        WHERE surah_number=$surah AND ayah_number=$ayah;
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);
        command.Parameters.AddWithValue("$ayah", ayahNumber);

        return command.ExecuteScalar() as string;
    }

    private static string? ReadContextBody(
        SqliteConnection connection,
        SqliteTransaction tx,
        long contextBlockId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        SELECT body
        FROM context_notes
        WHERE context_block_id=$id;
        """;
        command.Parameters.AddWithValue("$id", contextBlockId);

        return command.ExecuteScalar() as string;
    }

    private static void EnsureContextExists(
        SqliteConnection connection,
        SqliteTransaction tx,
        long contextBlockId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        SELECT COUNT(*)
        FROM context_blocks
        WHERE id=$id AND is_active=1;
        """;
        command.Parameters.AddWithValue("$id", contextBlockId);

        long count = Convert.ToInt64(command.ExecuteScalar());
        if (count != 1)
        {
            throw new InvalidOperationException(
                "The selected context block is no longer active.");
        }
    }

    private static IReadOnlyList<NoteRevision> ReadHistory(
        SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var result = new List<NoteRevision>();

        while (reader.Read())
        {
            result.Add(new NoteRevision(
                reader.GetInt64(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2))));
        }

        return result;
    }
}
