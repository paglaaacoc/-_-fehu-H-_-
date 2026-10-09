using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 2 ||
    (args[0] != "--seed" && args[0] != "--verify"))
{
    Console.Error.WriteLine(
        "Usage: PortabilityProbe --seed <portable-root> | --verify <portable-root>");
    return 2;
}

string root = Path.GetFullPath(args[1]);
string data = Path.Combine(root, "Data");
string db = Path.Combine(data, "research.sqlite");
string settings = Path.Combine(data, "settings.json");
string corpus = Path.Combine(root, "Corpus", "corpus.sqlite");

if (!File.Exists(db))
{
    throw new FileNotFoundException("research.sqlite is missing.", db);
}

if (!File.Exists(corpus))
{
    throw new FileNotFoundException("corpus.sqlite is missing.", corpus);
}

if (args[0] == "--seed")
{
    Seed(db, settings);
    Console.WriteLine("Portable research state seeded.");
    return 0;
}

Verify(db, settings);
Console.WriteLine("Portable research-state verification: PASS");
return 0;

static void Seed(string db, string settingsPath)
{
    var contexts = new ContextRepository(db);
    var notes = new NoteRepository(db);

    IReadOnlyList<ContextBlock> existing =
        contexts.EnsureSeeded(2, 286);

    // Make this idempotent for local reruns by only creating the fixture
    // from the untouched whole-Surah seed.
    if (existing.Count == 1 &&
        existing[0].StartAyah == 1 &&
        existing[0].EndAyah == 286)
    {
        long first = existing[0].Id;
        long second = contexts.SplitAfter(first, 5);

        contexts.ExtendEnd(first); // 1–6 / 7–286
        contexts.SetStatus(first, "Owner Reviewed");
        contexts.SetStatus(second, "Accepted");

        notes.SaveAyahNote(
            2,
            255,
            "Portable ayah note v1\nবাংলা line retained verbatim.");
        notes.SaveAyahNote(
            2,
            255,
            "Portable ayah note v2\nবাংলা line retained verbatim.\nFinal fixture.");

        notes.SaveContextNote(
            first,
            "Portable context note v1 — owner research state.");
        notes.SaveContextNote(
            first,
            "Portable context note v2 — owner research state.\nSecond line.");
    }

    contexts.ValidateMap(2, 286);

    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

    var settings = new JsonObject
    {
        ["SchemaVersion"] = 1,
        ["Theme"] = "OLED Cyan",
        ["Zoom"] = 1.10,
        ["ShowUthmani"] = true,
        ["ShowIndoPak"] = false,
        ["ShowIndoPakNastaleeq"] = true,
        ["LastSurahNumber"] = 2,
        ["SourceSelectionInitialized"] = true,
        ["SelectedTranslationIds"] = new JsonArray(19, 20, 85, 161, 163, 213),
        ["SelectedTafsirIds"] = new JsonArray(169, 166),
        ["WindowX"] = 120,
        ["WindowY"] = 90,
        ["WindowWidth"] = 1800,
        ["WindowHeight"] = 1000
    };

    File.WriteAllText(
        settingsPath,
        settings.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        }));
}

static void Verify(string db, string settingsPath)
{
    if (!File.Exists(settingsPath))
    {
        throw new FileNotFoundException(
            "Portable settings.json is missing.",
            settingsPath);
    }

    JsonNode? root = JsonNode.Parse(File.ReadAllText(settingsPath));
    JsonObject settings = root?.AsObject()
        ?? throw new InvalidDataException("settings.json is invalid.");

    Require(
        settings["Theme"]?.GetValue<string>() == "OLED Cyan",
        "Theme did not remain portable.");

    double zoom = settings["Zoom"]?.GetValue<double>()
        ?? throw new InvalidDataException("Zoom missing.");
    Require(
        Math.Abs(zoom - 1.10) < 0.001,
        "Zoom did not remain portable.");

    Require(
        settings["ShowIndoPak"]?.GetValue<bool>() == false,
        "Arabic-script visibility did not remain portable.");

    Require(
        settings["LastSurahNumber"]?.GetValue<int>() == 2,
        "Last Surah did not remain portable.");

    var contexts = new ContextRepository(db);
    var notes = new NoteRepository(db);

    IReadOnlyList<ContextBlock> blocks = contexts.GetBlocks(2);
    Require(blocks.Count == 2, "Expected two active context blocks.");

    ContextBlock first = blocks[0];
    ContextBlock second = blocks[1];

    Require(
        first.StartAyah == 1 &&
        first.EndAyah == 6 &&
        first.Status == "Owner Reviewed",
        "First context block did not survive relocation.");

    Require(
        second.StartAyah == 7 &&
        second.EndAyah == 286 &&
        second.Status == "Accepted",
        "Accepted context block did not survive relocation.");

    contexts.ValidateMap(2, 286);

    Require(
        contexts.GetHistory(first.Id, 50).Count >= 2,
        "Boundary history did not survive relocation.");

    Require(
        notes.GetAyahNote(2, 255)?.Body ==
        "Portable ayah note v2\nবাংলা line retained verbatim.\nFinal fixture.",
        "Current Ayah note did not survive relocation.");

    IReadOnlyList<NoteRevision> ayahHistory =
        notes.GetAyahHistory(2, 255, 50);

    Require(
        ayahHistory.Count >= 1 &&
        ayahHistory.Any(x =>
            x.PriorBody ==
            "Portable ayah note v1\nবাংলা line retained verbatim."),
        "Ayah note revision history did not survive relocation.");

    Require(
        notes.GetContextNote(first.Id)?.Body ==
        "Portable context note v2 — owner research state.\nSecond line.",
        "Current Context note did not survive relocation.");

    IReadOnlyList<NoteRevision> contextHistory =
        notes.GetContextHistory(first.Id, 50);

    Require(
        contextHistory.Count >= 1 &&
        contextHistory.Any(x =>
            x.PriorBody ==
            "Portable context note v1 — owner research state."),
        "Context note revision history did not survive relocation.");

    // Accepted locking must remain active after relocation.
    bool locked = false;
    try
    {
        contexts.ShrinkStart(second.Id);
    }
    catch (InvalidOperationException ex)
        when (ex.Message.Contains("Accepted", StringComparison.Ordinal))
    {
        locked = true;
    }

    Require(locked, "Accepted context lock did not survive relocation.");

    using var connection = new SqliteConnection($"Data Source={db}");
    connection.Open();

    using var meta = connection.CreateCommand();
    meta.CommandText =
        "SELECT value FROM meta WHERE key='schema_version';";
    string? schema = meta.ExecuteScalar() as string;

    string expectedSchema =
        ResearchDatabase.SchemaVersion.ToString(
            CultureInfo.InvariantCulture);

    Require(
        schema == expectedSchema,
        $"Research DB schema version is not {expectedSchema}.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
