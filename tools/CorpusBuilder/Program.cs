using Microsoft.Data.Sqlite;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string DefaultLegacyApiBase = "https://api.quran.com/api/v4";

string outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Corpus"));

Directory.CreateDirectory(outputDirectory);

string dbPath = Path.Combine(outputDirectory, "corpus.sqlite");
string manifestPath = Path.Combine(outputDirectory, "manifest.json");

if (File.Exists(dbPath))
{
    File.Delete(dbPath);
}

string apiBase = (Environment.GetEnvironmentVariable("QURAN_API_BASE") ?? DefaultLegacyApiBase).TrimEnd('/');
string? clientId = Environment.GetEnvironmentVariable("QF_CLIENT_ID");
string? accessToken = Environment.GetEnvironmentVariable("QF_ACCESS_TOKEN");

using var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(120)
};
http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

if (!string.IsNullOrWhiteSpace(clientId))
{
    http.DefaultRequestHeaders.Add("x-client-id", clientId);
}

if (!string.IsNullOrWhiteSpace(accessToken))
{
    http.DefaultRequestHeaders.Add("x-auth-token", accessToken);
}

Console.WriteLine($"CorpusBuilder API base: {apiBase}");
Console.WriteLine(string.IsNullOrWhiteSpace(clientId)
    ? "Authentication headers: not supplied (legacy/public-source mode)."
    : "Authentication headers: supplied.");

var translationsCatalog = await GetArrayAsync(http, $"{apiBase}/resources/translations?language=en", "translations");
var tafsirsCatalog = await GetArrayAsync(http, $"{apiBase}/resources/tafsirs?language=en", "tafsirs");

var selectedTranslations = translationsCatalog
    .OfType<JsonObject>()
    .Where(IsEnglishOrBengali)
    .OrderBy(x => LanguageSort(x))
    .ThenBy(x => x["id"]?.GetValue<int>() ?? 0)
    .ToList();

var selectedTafsirs = tafsirsCatalog
    .OfType<JsonObject>()
    .Where(IsEnglishOrBengali)
    .OrderBy(x => LanguageSort(x))
    .ThenBy(x => x["id"]?.GetValue<int>() ?? 0)
    .ToList();

Console.WriteLine($"Translation-catalog resources selected: {selectedTranslations.Count}");
Console.WriteLine($"Tafsir resources selected: {selectedTafsirs.Count}");

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

Execute(connection, """
PRAGMA journal_mode = DELETE;
PRAGMA synchronous = FULL;
PRAGMA foreign_keys = ON;

CREATE TABLE corpus_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE chapters (
    chapter_number INTEGER PRIMARY KEY,
    verses_count INTEGER NOT NULL,
    name_arabic TEXT,
    name_simple TEXT,
    name_complex TEXT,
    translated_name_en TEXT,
    translated_name_bn TEXT
);

CREATE TABLE verses (
    verse_key TEXT PRIMARY KEY,
    chapter_number INTEGER NOT NULL,
    verse_number INTEGER NOT NULL,
    text_uthmani TEXT NOT NULL,
    text_indopak TEXT NOT NULL,
    text_indopak_nastaleeq TEXT NOT NULL,
    FOREIGN KEY(chapter_number) REFERENCES chapters(chapter_number)
);

CREATE UNIQUE INDEX idx_verses_chapter_verse
ON verses(chapter_number, verse_number);

CREATE TABLE resources (
    resource_kind TEXT NOT NULL,
    resource_id INTEGER NOT NULL,
    display_type TEXT NOT NULL,
    language_name TEXT NOT NULL,
    name TEXT NOT NULL,
    author_name TEXT,
    slug TEXT,
    source_endpoint TEXT NOT NULL,
    row_count INTEGER NOT NULL DEFAULT 0,
    content_sha256 TEXT,
    PRIMARY KEY(resource_kind, resource_id)
);

CREATE TABLE translations (
    resource_id INTEGER NOT NULL,
    verse_key TEXT NOT NULL,
    source_row_id INTEGER,
    raw_text TEXT NOT NULL,
    foot_notes_json TEXT,
    PRIMARY KEY(resource_id, verse_key),
    FOREIGN KEY(verse_key) REFERENCES verses(verse_key)
);

CREATE INDEX idx_translations_verse
ON translations(verse_key);

CREATE TABLE tafsir_records (
    resource_id INTEGER NOT NULL,
    source_row_id INTEGER NOT NULL,
    verse_key TEXT,
    group_verse_key_from TEXT,
    group_verse_key_to TEXT,
    start_verse_id INTEGER,
    end_verse_id INTEGER,
    raw_text TEXT NOT NULL,
    PRIMARY KEY(resource_id, source_row_id)
);

CREATE INDEX idx_tafsir_resource_verse
ON tafsir_records(resource_id, verse_key);

CREATE INDEX idx_tafsir_resource_range
ON tafsir_records(resource_id, start_verse_id, end_verse_id);
""");

