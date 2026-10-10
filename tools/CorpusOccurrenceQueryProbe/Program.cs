using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;

static void Check(bool ok, string detail)
{
    if (!ok) throw new InvalidDataException("Gate3: " + detail);
}
static void MustThrow<T>(Action call, string detail) where T : Exception
{
    try { call(); }
    catch (T) { return; }
    throw new InvalidDataException("Expected exception not raised: " + detail);
}

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: CorpusOccurrenceQueryProbe <corpus> <wbw> <derived>");
    return 64;
}
var engine = new CorpusOccurrenceRepository(args[0], args[1], args[2]);
var god = engine.SearchLemma("{ll~ah", "PN", limit: 50);
Check(god.WordOccurrences == 2699 && god.DistinctAyat == 1821 && god.Hits.Count == 50,
    "Allah lemma word and distinct-verse counts");
Check(god.HasMore && god.Hits.All(h => h.Position > 0 && h.WordId > 0),
    "Word positions, stable paging or source provenance");
var god2 = engine.SearchLemma("{ll~ah", "PN", limit: 50, offset: 50);
Check(!god.Hits.Select(h => h.WordId).Intersect(god2.Hits.Select(h => h.WordId)).Any(),
    "An Allah word position was duplicated across pages");
var tail = engine.SearchLemma("{ll~ah", "PN", limit: 50, offset: 2695);
Check(tail.Hits.Count == 4 && !tail.HasMore, "Allah last page incomplete");
var mercy = engine.SearchLemma("raHomap", "N", limit: 50);
Check(mercy.WordOccurrences == 114 && mercy.DistinctAyat == 112 &&
      mercy.Hits.Count == 50, "Rahmah noun counts");
var suggest = engine.SuggestLemmas("ٱللَّهِ");
Check(suggest.Any(s => s.Lemma == "{ll~ah" && s.Pos == "PN" &&
      s.Occurrences == 2699 && s.DistinctAyat == 1821),
    "Conservative accepted-word lemma suggestion for Allah");
Check(engine.SuggestLemmas("!@#").Count == 0,
    "Non-Arabic punctuation must never suggest an invented lemma");
var unknown = engine.SearchLemma("totally-unrecognized-lemma", "N");
Check(unknown.WordOccurrences == 0 && unknown.DistinctAyat == 0 &&
      unknown.Hits.Count == 0, "Unknown lemma should not fabricate results");

using var db = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = args[2], Mode = SqliteOpenMode.ReadOnly, Pooling = false
}.ToString());
db.Open();
using var query = db.CreateCommand();
query.CommandText = "SELECT text_uthmani FROM word_positions WHERE verse_key='1:1' AND position IN (1,2) ORDER BY position;";
var exactWords = new List<string>();
using (var reader = query.ExecuteReader())
    while (reader.Read()) exactWords.Add(reader.GetString(0));
Check(exactWords.Count == 2, "Canonical word 1:1 positional input");
var phrase = engine.SearchExactWords(exactWords);
Check(phrase.WordOccurrences == 3 && phrase.DistinctAyat == 3 &&
      phrase.Hits.Count == 3, "Exact adjacent Uthmani phrase count");
Check(phrase.Hits.Any(h => h.VerseKey == "1:1" && h.Position == 1),
    "Canonical verse 1:1 must appear among phrase sources");
var identical = engine.FindIdenticalAyat("1:1", "uthmani");
Check(identical.Count == 1 && identical[0].VerseKey == "1:1",
    "Full canonical Ayah identity count");
Check(engine.FindIdenticalAyat("1:1", "indopak").Count > 0,
    "Explicit IndoPak full-verse equality query");
MustThrow<ArgumentException>(() => engine.FindIdenticalAyat("1:1", "untrusted-column"),
    "script column whitelisting");
MustThrow<ArgumentException>(() => engine.SearchExactWords([]),
    "empty phrase rejection");
MustThrow<ArgumentOutOfRangeException>(() => engine.SearchLemma("{ll~ah", "PN", 101),
    "unbounded lemma page");
MustThrow<InvalidDataException>(() =>
    new CorpusOccurrenceRepository(args[0], args[1], args[2],
        new string('0', 64)), "tampered derived annotation digest");
Console.WriteLine("R2 Gate3 corpus occurrence query PASS: Allah=2699/1821, " +
    "Rahmah=114/112, canonical lemma suggestions, all last-page positions, " +
    "three exact adjacent phrase matches, identical Ayah, and fail-closed hashes.");
return 0;
