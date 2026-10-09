using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

string root =
    Path.Combine(
        Path.GetTempPath(),
        "THTRP-v13-ResearchOrganization-" +
        Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(root);

string db =
    Path.Combine(
        root,
        "research.sqlite");

try
{
    ResearchDatabase.InitializeAt(db);

    Require(
        ReadScalar(
            db,
            "SELECT value FROM meta WHERE key='schema_version';") ==
            "7",
        "v1.3 must initialize research schema 7.");

    Require(
        Convert.ToInt32(
            ReadScalar(
                db,
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table'
                  AND name IN (
                    'research_activity_events',
                    'context_operations',
                    'context_operation_blocks');
                """)) == 3,
        "Schema 7 provenance tables are missing.");

    var contexts =
        new ContextRepository(db);

    ContextBlock seed =
        contexts.EnsureSeeded(2, 10)
            .Single();

    long second =
        contexts.SplitAfter(
            seed.Id,
            4);

    AssertSingleOperation(
        db,
        "Split",
        "ContextSplit",
        expectedBlocks: 2);

    contexts.ExtendEnd(seed.Id);

    AssertSingleOperation(
        db,
        "ExtendEnd",
        "ContextExtendEnd",
        expectedBlocks: 2);

    contexts.ShrinkEnd(seed.Id);

    AssertSingleOperation(
        db,
        "ShrinkEnd",
        "ContextShrinkEnd",
        expectedBlocks: 2);

    contexts.SetStatus(
        second,
        "Accepted");

    Require(
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM context_operations
            WHERE operation_type='StatusChange';
            """) == 1,
        "One Context status change must create one logical operation.");

    using (var connection = Open(db))
    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
        SELECT old_status, new_status
        FROM context_operation_blocks ob
        JOIN context_operations o
          ON o.id=ob.operation_id
        WHERE o.operation_type='StatusChange'
        ORDER BY ob.id DESC
        LIMIT 1;
        """;

        using var reader =
            command.ExecuteReader();

        Require(
            reader.Read() &&
            reader.GetString(0) ==
                "Proposed" &&
            reader.GetString(1) ==
                "Accepted",
            "Context status provenance must retain exact old/new status.");
    }

    contexts.SetStatus(
        second,
        "Owner Reviewed");

    long merged =
        contexts.MergeWithNext(
            seed.Id);

    AssertSingleOperation(
        db,
        "Merge",
        "ContextMerged",
        expectedBlocks: 2);

    ContextProposalImportResult imported =
        contexts.ImportProposal(
            2,
            10,
            "1-3\n4-10",
            new[]
            {
                new ContextProposalRange(1, 3),
                new ContextProposalRange(4, 10)
            });

    Require(
        imported.BlockCount == 2 &&
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM context_operations
            WHERE operation_type='ProposalImport';
            """) == 1 &&
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM research_activity_events
            WHERE event_type='ContextProposalImported';
            """) == 1,
        "Proposal import must be one logical operation and one Timeline event.");

    contexts.DiscardUnworkedProposal(
        imported.ImportId,
        2);

    Require(
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM context_operations
            WHERE operation_type='ProposalDiscard';
            """) == 1 &&
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM research_activity_events
            WHERE event_type='ContextProposalDiscarded';
            """) == 1,
        "Proposal discard must be one logical operation and one Timeline event.");

    ContextBlock parent =
        contexts.GetBlocks(2)
            .Single();

    var slices =
        new WorkingSliceRepository(db);

    WorkingSlice first =
        slices.Create(
            new WorkingSliceCreateRequest(
                2,
                parent.Id,
                1,
                4,
                "Creation research"));

    WorkingSlice secondSlice =
        slices.Create(
            new WorkingSliceCreateRequest(
                2,
                parent.Id,
                5,
                10,
                "Covenant research"));

    Require(
        slices.List(
                "All",
                "Covenant",
                "Research")
            .Single()
            .Id ==
            secondSlice.Id,
        "Working Slice navigator query must search saved slices.");

    bool changed =
        slices.Save(
            new WorkingSliceSaveRequest(
                first.Id,
                "Creation research",
                "In Review",
                "Compare English and বাংলা renderings.",
                "Provisional conclusion."));

    Require(
        changed,
        "Changed Working Slice must save.");

    WorkingSliceRevision revision =
        slices.GetRevisions(first.Id)
            .Single();

    Require(
        revision.ChangeSummary.Contains(
            "Draft → In Review",
            StringComparison.Ordinal) &&
        revision.ChangeSummary.Contains(
            "notes added",
            StringComparison.Ordinal) &&
        revision.ChangeSummary.Contains(
            "conclusion added",
            StringComparison.Ordinal) &&
        revision.ChangedFieldsJson.Contains(
            "research_notes",
            StringComparison.Ordinal),
        "Working Slice revision must summarize exactly what changed.");

    var notes =
        new NoteRepository(db);

    notes.SaveAyahNote(
        2,
        2,
        "Current note.");

    notes.SaveAyahNote(
        2,
        2,
        "Current note revised.");

    var bookmarks =
        new BookmarkRepository(db);

    bookmarks.Save(
        2,
        2,
        "Research return point",
        "Bookmark note.");

    WorkingSlice removed =
        slices.Remove(
            secondSlice.Id);

    Require(
        removed.Id ==
            secondSlice.Id &&
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM working_slices
            WHERE id=$id AND is_active=0;
            """,
            ("$id", secondSlice.Id)) == 1 &&
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM research_activity_events
            WHERE event_type='WorkingSliceRemoved'
              AND working_slice_id=$id;
            """,
            ("$id", secondSlice.Id)) == 1,
        "Working Slice removal must preserve the record and add one immutable activity event.");

    var history =
        new HistoryRepository(db);

    IReadOnlyList<ResearchHistoryEntry>
        organized =
            history.SearchOrganized(
                "বাংলা",
                "WorkingSlices",
                2,
                100);

    Require(
        organized.Count == 1 &&
        organized[0].WorkingSliceId ==
            first.Id &&
        organized[0].RevisionCount == 1,
        "Organized History must search current Working Slice content and keep revisions subordinate.");

    IReadOnlyList<ResearchHistoryEntry>
        removedHistory =
            history.SearchOrganized(
                "Covenant research",
                "WorkingSlices",
                2,
                100);

    Require(
        removedHistory.Count == 1 &&
        removedHistory[0].WorkingSliceId
            is null &&
        removedHistory[0].JumpLabel ==
            "Open parent context",
        "Removed Working Slice must stay in Organized History without reopening.");

    SeedTimelineScale(
        db,
        2505);

    IReadOnlyList<ResearchHistoryEntry>
        largeTimeline =
            history.SearchTimeline(
                "Scale event",
                "Context",
                2,
                3000);

    Require(
        largeTimeline.Count == 2505,
        $">2,000-event Timeline query returned {largeTimeline.Count}, expected 2,505.");

    Require(
        largeTimeline[0].ChangedUtc >=
        largeTimeline[^1].ChangedUtc,
        "Timeline must remain reverse chronological at scale.");

    Console.WriteLine(
        "THTRP v1.3 research organization/provenance/scaling contract: PASS");
}
finally
{
    try
    {
        SqliteConnection.ClearAllPools();

        Directory.Delete(
            root,
            recursive: true);
    }
    catch
    {
    }
}

static void AssertSingleOperation(
    string db,
    string operationType,
    string eventType,
    int expectedBlocks)
{
    Require(
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM context_operations
            WHERE operation_type=$type;
            """,
            ("$type", operationType)) == 1,
        $"{operationType} must create exactly one logical Context operation.");

    long operationId;

    using (var connection = Open(db))
    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
        SELECT id
        FROM context_operations
        WHERE operation_type=$type
        ORDER BY id DESC
        LIMIT 1;
        """;
        command.Parameters.AddWithValue(
            "$type",
            operationType);

        operationId =
            Convert.ToInt64(
                command.ExecuteScalar());
    }

    Require(
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM context_operation_blocks
            WHERE operation_id=$id;
            """,
            ("$id", operationId)) ==
            expectedBlocks,
        $"{operationType} must preserve all affected Context blocks.");

    Require(
        Count(
            db,
            """
            SELECT COUNT(*)
            FROM research_activity_events
            WHERE event_type=$event;
            """,
            ("$event", eventType)) == 1,
        $"{operationType} must create exactly one Timeline event.");
}