string buildUtc =
    Environment.GetEnvironmentVariable("QURAN_CORPUS_BUILD_UTC") ??
    DateTimeOffset.UtcNow.ToString("O");

InsertMeta(connection, "schema_version", "1");
InsertMeta(connection, "source_api_base", apiBase);
InsertMeta(connection, "build_utc", buildUtc);
InsertMeta(connection, "runtime_network_required", "false");

await ImportChaptersAsync(http, apiBase, connection);
await ImportArabicAsync(http, apiBase, connection);

foreach (JsonObject resource in selectedTranslations)
{
    await ImportTranslationAsync(http, apiBase, connection, resource);
}

foreach (JsonObject resource in selectedTafsirs)
{
    await ImportTafsirAsync(http, apiBase, connection, resource);
}

VerifyCorpus(connection, selectedTranslations.Count, selectedTafsirs.Count);

Execute(connection, "PRAGMA optimize;");
connection.Close();
SqliteConnection.ClearAllPools();

string databaseSha = Sha256File(dbPath);

var manifest = new JsonObject
{
    ["schema_version"] = 1,
    ["created_utc"] = buildUtc,
    ["source_api_base"] = apiBase,
    ["database_sha256"] = databaseSha,
    ["verse_count"] = 6236,
    ["chapter_count"] = 114,
    ["arabic_scripts"] = new JsonArray("uthmani", "indopak", "indopak_nastaleeq"),
    ["translation_resource_count"] = selectedTranslations.Count,
    ["tafsir_resource_count"] = selectedTafsirs.Count,
    ["translations"] = new JsonArray(selectedTranslations.Select(ToManifestResource).ToArray()),
    ["tafsirs"] = new JsonArray(selectedTafsirs.Select(ToManifestResource).ToArray())
};

File.WriteAllText(
    manifestPath,
    manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
    new UTF8Encoding(false));

Console.WriteLine($"Corpus database: {dbPath}");
Console.WriteLine($"Corpus SHA-256: {databaseSha}");
Console.WriteLine("Corpus verification: PASS");

