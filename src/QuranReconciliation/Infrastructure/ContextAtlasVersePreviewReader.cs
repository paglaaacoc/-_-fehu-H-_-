using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace QuranReconciliation.Infrastructure;

// Isolated, SELECT-only Context Atlas preview. Never opens owner research.sqlite.
internal sealed class ContextAtlasVersePreviewReader
{
    private readonly string _path;

    internal ContextAtlasVersePreviewReader(string? path = null)
    {
        _path = path ?? AppPaths.CorpusDatabase;
    }

    internal IReadOnlyList<ContextAtlasPreviewVerse> ReadRange(
        int surah, int start, int end, int englishId, int bengaliId)
    {
        if (surah is < 1 or > 114 || start < 1 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(start),
                "A valid Surah-local ayah range is required.");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        var verses = new List<ContextAtlasPreviewVerse>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT verse_key, verse_number, text_uthmani,
                       text_indopak, text_indopak_nastaleeq
                FROM verses
                WHERE chapter_number=$surah
                  AND verse_number BETWEEN $start AND $end
                ORDER BY verse_number;
                """;
            command.Parameters.AddWithValue("$surah", surah);
            command.Parameters.AddWithValue("$start", start);
            command.Parameters.AddWithValue("$end", end);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                verses.Add(new ContextAtlasPreviewVerse(
                    reader.GetString(0), reader.GetInt32(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    null, null));
            }
        }

        if (verses.Count != end - start + 1)
        {
            throw new InvalidDataException("Canonical Arabic evidence is incomplete for this passage.");
        }

        if (englishId <= 0 && bengaliId <= 0)
        {
            return verses;
        }

        var translations = new Dictionary<string, (string? English, string? Bengali)>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT t.verse_key, t.resource_id, t.raw_text
                FROM translations t
                JOIN verses v ON v.verse_key=t.verse_key
                WHERE v.chapter_number=$surah
                  AND v.verse_number BETWEEN $start AND $end
                  AND t.resource_id IN ($en, $bn)
                ORDER BY v.verse_number;
                """;
            command.Parameters.AddWithValue("$surah", surah);
            command.Parameters.AddWithValue("$start", start);
            command.Parameters.AddWithValue("$end", end);
            command.Parameters.AddWithValue("$en", englishId);
            command.Parameters.AddWithValue("$bn", bengaliId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                int resourceId = reader.GetInt32(1);
                string value = AsPlainText(reader.IsDBNull(2) ? "" : reader.GetString(2));
                translations.TryGetValue(key, out var previous);
                translations[key] = resourceId == englishId
                    ? (value, previous.Bengali)
                    : (previous.English, value);
            }
        }

        return verses.Select(x =>
        {
            translations.TryGetValue(x.VerseKey, out var translation);
            return x with { English = translation.English, Bengali = translation.Bengali };
        }).ToList();
    }

    private static string AsPlainText(string text)
    {
        string withoutTags = Regex.Replace(text, "<[^>]*>", " ");
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"[ \t]{2,}", " ").Trim();
    }
}