static void SeedTimelineScale(
    string db,
    int count)
{
    using var connection =
        Open(db);

    using var tx =
        connection.BeginTransaction();

    using var command =
        connection.CreateCommand();

    command.Transaction = tx;
    command.CommandText = """
    INSERT INTO research_activity_events(
        event_type,
        entity_type,
        entity_id,
        surah_number,
        ayah_number,
        context_block_id,
        working_slice_id,
        start_ayah,
        end_ayah,
        summary,
        detail_json,
        occurred_utc)
    VALUES(
        'ContextScaleProbe',
        'ContextOperation',
        $id,
        2,
        NULL,
        NULL,
        NULL,
        1,
        10,
        $summary,
        '{}',
        $time);
    """;

    var id =
        command.Parameters.Add(
            "$id",
            SqliteType.Integer);

    var summary =
        command.Parameters.Add(
            "$summary",
            SqliteType.Text);

    var time =
        command.Parameters.Add(
            "$time",
            SqliteType.Text);

    DateTimeOffset start =
        new(
            2026,
            10,
            6,
            12,
            0,
            0,
            TimeSpan.Zero);

    for (int i = 0;
         i < count;
         i++)
    {
        id.Value =
            100000 + i;

        summary.Value =
            $"Scale event {i:D4}";

        time.Value =
            start.AddSeconds(i)
                .ToString("O");

        command.ExecuteNonQuery();
    }

    tx.Commit();
}

static SqliteConnection Open(
    string db)
{
    var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    Path.GetFullPath(db),
                Pooling =
                    false
            }.ToString());

    connection.Open();
    return connection;
}

static int Count(
    string db,
    string sql,
    params (string Name, object Value)[]
        parameters)
{
    using var connection =
        Open(db);

    using var command =
        connection.CreateCommand();

    command.CommandText =
        sql;

    foreach ((string name, object value)
             in parameters)
    {
        command.Parameters.AddWithValue(
            name,
            value);
    }

    return Convert.ToInt32(
        command.ExecuteScalar());
}

static string? ReadScalar(
    string db,
    string sql)
{
    using var connection =
        Open(db);

    using var command =
        connection.CreateCommand();

    command.CommandText =
        sql;

    return command.ExecuteScalar()
        ?.ToString();
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
