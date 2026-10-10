using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;

static void Check(bool condition, string detail)
{
    if (!condition) throw new Exception("C2a FAIL: " + detail);
}
static void Throws<T>(Action work, string detail) where T : Exception
{
    try { work(); }
    catch (T) { return; }
    throw new Exception("C2a expected failure: " + detail);
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CorpusSearchQueryProbe <pinned corpus.sqlite> <pinned corpus-search.sqlite>");
    return 64;
}
string corpus = Path.GetFullPath(args[0]), index = Path.GetFullPath(args[1]);
var engine = new CorpusSearchRepository(corpus, index);
var arabic = engine.Search(new CorpusSearchQuery("الله", CorpusSearchLanguage.Arabic, Limit: 30));
Check(arabic.Hits.Count > 0, "Arabic lookup must return real Ayat");
Check(arabic.Hits.All(h => h.Language == "arabic"), "Arabic filter must exclude translations");
Check(arabic.Hits.All(h => h.ChapterNumber >= 1 && h.VerseNumber >= 1),
    "Invalid canonical verse identity");
Check(arabic.Hits.Any(h => h.Match == CorpusSearchMatch.Normalized),
    "Unvocalized Arabic should reveal normalized hits");
Check(arabic.Hits.Any(h => h.VerseKey == "1:1"), "Bismillah occurrence must not be suppressed");

var literal = engine.Search(new CorpusSearchQuery(
    "ٱللَّهِ", CorpusSearchLanguage.Arabic, "uthmani", IncludeNormalized: false));
Check(literal.Hits.Count > 0 && literal.Hits.All(h => h.Match == CorpusSearchMatch.Exact),
    "Literal search must return only exact original Uthmani text");
Check(literal.Hits.All(h => h.Script == "uthmani"), "Arabic script filter leaked");

var indo = engine.Search(new CorpusSearchQuery(
    "الله", CorpusSearchLanguage.Arabic, "indopak-nastaleeq", Limit: 15));
Check(indo.Hits.Count > 0 && indo.Hits.All(h => h.Script == "indopak-nastaleeq"),
    "IndoPak Nastaleeq source filter leaked");
var english = engine.Search(new CorpusSearchQuery("Allah", CorpusSearchLanguage.English));
Check(english.Hits.Count > 0 && english.Hits.All(h => h.Language == "english" &&
    h.SourceKind == "translation" && h.TranslationSourceId != null),
    "English source attribution missing");

var bangla = engine.Search(new CorpusSearchQuery("আল্লাহ", CorpusSearchLanguage.Bangla));
Check(bangla.Hits.Count > 0 && bangla.Hits.All(h => h.Language == "bangla" &&
    h.SourceKind == "translation"), "Bangla language filter leaked");
Check(bangla.Hits.Any(h => h.DisplayText.Contains("আল্লাহ", StringComparison.Ordinal)),
    "Bangla result must preserve the original readable spelling");

var paged = engine.Search(new CorpusSearchQuery("الله", CorpusSearchLanguage.Arabic, Limit: 7));
var next = engine.Search(new CorpusSearchQuery("الله", CorpusSearchLanguage.Arabic, Limit: 7, Offset: 7));
Check(paged.Hits.Count == 7 && paged.HasMore && next.Hits.Count == 7,
    "Multi-page Arabic result pagination failed");
static string Identity(CorpusSearchHit h) =>
    h.VerseKey + "|" + h.SourceKind + "|" + h.SourceName + "|" + h.Script;
Check(!paged.Hits.Select(Identity).Intersect(next.Hits.Select(Identity)).Any(),
    "Overlapping pagination duplicated a source occurrence");
var repeat = engine.Search(new CorpusSearchQuery("الله", CorpusSearchLanguage.Arabic, Limit: 7));
Check(paged.Hits.Select(Identity).SequenceEqual(repeat.Hits.Select(Identity)),
    "Query ordering must be deterministic");

var exactAll = engine.Search(new CorpusSearchQuery(
    "الله", CorpusSearchLanguage.Arabic, IncludeNormalized: false, Limit: 30));
Check(exactAll.Hits.All(h => h.Match == CorpusSearchMatch.Exact),
    "Normalized matches leaked into exact-only search");
Check(!engine.Search(new CorpusSearchQuery("%", IncludeNormalized: true)).Hits.Any(),
    "Punctuation-only search must not turn into a wildcard.");
Check(!engine.Search(new CorpusSearchQuery("' OR 1=1 --", IncludeNormalized: false)).Hits.Any(),
    "SQL-like query must be treated as literal user text.");
Throws<ArgumentException>(() => engine.Search(new CorpusSearchQuery("X", Limit: 101)),
    "Unbounded pages must be refused");
Throws<ArgumentException>(() => engine.Search(new CorpusSearchQuery("X", Script: "bad")),
    "Unknown script filter must be refused");
Throws<ArgumentException>(() => engine.Search(new CorpusSearchQuery("X", Offset: -1)),
    "Negative offset must be refused");
Throws<InvalidDataException>(() => new CorpusSearchRepository(
    corpus, index, expectedIndexSha256: new string('0',64)),
    "Tampered index SHA must fail closed");
Throws<InvalidDataException>(() => new CorpusSearchRepository(
    corpus, index, expectedCorpusSha256: new string('0',64)),
    "Mismatched corpus SHA must fail closed");

// Complete-occurrence proof on a bounded, repeated Arabic phrase: compare
// all paginated source hits with an independent SQL count.
var all = new List<CorpusSearchHit>();
int offset = 0;
while (true)
{
    var page = engine.Search(new CorpusSearchQuery(
        "بسم", CorpusSearchLanguage.Arabic,
        IncludeNormalized: true, Limit: 75, Offset: offset));
    all.AddRange(page.Hits);
    if (!page.HasMore) break;
    offset += page.Hits.Count;
    Check(offset < 100000, "Paged result did not terminate");
}
using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = index, Mode = SqliteOpenMode.ReadOnly, Pooling = false
}.ToString()))
{
    db.Open();
    using var count = db.CreateCommand();
    count.CommandText = """
        SELECT COUNT(*) FROM search_documents
        WHERE language='arabic' AND (
            instr(original_text,$query)>0 OR
            instr(normalized_text,$normalized)>0);
        """;
    count.Parameters.AddWithValue("$query", "بسم");
    count.Parameters.AddWithValue("$normalized", "بسم");
    long expected = (long)count.ExecuteScalar()!;
    Check(expected > 0 && all.Count == expected,
        "Incomplete normalized Arabic occurrence discovery across pages.");
}
Check(all.Select(Identity).Distinct().Count() == all.Count,
    "An occurrence was listed more than once.");
Console.WriteLine(
    $"C2a search query PASS: Arabic {arabic.Hits.Count} (first page), " +
    $"English {english.Hits.Count}, Bangla {bangla.Hits.Count}, " +
    $"complete exact Arabic fragment occurrences {all.Count}, " +
    "safe pagination, source attribution, normalized labels and pinned hashes.");
return 0;
