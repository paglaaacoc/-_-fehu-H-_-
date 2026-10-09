using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

const string DefaultApiBase = "https://api.quran.com/api/v4";

string outputDirectory =
    args.Length > 0
        ? Path.GetFullPath(args[0])
        : Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "Corpus"));

Directory.CreateDirectory(
    outputDirectory);

string dbPath =
    Path.Combine(
        outputDirectory,
        "word-by-word.sqlite");

string manifestPath =
    Path.Combine(
        outputDirectory,
        "word-by-word.manifest.json");

if (File.Exists(dbPath))
{
    File.Delete(dbPath);
}

string apiBase =
    (Environment.GetEnvironmentVariable(
        "QURAN_API_BASE") ??
     DefaultApiBase)
    .TrimEnd('/');

using var http =
    new HttpClient
    {
        Timeout =
            TimeSpan.FromSeconds(120)
    };

http.DefaultRequestHeaders.Accept.ParseAdd(
    "application/json");

using var connection =
    new SqliteConnection(
        $"Data Source={dbPath}");

connection.Open();

Execute(
    connection,
    """
    PRAGMA journal_mode = DELETE;
    PRAGMA synchronous = FULL;
    PRAGMA foreign_keys = ON;

    CREATE TABLE meta (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
    );

    CREATE TABLE words (
        word_id INTEGER PRIMARY KEY,
        verse_key TEXT NOT NULL,
        position INTEGER NOT NULL,
        text_uthmani TEXT NOT NULL,
        translation_en TEXT NOT NULL,
        transliteration TEXT NOT NULL
    );

    CREATE INDEX idx_words_verse_position
        ON words(verse_key, position);
    """);

string buildUtc =
    Environment.GetEnvironmentVariable(
        "QURAN_WBW_BUILD_UTC") ??
    DateTimeOffset.UtcNow.ToString("O");

InsertMeta(
    connection,
    "schema_version",
    "1");

InsertMeta(
    connection,
    "source_api_base",
    apiBase);

InsertMeta(
    connection,
    "source_mode",
    "Quran Foundation / Quran.com verse words=true · English word gloss + transliteration");

InsertMeta(
    connection,
    "build_utc",
    buildUtc);

InsertMeta(
    connection,
    "runtime_network_required",
    "false");

int verseCount = 0;
int wordCount = 0;

using var contentHash =
    IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256);

using var tx =
    connection.BeginTransaction();

