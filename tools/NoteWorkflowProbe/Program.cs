using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;

string root = Path.Combine(
    Path.GetTempPath(),
    "QuranReconciliation-NoteProbe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

string db = Path.Combine(root, "research.sqlite");

try
{
    CreateSchema(db);
    ResearchDatabase.InitializeAt(db);

    var notes = new NoteRepository(db);

    const string ayahV1 =
        "First line exactly as typed.\nSecond line — বাংলা note.";
    const string ayahV2 =
        "First line revised.\nSecond line — বাংলা note.\nThird line.";

    Require(notes.GetAyahNote(2, 255) is null,
        "Ayah note should initially be absent.");

    Require(notes.SaveAyahNote(2, 255, ayahV1),
        "First Ayah note save should report a change.");

    var loadedAyah = notes.GetAyahNote(2, 255);
    Require(loadedAyah?.Body == ayahV1,
        "Ayah note must round-trip verbatim.");

    Require(!notes.SaveAyahNote(2, 255, ayahV1),
        "Saving identical Ayah text must not create a revision.");
    Require(notes.GetAyahHistory(2, 255, 20).Count == 0,
        "Identical Ayah save must not create history.");

    Require(notes.SaveAyahNote(2, 255, ayahV2),
        "Changed Ayah note should report a change.");

    var ayahHistory = notes.GetAyahHistory(2, 255, 20);
    Require(ayahHistory.Count == 1,
        "Changed Ayah note must create one prior revision.");
    Require(ayahHistory[0].PriorBody == ayahV1,
        "Ayah history must contain the exact prior body.");

    const string contextV1 =
        "Context thought: preserve ambiguity; do not flatten.";
    const string contextV2 =
        "Context thought revised.\nCheck parallel passage.";

    Require(notes.SaveContextNote(1, contextV1),
        "First Context note save should report a change.");
    Require(notes.GetContextNote(1)?.Body == contextV1,
        "Context note must round-trip verbatim.");

    Require(!notes.SaveContextNote(1, contextV1),
        "Identical Context save must not create a revision.");
    Require(notes.GetContextHistory(1, 20).Count == 0,
        "Identical Context save must not create history.");

    Require(notes.SaveContextNote(1, contextV2),
        "Changed Context note should report a change.");

    var contextHistory = notes.GetContextHistory(1, 20);
    Require(contextHistory.Count == 1,
        "Changed Context note must create one prior revision.");
    Require(contextHistory[0].PriorBody == contextV1,
        "Context history must contain the exact prior body.");

    Require(notes.SaveAyahNote(2, 255, string.Empty),
        "Clearing an Ayah note is a real revision.");

    ayahHistory = notes.GetAyahHistory(2, 255, 20);
    Require(ayahHistory.Count == 2,
        "Clearing an Ayah note must preserve the prior text.");
    Require(ayahHistory[0].PriorBody == ayahV2,
        "Latest Ayah history must contain the pre-clear body.");

    Require(notes.GetContextHistory(1, 20).Count == 1,
        "Ayah note changes must not alter Context history.");

    Console.WriteLine("Build 5 Ayah/Context notes + revision contract: PASS");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateSchema(string path)
{
    using var connection = new SqliteConnection($"Data Source={path}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = """
    PRAGMA foreign_keys=ON;

    CREATE TABLE context_blocks (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        start_ayah INTEGER NOT NULL,
        end_ayah INTEGER NOT NULL,
        status TEXT NOT NULL DEFAULT 'Proposed',
        owner_note TEXT,
        created_utc TEXT NOT NULL,
        updated_utc TEXT NOT NULL,
        origin TEXT NOT NULL DEFAULT 'Manual',
        is_active INTEGER NOT NULL DEFAULT 1
    );

    INSERT INTO context_blocks(
        id, surah_number, start_ayah, end_ayah,
        status, owner_note, created_utc, updated_utc,
        origin, is_active)
    VALUES(
        1, 1, 1, 7,
        'Proposed', NULL,
        '2026-10-03T00:00:00+00:00',
        '2026-10-03T00:00:00+00:00',
        'Probe', 1);

    CREATE TABLE ayah_notes (
        surah_number INTEGER NOT NULL,
        ayah_number INTEGER NOT NULL,
        body TEXT NOT NULL DEFAULT '',
        updated_utc TEXT NOT NULL,
        PRIMARY KEY(surah_number, ayah_number)
    );

    CREATE TABLE ayah_note_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        ayah_number INTEGER NOT NULL,
        prior_body TEXT NOT NULL,
        changed_utc TEXT NOT NULL
    );

    CREATE TABLE context_notes (
        context_block_id INTEGER PRIMARY KEY,
        body TEXT NOT NULL DEFAULT '',
        updated_utc TEXT NOT NULL,
        FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
    );

    CREATE TABLE context_note_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        context_block_id INTEGER NOT NULL,
        prior_body TEXT NOT NULL,
        changed_utc TEXT NOT NULL,
        FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
    );
    """;
    command.ExecuteNonQuery();
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
