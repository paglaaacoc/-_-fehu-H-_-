using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;

if (args.Length == 2 && args[0] == "--inspect")
{
    InspectAppDatabase(args[1]);
    Console.WriteLine("Build 10 packaged research DB inspection: PASS");
    return;
}

if (args.Length == 2 && args[0] == "--create-legacy")
{
    string legacyPath = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
    CreateLegacySchema(legacyPath);
    Console.WriteLine("Legacy schema-1 research DB created.");
    return;
}

string root = Path.Combine(
    Path.GetTempPath(),
    "QuranReconciliation-ContextProbe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string db = Path.Combine(root, "research.sqlite");

try
{
    CreateSchema(db);
    ResearchDatabase.InitializeAt(db);

    var repo = new ContextRepository(db);

    var seeded = repo.EnsureSeeded(2, 10);
    Require(seeded.Count == 1, "Expected one seed block.");
    Require(seeded[0].StartAyah == 1 && seeded[0].EndAyah == 10,
        "Seed must cover the whole Surah.");
    repo.ValidateMap(2, 10);

    long firstId = seeded[0].Id;
    long secondId = repo.SplitAfter(firstId, 4);

    var split = repo.GetBlocks(2);
    Require(split.Count == 2, "Split should produce two active blocks.");
    Require(split[0].StartAyah == 1 && split[0].EndAyah == 4,
        "First split range mismatch.");
    Require(split[1].StartAyah == 5 && split[1].EndAyah == 10,
        "Second split range mismatch.");
    repo.ValidateMap(2, 10);

    repo.ExtendEnd(firstId);
    var shifted = repo.GetBlocks(2);
    Require(shifted[0].EndAyah == 5 && shifted[1].StartAyah == 6,
        "Extending end must move the shared boundary without overlap/gap.");
    repo.ValidateMap(2, 10);

    repo.ShrinkEnd(firstId);
    shifted = repo.GetBlocks(2);
    Require(shifted[0].EndAyah == 4 && shifted[1].StartAyah == 5,
        "Shrinking end must return the shared boundary.");
    repo.ValidateMap(2, 10);

    repo.SetStatus(secondId, "Accepted");

    bool acceptedLocked = false;
    try
    {
        repo.MergeWithNext(firstId);
    }
    catch (InvalidOperationException ex)
        when (ex.Message.Contains("Accepted", StringComparison.Ordinal))
    {
        acceptedLocked = true;
    }

    Require(acceptedLocked, "Accepted neighbor must block boundary merge.");

    repo.SetStatus(secondId, "Owner Reviewed");
    long mergedId = repo.MergeWithNext(firstId);

    var merged = repo.GetBlocks(2);
    Require(merged.Count == 1, "Merge should leave one active block.");
    Require(merged[0].StartAyah == 1 && merged[0].EndAyah == 10,
        "Merged block must cover the whole Surah.");
    repo.ValidateMap(2, 10);

    long newSecond = repo.SplitAfter(mergedId, 3);
    repo.ExtendStart(newSecond);

    var afterStartShift = repo.GetBlocks(2);
    Require(afterStartShift[0].EndAyah == 2 &&
            afterStartShift[1].StartAyah == 3,
        "Extend start must take one ayah from the previous block.");
    repo.ValidateMap(2, 10);

    repo.ShrinkStart(newSecond);
    repo.ValidateMap(2, 10);

    repo.SetStatus(newSecond, "Accepted");

    bool acceptedSelfLocked = false;
    try
    {
        repo.ShrinkStart(newSecond);
    }
    catch (InvalidOperationException ex)
        when (ex.Message.Contains("Accepted", StringComparison.Ordinal))
    {
        acceptedSelfLocked = true;
    }

    Require(acceptedSelfLocked, "Accepted block must reject boundary edits.");

    Require(repo.GetHistory(mergedId, 50).Count > 0,
        "Boundary history must be retained for the surviving block.");
    Require(repo.GetHistory(newSecond, 50).Count > 0,
        "Boundary history must be retained for the split block.");

    var seedThree = repo.EnsureSeeded(3, 12);
    Require(seedThree.Count == 1 &&
            seedThree[0].Origin == "Unsegmented seed",
        "Surah 3 should begin with a neutral seed.");

    IReadOnlyList<QuranReconciliation.Models.ContextProposalRange> proposal =
        ContextProposalParser.Parse(
            "1-3\n4–8\n9\n10-12",
            12);

    var imported =
        repo.ImportProposal(
            3,
            12,
            "1-3\n4–8\n9\n10-12",
            proposal);

    Require(imported.BlockCount == 4,
        "Proposal import should create four active blocks.");
    Require(imported.NormalizedRanges == "1–3, 4–8, 9, 10–12",
        "Proposal normalization mismatch.");

    var importedBlocks = repo.GetBlocks(3);
    Require(importedBlocks.Count == 4,
        "Imported Context Map should contain four active blocks.");
    Require(importedBlocks.All(x => x.Status == "Proposed"),
        "Imported blocks must always start Proposed.");
    Require(importedBlocks.All(x =>
            x.Origin == $"Proposal import #{imported.ImportId}"),
        "Imported blocks must retain proposal provenance.");
    repo.ValidateMap(3, 12);

    bool gapRejected = false;
    try
    {
        ContextProposalParser.Parse(
            "1-3\n5-12",
            12);
    }
    catch (InvalidDataException)
    {
        gapRejected = true;
    }
    Require(gapRejected,
        "Proposal parser must reject gaps.");

    repo.SetStatus(
        importedBlocks[1].Id,
        "Accepted");

    bool importBlockedByAccepted = false;
    try
    {
        repo.ImportProposal(
            3,
            12,
            "1-6\n7-12",
            ContextProposalParser.Parse(
                "1-6\n7-12",
                12));
    }
    catch (InvalidOperationException ex)
        when (ex.Message.Contains(
            "Accepted",
            StringComparison.Ordinal))
    {
        importBlockedByAccepted = true;
    }

    Require(importBlockedByAccepted,
        "Proposal import must never overwrite an Accepted block.");
    Require(repo.GetBlocks(3).Count == 4,
        "Blocked import must leave the active Context Map unchanged.");

    var seedFour = repo.EnsureSeeded(4, 8);
    Require(seedFour.Count == 1 &&
            seedFour[0].Origin == "Unsegmented seed",
        "Surah 4 should begin with a neutral seed.");

    var importedFour = repo.ImportProposal(
        4,
        8,
        "1-2\n3-5\n6-8",
        ContextProposalParser.Parse(
            "1-2\n3-5\n6-8",
            8));

    var discardable = repo.GetDiscardableProposal(4);
    Require(discardable is not null &&
            discardable.CanDiscard &&
            discardable.ImportId == importedFour.ImportId &&
            discardable.BlockCount == 3,
        "Fresh imported proposal should be safely discardable.");

    repo.DiscardUnworkedProposal(
        importedFour.ImportId,
        4);

    var restoredFour = repo.GetBlocks(4);
    Require(restoredFour.Count == 1 &&
            restoredFour[0].StartAyah == 1 &&
            restoredFour[0].EndAyah == 8 &&
            restoredFour[0].Origin == "Unsegmented seed",
        "Discard should restore the exact previous Context Map.");

    repo.EnsureSeeded(5, 6);

    var importFive = repo.ImportProposal(
        5,
        6,
        "1-3\n4-6",
        ContextProposalParser.Parse(
            "1-3\n4-6",
            6));

    var fiveBlocks = repo.GetBlocks(5);
    repo.SetStatus(
        fiveBlocks[0].Id,
        "Owner Reviewed");

    var noLongerDiscardable =
        repo.GetDiscardableProposal(5);

    Require(noLongerDiscardable is not null &&
            !noLongerDiscardable.CanDiscard,
        "Worked proposal must stop being discardable.");

    using var connection = new SqliteConnection($"Data Source={db}");
    connection.Open();

    using var inactive = connection.CreateCommand();
    inactive.CommandText =
        "SELECT COUNT(*) FROM context_blocks WHERE is_active=0;";
    long inactiveCount = Convert.ToInt64(inactive.ExecuteScalar());
    Require(inactiveCount >= 1,
        "Merged blocks must be retained as inactive history, not deleted.");

    Console.WriteLine("Build 10 context/proposal/discard workflow contract: PASS");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void InspectAppDatabase(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("Packaged research DB is missing.", path);
    }

    using var connection = new SqliteConnection($"Data Source={path}");
    connection.Open();

    Require(HasColumn(connection, "context_blocks", "origin"),
        "context_blocks.origin migration missing.");
    Require(HasColumn(connection, "context_blocks", "is_active"),
        "context_blocks.is_active migration missing.");
    Require(HasColumn(connection, "context_blocks", "proposal_import_id"),
        "context_blocks.proposal_import_id migration missing.");
    Require(HasTable(connection, "context_proposal_imports"),
        "context_proposal_imports table migration missing.");
    Require(HasColumn(connection, "context_proposal_imports", "discarded_utc"),
        "context_proposal_imports.discarded_utc migration missing.");
    Require(HasTable(connection, "context_proposal_replaced_blocks"),
        "context_proposal_replaced_blocks table migration missing.");
    Require(HasTable(connection, "working_slices"),
        "working_slices table migration missing.");
    Require(HasTable(connection, "working_slice_revisions"),
        "working_slice_revisions table migration missing.");

    using var count = connection.CreateCommand();
    count.CommandText =
        "SELECT COUNT(*) FROM context_blocks WHERE surah_number=1 AND is_active=1;";
    long active = Convert.ToInt64(count.ExecuteScalar());
    Require(active >= 1,
        "Packaged app launch should seed the current Surah context map.");
}

static bool HasColumn(
    SqliteConnection connection,
    string table,
    string column)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({table});";

    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        if (string.Equals(
            reader.GetString(1),
            column,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static bool HasTable(
    SqliteConnection connection,
    string table)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
    SELECT COUNT(*)
    FROM sqlite_master
    WHERE type='table' AND name=$table;
    """;
    command.Parameters.AddWithValue("$table", table);

    return Convert.ToInt64(
        command.ExecuteScalar()) == 1;
}

static void CreateLegacySchema(string path)
{
    using var connection = new SqliteConnection($"Data Source={path}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = """
    PRAGMA foreign_keys=ON;

    CREATE TABLE meta (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
    );

    INSERT INTO meta(key, value)
    VALUES ('schema_version', '1');

    CREATE TABLE context_blocks (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        start_ayah INTEGER NOT NULL,
        end_ayah INTEGER NOT NULL,
        status TEXT NOT NULL DEFAULT 'Proposed',
        owner_note TEXT,
        created_utc TEXT NOT NULL,
        updated_utc TEXT NOT NULL
    );

    CREATE TABLE context_boundary_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        context_block_id INTEGER NOT NULL,
        old_start_ayah INTEGER NOT NULL,
        old_end_ayah INTEGER NOT NULL,
        new_start_ayah INTEGER NOT NULL,
        new_end_ayah INTEGER NOT NULL,
        owner_note TEXT,
        changed_utc TEXT NOT NULL,
        FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
    );

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
        is_active INTEGER NOT NULL DEFAULT 1,
        proposal_import_id INTEGER
    );

    CREATE TABLE context_proposal_imports (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        raw_payload TEXT NOT NULL,
        normalized_ranges TEXT NOT NULL,
        created_utc TEXT NOT NULL,
        discarded_utc TEXT
    );

    CREATE TABLE context_proposal_replaced_blocks (
        import_id INTEGER NOT NULL,
        context_block_id INTEGER NOT NULL,
        position INTEGER NOT NULL,
        prior_status TEXT NOT NULL,
        prior_origin TEXT NOT NULL,
        prior_updated_utc TEXT NOT NULL,
        PRIMARY KEY(import_id, context_block_id)
    );

    CREATE TABLE context_notes (
        context_block_id INTEGER PRIMARY KEY,
        body TEXT NOT NULL DEFAULT '',
        updated_utc TEXT NOT NULL
    );

    CREATE TABLE working_slices (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        surah_number INTEGER NOT NULL,
        context_block_id INTEGER NOT NULL,
        start_ayah INTEGER NOT NULL,
        end_ayah INTEGER NOT NULL,
        title TEXT NOT NULL,
        status TEXT NOT NULL DEFAULT 'Draft',
        research_notes TEXT NOT NULL DEFAULT '',
        conclusion TEXT NOT NULL DEFAULT '',
        created_utc TEXT NOT NULL,
        updated_utc TEXT NOT NULL,
        is_active INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE context_boundary_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        context_block_id INTEGER NOT NULL,
        old_start_ayah INTEGER NOT NULL,
        old_end_ayah INTEGER NOT NULL,
        new_start_ayah INTEGER NOT NULL,
        new_end_ayah INTEGER NOT NULL,
        owner_note TEXT,
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