static async Task ImportChaptersAsync(HttpClient http, string apiBase, SqliteConnection connection)
{
    var en = await GetArrayAsync(http, $"{apiBase}/chapters?language=en", "chapters");
    var bn = await GetArrayAsync(http, $"{apiBase}/chapters?language=bn", "chapters");

    var bnById = bn
        .OfType<JsonObject>()
        .Where(x => x["id"] is not null)
        .ToDictionary(x => x["id"]!.GetValue<int>(), x => x);

    using var tx = connection.BeginTransaction();

    foreach (JsonObject chapter in en.OfType<JsonObject>())
    {
        int id = chapter["id"]!.GetValue<int>();
        bnById.TryGetValue(id, out JsonObject? bnChapter);

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        INSERT INTO chapters(
            chapter_number, verses_count, name_arabic, name_simple, name_complex,
            translated_name_en, translated_name_bn)
        VALUES ($id, $count, $arabic, $simple, $complex, $en, $bn);
        """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$count", chapter["verses_count"]?.GetValue<int>() ?? 0);
        cmd.Parameters.AddWithValue("$arabic", DbValue(chapter["name_arabic"]?.GetValue<string>()));
        cmd.Parameters.AddWithValue("$simple", DbValue(chapter["name_simple"]?.GetValue<string>()));
        cmd.Parameters.AddWithValue("$complex", DbValue(chapter["name_complex"]?.GetValue<string>()));
        cmd.Parameters.AddWithValue("$en", DbValue(chapter["translated_name"]?["name"]?.GetValue<string>()));
        cmd.Parameters.AddWithValue("$bn", DbValue(bnChapter?["translated_name"]?["name"]?.GetValue<string>()));
        cmd.ExecuteNonQuery();
    }

    tx.Commit();

    long count = ScalarLong(connection, "SELECT COUNT(*) FROM chapters;");
    if (count != 114)
    {
        throw new InvalidOperationException($"Expected 114 chapters, got {count}.");
    }

    long ayahCount = ScalarLong(connection, "SELECT SUM(verses_count) FROM chapters;");
    if (ayahCount != 6236)
    {
        throw new InvalidOperationException($"Expected chapter metadata to total 6236 ayat, got {ayahCount}.");
    }

    Console.WriteLine("Chapters: 114 / ayat metadata total: 6236");
}

static async Task ImportArabicAsync(HttpClient http, string apiBase, SqliteConnection connection)
{
    var scripts = new[]
    {
        (Api: "uthmani", Column: "text_uthmani"),
        (Api: "indopak", Column: "text_indopak"),
        (Api: "indopak_nastaleeq", Column: "text_indopak_nastaleeq")
    };

    var rows = new Dictionary<string, VerseAccumulator>(StringComparer.Ordinal);

    foreach (var script in scripts)
    {
        Console.WriteLine($"Downloading Arabic script: {script.Api}");

        for (int chapter = 1; chapter <= 114; chapter++)
        {
            string url = $"{apiBase}/quran/verses/{script.Api}?chapter_number={chapter}";
            JsonArray verses = await GetArrayAsync(http, url, "verses");

            foreach (JsonNode? node in verses)
            {
                if (node is not JsonObject verse)
                {
                    continue;
                }

                string verseKey = verse["verse_key"]?.GetValue<string>()
                    ?? throw new InvalidOperationException($"{script.Api}: missing verse_key.");

                string text = ReadScriptText(verse, script.Column);

                VerseAccumulator acc;
                if (rows.TryGetValue(verseKey, out VerseAccumulator? found) && found is not null)
                {
                    acc = found;
                }
                else
                {
                    (int surah, int ayah) = ParseVerseKey(verseKey);
                    acc = new VerseAccumulator(surah, ayah);
                    rows.Add(verseKey, acc);
                }

                switch (script.Api)
                {
                    case "uthmani": acc.Uthmani = text; break;
                    case "indopak": acc.IndoPak = text; break;
                    case "indopak_nastaleeq": acc.IndoPakNastaleeq = text; break;
                }
            }
        }
    }

    if (rows.Count != 6236)
    {
        throw new InvalidOperationException($"Expected 6236 Arabic verse keys, got {rows.Count}.");
    }

    using var tx = connection.BeginTransaction();

    foreach ((string verseKey, VerseAccumulator v) in rows.OrderBy(x => x.Value.Surah).ThenBy(x => x.Value.Ayah))
    {
        if (string.IsNullOrWhiteSpace(v.Uthmani) ||
            string.IsNullOrWhiteSpace(v.IndoPak) ||
            string.IsNullOrWhiteSpace(v.IndoPakNastaleeq))
        {
            throw new InvalidOperationException($"Incomplete Arabic scripts at {verseKey}.");
        }

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        INSERT INTO verses(
            verse_key, chapter_number, verse_number,
            text_uthmani, text_indopak, text_indopak_nastaleeq)
        VALUES ($key, $chapter, $verse, $uthmani, $indopak, $nastaleeq);
        """;
        cmd.Parameters.AddWithValue("$key", verseKey);
        cmd.Parameters.AddWithValue("$chapter", v.Surah);
        cmd.Parameters.AddWithValue("$verse", v.Ayah);
        cmd.Parameters.AddWithValue("$uthmani", v.Uthmani!);
        cmd.Parameters.AddWithValue("$indopak", v.IndoPak!);
        cmd.Parameters.AddWithValue("$nastaleeq", v.IndoPakNastaleeq!);
        cmd.ExecuteNonQuery();
    }

    tx.Commit();
    Console.WriteLine("Arabic: 6236 verse keys × 3 scripts");
}