for (int chapter = 1;
     chapter <= 114;
     chapter++)
{
    int page = 1;

    while (true)
    {
        string url =
            $"{apiBase}/verses/by_chapter/{chapter}" +
            "?language=en" +
            "&words=true" +
            "&word_fields=text_uthmani" +
            "&per_page=50" +
            $"&page={page}";

        JsonObject root =
            await GetObjectAsync(
                http,
                url);

        JsonArray verses =
            root["verses"]?.AsArray()
            ?? throw new InvalidOperationException(
                $"Chapter {chapter}: missing verses array.");

        foreach (JsonNode? verseNode in verses)
        {
            if (verseNode is not JsonObject verse)
            {
                continue;
            }

            string verseKey =
                verse["verse_key"]?.GetValue<string>()
                ?? throw new InvalidOperationException(
                    $"Chapter {chapter}: verse missing verse_key.");

            JsonArray words =
                verse["words"]?.AsArray()
                ?? throw new InvalidOperationException(
                    $"{verseKey}: missing words array.");

            foreach (JsonNode? wordNode in words)
            {
                if (wordNode is not JsonObject word)
                {
                    continue;
                }

                string charType =
                    word["char_type_name"]?.GetValue<string>()
                    ?? string.Empty;

                if (!string.Equals(
                        charType,
                        "word",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long id =
                    word["id"]?.GetValue<long>()
                    ?? throw new InvalidOperationException(
                        $"{verseKey}: word missing id.");

                int position =
                    word["position"]?.GetValue<int>()
                    ?? throw new InvalidOperationException(
                        $"{verseKey}: word {id} missing position.");

                string arabic =
                    word["text_uthmani"]?.GetValue<string>()
                    ?? string.Empty;

                string meaning =
                    word["translation"]?["text"]?.GetValue<string>()
                    ?? string.Empty;

                string transliteration =
                    word["transliteration"]?["text"]?.GetValue<string>()
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(arabic) ||
                    string.IsNullOrWhiteSpace(meaning))
                {
                    throw new InvalidOperationException(
                        $"{verseKey}:{position} is missing Arabic or English word meaning.");
                }

                using var command =
                    connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                INSERT INTO words(
                    word_id,
                    verse_key,
                    position,
                    text_uthmani,
                    translation_en,
                    transliteration)
                VALUES(
                    $id,
                    $verse,
                    $position,
                    $arabic,
                    $meaning,
                    $transliteration);
                """;
                command.Parameters.AddWithValue(
                    "$id",
                    id);
                command.Parameters.AddWithValue(
                    "$verse",
                    verseKey);
                command.Parameters.AddWithValue(
                    "$position",
                    position);
                command.Parameters.AddWithValue(
                    "$arabic",
                    arabic);
                command.Parameters.AddWithValue(
                    "$meaning",
                    meaning);
                command.Parameters.AddWithValue(
                    "$transliteration",
                    transliteration);
                command.ExecuteNonQuery();

                AppendHash(
                    contentHash,
                    $"{id}\u001f{verseKey}\u001f{position}\u001f{arabic}\u001f{meaning}\u001f{transliteration}\n");

                wordCount++;
            }

            verseCount++;
        }

        JsonObject? pagination =
            root["pagination"] as JsonObject;

        int? nextPage =
            pagination?["next_page"]?.GetValue<int?>();

        if (nextPage is null ||
            verses.Count == 0)
        {
            break;
        }

        page = nextPage.Value;
    }

    Console.WriteLine(
        $"Chapter {chapter}: complete");
}

tx.Commit();

if (verseCount != 6236)
{
    throw new InvalidOperationException(
        $"Expected 6236 verses, got {verseCount}.");
}

if (wordCount < 76000 ||
    wordCount > 79000)
{
    throw new InvalidOperationException(
        $"Unexpected word count: {wordCount}.");
}

long duplicateVersePositions =
    ScalarLong(
        connection,
        """
        SELECT COUNT(*)
        FROM (
            SELECT verse_key, position, COUNT(*) AS c
            FROM words
            GROUP BY verse_key, position
            HAVING c <> 1
        );
        """);

if (duplicateVersePositions != 0)
{
    throw new InvalidOperationException(
        $"Word position uniqueness failed for {duplicateVersePositions} verse positions.");
}

string contentSha =
    Convert.ToHexString(
        contentHash.GetHashAndReset())
    .ToLowerInvariant();

InsertMeta(
    connection,
    "content_sha256",
    contentSha);

Execute(
    connection,
    "PRAGMA optimize;");

connection.Close();
SqliteConnection.ClearAllPools();

string databaseSha =
    Sha256File(dbPath);

var manifest =
    new JsonObject
    {
        ["schema_version"] = 1,
        ["created_utc"] = buildUtc,
        ["source_api_base"] = apiBase,
        ["database_sha256"] = databaseSha,
        ["content_sha256"] = contentSha,
        ["verse_count"] = verseCount,
        ["word_count"] = wordCount,
        ["language"] = "english",
        ["includes_transliteration"] = true,
        ["runtime_network_required"] = false
    };

File.WriteAllText(
    manifestPath,
    manifest.ToJsonString(
        new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }),
    new UTF8Encoding(false));

Console.WriteLine(
    $"Word-by-word database: {dbPath}");
Console.WriteLine(
    $"Word count: {wordCount}");
Console.WriteLine(
    $"Word-by-word DB SHA-256: {databaseSha}");
Console.WriteLine(
    $"Word-by-word content SHA-256: {contentSha}");
Console.WriteLine(
    "Word-by-word verification: PASS");

static async Task<JsonObject> GetObjectAsync(
    HttpClient http,
    string url)
{
    using HttpResponseMessage response =
        await http.GetAsync(url);

    response.EnsureSuccessStatusCode();

    string json =
        await response.Content.ReadAsStringAsync();

    return JsonNode.Parse(json) as JsonObject
        ?? throw new InvalidOperationException(
            $"Expected JSON object from {url}");
}

static void Execute(
    SqliteConnection connection,
    string sql)
{
    using var command =
        connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static long ScalarLong(
    SqliteConnection connection,
    string sql)
{
    using var command =
        connection.CreateCommand();
    command.CommandText = sql;

    return Convert.ToInt64(
        command.ExecuteScalar());
}

static void InsertMeta(
    SqliteConnection connection,
    string key,
    string value)
{
    using var command =
        connection.CreateCommand();

    command.CommandText = """
    INSERT INTO meta(key, value)
    VALUES($key, $value);
    """;

    command.Parameters.AddWithValue(
        "$key",
        key);

    command.Parameters.AddWithValue(
        "$value",
        value);

    command.ExecuteNonQuery();
}

static void AppendHash(
    IncrementalHash hash,
    string value)
{
    byte[] bytes =
        Encoding.UTF8.GetBytes(value);

    hash.AppendData(bytes);
}

static string Sha256File(
    string path)
{
    using var stream =
        File.OpenRead(path);

    return Convert.ToHexString(
        SHA256.HashData(stream))
        .ToLowerInvariant();
}
