using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace QuranReconciliation.Infrastructure;

internal enum CorpusSearchLanguage { All, Arabic, English, Bangla }
internal enum CorpusSearchMatch { Exact, Normalized }

internal sealed record CorpusSearchQuery(
    string Text,
    CorpusSearchLanguage Language = CorpusSearchLanguage.All,
    string Script = "all",
    bool IncludeNormalized = true,
    int Limit = 50,
    int Offset = 0,
    int? TranslationSourceId = null);

internal sealed record CorpusSearchHit(
    string VerseKey,
    int ChapterNumber,
    int VerseNumber,
    string SourceKind,
    int? TranslationSourceId,
    string SourceName,
    string Language,
    string Script,
    string DisplayText,
    CorpusSearchMatch Match);

internal sealed record CorpusSearchPage(
    IReadOnlyList<CorpusSearchHit> Hits,
    bool HasMore,
    int Offset,
    int Limit);

/// <summary>
/// Fully read-only search over the separately built C1 index. Canonical
/// Qur'an and research databases must never be changed by a query.
/// </summary>
internal sealed class CorpusSearchRepository
{
    internal const string SupportedSchema = "1";
    internal const string ExpectedCorpusSha256 =
        "188e29730b62efaff748e160f73bb2e6665769df4fa779ee1436af939c6e7862";
    internal const string ExpectedIndexSha256 =
        "ab2be67067172e17592a4bc981b5930ee5e45e3c7c313f332f78d151f198d830";

    private readonly string _indexPath;