static async Task ImportTranslationAsync(
    HttpClient http,
    string apiBase,
    SqliteConnection connection,
    JsonObject resource)
{
    int id = resource["id"]!.GetValue<int>();
    string name = resource["name"]?.GetValue<string>() ?? $"Translation {id}";
    string endpoint = $"{apiBase}/quran/translations/{id}?fields=verse_key,id,resource_name,language_name&foot_notes=true";

    Console.WriteLine($"Translation {id}: {name}");

    JsonObject root = await GetObjectAsync(http, endpoint);
    JsonArray rows = root["translations"]?.AsArray()
        ?? throw new InvalidOperationException($"Translation {id}: missing translations array.");

    using var tx = connection.BeginTransaction();

    int count = 0;
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    foreach (JsonNode? node in rows)
    {
        if (node is not JsonObject row)
        {
            continue;
        }

        string verseKey = row["verse_key"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Translation {id}: missing verse_key.");

        string text = row["text"]?.GetValue<string>() ?? string.Empty;
        string? footnotes = row["foot_notes"]?.ToJsonString();

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        INSERT INTO translations(resource_id, verse_key, source_row_id, raw_text, foot_notes_json)
        VALUES ($resource, $verse, $row, $text, $footnotes);
        """;
        cmd.Parameters.AddWithValue("$resource", id);
        cmd.Parameters.AddWithValue("$verse", verseKey);
        cmd.Parameters.AddWithValue("$row", DbValue(row["id"]?.GetValue<int?>()));
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$footnotes", DbValue(footnotes));
        cmd.ExecuteNonQuery();

        AppendHash(hash, $"{verseKey}\u001f{text}\u001f{footnotes}\n");
        count++;
    }

    tx.Commit();

    string sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    InsertResource(connection, "translation", resource, ClassifyTranslation(resource), endpoint, count, sha);

    if (count != 6236)
    {
        throw new InvalidOperationException($"Translation {id} ({name}) expected 6236 rows, got {count}.");
    }
}

static async Task ImportTafsirAsync(
    HttpClient http,
    string apiBase,
    SqliteConnection connection,
    JsonObject resource)
{
    int id = resource["id"]!.GetValue<int>();
    string name = resource["name"]?.GetValue<string>() ?? $"Tafsir {id}";
    string endpointPattern = $"{apiBase}/tafsirs/{id}/by_chapter/{{chapter}}?per_page=50";

    Console.WriteLine($"Tafsir {id}: {name}");

    int count = 0;
    var seenSourceRows = new HashSet<long>();
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    using var tx = connection.BeginTransaction();

    for (int chapter = 1; chapter <= 114; chapter++)
    {
        int page = 1;

        while (true)
        {
            string url =
                $"{apiBase}/tafsirs/{id}/by_chapter/{chapter}?per_page=50&page={page}";

            JsonObject root = await GetObjectAsync(http, url);
            JsonArray rows = root["tafsirs"]?.AsArray()
                ?? throw new InvalidOperationException(
                    $"Tafsir {id}, chapter {chapter}: missing tafsirs array.");

            foreach (JsonNode? node in rows)
            {
                if (node is not JsonObject row)
                {
                    continue;
                }

                long sourceRowId = row["id"]?.GetValue<long?>()
                    ?? checked((long)id * 10_000_000L + count + 1);

                if (!seenSourceRows.Add(sourceRowId))
                {
                    continue;
                }

                string? verseKey = row["verse_key"]?.GetValue<string>();
                string? rangeFrom = row["group_verse_key_from"]?.GetValue<string>() ?? verseKey;
                string? rangeTo = row["group_verse_key_to"]?.GetValue<string>() ?? verseKey;
                long? startVerseId = row["start_verse_id"]?.GetValue<long?>();
                long? endVerseId = row["end_verse_id"]?.GetValue<long?>();
                string text = row["text"]?.GetValue<string>() ?? string.Empty;

                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                INSERT INTO tafsir_records(
                    resource_id, source_row_id, verse_key,
                    group_verse_key_from, group_verse_key_to,
                    start_verse_id, end_verse_id, raw_text)
                VALUES ($resource, $row, $verse, $from, $to, $start, $end, $text);
                """;
                cmd.Parameters.AddWithValue("$resource", id);
                cmd.Parameters.AddWithValue("$row", sourceRowId);
                cmd.Parameters.AddWithValue("$verse", DbValue(verseKey));
                cmd.Parameters.AddWithValue("$from", DbValue(rangeFrom));
                cmd.Parameters.AddWithValue("$to", DbValue(rangeTo));
                cmd.Parameters.AddWithValue("$start", DbValue(startVerseId));
                cmd.Parameters.AddWithValue("$end", DbValue(endVerseId));
                cmd.Parameters.AddWithValue("$text", text);
                cmd.ExecuteNonQuery();

                AppendHash(hash, $"{sourceRowId}\u001f{rangeFrom}\u001f{rangeTo}\u001f{text}\n");
                count++;
            }

            JsonObject? pagination = root["pagination"] as JsonObject;
            int? nextPage = pagination?["next_page"]?.GetValue<int?>();

            if (nextPage is null || rows.Count == 0)
            {
                break;
            }

            page = nextPage.Value;
        }
    }

    tx.Commit();

    string sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    InsertResource(connection, "tafsir", resource, "Tafsir-oriented", endpointPattern, count, sha);

    if (count == 0)
    {
        throw new InvalidOperationException($"Tafsir {id} ({name}) returned zero records.");
    }

    Console.WriteLine($"  rows: {count}");
}

