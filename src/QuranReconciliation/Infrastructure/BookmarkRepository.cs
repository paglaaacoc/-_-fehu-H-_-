using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class BookmarkRepository
{
    private readonly string _databasePath;

    internal BookmarkRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.ResearchDatabase;
    }

    internal BookmarkEntry? Get(
        int surahNumber,
        int ayahNumber)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, surah_number, ayah_number,
               title, note, created_utc, updated_utc
        FROM bookmarks
        WHERE surah_number=$surah AND ayah_number=$ayah;
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);
        command.Parameters.AddWithValue("$ayah", ayahNumber);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? Read(reader)
            : null;
    }

    internal BookmarkEntry Save(
        int surahNumber,
        int ayahNumber,
        string? title,
        string? note)
    {
        string cleanTitle = title?.Trim() ?? string.Empty;
        string cleanNote = note?.Trim() ?? string.Empty;
        string now = DateTimeOffset.UtcNow.ToString("O");

        using var connection = Open();
        using var tx = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
            INSERT INTO bookmarks(
                surah_number, ayah_number,
                title, note, created_utc, updated_utc)
            VALUES(
                $surah, $ayah,
                $title, $note, $now, $now)
            ON CONFLICT(surah_number, ayah_number)
            DO UPDATE SET
                title=excluded.title,
                note=excluded.note,
                updated_utc=excluded.updated_utc;
            """;
            command.Parameters.AddWithValue("$surah", surahNumber);
            command.Parameters.AddWithValue("$ayah", ayahNumber);
            command.Parameters.AddWithValue("$title", cleanTitle);
            command.Parameters.AddWithValue("$note", cleanNote);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }

        long id;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
            SELECT id
            FROM bookmarks
            WHERE surah_number=$surah AND ayah_number=$ayah;
            """;
            read.Parameters.AddWithValue("$surah", surahNumber);
            read.Parameters.AddWithValue("$ayah", ayahNumber);
            id = Convert.ToInt64(read.ExecuteScalar());
        }

        string label =
            string.IsNullOrWhiteSpace(cleanTitle)
                ? $"{surahNumber}:{ayahNumber}"
                : cleanTitle;

        ResearchActivityWriter.Append(
            connection,
            tx,
            "BookmarkSaved",
            "Bookmark",
            id,
            surahNumber,
            ayahNumber,
            null,
            null,
            ayahNumber,
            ayahNumber,
            $"Saved bookmark · {label}",
            now,
            JsonSerializer.Serialize(
                new
                {
                    title = cleanTitle,
                    note = cleanNote
                }));

        tx.Commit();

        return Get(surahNumber, ayahNumber)
            ?? throw new InvalidOperationException(
                "Bookmark save did not produce a readable record.");
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
        return connection;
    }

    private static BookmarkEntry Read(
        SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)));
}
