using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace QuranReconciliation.Infrastructure;

/// <summary>One accepted Arabic word position, not one result per script or verse.</summary>
internal sealed record CorpusWordOccurrence(string VerseKey, int Position, int WordId, string UthmaniWord);
internal sealed record CorpusOccurrencePage(
    IReadOnlyList<CorpusWordOccurrence> Hits, long WordOccurrences,
    long DistinctAyat, int Offset, int Limit, bool HasMore);
internal sealed record CorpusLemmaCandidate(string Lemma, string Pos, long Occurrences, long DistinctAyat);
internal sealed record CorpusAyahOccurrence(string VerseKey, int FirstWordId,
    IReadOnlyList<int> WordPositions, string UthmaniExample);
internal sealed record CorpusAyahOccurrencePage(
    IReadOnlyList<CorpusAyahOccurrence> Hits, long WordOccurrences,
    long DistinctAyat, int Offset, int Limit, bool HasMore);
internal sealed record CorpusRepeatedAyah(string VerseKey, int Surah, int Ayah);

/// <summary>
/// Optional, read-only research engine backed by QAC-derived positional
/// annotations. It NEVER substitutes displayed canonical Qur'an text.
/// It does not alter the existing C1 literal/normalized search.
/// QAC ©2011 Kais Dukes (GNU GPL); corpus.quran.com/download/
/// </summary>
internal sealed class CorpusOccurrenceRepository
{
    internal const string Schema = "THTRP-morphology-positions-1";
    internal const string CorpusSha = CorpusSearchRepository.ExpectedCorpusSha256;
    internal const string WbwSha =
        "d119f2f113e916a9968f7275f87d31f1f8027ddcf2a5b1ff80e60b7c1b122a31";
    internal const string LemmaIndexSha =
        "96ed3f4c1a761ffa7e9be4018fa0c191fe08e20cd11270ca79c6bba599262c2d";
    internal const string QacBlob = "b91cec6e95d5e0306550b4aedacc7380dc71152a";

    private readonly string _path;
    private readonly string _corpus;
    private Dictionary<string, HashSet<(string Lemma, string Pos)>>? _lookup;