static void VerifyCorpus(SqliteConnection connection, int translationResources, int tafsirResources)
{
    RequireCount(connection, "chapters", 114);
    RequireCount(connection, "verses", 6236);

    long missingArabic = ScalarLong(connection, """
    SELECT COUNT(*) FROM verses
    WHERE length(trim(text_uthmani)) = 0
       OR length(trim(text_indopak)) = 0
       OR length(trim(text_indopak_nastaleeq)) = 0;
    """);
    if (missingArabic != 0)
    {
        throw new InvalidOperationException($"Arabic completeness failure: {missingArabic} incomplete ayat.");
    }

    long translationResourceCount =
        ScalarLong(connection, "SELECT COUNT(*) FROM resources WHERE resource_kind='translation';");
    if (translationResourceCount != translationResources)
    {
        throw new InvalidOperationException(
            $"Translation resource count mismatch: expected {translationResources}, got {translationResourceCount}.");
    }

    long badTranslationResources = ScalarLong(connection, """
    SELECT COUNT(*) FROM resources
    WHERE resource_kind='translation' AND row_count <> 6236;
    """);
    if (badTranslationResources != 0)
    {
        throw new InvalidOperationException(
            $"{badTranslationResources} translation resources do not contain exactly 6236 rows.");
    }

    long tafsirResourceCount =
        ScalarLong(connection, "SELECT COUNT(*) FROM resources WHERE resource_kind='tafsir';");
    if (tafsirResourceCount != tafsirResources)
    {
        throw new InvalidOperationException(
            $"Tafsir resource count mismatch: expected {tafsirResources}, got {tafsirResourceCount}.");
    }

    long unknownVerseTranslations = ScalarLong(connection, """
    SELECT COUNT(*) FROM translations t
    LEFT JOIN verses v ON v.verse_key=t.verse_key
    WHERE v.verse_key IS NULL;
    """);
    if (unknownVerseTranslations != 0)
    {
        throw new InvalidOperationException(
            $"Translations contain {unknownVerseTranslations} unknown verse keys.");
    }
}

