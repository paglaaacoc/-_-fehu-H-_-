using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

if (args.Length != 2)
{
    throw new InvalidOperationException(
        "Usage: JuzMetadataProbe <corpus.sqlite> <juz-map.json>");
}

string corpusPath =
    Path.GetFullPath(args[0]);

string mapPath =
    Path.GetFullPath(args[1]);

if (!File.Exists(corpusPath))
{
    throw new FileNotFoundException(
        "Pinned corpus not found.",
        corpusPath);
}

if (!File.Exists(mapPath))
{
    throw new FileNotFoundException(
        "Pinned Juz map not found.",
        mapPath);
}

var chapters =
    new List<ChapterSummary>();

using (var connection =
    new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = corpusPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString()))
{
    connection.Open();

    using var command =
        connection.CreateCommand();

    command.CommandText =
        """
        SELECT chapter_number,
               verses_count,
               name_simple,
               translated_name_bn
        FROM chapters
        ORDER BY chapter_number;
        """;

    using var reader =
        command.ExecuteReader();

    while (reader.Read())
    {
        chapters.Add(
            new ChapterSummary(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2)
                    ? $"Surah {reader.GetInt32(0)}"
                    : reader.GetString(2),
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3)));
    }
}

Require(
    chapters.Count == 114,
    $"Expected 114 Surahs, got {chapters.Count}.");

Require(
    chapters.Sum(x => x.VersesCount) == 6236,
    "Pinned corpus must contain 6,236 ayat.");

var juz =
    new JuzRepository(
        chapters,
        mapPath);

Require(
    juz.GetAll().Count == 30,
    "Expected exactly 30 Juz.");

Require(
    juz.Get(1).StartVerseKey == "1:1" &&
    juz.Get(1).EndVerseKey == "2:141",
    "Juz 1 boundary mismatch.");

Require(
    juz.Get(2).StartVerseKey == "2:142" &&
    juz.Get(2).EndVerseKey == "2:252",
    "Juz 2 boundary mismatch.");

Require(
    juz.Get(30).StartVerseKey == "78:1" &&
    juz.Get(30).EndVerseKey == "114:6",
    "Juz 30 boundary mismatch.");

Require(
    juz.GetJuzNumber(2, 141) == 1 &&
    juz.GetJuzNumber(2, 142) == 2 &&
    juz.GetJuzNumber(2, 252) == 2 &&
    juz.GetJuzNumber(2, 253) == 3,
    "Al-Baqarah Juz transition mapping mismatch.");

Require(
    juz.GetJuzNumber(18, 74) == 15 &&
    juz.GetJuzNumber(18, 75) == 16,
    "Al-Kahf Juz transition mapping mismatch.");

Require(
    juz.GetJuzNumber(77, 50) == 29 &&
    juz.GetJuzNumber(78, 1) == 30 &&
    juz.GetJuzNumber(114, 6) == 30,
    "Late-Qur'an Juz transition mapping mismatch.");

Require(
    juz.GetRangeLabel(
        2,
        138,
        152) ==
        "Juz 1 → 2",
    "Context crossing label mismatch.");

IReadOnlyList<JuzBoundary> starts =
    juz.GetStartsInsideRange(
        2,
        138,
        152);

Require(
    starts.Count == 1 &&
    starts[0].Number == 2 &&
    starts[0].StartVerseKey == "2:142",
    "Context crossing metadata did not identify Juz 2 beginning at 2:142.");

for (int surah = 1;
     surah <= chapters.Count;
     surah++)
{
    for (int ayah = 1;
         ayah <= chapters[surah - 1].VersesCount;
         ayah++)
    {
        int number =
            juz.GetJuzNumber(
                surah,
                ayah);

        Require(
            number is >= 1 and <= 30,
            $"Invalid Juz for {surah}:{ayah}: {number}.");
    }
}

Console.WriteLine(
    "The Holy Quran TRP v1.1 Juz metadata probe: PASS");
Console.WriteLine(
    "30 Juz cover all 6,236 canonical ayat exactly once.");

return 0;

static void Require(
    bool condition,
    string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            message);
    }
}
