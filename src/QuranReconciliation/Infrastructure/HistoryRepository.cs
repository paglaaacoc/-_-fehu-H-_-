using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class HistoryRepository
{
    private readonly string _databasePath;

    internal HistoryRepository(
        string? databasePath = null)
    {
        _databasePath =
            databasePath ??
            AppPaths.ResearchDatabase;
    }

    // Compatibility entry point: Timeline is now the immutable audit source.
    internal IReadOnlyList<ResearchHistoryEntry> Search(
        string? query,
        string category = "All",
        int surahNumber = 0,
        int limit = 80) =>
        SearchTimeline(
            query,
            category,
            surahNumber,
            limit);

    internal IReadOnlyList<ResearchHistoryEntry> SearchTimeline(
        string? query,
        string category = "All",
        int surahNumber = 0,
        int limit = 120)
    {
        string needle =
            query?.Trim() ??
            string.Empty;

        string pattern =
            SqliteLiteralLike.Pattern(needle);

        string normalizedCategory =
            NormalizeCategory(category);

        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();

        command.CommandText = """
        WITH timeline AS (
            SELECT
                e.id,
                CASE
                    WHEN e.event_type LIKE 'AyahNote%' THEN 'AyahNotes'
                    WHEN e.event_type LIKE 'ContextNote%' THEN 'ContextNotes'
                    WHEN e.event_type LIKE 'Bookmark%' THEN 'Bookmarks'
                    WHEN e.event_type LIKE 'WorkingSlice%' THEN 'WorkingSlices'
                    WHEN e.event_type LIKE 'Context%' THEN 'Context'
                    ELSE 'Context'
                END AS category,
                e.event_type,
                e.entity_type,
                e.entity_id,
                e.surah_number,
                e.ayah_number,
                e.context_block_id,
                CASE
                    WHEN ws.is_active=1 THEN e.working_slice_id
                    ELSE NULL
                END AS open_working_slice_id,
                e.start_ayah,
                e.end_ayah,
                e.summary,
                e.detail_json,
                e.occurred_utc,
                CASE
                    WHEN e.ayah_number IS NOT NULL
                        THEN CAST(e.surah_number AS TEXT) || ':' ||
                             CAST(e.ayah_number AS TEXT)
                    WHEN e.start_ayah IS NOT NULL
                        THEN 'Surah ' || CAST(e.surah_number AS TEXT) || ' · ' ||
                             CASE
                                 WHEN e.start_ayah=e.end_ayah
                                     THEN 'Ayah ' || CAST(e.start_ayah AS TEXT)
                                 ELSE 'Ayat ' || CAST(e.start_ayah AS TEXT) ||
                                      '–' || CAST(e.end_ayah AS TEXT)
                             END
                    ELSE 'Surah ' || CAST(e.surah_number AS TEXT)
                END AS target
            FROM research_activity_events e
            LEFT JOIN working_slices ws
              ON ws.id=e.working_slice_id
        )
        SELECT
            category,
            event_type,
            target,
            summary,
            occurred_utc,
            surah_number,
            ayah_number,
            context_block_id,
            open_working_slice_id,
            start_ayah,
            end_ayah,
            entity_type,
            entity_id
        FROM timeline
        WHERE ($category='All' OR category=$category)
          AND ($surah=0 OR surah_number=$surah)
          AND (
                $needle=''
             OR event_type LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR entity_type LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR target LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR summary LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR detail_json LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR occurred_utc LIKE $pattern ESCAPE '\' COLLATE NOCASE
          )
        ORDER BY occurred_utc DESC, id DESC
        LIMIT $limit;
        """;

        command.Parameters.AddWithValue("$category", normalizedCategory);
        command.Parameters.AddWithValue(
            "$surah",
            Math.Clamp(surahNumber, 0, 114));
        command.Parameters.AddWithValue("$needle", needle);
        command.Parameters.AddWithValue("$pattern", pattern);
        command.Parameters.AddWithValue(
            "$limit",
            Math.Clamp(limit, 1, 10000));

        using var reader = command.ExecuteReader();
        var result = new List<ResearchHistoryEntry>();

        while (reader.Read())
        {
            result.Add(
                new ResearchHistoryEntry(
                    reader.GetString(0),
                    EventLabel(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    DateTimeOffset.Parse(reader.GetString(4)),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetInt64(12),
                    0,
                    Array.Empty<HistoryRevisionItem>()));
        }

        return result;
    }

    internal IReadOnlyList<ResearchHistoryEntry> SearchOrganized(
        string? query,
        string category = "All",
        int surahNumber = 0,
        int limit = 600)
    {
        string needle =
            query?.Trim() ??
            string.Empty;

        string pattern =
            SqliteLiteralLike.Pattern(needle);

        string normalizedCategory =
            NormalizeCategory(category);

        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();

        command.CommandText = """
        WITH objects AS (
            SELECT
                'AyahNotes' AS category,
                'Ayah note' AS kind,
                'AyahNote' AS entity_type,
                NULL AS entity_id,
                CAST(n.surah_number AS TEXT) || ':' ||
                    CAST(n.ayah_number AS TEXT) AS target,
                n.body AS body,
                n.updated_utc AS changed_utc,
                n.surah_number,
                n.ayah_number,
                NULL AS context_block_id,
                NULL AS working_slice_id,
                n.ayah_number AS start_ayah,
                n.ayah_number AS end_ayah,
                (SELECT COUNT(*)
                 FROM ayah_note_history h
                 WHERE h.surah_number=n.surah_number
                   AND h.ayah_number=n.ayah_number) AS revision_count
            FROM ayah_notes n

            UNION ALL

            SELECT
                'Bookmarks',
                'Bookmark',
                'Bookmark',
                b.id,
                CAST(b.surah_number AS TEXT) || ':' ||
                    CAST(b.ayah_number AS TEXT),
                CASE
                    WHEN trim(b.title)='' THEN b.note
                    WHEN trim(b.note)='' THEN b.title
                    ELSE b.title || char(10) || b.note
                END,
                b.updated_utc,
                b.surah_number,
                b.ayah_number,
                NULL,
                NULL,
                b.ayah_number,
                b.ayah_number,
                0
            FROM bookmarks b

            UNION ALL

            SELECT
                'ContextNotes',
                'Context note',
                'ContextNote',
                cn.context_block_id,
                'Surah ' || CAST(cb.surah_number AS TEXT) || ' · ' ||
                    CASE
                        WHEN cb.start_ayah=cb.end_ayah
                            THEN 'Ayah ' || CAST(cb.start_ayah AS TEXT)
                        ELSE 'Ayat ' || CAST(cb.start_ayah AS TEXT) ||
                             '–' || CAST(cb.end_ayah AS TEXT)
                    END,
                cn.body,
                cn.updated_utc,
                cb.surah_number,
                NULL,
                cn.context_block_id,
                NULL,
                cb.start_ayah,
                cb.end_ayah,
                (SELECT COUNT(*)
                 FROM context_note_history h
                 WHERE h.context_block_id=cn.context_block_id)
            FROM context_notes cn
            JOIN context_blocks cb
              ON cb.id=cn.context_block_id

            UNION ALL

            SELECT
                'WorkingSlices',
                CASE
                    WHEN ws.is_active=1
                        THEN 'Working Slice · ' || ws.status
                    ELSE 'Working Slice · removed · ' || ws.status
                END,
                'WorkingSlice',
                ws.id,
                'Surah ' || CAST(ws.surah_number AS TEXT) || ' · ' ||
                    CASE
                        WHEN ws.start_ayah=ws.end_ayah
                            THEN 'Ayah ' || CAST(ws.start_ayah AS TEXT)
                        ELSE 'Ayat ' || CAST(ws.start_ayah AS TEXT) ||
                             '–' || CAST(ws.end_ayah AS TEXT)
                    END || ' · ' || ws.title,
                CASE
                    WHEN trim(ws.research_notes)='' AND trim(ws.conclusion)=''
                        THEN ws.title
                    WHEN trim(ws.conclusion)=''
                        THEN ws.research_notes
                    WHEN trim(ws.research_notes)=''
                        THEN 'Conclusion: ' || ws.conclusion
                    ELSE ws.research_notes || char(10) ||
                         'Conclusion: ' || ws.conclusion
                END,
                ws.updated_utc,
                ws.surah_number,
                NULL,
                ws.context_block_id,
                CASE WHEN ws.is_active=1 THEN ws.id ELSE NULL END,
                ws.start_ayah,
                ws.end_ayah,
                (SELECT COUNT(*)
                 FROM working_slice_revisions wr
                 WHERE wr.working_slice_id=ws.id)
            FROM working_slices ws

            UNION ALL

            SELECT
                'Context',
                CASE e.event_type
                    WHEN 'ContextSplit' THEN 'Context split'
                    WHEN 'ContextMerged' THEN 'Context merge'
                    WHEN 'ContextStatusChanged' THEN 'Context status'
                    WHEN 'ContextProposalImported' THEN 'Context proposal import'
                    WHEN 'ContextProposalDiscarded' THEN 'Context proposal discard'
                    WHEN 'ContextExtendStart' THEN 'Context boundary'
                    WHEN 'ContextShrinkStart' THEN 'Context boundary'
                    WHEN 'ContextExtendEnd' THEN 'Context boundary'
                    WHEN 'ContextShrinkEnd' THEN 'Context boundary'
                    ELSE 'Context activity'
                END,
                'ContextOperation',
                e.entity_id,
                CASE
                    WHEN e.start_ayah IS NULL
                        THEN 'Surah ' || CAST(e.surah_number AS TEXT)
                    WHEN e.start_ayah=e.end_ayah
                        THEN 'Surah ' || CAST(e.surah_number AS TEXT) ||
                             ' · Ayah ' || CAST(e.start_ayah AS TEXT)
                    ELSE 'Surah ' || CAST(e.surah_number AS TEXT) ||
                         ' · Ayat ' || CAST(e.start_ayah AS TEXT) ||
                         '–' || CAST(e.end_ayah AS TEXT)
                END,
                e.summary,
                e.occurred_utc,
                e.surah_number,
                NULL,
                e.context_block_id,
                NULL,
                e.start_ayah,
                e.end_ayah,
                0
            FROM research_activity_events e
            WHERE e.entity_type='ContextOperation'
        )
        SELECT
            category,
            kind,
            entity_type,
            entity_id,
            target,
            body,
            changed_utc,
            surah_number,
            ayah_number,
            context_block_id,
            working_slice_id,
            start_ayah,
            end_ayah,
            revision_count
        FROM objects
        WHERE ($category='All' OR category=$category)
          AND ($surah=0 OR surah_number=$surah)
          AND (
                $needle=''
             OR kind LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR target LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR body LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR changed_utc LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR (
                    entity_type='AyahNote'
                AND EXISTS (
                    SELECT 1
                    FROM ayah_note_history h
                    WHERE h.surah_number=objects.surah_number
                      AND h.ayah_number=objects.ayah_number
                      AND h.prior_body LIKE $pattern ESCAPE '\' COLLATE NOCASE
                )
             )
             OR (
                    entity_type='ContextNote'
                AND EXISTS (
                    SELECT 1
                    FROM context_note_history h
                    WHERE h.context_block_id=objects.entity_id
                      AND h.prior_body LIKE $pattern ESCAPE '\' COLLATE NOCASE
                )
             )
             OR (
                    entity_type='WorkingSlice'
                AND EXISTS (
                    SELECT 1
                    FROM working_slice_revisions wr
                    WHERE wr.working_slice_id=objects.entity_id
                      AND (
                            wr.prior_title LIKE $pattern ESCAPE '\' COLLATE NOCASE
                         OR wr.prior_status LIKE $pattern ESCAPE '\' COLLATE NOCASE
                         OR wr.prior_research_notes LIKE $pattern ESCAPE '\' COLLATE NOCASE
                         OR wr.prior_conclusion LIKE $pattern ESCAPE '\' COLLATE NOCASE
                         OR wr.change_summary LIKE $pattern ESCAPE '\' COLLATE NOCASE
                      )
                )
             )
          )
        ORDER BY
            surah_number ASC,
            CASE WHEN context_block_id IS NULL THEN 0 ELSE 1 END ASC,
            COALESCE(start_ayah, 0) ASC,
            COALESCE(context_block_id, 0) ASC,
            changed_utc DESC
        LIMIT $limit;
        """;

        command.Parameters.AddWithValue("$category", normalizedCategory);
        command.Parameters.AddWithValue(
            "$surah",
            Math.Clamp(surahNumber, 0, 114));
        command.Parameters.AddWithValue("$needle", needle);
        command.Parameters.AddWithValue("$pattern", pattern);
        command.Parameters.AddWithValue(
            "$limit",
            Math.Clamp(limit, 1, 2000));

        using var reader = command.ExecuteReader();
        var seeds = new List<OrganizedSeed>();

        while (reader.Read())
        {
            seeds.Add(
                new OrganizedSeed(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6)),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    reader.IsDBNull(10) ? null : reader.GetInt64(10),
                    reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    reader.GetInt32(13)));
        }

        reader.Close();

        var result =
            new List<ResearchHistoryEntry>(
                seeds.Count);

        foreach (OrganizedSeed seed in seeds)
        {
            IReadOnlyList<HistoryRevisionItem> revisions =
                seed.RevisionCount > 0
                    ? LoadRevisions(connection, seed, 20)
                    : Array.Empty<HistoryRevisionItem>();

            result.Add(
                new ResearchHistoryEntry(
                    seed.Category,
                    seed.Kind,
                    seed.Target,
                    seed.Body,
                    seed.ChangedUtc,
                    seed.SurahNumber,
                    seed.AyahNumber,
                    seed.ContextBlockId,
                    seed.WorkingSliceId,
                    seed.StartAyah,
                    seed.EndAyah,
                    seed.EntityType,
                    seed.EntityId,
                    seed.RevisionCount,
                    revisions));
        }

        return result;
    }

    private static IReadOnlyList<HistoryRevisionItem> LoadRevisions(
        SqliteConnection connection,
        OrganizedSeed seed,
        int limit)
    {
        using var command = connection.CreateCommand();

        if (seed.EntityType == "AyahNote" &&
            seed.AyahNumber is int ayah)
        {
            command.CommandText = """
            SELECT
                'Ayah note revision',
                prior_body,
                changed_utc
            FROM ayah_note_history
            WHERE surah_number=$surah
              AND ayah_number=$ayah
            ORDER BY id DESC
            LIMIT $limit;
            """;
            command.Parameters.AddWithValue("$surah", seed.SurahNumber);
            command.Parameters.AddWithValue("$ayah", ayah);
        }
        else if (seed.EntityType == "ContextNote" &&
                 seed.EntityId is long contextId)
        {
            command.CommandText = """
            SELECT
                'Context note revision',
                prior_body,
                changed_utc
            FROM context_note_history
            WHERE context_block_id=$id
            ORDER BY id DESC
            LIMIT $limit;
            """;
            command.Parameters.AddWithValue("$id", contextId);
        }
        else if (seed.EntityType == "WorkingSlice" &&
                 seed.EntityId is long sliceId)
        {
            command.CommandText = """
            SELECT
                change_summary,
                'Prior title: ' || prior_title || char(10) ||
                'Prior status: ' || prior_status ||
                CASE
                    WHEN trim(prior_research_notes)=''
                        THEN ''
                    ELSE char(10) || char(10) || prior_research_notes
                END ||
                CASE
                    WHEN trim(prior_conclusion)=''
                        THEN ''
                    ELSE char(10) || char(10) ||
                         'Conclusion: ' || prior_conclusion
                END,
                changed_utc
            FROM working_slice_revisions
            WHERE working_slice_id=$id
            ORDER BY id DESC
            LIMIT $limit;
            """;
            command.Parameters.AddWithValue("$id", sliceId);
        }
        else
        {
            return Array.Empty<HistoryRevisionItem>();
        }

        command.Parameters.AddWithValue(
            "$limit",
            Math.Clamp(limit, 1, 100));

        using var reader = command.ExecuteReader();
        var result = new List<HistoryRevisionItem>();

        while (reader.Read())
        {
            result.Add(
                new HistoryRevisionItem(
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTimeOffset.Parse(reader.GetString(2))));
        }

        return result;
    }

    private SqliteConnection OpenReadOnly()
    {
        var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.GetFullPath(_databasePath),
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false
                }.ToString());

        connection.Open();
        return connection;
    }

    private static string NormalizeCategory(
        string category) =>
        category is "AyahNotes" or
            "ContextNotes" or
            "Context" or
            "Bookmarks" or
            "WorkingSlices"
            ? category
            : "All";

    private static string EventLabel(
        string eventType) =>
        eventType switch
        {
            "AyahNoteCreated" => "Ayah note created",
            "AyahNoteRevised" => "Ayah note revised",
            "AyahNoteSaved" => "Ayah note",
            "AyahNoteRevision" => "Ayah note revision",
            "ContextNoteCreated" => "Context note created",
            "ContextNoteRevised" => "Context note revised",
            "ContextNoteSaved" => "Context note",
            "ContextNoteRevision" => "Context note revision",
            "BookmarkSaved" => "Bookmark saved",
            "WorkingSliceCreated" => "Working Slice created",
            "WorkingSliceRevised" => "Working Slice revised",
            "WorkingSliceSaved" => "Working Slice",
            "WorkingSliceRevision" => "Working Slice revision",
            "WorkingSliceRemoved" => "Working Slice removed",
            "ContextSplit" => "Context split",
            "ContextMerged" => "Context merge",
            "ContextStatusChanged" => "Context status changed",
            "ContextProposalImported" => "Context proposal imported",
            "ContextProposalDiscarded" => "Context proposal discarded",
            "ContextExtendStart" => "Context boundary",
            "ContextShrinkStart" => "Context boundary",
            "ContextExtendEnd" => "Context boundary",
            "ContextShrinkEnd" => "Context boundary",
            "ContextBoundaryLegacy" => "Context boundary",
            "ContextStateMigrated" => "Context state migrated",
            _ => eventType
        };

    private sealed record OrganizedSeed(
        string Category,
        string Kind,
        string EntityType,
        long? EntityId,
        string Target,
        string Body,
        DateTimeOffset ChangedUtc,
        int SurahNumber,
        int? AyahNumber,
        long? ContextBlockId,
        long? WorkingSliceId,
        int? StartAyah,
        int? EndAyah,
        int RevisionCount);
}