static void InsertResource(
    SqliteConnection connection,
    string kind,
    JsonObject resource,
    string displayType,
    string sourceEndpoint,
    int rowCount,
    string sha)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
    INSERT INTO resources(
        resource_kind, resource_id, display_type, language_name,
        name, author_name, slug, source_endpoint, row_count, content_sha256)
    VALUES ($kind, $id, $type, $language, $name, $author, $slug, $endpoint, $rows, $sha);
    """;
    cmd.Parameters.AddWithValue("$kind", kind);
    cmd.Parameters.AddWithValue("$id", resource["id"]!.GetValue<int>());
    cmd.Parameters.AddWithValue("$type", displayType);
    cmd.Parameters.AddWithValue("$language", resource["language_name"]?.GetValue<string>() ?? string.Empty);
    cmd.Parameters.AddWithValue("$name", resource["name"]?.GetValue<string>() ?? string.Empty);
    cmd.Parameters.AddWithValue("$author", DbValue(resource["author_name"]?.GetValue<string>()));
    cmd.Parameters.AddWithValue("$slug", DbValue(resource["slug"]?.GetValue<string>()));
    cmd.Parameters.AddWithValue("$endpoint", sourceEndpoint);
    cmd.Parameters.AddWithValue("$rows", rowCount);
    cmd.Parameters.AddWithValue("$sha", sha);
    cmd.ExecuteNonQuery();
}

static JsonObject ToManifestResource(JsonObject resource)
{
    return new JsonObject
    {
        ["id"] = resource["id"]?.DeepClone(),
        ["name"] = resource["name"]?.DeepClone(),
        ["author_name"] = resource["author_name"]?.DeepClone(),
        ["slug"] = resource["slug"]?.DeepClone(),
        ["language_name"] = resource["language_name"]?.DeepClone()
    };
}

static string ClassifyTranslation(JsonObject resource)
{
    int id = resource["id"]?.GetValue<int>() ?? 0;
    string joined = string.Join(
        " ",
        resource["name"]?.GetValue<string>() ?? string.Empty,
        resource["slug"]?.GetValue<string>() ?? string.Empty,
        resource["author_name"]?.GetValue<string>() ?? string.Empty)
        .ToLowerInvariant();

    if (id == 57 || joined.Contains("transliteration"))
    {
        return "Transliteration";
    }

    if (joined.Contains("tafsir") ||
        joined.Contains("tafseer") ||
        joined.Contains("tafhim") ||
        joined.Contains("commentary"))
    {
        return "Translation + Commentary";
    }

    return "Translation";
}

static bool IsEnglishOrBengali(JsonObject resource)
{
    string language = resource["language_name"]?.GetValue<string>() ?? string.Empty;
    return language.Equals("english", StringComparison.OrdinalIgnoreCase)
        || language.Equals("bengali", StringComparison.OrdinalIgnoreCase)
        || language.Equals("bangla", StringComparison.OrdinalIgnoreCase);
}

static int LanguageSort(JsonObject resource)
{
    string language = resource["language_name"]?.GetValue<string>() ?? string.Empty;
    return language.Equals("english", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
}

static async Task<JsonArray> GetArrayAsync(
    HttpClient http,
    string url,
    string property)
{
    JsonObject root = await GetObjectAsync(http, url);
    return root[property]?.AsArray()
        ?? throw new InvalidOperationException($"{url}: missing '{property}' array.");
}

static async Task<JsonObject> GetObjectAsync(HttpClient http, string url)
{
    const int attempts = 5;

    for (int attempt = 1; attempt <= attempts; attempt++)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(url);

            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                if (attempt == attempts)
                {
                    response.EnsureSuccessStatusCode();
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                continue;
            }

            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync();
            JsonNode? node = await JsonNode.ParseAsync(stream);
            return node?.AsObject()
                ?? throw new InvalidOperationException($"{url}: response is not a JSON object.");
        }
        catch (HttpRequestException) when (attempt < attempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        }
    }

    throw new InvalidOperationException($"{url}: exhausted retries.");
}

static string ReadScriptText(JsonObject verse, string preferredProperty)
{
    if (verse[preferredProperty] is JsonValue preferred &&
        preferred.TryGetValue<string>(out string? preferredText) &&
        !string.IsNullOrWhiteSpace(preferredText))
    {
        return preferredText;
    }

    foreach ((string key, JsonNode? value) in verse)
    {
        if (key.StartsWith("text_", StringComparison.Ordinal) &&
            value is JsonValue candidate &&
            candidate.TryGetValue<string>(out string? text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }
    }

    throw new InvalidOperationException(
        $"Could not find script text property '{preferredProperty}' in verse payload.");
}

static (int Surah, int Ayah) ParseVerseKey(string key)
{
    string[] parts = key.Split(':', 2);
    return (int.Parse(parts[0]), int.Parse(parts[1]));
}

static void Execute(SqliteConnection connection, string sql)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

static void InsertMeta(SqliteConnection connection, string key, string value)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "INSERT OR REPLACE INTO corpus_meta(key,value) VALUES ($k,$v);";
    cmd.Parameters.AddWithValue("$k", key);
    cmd.Parameters.AddWithValue("$v", value);
    cmd.ExecuteNonQuery();
}

static void RequireCount(SqliteConnection connection, string table, long expected)
{
    long actual = ScalarLong(connection, $"SELECT COUNT(*) FROM {table};");
    if (actual != expected)
    {
        throw new InvalidOperationException($"{table}: expected {expected} rows, got {actual}.");
    }
}

static long ScalarLong(SqliteConnection connection, string sql)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    return Convert.ToInt64(cmd.ExecuteScalar());
}

static object DbValue(object? value) => value ?? DBNull.Value;

static void AppendHash(IncrementalHash hash, string text)
{
    hash.AppendData(Encoding.UTF8.GetBytes(text));
}

static string Sha256File(string path)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
}

sealed class VerseAccumulator(int surah, int ayah)
{
    public int Surah { get; } = surah;
    public int Ayah { get; } = ayah;
    public string? Uthmani { get; set; }
    public string? IndoPak { get; set; }
    public string? IndoPakNastaleeq { get; set; }
}
