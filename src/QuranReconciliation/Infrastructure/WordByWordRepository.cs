using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class WordByWordRepository
{
    private readonly string _databasePath;

    internal WordByWordRepository(
        string? databasePath = null)
    {
        _databasePath =
            databasePath ??
            AppPaths.WordByWordDatabase;
    }

    internal bool IsAvailable =>
        File.Exists(_databasePath);

    internal IReadOnlyList<WordByWordEntry>
        GetVerse(string verseKey)
    {
        if (!File.Exists(_databasePath))
        {
            return Array.Empty<WordByWordEntry>();
        }

        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            };

        using var connection =
            new SqliteConnection(
                builder.ToString());
        connection.Open();

        using var command =
            connection.CreateCommand();
        command.CommandText = """
        SELECT word_id,
               verse_key,
               position,
               text_uthmani,
               translation_en,
               transliteration
        FROM words
        WHERE verse_key=$verse
        ORDER BY position, word_id;
        """;
        command.Parameters.AddWithValue(
            "$verse",
            verseKey);

        using var reader =
            command.ExecuteReader();

        var result =
            new List<WordByWordEntry>();

        while (reader.Read())
        {
            result.Add(
                new WordByWordEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)));
        }

        return result;
    }
}
