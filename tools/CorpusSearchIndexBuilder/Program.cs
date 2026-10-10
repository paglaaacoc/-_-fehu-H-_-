using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using System.Security.Cryptography;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: CorpusSearchIndexBuilder <read-only corpus.sqlite> <output corpus-search.sqlite>");
    return 64;
}

string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (!File.Exists(input))
    throw new FileNotFoundException("Authoritative corpus not found.", input);
if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Search output must never overwrite the canonical corpus.");

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
string staged = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
try
{
    string originalSha = Sha256(input);
    long verses = 0, arabicDocs = 0, translationDocs = 0;
    using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = input, Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Private
    }.ToString()))
    using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = staged, Mode = SqliteOpenMode.ReadWriteCreate,
        // Windows must release the staged file handle before atomic rename.
        Pooling = false
    }.ToString()))
    {
        source.Open();
        target.Open();

        using (var schema = target.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA journal_mode = DELETE;
                PRAGMA synchronous = FULL;
                CREATE TABLE search_index_meta (
                    key TEXT PRIMARY KEY, value TEXT NOT NULL
                );
                CREATE TABLE search_documents (
                    id INTEGER PRIMARY KEY,
                    verse_key TEXT NOT NULL,
                    chapter_number INTEGER NOT NULL,
                    verse_number INTEGER NOT NULL,
                    source_kind TEXT NOT NULL CHECK(source_kind IN ('arabic','translation')),
                    source_id INTEGER,
                    source_name TEXT NOT NULL,
                    language TEXT NOT NULL,
                    script TEXT NOT NULL,
                    original_text TEXT NOT NULL,
                    normalized_text TEXT NOT NULL
                );
                CREATE INDEX idx_search_documents_location
                  ON search_documents(chapter_number, verse_number, source_kind);
                CREATE INDEX idx_search_documents_source
                  ON search_documents(language, source_id);
                CREATE VIRTUAL TABLE search_fts USING fts5(
                    normalized_text,
                    content='search_documents',
                    content_rowid='id',
                    tokenize="unicode61 remove_diacritics 0 categories 'L* N* Mn Mc Me'"
                );
                """;
            schema.ExecuteNonQuery();
        }

        using var transaction = target.BeginTransaction();
        using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO search_documents
              (verse_key,chapter_number,verse_number,source_kind,source_id,
               source_name,language,script,original_text,normalized_text)
            VALUES
              ($key,$chapter,$ayah,$kind,$source,$name,$lang,$script,$original,$normalized);
            """;
        foreach (string parameter in new[]
        {
            "$key","$chapter","$ayah","$kind","$source",
            "$name","$lang","$script","$original","$normalized"
        })
            insert.Parameters.Add(new SqliteParameter(parameter, DBNull.Value));
        insert.Prepare();

        void Add(string key, int chapter, int ayah, string kind,
                 int? sourceId, string sourceName, string language,
                 string script, string raw)
        {
            string plain = kind == "translation"
                ? CorpusSearchTextNormalizer.PlainTranslationText(raw)
                : raw;
            string normalized = CorpusSearchTextNormalizer.Normalize(plain, language);
            if (normalized.Length == 0)
                throw new InvalidDataException(
                    $"Empty normalized search text at {key} ({kind}, {sourceName}).");

            insert.Parameters["$key"].Value = key;
            insert.Parameters["$chapter"].Value = chapter;
            insert.Parameters["$ayah"].Value = ayah;
            insert.Parameters["$kind"].Value = kind;
            insert.Parameters["$source"].Value = (object?)sourceId ?? DBNull.Value;
            insert.Parameters["$name"].Value = sourceName;
            insert.Parameters["$lang"].Value = language;
            insert.Parameters["$script"].Value = script;
            insert.Parameters["$original"].Value = raw;
            insert.Parameters["$normalized"].Value = normalized;
            insert.ExecuteNonQuery();
        }

        using (var select = source.CreateCommand())
        {
            select.CommandText = """
                SELECT verse_key,chapter_number,verse_number,
                       text_uthmani,text_indopak,text_indopak_nastaleeq
                FROM verses ORDER BY chapter_number,verse_number;
                """;
            using var rows = select.ExecuteReader();
            while (rows.Read())
            {
                string key = rows.GetString(0);
                int chapter = rows.GetInt32(1), ayah = rows.GetInt32(2);
                Add(key, chapter, ayah, "arabic", null,
                    "Uthmani", "arabic", "uthmani", rows.GetString(3));
                Add(key, chapter, ayah, "arabic", null,
                    "IndoPak", "arabic", "indopak", rows.GetString(4));
                Add(key, chapter, ayah, "arabic", null,
                    "IndoPak Nastaleeq", "arabic", "indopak-nastaleeq",
                    rows.GetString(5));
                verses++;
                arabicDocs += 3;
            }
        }

        if (verses != 6236)
            throw new InvalidDataException(
                $"Expected exactly 6,236 canonical Ayat, found {verses}.");

        using (var select = source.CreateCommand())
        {
            select.CommandText = """
                SELECT t.verse_key,v.chapter_number,v.verse_number,
                       r.resource_id,r.name,r.language_name,t.raw_text
                FROM translations t
                JOIN verses v ON v.verse_key = t.verse_key
                JOIN resources r
                  ON r.resource_kind = 'translation'
                 AND r.resource_id = t.resource_id
                WHERE lower(r.language_name) IN ('english','bengali','bangla')
                ORDER BY v.chapter_number,v.verse_number,r.resource_id;
                """;
            using var rows = select.ExecuteReader();
            while (rows.Read())
            {
                string language = rows.GetString(5).ToLowerInvariant();
                if (language == "bengali") language = "bangla";
                Add(rows.GetString(0), rows.GetInt32(1), rows.GetInt32(2),
                    "translation", rows.GetInt32(3), rows.GetString(4),
                    language, "", rows.GetString(6));
                translationDocs++;
            }
        }
        if (translationDocs == 0)
            throw new InvalidDataException("No English/Bangla translations were indexed.");

        long corpusTranslationCount;
        using (var count = source.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM translations;";
            corpusTranslationCount = (long)count.ExecuteScalar()!;
        }
        if (translationDocs != corpusTranslationCount)
            throw new InvalidDataException(
                $"Unindexed translation records: {translationDocs}/{corpusTranslationCount}. " +
                "Check source languages and foreign keys before publishing an index.");

        using (var update = target.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                INSERT INTO search_fts(search_fts) VALUES ('rebuild');
                INSERT INTO search_index_meta VALUES ('schema','1');
                INSERT INTO search_index_meta VALUES ('normalizer',
                    'CorpusSearchTextNormalizer-v1');
                INSERT INTO search_index_meta VALUES ('corpus_sha256',$sha);
                INSERT INTO search_index_meta VALUES ('verse_count',$verses);
                INSERT INTO search_index_meta VALUES ('arabic_documents',$arabic);
                INSERT INTO search_index_meta VALUES ('translation_documents',$translations);
                INSERT INTO search_index_meta VALUES ('document_count',$total);
                """;
            update.Parameters.AddWithValue("$sha", originalSha);
            update.Parameters.AddWithValue("$verses", verses.ToString());
            update.Parameters.AddWithValue("$arabic", arabicDocs.ToString());
            update.Parameters.AddWithValue("$translations", translationDocs.ToString());
            update.Parameters.AddWithValue("$total", (arabicDocs + translationDocs).ToString());
            update.ExecuteNonQuery();
        }

        using (var check = target.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM search_fts;";
            long indexed = (long)check.ExecuteScalar()!;
            if (indexed != arabicDocs + translationDocs)
                throw new InvalidDataException(
                    $"Full-text index count mismatch: {indexed}.");
        }
        transaction.Commit();
        Console.WriteLine(
            $"Index staged: {verses} Ayat, {arabicDocs} Arabic script rows, " +
            $"{translationDocs} translations; source SHA-256 {originalSha}");
    }
    // Reject a source corpus changed while the builder was reading it.
    if (!string.Equals(Sha256(input), Sha256FromIndex(staged),
        StringComparison.OrdinalIgnoreCase))
        throw new IOException("Canonical corpus changed during index generation.");

    File.Move(staged, output, overwrite: true);
    Console.WriteLine("Verified read-only deployment index: " + output);
    return 0;
}
finally
{
    if (File.Exists(staged)) File.Delete(staged);
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static string Sha256FromIndex(string path)
{
    using var db = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = path, Mode = SqliteOpenMode.ReadOnly,
        // Do not retain a pooled handle on the staged file when verifying its digest.
        Pooling = false
    }.ToString());
    db.Open();
    using var read = db.CreateCommand();
    read.CommandText =
        "SELECT value FROM search_index_meta WHERE key = 'corpus_sha256';";
    return (string)(read.ExecuteScalar() ??
        throw new InvalidDataException("Missing corpus SHA in search index."));
}
