using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using System.Security.Cryptography;

static void Need(bool condition, string claim)
{
    if (!condition) throw new InvalidDataException(claim);
}
Need(CorpusSearchTextNormalizer.Normalize("ٱللَّهِ", "arabic") == "الله",
    "Uthmani Allah normalization failed.");
Need(CorpusSearchTextNormalizer.Normalize("اللّٰهِ", "arabic") == "الله",
    "IndoPak Allah normalization failed.");
Need(CorpusSearchTextNormalizer.Normalize("الرَّحْمَـٰنِ", "arabic") == "الرحمن",
    "Arabic vowel and tatweel normalization failed.");
Need(CorpusSearchTextNormalizer.Normalize("বাংলা", "bangla") == "বাংলা",
    "Bangla vowel marks were removed.");
Need(CorpusSearchTextNormalizer.Normalize("The Merciful!", "english") ==
    "the merciful", "English normalization failed.");
Need(CorpusSearchTextNormalizer.PlainTranslationText(
    "Allah &amp; Mercy <sup>1</sup>") == "Allah & Mercy  1 ",
    "HTML must not be treated as source words.");

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CorpusSearchIndexProbe <corpus.sqlite> <corpus-search.sqlite>");
    return 64;
}
string corpus = Path.GetFullPath(args[0]);
string index = Path.GetFullPath(args[1]);
using var db = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = index, Mode = SqliteOpenMode.ReadOnly
}.ToString());
db.Open();

string Scalar(string sql)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    return Convert.ToString(cmd.ExecuteScalar()) ??
        throw new InvalidDataException("Missing required index value.");
}
using var original = File.OpenRead(corpus);
string sha = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
Need(Scalar("SELECT value FROM search_index_meta WHERE key='corpus_sha256'") == sha,
    "Search index source SHA mismatches the verified corpus.");
Need(Scalar("SELECT value FROM search_index_meta WHERE key='schema'") == "1",
    "Unexpected search schema.");
Need(Scalar("SELECT value FROM search_index_meta WHERE key='verse_count'") == "6236",
    "Canonical verse count mismatch.");
Need(Scalar("SELECT COUNT(*) FROM search_documents WHERE source_kind='arabic'") ==
    "18708", "Arabic variant count mismatch.");
Need(Scalar("SELECT COUNT(*) FROM search_documents WHERE source_kind='translation'") ==
    "81068", "Accepted corpus translation coverage mismatch.");
Need(Scalar("SELECT COUNT(*) FROM search_documents WHERE source_kind='tafsir'") ==
    "0", "Deferred tafsir must not appear in search.");
Need(Scalar("SELECT COUNT(*) FROM search_fts") ==
    Scalar("SELECT COUNT(*) FROM search_documents"), "FTS row count mismatch.");

using (var check = db.CreateCommand())
{
    check.CommandText = """
        SELECT COUNT(*) FROM search_fts f
        JOIN search_documents d ON d.id = f.rowid
        WHERE search_fts MATCH $phrase
          AND d.verse_key='1:1' AND d.language='arabic'
          AND d.script='uthmani';
        """;
    check.Parameters.AddWithValue("$phrase", "الله");
    Need((long)check.ExecuteScalar()! > 0,
        "Unvocalized Arabic should find vocalized canonical text.");
}
Console.WriteLine("Search index golden and corpus coverage checks PASS.");
return 0;
