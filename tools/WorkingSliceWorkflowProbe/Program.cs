using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

string root = Path.Combine(
    Path.GetTempPath(),
    "QuranReconciliation-WorkingSliceProbe-" +
    Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(root);

string db = Path.Combine(root, "research.sqlite");

try
{
    CreateSchema(db);
    ResearchDatabase.InitializeAt(db);

    var repo = new WorkingSliceRepository(db);

    WorkingSlice first = repo.Create(
        new WorkingSliceCreateRequest(
            2,
            1,
            1,
            5,
            "Creation sequence"));

    Require(first.Status == "Draft",
        "New Working Slice must start Draft.");

    WorkingSlice second = repo.Create(
        new WorkingSliceCreateRequest(
            2,
            1,
            6,
            10,
            "Covenant sequence"));

    Require(first.Id != second.Id,
        "Multiple Working Slices must coexist under one Context Block.");

    Require(repo.List().Count == 2,
        "Both Working Slices should remain independently reopenable.");

    bool changed = repo.Save(
        new WorkingSliceSaveRequest(
            first.Id,
            "Creation sequence",
            "In Review",
            "Compare English and Bangla renderings.",
            "Provisional reconciliation conclusion."));

    Require(changed,
        "Changed Working Slice save must report a revision.");

    WorkingSlice reloaded = repo.Get(first.Id);

    Require(reloaded.Status == "In Review" &&
            reloaded.ResearchNotes.Contains("Compare", StringComparison.Ordinal) &&
            reloaded.Conclusion.Contains("Provisional", StringComparison.Ordinal),
        "Working Slice current state did not persist.");

    IReadOnlyList<WorkingSliceRevision> revisions =
        repo.GetRevisions(first.Id);

    Require(revisions.Count == 1 &&
            revisions[0].PriorStatus == "Draft" &&
            revisions[0].PriorResearchNotes == string.Empty &&
            revisions[0].PriorConclusion == string.Empty,
        "Working Slice save must preserve the exact prior state.");

    bool unchanged = repo.Save(
        new WorkingSliceSaveRequest(
            first.Id,
            "Creation sequence",
            "In Review",
            "Compare English and Bangla renderings.",
            "Provisional reconciliation conclusion."));

    Require(!unchanged,
        "Identical Working Slice save must not create revision noise.");

    Require(repo.GetRevisions(first.Id).Count == 1,
        "Identical save must not duplicate Working Slice revisions.");

    Require(repo.List("In Review").Count == 1 &&
            repo.List("Draft").Count == 1,
        "Working Slice status filters must remain independent.");

    WorkingSlice removed =
        repo.Remove(first.Id);

    Require(
        removed.Id == first.Id &&
        removed.Status == "In Review" &&
        removed.ResearchNotes.Contains(
            "Compare",
            StringComparison.Ordinal),
        "Soft removal must preserve and return the last committed Working Slice state.");

    Require(
        repo.List().Count == 1 &&
        repo.List()[0].Id == second.Id,
        "Removed Working Slice must disappear from the active list.");

    bool removedGetRejected = false;

    try
    {
        repo.Get(first.Id);
    }
    catch (InvalidOperationException)
    {
        removedGetRejected = true;
    }

    Require(
        removedGetRejected,
        "Removed Working Slice must no longer reopen as active.");

    Require(
        repo.GetRevisions(first.Id).Count == 1,
        "Soft removal must preserve Working Slice revision history.");

    using (var verify =
        new SqliteConnection(
            $"Data Source={db}"))
    {
        verify.Open();

        using var command =
            verify.CreateCommand();

        command.CommandText =
            """
            SELECT is_active,
                   title,
                   status,
                   research_notes,
                   conclusion
            FROM working_slices
            WHERE id=$id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            first.Id);

        using var reader =
            command.ExecuteReader();

        Require(
            reader.Read(),
            "Soft removal must not delete the Working Slice row.");

        Require(
            reader.GetInt32(0) == 0 &&
            reader.GetString(1) == "Creation sequence" &&
            reader.GetString(2) == "In Review" &&
            reader.GetString(3) ==
                "Compare English and Bangla renderings." &&
            reader.GetString(4) ==
                "Provisional reconciliation conclusion.",
            "Soft removal changed or deleted the committed Working Slice record.");
    }

    bool invalidRangeRejected = false;

    try
    {
        repo.Create(
            new WorkingSliceCreateRequest(
                2,
                1,
                9,
                12,
                "Outside parent"));
    }
    catch (InvalidDataException)
    {
        invalidRangeRejected = true;
    }

    Require(invalidRangeRejected,
        "Working Slice range must stay inside the parent Context Block.");

    // Build 1.9 literal-query regression. Wildcard characters typed by the
    // owner must never turn into full-table SQL LIKE wildcard searches.
    WorkingSlice literal = repo.Create(
        new WorkingSliceCreateRequest(
            2, 1, 7, 9, "Literal % underscore _ slash \\ in research"));
    Require(repo.List("All", "%").Count == 1 &&
            repo.List("All", "%")[0].Id == literal.Id,
        "Percent search must match a literal percent, not all slices.");
    Require(repo.List("All", "_").Count == 1 &&
            repo.List("All", "_")[0].Id == literal.Id,
        "Underscore search must match literal underscore only.");
    Require(repo.List("All", "\\").Count == 1 &&
            repo.List("All", "\\")[0].Id == literal.Id,
        "Backslash search must match literal slash, not malformed ESCAPE SQL.");
    Require(repo.List("All", "absent%").Count == 0,
        "Wildcards must not broaden an otherwise unmatched literal search.");

    Console.WriteLine(
        "Build 1.9 literal Working Slice search: PASS");

    Console.WriteLine(
        "Build 10 R12 Working Slice persistence/revision/removal contract: PASS");
}
finally
{
    try
    {
        Directory.Delete(
            root,
            recursive: true);
    }
    catch
    {
    }
}

static void CreateSchema(string path)
{
    using var connection =
        new SqliteConnection(
            $"Data Source={path}");

    connection.Open();

    using var command =
        connection.CreateCommand();

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
        is_active INTEGER NOT NULL DEFAULT 1
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
        is_active INTEGER NOT NULL DEFAULT 1,
        FOREIGN KEY(context_block_id)
            REFERENCES context_blocks(id)
    );

    CREATE TABLE working_slice_revisions (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        working_slice_id INTEGER NOT NULL,
        prior_title TEXT NOT NULL,
        prior_status TEXT NOT NULL,
        prior_research_notes TEXT NOT NULL,
        prior_conclusion TEXT NOT NULL,
        changed_utc TEXT NOT NULL,
        FOREIGN KEY(working_slice_id)
            REFERENCES working_slices(id)
    );

    INSERT INTO context_blocks(
        id,
        surah_number,
        start_ayah,
        end_ayah,
        status,
        owner_note,
        created_utc,
        updated_utc,
        origin,
        is_active)
    VALUES(
        1,
        2,
        1,
        10,
        'Owner Reviewed',
        NULL,
        '2026-10-04T00:00:00+00:00',
        '2026-10-04T00:00:00+00:00',
        'Manual',
        1);
    """;

    command.ExecuteNonQuery();
}

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