    internal CorpusOccurrenceRepository(string corpus, string wbw, string derived,
        string expectedDerivedHash = LemmaIndexSha)
    {
        if (!File.Exists(corpus) || !File.Exists(wbw) || !File.Exists(derived))
            throw new FileNotFoundException("A pinned canonical or derived word-index file is absent.");
        var paths = new[] { Path.GetFullPath(corpus), Path.GetFullPath(wbw), Path.GetFullPath(derived) };
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidDataException("The morphological index must be separate from both Quran authorities.");
        if (!Hash(corpus).Equals(CorpusSha, StringComparison.OrdinalIgnoreCase) ||
            !Hash(wbw).Equals(WbwSha, StringComparison.OrdinalIgnoreCase) ||
            !Hash(derived).Equals(expectedDerivedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Canonical or derived research evidence SHA-256 mismatch.");

        _path = derived;
        _corpus = corpus;
        using var db = Open(_path);
        string Meta(string key)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT value FROM metadata WHERE key=$key;";
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() as string ??
                throw new InvalidDataException("Missing lemma-index metadata: " + key);
        }
        if (Meta("schema") != Schema ||
            Meta("canonical_corpus_sha256") != CorpusSha ||
            Meta("accepted_wbw_sha256") != WbwSha ||
            Meta("qac_morphology_blob_sha1") != QacBlob ||
            Meta("word_positions") != "77429" ||
            Meta("lemma_positions") != "74608")
            throw new InvalidDataException("Derived lemma-index schema, counts or attribution are inconsistent.");
    }

    internal CorpusOccurrencePage SearchLemma(string lemma, string pos,
        int limit = 50, int offset = 0)
    {
        ValidateText(lemma, 80);
        ValidateText(pos, 20);
        ValidatePage(limit, offset);
        using var db = Open(_path);
        using var c = db.CreateCommand();
        c.CommandText = """
            SELECT COUNT(*),COUNT(DISTINCT verse_key)
            FROM lemma_positions WHERE lemma=$lemma AND pos=$pos;
            """;
        c.Parameters.AddWithValue("$lemma", lemma);
        c.Parameters.AddWithValue("$pos", pos);
        (long total, long ayat) = Count(c);

        using var q = db.CreateCommand();
        q.CommandText = """
            SELECT w.verse_key,w.position,w.word_id,w.text_uthmani
            FROM lemma_positions l JOIN word_positions w
              ON w.verse_key=l.verse_key AND w.position=l.position
            WHERE l.lemma=$lemma AND l.pos=$pos
            ORDER BY w.word_id LIMIT $take OFFSET $skip;
            """;
        q.Parameters.AddWithValue("$lemma", lemma);
        q.Parameters.AddWithValue("$pos", pos);
        return Page(q, total, ayat, limit, offset);
    }

    /// <summary>
    /// One card per Ayah, with all verified lemma positions inside it.
    /// Pages count distinct Ayat, not individual repeated words.
    /// </summary>
    internal CorpusAyahOccurrencePage SearchLemmaAyat(string lemma, string pos,
        int limit = 50, int offset = 0)
    {
        ValidateText(lemma, 80);
        ValidateText(pos, 20);
        ValidatePage(limit, offset);
        using var db = Open(_path);
        using var count = db.CreateCommand();
        count.CommandText = """
            SELECT COUNT(*),COUNT(DISTINCT verse_key)
            FROM lemma_positions WHERE lemma=$lemma AND pos=$pos;
            """;
        count.Parameters.AddWithValue("$lemma", lemma);
        count.Parameters.AddWithValue("$pos", pos);
        var (words, ayat) = Count(count);
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT l.verse_key, MIN(w.word_id), GROUP_CONCAT(l.position, ','),
                   MIN(w.text_uthmani)
            FROM lemma_positions l JOIN word_positions w
              ON l.verse_key=w.verse_key AND l.position=w.position
            WHERE l.lemma=$lemma AND l.pos=$pos
            GROUP BY l.verse_key
            ORDER BY MIN(w.word_id)
            LIMIT $take OFFSET $skip;
            """;
        cmd.Parameters.AddWithValue("$lemma", lemma);
        cmd.Parameters.AddWithValue("$pos", pos);
        cmd.Parameters.AddWithValue("$take", limit + 1);
        cmd.Parameters.AddWithValue("$skip", offset);
        var rows = new List<CorpusAyahOccurrence>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var positions = rd.GetString(2).Split(',')
                .Select(int.Parse).OrderBy(p => p).ToArray();
            rows.Add(new CorpusAyahOccurrence(rd.GetString(0), rd.GetInt32(1),
                positions, rd.GetString(3)));
        }
        bool more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new CorpusAyahOccurrencePage(rows, words, ayat, offset, limit, more);
    }

    /// <summary>
    /// Exact whole-word positional matching; 1–8 adjacent canonical Uthmani
    /// words within one Ayah. No linguistic expansion or fuzzy assumptions.
    /// </summary>
    internal CorpusOccurrencePage SearchExactWords(
        IReadOnlyList<string> exactWords, int limit = 50, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(exactWords);
        if (exactWords.Count is < 1 or > 8)
            throw new ArgumentException("Provide between one and eight exact Uthmani word tokens.");
        foreach (string word in exactWords) ValidateText(word, 128);
        ValidatePage(limit, offset);

        var joins = new List<string>();
        for (int i = 1; i < exactWords.Count; i++)
            joins.Add($"JOIN word_positions w{i} ON w{i}.verse_key=w0.verse_key " +
                      $"AND w{i}.position=w0.position+{i}");
        string where = string.Join(" AND ", Enumerable.Range(0, exactWords.Count)
            .Select(i => $"w{i}.text_uthmani=$word{i}"));
        string body = "FROM word_positions w0 " + string.Join(" ", joins) + " WHERE " + where;
        using var db = Open(_path);
        using var c = db.CreateCommand();
        c.CommandText = "SELECT COUNT(*),COUNT(DISTINCT w0.verse_key) " + body + ";";
        for (int i = 0; i < exactWords.Count; i++)
            c.Parameters.AddWithValue("$word" + i, exactWords[i]);
        (long total, long ayat) = Count(c);
        using var q = db.CreateCommand();
        q.CommandText =
            "SELECT w0.verse_key,w0.position,w0.word_id,w0.text_uthmani " +
            body + " ORDER BY w0.word_id LIMIT $take OFFSET $skip;";
        for (int i = 0; i < exactWords.Count; i++)
            q.Parameters.AddWithValue("$word" + i, exactWords[i]);
        return Page(q, total, ayat, limit, offset);
    }

    /// <summary>One canonical Ayah's full text, compared verbatim in one script.</summary>
    internal IReadOnlyList<CorpusRepeatedAyah> FindIdenticalAyat(
        string verseKey, string script = "uthmani")
    {
        ValidateText(verseKey, 14);
        string col = script switch
        {
            "uthmani" => "text_uthmani",
            "indopak" => "text_indopak",
            "indopak-nastaleeq" => "text_indopak_nastaleeq",
            _ => throw new ArgumentException("Unknown canonical Arabic script.", nameof(script))
        };
        using var db = Open(_corpus);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"""
            SELECT verse_key,chapter_number,verse_number FROM verses
            WHERE {col}=(SELECT {col} FROM verses WHERE verse_key=$key)
            ORDER BY chapter_number,verse_number;
            """;
        cmd.Parameters.AddWithValue("$key", verseKey);
        var results = new List<CorpusRepeatedAyah>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            results.Add(new CorpusRepeatedAyah(rd.GetString(0), rd.GetInt32(1), rd.GetInt32(2)));
        if (results.Count == 0)
            throw new ArgumentException("Unknown canonical Surah:Ayah reference.", nameof(verseKey));
        return results;
    }

    /// <summary>
    /// Conservative suggestions only from normalized ACCEPTED whole Uthmani
    /// words: never invent a lemma by substring or blind letter replacement.
    /// A visible candidate/POS choice is required when ambiguous.
    /// </summary>
    internal IReadOnlyList<CorpusLemmaCandidate> SuggestLemmas(string arabicWord, int max = 12)
    {
        ValidateText(arabicWord, 128);
        if (max is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(max));
        string normalized = CorpusSearchTextNormalizer.Normalize(arabicWord, "arabic");
        if (normalized.Length == 0) return [];
        if (_lookup is null)
        {
            var lookup = new Dictionary<string, HashSet<(string, string)>>(StringComparer.Ordinal);
            using var db = Open(_path);
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT w.text_uthmani,l.lemma,l.pos
                FROM lemma_positions l JOIN word_positions w
                  ON l.verse_key=w.verse_key AND l.position=w.position;
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string key = CorpusSearchTextNormalizer.Normalize(rd.GetString(0), "arabic");
                if (key.Length == 0) continue;
                if (!lookup.TryGetValue(key, out var candidates))
                    lookup[key] = candidates = new HashSet<(string, string)>();
                candidates.Add((rd.GetString(1), rd.GetString(2)));
            }
            _lookup = lookup;
        }
        if (!_lookup.TryGetValue(normalized, out var found)) return [];
        using var db2 = Open(_path);
        var result = new List<CorpusLemmaCandidate>();
        foreach (var (lemma, pos) in found.OrderBy(v => v.Item1, StringComparer.Ordinal)
                     .ThenBy(v => v.Item2, StringComparer.Ordinal).Take(max))
        {
            using var c = db2.CreateCommand();
            c.CommandText = """
                SELECT COUNT(*),COUNT(DISTINCT verse_key)
                FROM lemma_positions WHERE lemma=$lemma AND pos=$pos;
                """;
            c.Parameters.AddWithValue("$lemma", lemma);
            c.Parameters.AddWithValue("$pos", pos);
            var (count, ayat) = Count(c);
            result.Add(new CorpusLemmaCandidate(lemma, pos, count, ayat));
        }
        return result;
    }

    private static CorpusOccurrencePage Page(SqliteCommand cmd,
        long count, long ayat, int limit, int offset)
    {
        cmd.Parameters.AddWithValue("$take", limit + 1);
        cmd.Parameters.AddWithValue("$skip", offset);
        var list = new List<CorpusWordOccurrence>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new CorpusWordOccurrence(rd.GetString(0), rd.GetInt32(1),
                rd.GetInt32(2), rd.GetString(3)));
        bool more = list.Count > limit;
        if (more) list.RemoveAt(list.Count - 1);
        return new CorpusOccurrencePage(list, count, ayat, offset, limit, more);
    }

    private static (long, long) Count(SqliteCommand cmd)
    {
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) throw new InvalidDataException("Expected aggregate count result.");
        return (rd.GetInt64(0), rd.GetInt64(1));
    }

    private static void ValidateText(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > max ||
            text.Any(char.IsControl))
            throw new ArgumentException("Invalid exact research query string.");
    }

    private static void ValidatePage(int limit, int offset)
    {
        if (limit is < 1 or > 100 || offset is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(offset), "Unbounded pages are refused.");
    }

    private static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, Cache = SqliteCacheMode.Private
        }.ToString());
        db.Open();
        return db;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
