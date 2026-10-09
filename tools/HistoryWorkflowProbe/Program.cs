using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;

string root = Path.Combine(
    Path.GetTempPath(),
    "QuranReconciliation-HistoryProbe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

string db = Path.Combine(root, "research.sqlite");

try
{
    CreateFixture(db);
    ResearchDatabase.InitializeAt(db);

    var bookmarks = new BookmarkRepository(db);

    var firstBookmark = bookmarks.Save(
        2,
        255,
        "Ayatul Kursi review",
        "Return here before final reconciliation.");

    var revisedBookmark = bookmarks.Save(
        2,
        255,
        "Ayatul Kursi decision point",
        "Return here before final reconciliation.");

    Require(
        firstBookmark.Id == revisedBookmark.Id,
        "Bookmark save must upsert one durable bookmark per ayah.");

    var history = new HistoryRepository(db);

    var timeline =
        history.SearchTimeline(
            null,
            "All",
            0,
            50);

    Require(
        timeline.Count == 15,
        $"Expected 15 immutable Timeline events after schema-6 backfill plus two bookmark saves, got {timeline.Count}.");

    var ayahTimeline =
        history.SearchTimeline(
            "2:255",
            "AyahNotes",
            2,
            50);

    Require(
        ayahTimeline.Count == 2 &&
        ayahTimeline.All(
            x => x.Category == "AyahNotes"),
        "Timeline Ayah filter should retain current and revision events.");

    var banglaTimeline =
        history.SearchTimeline(
            "বাংলা",
            "AyahNotes",
            2,
            50);

    Require(
        banglaTimeline.Count == 1,
        "Timeline must search migrated Bengali revision payload text.");

    var boundary =
        history.SearchTimeline(
            "boundary",
            "Context",
            2,
            50);

    Require(
        boundary.Count == 3 &&
        boundary.All(
            x => x.Kind == "Context boundary"),
        "Legacy boundary activity must remain searchable after schema-7 migration.");

    var proposalImport =
        history.SearchTimeline(
            "1–5, 6–10",
            "Context",
            2,
            50);

    Require(
        proposalImport.Count == 1 &&
        proposalImport[0].Kind ==
            "Context proposal imported" &&
        proposalImport[0].CanJump,
        "One migrated proposal import must remain one Timeline event.");

    var mechanicalImportNoise =
        history.SearchTimeline(
            "Created by proposal import",
            "Context",
            2,
            50);

    Require(
        mechanicalImportNoise.Count == 0,
        "Per-block proposal creation noise must stay out of Timeline.");

    var slices =
        history.SearchTimeline(
            "Reconciliation Alpha",
            "WorkingSlices",
            2,
            50);

    Require(
        slices.Count == 2 &&
        slices.All(
            x => x.WorkingSliceId == 7),
        "Active Working Slice activity must remain searchable and reopenable.");

    var removedSlice =
        history.SearchTimeline(
            "Removed Slice Omega",
            "WorkingSlices",
            2,
            50);

    Require(
        removedSlice.Count == 1 &&
        removedSlice[0].Kind ==
            "Working Slice removed" &&
        removedSlice[0].WorkingSliceId is null &&
        removedSlice[0].ContextBlockId == 2 &&
        removedSlice[0].JumpAyah == 6 &&
        removedSlice[0].CanJump &&
        removedSlice[0].JumpLabel ==
            "Open parent context",
        "Removed Working Slice activity must navigate to its parent Context, never reopen inactive state.");

    var context =
        history.SearchTimeline(
            "parallel passage",
            "ContextNotes",
            2,
            50);

    Require(
        context.Count == 1 &&
        context[0].ContextBlockId == 1 &&
        context[0].JumpAyah == 1,
        "Context-note Timeline event must carry Context navigation metadata.");

    var bookmark =
        history.SearchTimeline(
            "decision point",
            "Bookmarks",
            2,
            50);

    Require(
        bookmark.Count == 1 &&
        bookmark[0].Category == "Bookmarks" &&
        bookmark[0].SurahNumber == 2 &&
        bookmark[0].AyahNumber == 255 &&
        bookmark[0].CanJump,
        "Bookmark Timeline event must be searchable and directly navigable.");

    var caseInsensitive =
        history.SearchTimeline(
            "REVISED",
            "All",
            0,
            50);

    Require(
        caseInsensitive.Count >= 1,
        "Timeline search must be case-insensitive.");

    var timestamp =
        history.SearchTimeline(
            "2026-10-03",
            "All",
            0,
            50);

    Require(
        timestamp.Count == 13,
        "Migrated Timeline records must remain searchable by stored change date/time.");

    var organized =
        history.SearchOrganized(
            null,
            "All",
            2,
            50);

    Require(
        organized.Count == 5,
        $"Organized view should expose five current parent objects in the fixture, got {organized.Count}.");

    var organizedAyah =
        history.SearchOrganized(
            "বাংলা",
            "AyahNotes",
            2,
            50);

    Require(
        organizedAyah.Count == 1 &&
        organizedAyah[0].Kind ==
            "Ayah note" &&
        organizedAyah[0].RevisionCount == 1 &&
        organizedAyah[0].Revisions.Count == 0 &&
        history.LoadRevisionsOnDemand(organizedAyah[0])[0].Body.Contains(
            "বাংলা",
            StringComparison.Ordinal),
        "Organized History must surface a parent object when only a nested revision matches.");

    var organizedSlice =
        history.SearchOrganized(
            "Reconciliation Alpha",
            "WorkingSlices",
            2,
            50);

    Require(
        organizedSlice.Count == 1 &&
        organizedSlice[0].RevisionCount == 1 &&
        organizedSlice[0].Revisions.Count == 0 &&
        history.LoadRevisionsOnDemand(organizedSlice[0]).Count == 1 &&
        organizedSlice[0].WorkingSliceId == 7,
        "Organized History must collapse current Working Slice plus revisions into one reopenable parent card.");

    var organizedRemoved =
        history.SearchOrganized(
            "Removed Slice Omega",
            "WorkingSlices",
            2,
            50);

    Require(
        organizedRemoved.Count == 1 &&
        organizedRemoved[0].WorkingSliceId is null &&
        organizedRemoved[0].JumpLabel ==
            "Open parent context",
        "Organized History must preserve removed Working Slice audit state without reopening it.");

    Console.WriteLine(
        "v1.3 Organized History + immutable Timeline contract: PASS");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateFixture(string path)
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
        status TEXT NOT NULL,
        owner_note TEXT,
        created_utc TEXT NOT NULL,
        updated_utc TEXT NOT NULL,
        origin TEXT NOT NULL,
        is_active INTEGER NOT NULL DEFAULT 1,
        proposal_import_id INTEGER
    );

    CREATE TABLE context_boundary_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        context_block_id INTEGER NOT NULL,
        old_start_ayah INTEGER NOT NULL,
        old_end_ayah INTEGER NOT NULL,
        new_start_ayah INTEGER NOT NULL,
        new_end_ayah INTEGER NOT NULL,
        owner_note TEXT,
        changed_utc TEXT NOT NULL
    );

    CREATE TABLE ayah_notes (
        surah_number INTEGER NOT NULL,
        ayah_number INTEGER NOT NULL,
        body TEXT NOT NULL,
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
        body TEXT NOT NULL,
        updated_utc TEXT NOT NULL
    );

    CREATE TABLE context_note_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        context_block_id INTEGER NOT NULL,
        prior_body TEXT NOT NULL,
        changed_utc TEXT NOT NULL
    );

    CREATE TABLE bookmarks (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        ayah_number INTEGER NOT NULL,
        title TEXT NOT NULL DEFAULT '',
        note TEXT NOT NULL DEFAULT '',
        created_utc TEXT NOT NULL,
        updated_utc TEXT NOT NULL,
        UNIQUE(surah_number, ayah_number)
    );

    CREATE TABLE context_proposal_imports (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        raw_payload TEXT NOT NULL,
        normalized_ranges TEXT NOT NULL,
        created_utc TEXT NOT NULL,
        discarded_utc TEXT
    );

    CREATE TABLE working_slices (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        context_block_id INTEGER NOT NULL,
        start_ayah INTEGER NOT NULL,
        end_ayah INTEGER NOT NULL,
        title TEXT NOT NULL,
        status TEXT NOT NULL,
        research_notes TEXT NOT NULL,
        conclusion TEXT NOT NULL,
        created_utc TEXT NOT NULL,
        updated_utc TEXT NOT NULL,
        is_active INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE working_slice_revisions (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        working_slice_id INTEGER NOT NULL,
        prior_title TEXT NOT NULL,
        prior_status TEXT NOT NULL,
        prior_research_notes TEXT NOT NULL,
        prior_conclusion TEXT NOT NULL,
        changed_utc TEXT NOT NULL
    );

    INSERT INTO context_blocks(
        id, surah_number, start_ayah, end_ayah,
        status, created_utc, updated_utc, origin, is_active, proposal_import_id)
    VALUES
        (1, 2, 1, 5, 'Owner Reviewed',
         '2026-10-03T10:00:00+00:00',
         '2026-10-03T10:30:00+00:00',
         'Manual', 1, 1),
        (2, 2, 6, 10, 'Accepted',
         '2026-10-03T10:00:00+00:00',
         '2026-10-03T10:30:00+00:00',
         'Manual', 1, 1);

    INSERT INTO ayah_notes(
        surah_number, ayah_number, body, updated_utc)
    VALUES(
        2, 255,
        'Current ayah note revised.',
        '2026-10-03T12:00:00+00:00');

    INSERT INTO ayah_note_history(
        surah_number, ayah_number, prior_body, changed_utc)
    VALUES(
        2, 255,
        'Earlier ayah note with বাংলা phrase.',
        '2026-10-03T11:30:00+00:00');

    INSERT INTO context_notes(
        context_block_id, body, updated_utc)
    VALUES(
        1,
        'Current context note: check parallel passage.',
        '2026-10-03T12:10:00+00:00');

    INSERT INTO context_note_history(
        context_block_id, prior_body, changed_utc)
    VALUES(
        1,
        'Earlier context note.',
        '2026-10-03T11:40:00+00:00');

    INSERT INTO context_proposal_imports(
        id,
        surah_number,
        raw_payload,
        normalized_ranges,
        created_utc)
    VALUES(
        1,
        2,
        '1-5\n6-10',
        '1–5, 6–10',
        '2026-10-03T10:45:00+00:00');

    INSERT INTO working_slices(
        id,
        surah_number,
        context_block_id,
        start_ayah,
        end_ayah,
        title,
        status,
        research_notes,
        conclusion,
        created_utc,
        updated_utc,
        is_active)
    VALUES(
        7,
        2,
        1,
        1,
        5,
        'Reconciliation Alpha',
        'In Review',
        'Compare source renderings carefully.',
        'Current conclusion remains provisional.',
        '2026-10-03T13:00:00+00:00',
        '2026-10-03T13:30:00+00:00',
        1);

    INSERT INTO working_slices(
        id,
        surah_number,
        context_block_id,
        start_ayah,
        end_ayah,
        title,
        status,
        research_notes,
        conclusion,
        created_utc,
        updated_utc,
        is_active)
    VALUES(
        8,
        2,
        2,
        6,
        10,
        'Removed Slice Omega',
        'Draft',
        'Last saved text remains preserved.',
        '',
        '2026-10-03T14:00:00+00:00',
        '2026-10-03T14:30:00+00:00',
        0);

    INSERT INTO working_slice_revisions(
        working_slice_id,
        prior_title,
        prior_status,
        prior_research_notes,
        prior_conclusion,
        changed_utc)
    VALUES(
        7,
        'Reconciliation Alpha',
        'Draft',
        'Initial reconciliation notes.',
        '',
        '2026-10-03T13:15:00+00:00');

    INSERT INTO context_boundary_history(
        context_block_id,
        old_start_ayah, old_end_ayah,
        new_start_ayah, new_end_ayah,
        owner_note, changed_utc)
    VALUES(
        1,
        1, 5,
        1, 5,
        'Created by proposal import #1',
        '2026-10-03T10:45:00+00:00'),
        (2,
        6, 10,
        6, 10,
        'Superseded by proposal import #1',
        '2026-10-03T10:45:00+00:00'),
        (1,
        1, 4,
        1, 5,
        'Extended end later',
        '2026-10-03T11:00:00+00:00'),
        (2,
        5, 10,
        6, 10,
        'Gave start ayah to block 1',
        '2026-10-03T11:00:00+00:00'),
        (1,
        1, 5,
        1, 5,
        'Boundary confirmation',
        '2026-10-03T11:05:00+00:00');
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