    internal CorpusSearchRepository(
        string corpusPath, string indexPath,
        string expectedCorpusSha256 = ExpectedCorpusSha256,
        string expectedIndexSha256 = ExpectedIndexSha256)
    {
        if (!File.Exists(corpusPath))
            throw new FileNotFoundException("Pinned canonical Qur'an corpus is missing.", corpusPath);
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("Prebuilt read-only Qur'an search index is missing.", indexPath);
        if (Path.GetFullPath(corpusPath).Equals(Path.GetFullPath(indexPath),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A search index must be separate from the canonical corpus.");

        string sourceHash = Hash(corpusPath);
        string indexHash = Hash(indexPath);
        if (!sourceHash.Equals(expectedCorpusSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The canonical corpus does not match the pinned search source.");
        if (!indexHash.Equals(expectedIndexSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Search index bytes do not match the verified pinned artifact.");

        _indexPath = indexPath;
        using var db = OpenReadOnly();
        string Meta(string key)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT value FROM search_index_meta WHERE key=$key;";
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() as string ??
                throw new InvalidDataException("Missing search index metadata: " + key);
        }
        if (Meta("schema") != SupportedSchema ||
            Meta("normalizer") != "CorpusSearchTextNormalizer-v" + CorpusSearchTextNormalizer.Version ||
            !Meta("corpus_sha256").Equals(sourceHash, StringComparison.OrdinalIgnoreCase) ||
            Meta("verse_count") != "6236" ||
            Meta("arabic_documents") != "18708" ||
            Meta("translation_documents") != "81068" ||
            Meta("document_count") != "99776")
        {
            throw new InvalidDataException("Search index schema, coverage, or corpus provenance is inconsistent.");
        }
    }

    internal CorpusSearchPage Search(CorpusSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query), "Limit must be from 1 to 100.");
        if (query.Offset is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(query), "Offset is out of bounds.");
        if (query.TranslationSourceId is < 0)
            throw new ArgumentOutOfRangeException(nameof(query), "Translation source ID must be nonnegative.");

        string script = query.Script?.Trim().ToLowerInvariant() ?? "all";
        if (script is not ("all" or "uthmani" or "indopak" or "indopak-nastaleeq"))
            throw new ArgumentException("Unsupported canonical Arabic script filter.", nameof(query));

        string text = (query.Text ?? string.Empty).Trim();
        if (text.Length is < 1 or > 256 || text.Any(char.IsControl))
            throw new ArgumentException("Search input must contain 1–256 non-control characters.", nameof(query));
        string normArabic = CorpusSearchTextNormalizer.Normalize(text, "arabic");
        string normOther = CorpusSearchTextNormalizer.Normalize(text, "english");
        if (normArabic.Length == 0 && normOther.Length == 0)
            return new CorpusSearchPage(Array.Empty<CorpusSearchHit>(), false, query.Offset, query.Limit);

        string language = query.Language switch
        {
            CorpusSearchLanguage.All => "all",
            CorpusSearchLanguage.Arabic => "arabic",
            CorpusSearchLanguage.English => "english",
            CorpusSearchLanguage.Bangla => "bangla",
            _ => throw new ArgumentOutOfRangeException(nameof(query), "Unsupported search language.")
        };

        // Parameterized INSTR deliberately preserves substring occurrences,
        // including suffixes and inflections which an FTS token lookup may
        // omit. No SQL LIKE wildcards, user-generated SQL, or ranking guesses.
        const string match = """
            instr(d.original_text, $exact) > 0 OR
            ($include_normalized = 1 AND instr(d.normalized_text,
                CASE WHEN d.language='arabic' THEN $norm_ar ELSE $norm_other END) > 0
                AND length(CASE WHEN d.language='arabic' THEN $norm_ar ELSE $norm_other END) > 0)
            """;
        using var db = OpenReadOnly();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"""
            SELECT d.verse_key, d.chapter_number, d.verse_number,
                   d.source_kind, d.source_id, d.source_name,
                   d.language, d.script, d.original_text,
                   CASE WHEN instr(d.original_text, $exact) > 0 THEN 0 ELSE 1 END AS match_rank
            FROM search_documents AS d
            WHERE ({match})
              AND ($lang='all' OR d.language=$lang)
              AND ($script='all' OR (d.source_kind='arabic' AND d.script=$script))
              AND ($source_id < 0 OR
                   (d.source_kind='translation' AND d.source_id=$source_id))
            ORDER BY match_rank ASC, d.chapter_number ASC, d.verse_number ASC,
                     d.source_kind ASC, d.language ASC, d.source_id ASC,
                     d.script ASC, d.id ASC
            LIMIT $take OFFSET $skip;
            """;
        cmd.Parameters.AddWithValue("$exact", text);
        cmd.Parameters.AddWithValue("$norm_ar", normArabic);
        cmd.Parameters.AddWithValue("$norm_other", normOther);
        cmd.Parameters.AddWithValue("$include_normalized", query.IncludeNormalized ? 1 : 0);
        cmd.Parameters.AddWithValue("$lang", language);
        cmd.Parameters.AddWithValue("$script", script);
        cmd.Parameters.AddWithValue("$source_id", query.TranslationSourceId ?? -1);
        cmd.Parameters.AddWithValue("$take", query.Limit + 1);
        cmd.Parameters.AddWithValue("$skip", query.Offset);

        var hits = new List<CorpusSearchHit>(query.Limit + 1);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string kind = reader.GetString(3);
            string original = reader.GetString(8);
            hits.Add(new CorpusSearchHit(
                reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2),
                kind, reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7),
                kind == "translation"
                    ? CorpusSearchTextNormalizer.PlainTranslationText(original) : original,
                reader.GetInt32(9) == 0 ? CorpusSearchMatch.Exact : CorpusSearchMatch.Normalized));
        }
        bool hasMore = hits.Count > query.Limit;
        if (hasMore) hits.RemoveAt(hits.Count - 1);
        return new CorpusSearchPage(hits, hasMore, query.Offset, query.Limit);
    }

    private SqliteConnection OpenReadOnly()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _indexPath, Mode = SqliteOpenMode.ReadOnly,
                Pooling = false, Cache = SqliteCacheMode.Private
            }.ToString());
        connection.Open();
        return connection;
    }

    private static string Hash(string file)
    {
        using var input = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
