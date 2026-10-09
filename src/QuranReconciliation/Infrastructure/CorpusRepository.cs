using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace QuranReconciliation.Infrastructure;

internal sealed partial class CorpusRepository
{
    private readonly string _databasePath;

    internal CorpusRepository()
    {
        _databasePath = AppPaths.CorpusDatabase;

        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException(
                "Verified corpus database is missing from the portable app folder.",
                _databasePath);
        }
    }

    internal IReadOnlyList<ChapterSummary> GetChapters()
    {
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT chapter_number, verses_count, name_simple, translated_name_bn
        FROM chapters
        ORDER BY chapter_number;
        """;

        using var reader = command.ExecuteReader();
        var result = new List<ChapterSummary>(114);

        while (reader.Read())
        {
            result.Add(new ChapterSummary(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? $"Surah {reader.GetInt32(0)}" : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        if (result.Count != 114)
        {
            throw new InvalidDataException(
                $"Corpus chapter integrity failure: expected 114, found {result.Count}.");
        }

        return result;
    }

    internal IReadOnlyList<ResourceSummary> GetResources()
    {
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT resource_kind, resource_id, display_type, language_name, name, author_name
        FROM resources
        ORDER BY
            CASE lower(language_name)
                WHEN 'english' THEN 0
                WHEN 'bengali' THEN 1
                WHEN 'bangla' THEN 1
                ELSE 2
            END,
            CASE resource_kind WHEN 'translation' THEN 0 ELSE 1 END,
            resource_id;
        """;

        using var reader = command.ExecuteReader();
        var result = new List<ResourceSummary>();

        while (reader.Read())
        {
            result.Add(new ResourceSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return result;
    }

    internal IReadOnlyList<VerseBundle> GetChapter(
        int chapterNumber,
        IReadOnlyCollection<int> translationIds,
        IReadOnlyCollection<int> tafsirIds)
    {
        using var connection = OpenReadOnly();

        var verses = ReadVerses(connection, chapterNumber);
        var translations = ReadTranslations(connection, chapterNumber, translationIds);
        var tafsirsByAnchor = IndexTafsirsByAnchor(
            ReadTafsirs(connection, chapterNumber, tafsirIds));

        return verses.Select(v => new VerseBundle
        {
            VerseKey = v.VerseKey,
            VerseNumber = v.VerseNumber,
            Uthmani = v.Uthmani,
            IndoPak = v.IndoPak,
            IndoPakNastaleeq = v.IndoPakNastaleeq,
            Translations = translations.TryGetValue(v.VerseKey, out var t)
                ? t
                : Array.Empty<SourceText>(),
            Tafsirs = tafsirsByAnchor.TryGetValue(v.VerseKey, out var tf)
                ? tf
                : Array.Empty<SourceText>()
        }).ToList();
    }

    private SqliteConnection OpenReadOnly()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static List<BaseVerse> ReadVerses(SqliteConnection connection, int chapterNumber)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT verse_key, verse_number, text_uthmani, text_indopak, text_indopak_nastaleeq
        FROM verses
        WHERE chapter_number = $chapter
        ORDER BY verse_number;
        """;
        command.Parameters.AddWithValue("$chapter", chapterNumber);

        using var reader = command.ExecuteReader();
        var rows = new List<BaseVerse>();

        while (reader.Read())
        {
            rows.Add(new BaseVerse(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    private static Dictionary<string, IReadOnlyList<SourceText>> ReadTranslations(
        SqliteConnection connection,
        int chapterNumber,
        IReadOnlyCollection<int> ids)
    {
        var result = new Dictionary<string, List<SourceText>>(StringComparer.Ordinal);

        if (ids.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<SourceText>>();
        }

        using var command = connection.CreateCommand();
        string idSql = AddIdParameters(command, ids, "$tr");

        command.CommandText = $"""
        SELECT t.verse_key, t.resource_id, r.name, r.display_type, r.language_name,
               t.raw_text, t.foot_notes_json
        FROM translations t
        JOIN resources r
          ON r.resource_kind='translation' AND r.resource_id=t.resource_id
        JOIN verses v ON v.verse_key=t.verse_key
        WHERE v.chapter_number=$chapter
          AND t.resource_id IN ({idSql})
        ORDER BY
            v.verse_number,
            CASE lower(r.language_name)
                WHEN 'english' THEN 0
                WHEN 'bengali' THEN 1
                WHEN 'bangla' THEN 1
                ELSE 2
            END,
            t.resource_id;
        """;
        command.Parameters.AddWithValue("$chapter", chapterNumber);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            string verseKey = reader.GetString(0);
            if (!result.TryGetValue(verseKey, out List<SourceText>? list))
            {
                list = new List<SourceText>();
                result.Add(verseKey, list);
            }

            TranslationPresentation presentation =
                ParseTranslationPresentation(
                    reader.GetString(5),
                    reader.IsDBNull(6)
                        ? null
                        : reader.GetString(6));

            list.Add(new SourceText(
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                presentation.Text,
                StoredFootnotes: presentation.Footnotes));
        }

        return result.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<SourceText>)x.Value,
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<TafsirRange> ReadTafsirs(
        SqliteConnection connection,
        int chapterNumber,
        IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<TafsirRange>();
        }

        using var command = connection.CreateCommand();
        string idSql = AddIdParameters(command, ids, "$tf");

        command.CommandText = $"""
        SELECT
            tf.resource_id,
            r.name,
            r.display_type,
            r.language_name,
            tf.verse_key,
            tf.group_verse_key_from,
            tf.group_verse_key_to,
            tf.raw_text
        FROM tafsir_records tf
        JOIN resources r
          ON r.resource_kind='tafsir' AND r.resource_id=tf.resource_id
        WHERE tf.resource_id IN ({idSql})
          AND (
                tf.verse_key LIKE $prefix
             OR tf.group_verse_key_from LIKE $prefix
             OR tf.group_verse_key_to LIKE $prefix
          )
        ORDER BY tf.resource_id, tf.source_row_id;
        """;
        command.Parameters.AddWithValue("$prefix", chapterNumber + ":%");

        using var reader = command.ExecuteReader();
        var result = new List<TafsirRange>();

        while (reader.Read())
        {
            string? verse = reader.IsDBNull(4) ? null : reader.GetString(4);
            string? from = reader.IsDBNull(5) ? verse : reader.GetString(5);
            string? to = reader.IsDBNull(6) ? verse : reader.GetString(6);

            result.Add(new TafsirRange(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                from,
                to,
                ToPlainText(reader.GetString(7))));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SourceText>> IndexTafsirsByAnchor(
        IReadOnlyList<TafsirRange> ranges)
    {
        var indexed = new Dictionary<string, List<SourceText>>(
            StringComparer.Ordinal);

        foreach (TafsirRange range in ranges)
        {
            string? anchor =
                range.FromVerseKey ??
                range.ToVerseKey;

            if (string.IsNullOrWhiteSpace(anchor))
            {
                continue;
            }

            string? scope =
                range.FromVerseKey is not null &&
                range.ToVerseKey is not null &&
                !string.Equals(
                    range.FromVerseKey,
                    range.ToVerseKey,
                    StringComparison.Ordinal)
                    ? $"{range.FromVerseKey}–{range.ToVerseKey}"
                    : null;

            if (!indexed.TryGetValue(anchor, out List<SourceText>? list))
            {
                list = [];
                indexed.Add(anchor, list);
            }

            list.Add(new SourceText(
                range.ResourceId,
                range.ResourceName,
                range.DisplayType,
                range.LanguageName,
                range.Text,
                scope));
        }

        return indexed.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<SourceText>)x.Value,
            StringComparer.Ordinal);
    }

    private static TranslationPresentation ParseTranslationPresentation(
        string rawText,
        string? footNotesJson)
    {
        var markerById =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        var markerOrder = new List<string>();

        string withMarkers =
            TranslationFootnoteMarkerRegex().Replace(
                rawText ?? string.Empty,
                match =>
                {
                    string id =
                        match.Groups["id"].Value;

                    string marker =
                        WebUtility.HtmlDecode(
                            TagRegex().Replace(
                                match.Groups["marker"].Value,
                                string.Empty)).Trim();

                    if (string.IsNullOrWhiteSpace(marker))
                    {
                        marker =
                            (markerOrder.Count + 1)
                            .ToString(
                                System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (!markerById.ContainsKey(id))
                    {
                        markerById.Add(id, marker);
                        markerOrder.Add(id);
                    }

                    return $" [{marker}]";
                });

        string text = ToPlainText(withMarkers);

        if (string.IsNullOrWhiteSpace(footNotesJson) ||
            footNotesJson == "{}" ||
            footNotesJson == "null")
        {
            return new TranslationPresentation(
                text,
                Array.Empty<SourceFootnote>());
        }

        var bodies =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(footNotesJson);

            if (document.RootElement.ValueKind ==
                JsonValueKind.Object)
            {
                foreach (JsonProperty property in
                    document.RootElement.EnumerateObject())
                {
                    string body =
                        property.Value.ValueKind ==
                        JsonValueKind.String
                            ? property.Value.GetString() ??
                                string.Empty
                            : property.Value.ToString();

                    bodies[property.Name] =
                        ToPlainText(body);
                }
            }
        }
        catch (JsonException)
        {
            // Preserve the source text even if a malformed optional
            // footnote payload is encountered. Corpus bytes stay untouched.
        }

        var footnotes =
            new List<SourceFootnote>();

        var added =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (string id in markerOrder)
        {
            if (!bodies.TryGetValue(id, out string? body) ||
                string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            footnotes.Add(
                new SourceFootnote(
                    markerById[id],
                    body));

            added.Add(id);
        }

        foreach ((string id, string body) in bodies)
        {
            if (added.Contains(id) ||
                string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            string marker =
                markerById.TryGetValue(id, out string? known)
                    ? known
                    : (footnotes.Count + 1)
                        .ToString(
                            System.Globalization.CultureInfo.InvariantCulture);

            footnotes.Add(
                new SourceFootnote(
                    marker,
                    body));
        }

        return new TranslationPresentation(
            text,
            footnotes);
    }

    private static string AddIdParameters(
        SqliteCommand command,
        IReadOnlyCollection<int> ids,
        string prefix)
    {
        var names = new List<string>(ids.Count);
        int index = 0;

        foreach (int id in ids.Distinct().OrderBy(x => x))
        {
            string name = prefix + index++;
            names.Add(name);
            command.Parameters.AddWithValue(name, id);
        }

        return string.Join(",", names);
    }

    private static (int Chapter, int Ayah) ParseVerseKey(string key)
    {
        string[] parts = key.Split(':', 2);
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static string ToPlainText(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        string text = BreakRegex().Replace(source, "\n");
        text = TagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = MultiBlankLineRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<sup\s+foot_note\s*=\s*['""]?(?<id>\d+)['""]?\s*>(?<marker>.*?)</sup>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TranslationFootnoteMarkerRegex();

    [GeneratedRegex(@"<\s*(br\s*/?|/p|/div|/li)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex MultiBlankLineRegex();

    private sealed record TranslationPresentation(
        string Text,
        IReadOnlyList<SourceFootnote> Footnotes);

    private sealed record BaseVerse(
        string VerseKey,
        int VerseNumber,
        string Uthmani,
        string IndoPak,
        string IndoPakNastaleeq);

    private sealed record TafsirRange(
        int ResourceId,
        string ResourceName,
        string DisplayType,
        string LanguageName,
        string? FromVerseKey,
        string? ToVerseKey,
        string Text);
}
