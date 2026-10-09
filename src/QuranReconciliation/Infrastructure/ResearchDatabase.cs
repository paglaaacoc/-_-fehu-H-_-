using Microsoft.Data.Sqlite;

namespace QuranReconciliation.Infrastructure;

internal static class ResearchDatabase
{
    internal const int SchemaVersion = 7;

    internal static void Initialize()
    {
        AppPaths.EnsurePortableDirectories();
        InitializeAt(AppPaths.ResearchDatabase);
    }

    internal static void InitializeAt(
        string databasePath)
    {
        string fullPath =
            Path.GetFullPath(databasePath);

        string? directory =
            Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        RejectNewerSchema(fullPath);

        using var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = fullPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false
                }.ToString());

        connection.Open();

        using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            """;
            pragmas.ExecuteNonQuery();
        }

        using var tx = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS context_blocks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            surah_number INTEGER NOT NULL,
            start_ayah INTEGER NOT NULL,
            end_ayah INTEGER NOT NULL,
            status TEXT NOT NULL DEFAULT 'Proposed',
            owner_note TEXT,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            CHECK (surah_number BETWEEN 1 AND 114),
            CHECK (start_ayah >= 1),
            CHECK (end_ayah >= start_ayah)
        );

        CREATE TABLE IF NOT EXISTS context_boundary_history (
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

        CREATE TABLE IF NOT EXISTS ayah_notes (
            surah_number INTEGER NOT NULL,
            ayah_number INTEGER NOT NULL,
            body TEXT NOT NULL DEFAULT '',
            updated_utc TEXT NOT NULL,
            PRIMARY KEY(surah_number, ayah_number)
        );

        CREATE TABLE IF NOT EXISTS ayah_note_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            surah_number INTEGER NOT NULL,
            ayah_number INTEGER NOT NULL,
            prior_body TEXT NOT NULL,
            changed_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS context_notes (
            context_block_id INTEGER PRIMARY KEY,
            body TEXT NOT NULL DEFAULT '',
            updated_utc TEXT NOT NULL,
            FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
        );

        CREATE TABLE IF NOT EXISTS context_note_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            context_block_id INTEGER NOT NULL,
            prior_body TEXT NOT NULL,
            changed_utc TEXT NOT NULL,
            FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
        );

        CREATE TABLE IF NOT EXISTS bookmarks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            surah_number INTEGER NOT NULL,
            ayah_number INTEGER NOT NULL,
            title TEXT NOT NULL DEFAULT '',
            note TEXT NOT NULL DEFAULT '',
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            UNIQUE(surah_number, ayah_number),
            CHECK (surah_number BETWEEN 1 AND 114),
            CHECK (ayah_number >= 1)
        );

        CREATE TABLE IF NOT EXISTS context_proposal_imports (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            surah_number INTEGER NOT NULL,
            raw_payload TEXT NOT NULL,
            normalized_ranges TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            CHECK (surah_number BETWEEN 1 AND 114)
        );

        CREATE INDEX IF NOT EXISTS idx_context_proposal_imports_surah
            ON context_proposal_imports(surah_number, id);

        CREATE TABLE IF NOT EXISTS working_slices (
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
            is_active INTEGER NOT NULL DEFAULT 1,
            CHECK (surah_number BETWEEN 1 AND 114),
            CHECK (start_ayah >= 1),
            CHECK (end_ayah >= start_ayah),
            CHECK (status IN ('Draft','In Review','Resolved')),
            FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
        );

        CREATE TABLE IF NOT EXISTS working_slice_revisions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            working_slice_id INTEGER NOT NULL,
            prior_title TEXT NOT NULL,
            prior_status TEXT NOT NULL,
            prior_research_notes TEXT NOT NULL,
            prior_conclusion TEXT NOT NULL,
            changed_utc TEXT NOT NULL,
            FOREIGN KEY(working_slice_id) REFERENCES working_slices(id)
        );

        CREATE INDEX IF NOT EXISTS idx_working_slices_status_updated
            ON working_slices(is_active, status, updated_utc);

        CREATE INDEX IF NOT EXISTS idx_working_slices_surah
            ON working_slices(surah_number, start_ayah, end_ayah);

        CREATE INDEX IF NOT EXISTS idx_working_slice_revisions_slice
            ON working_slice_revisions(working_slice_id, id);

        CREATE TABLE IF NOT EXISTS context_operations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            surah_number INTEGER NOT NULL,
            operation_type TEXT NOT NULL,
            summary TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            proposal_import_id INTEGER,
            CHECK (surah_number BETWEEN 1 AND 114)
        );

        CREATE TABLE IF NOT EXISTS context_operation_blocks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            operation_id INTEGER NOT NULL,
            context_block_id INTEGER NOT NULL,
            role TEXT NOT NULL,
            old_start_ayah INTEGER,
            old_end_ayah INTEGER,
            new_start_ayah INTEGER,
            new_end_ayah INTEGER,
            old_status TEXT,
            new_status TEXT,
            FOREIGN KEY(operation_id) REFERENCES context_operations(id),
            FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
        );

        CREATE INDEX IF NOT EXISTS idx_context_operations_surah_time
            ON context_operations(surah_number, created_utc, id);

        CREATE INDEX IF NOT EXISTS idx_context_operation_blocks_operation
            ON context_operation_blocks(operation_id, id);

        CREATE INDEX IF NOT EXISTS idx_context_operation_blocks_context
            ON context_operation_blocks(context_block_id, operation_id);

        CREATE TABLE IF NOT EXISTS research_activity_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            event_type TEXT NOT NULL,
            entity_type TEXT NOT NULL,
            entity_id INTEGER,
            surah_number INTEGER NOT NULL,
            ayah_number INTEGER,
            context_block_id INTEGER,
            working_slice_id INTEGER,
            start_ayah INTEGER,
            end_ayah INTEGER,
            summary TEXT NOT NULL,
            detail_json TEXT NOT NULL DEFAULT '{}',
            occurred_utc TEXT NOT NULL,
            CHECK (surah_number BETWEEN 1 AND 114)
        );

        CREATE INDEX IF NOT EXISTS idx_research_activity_time
            ON research_activity_events(occurred_utc, id);

        CREATE INDEX IF NOT EXISTS idx_research_activity_surah_time
            ON research_activity_events(surah_number, occurred_utc, id);

        CREATE INDEX IF NOT EXISTS idx_research_activity_entity_time
            ON research_activity_events(entity_type, entity_id, occurred_utc, id);

        CREATE INDEX IF NOT EXISTS idx_research_activity_context_time
            ON research_activity_events(context_block_id, occurred_utc, id);

        CREATE INDEX IF NOT EXISTS idx_research_activity_slice_time
            ON research_activity_events(working_slice_id, occurred_utc, id);

        CREATE TABLE IF NOT EXISTS context_proposal_replaced_blocks (
            import_id INTEGER NOT NULL,
            context_block_id INTEGER NOT NULL,
            position INTEGER NOT NULL,
            prior_status TEXT NOT NULL,
            prior_origin TEXT NOT NULL,
            prior_updated_utc TEXT NOT NULL,
            PRIMARY KEY(import_id, context_block_id),
            FOREIGN KEY(import_id) REFERENCES context_proposal_imports(id),
            FOREIGN KEY(context_block_id) REFERENCES context_blocks(id)
        );

        CREATE INDEX IF NOT EXISTS idx_context_proposal_replaced_blocks_import
            ON context_proposal_replaced_blocks(import_id, position);

        CREATE INDEX IF NOT EXISTS idx_bookmarks_surah_ayah
            ON bookmarks(surah_number, ayah_number);
        """;

        command.ExecuteNonQuery();

        EnsureColumn(
            connection,
            tx,
            "context_blocks",
            "origin",
            "TEXT NOT NULL DEFAULT 'Manual'");

        EnsureColumn(
            connection,
            tx,
            "context_blocks",
            "is_active",
            "INTEGER NOT NULL DEFAULT 1");

        EnsureColumn(
            connection,
            tx,
            "context_blocks",
            "proposal_import_id",
            "INTEGER");

        EnsureColumn(
            connection,
            tx,
            "context_proposal_imports",
            "discarded_utc",
            "TEXT");

        EnsureColumn(
            connection,
            tx,
            "working_slice_revisions",
            "changed_fields_json",
            "TEXT NOT NULL DEFAULT '[]'");

        EnsureColumn(
            connection,
            tx,
            "working_slice_revisions",
            "change_summary",
            "TEXT NOT NULL DEFAULT 'Legacy revision'");

        using var backfill = connection.CreateCommand();
        backfill.Transaction = tx;
        backfill.CommandText = """
        INSERT OR IGNORE INTO context_proposal_replaced_blocks(
            import_id,
            context_block_id,
            position,
            prior_status,
            prior_origin,
            prior_updated_utc)
        SELECT
            i.id,
            h.context_block_id,
            cb.start_ayah,
            cb.status,
            replace(
                cb.origin,
                ' · superseded by proposal import #' || i.id,
                ''),
            cb.created_utc
        FROM context_proposal_imports i
        JOIN context_boundary_history h
          ON h.owner_note =
             'Superseded by proposal import #' || i.id
        JOIN context_blocks cb
          ON cb.id = h.context_block_id;
        """;
        backfill.ExecuteNonQuery();

        BackfillLegacyActivity(
            connection,
            tx);

        using var version = connection.CreateCommand();
        version.Transaction = tx;
        version.CommandText = """
        INSERT INTO meta(key, value)
        VALUES ('schema_version', $version)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        """;
        version.Parameters.AddWithValue(
            "$version",
            SchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        version.ExecuteNonQuery();

        tx.Commit();
    }


    private static void BackfillLegacyActivity(
        SqliteConnection connection,
        SqliteTransaction tx)
    {
        using var count = connection.CreateCommand();
        count.Transaction = tx;
        count.CommandText =
            "SELECT COUNT(*) FROM research_activity_events;";

        if (Convert.ToInt64(count.ExecuteScalar()) != 0)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'AyahNoteSaved', 'AyahNote', NULL,
            surah_number, ayah_number, NULL, NULL,
            ayah_number, ayah_number,
            'Ayah note current state migrated into schema 7',
            json_object('body', body), updated_utc
        FROM ayah_notes;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'AyahNoteRevision', 'AyahNoteRevision', id,
            surah_number, ayah_number, NULL, NULL,
            ayah_number, ayah_number,
            'Ayah note revision preserved from pre-v1.3 history',
            json_object('body', prior_body), changed_utc
        FROM ayah_note_history;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'ContextNoteSaved', 'ContextNote', cn.context_block_id,
            cb.surah_number, NULL, cn.context_block_id, NULL,
            cb.start_ayah, cb.end_ayah,
            'Context note current state migrated into schema 7',
            json_object('body', cn.body), cn.updated_utc
        FROM context_notes cn
        JOIN context_blocks cb ON cb.id=cn.context_block_id;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'ContextNoteRevision', 'ContextNoteRevision', h.id,
            cb.surah_number, NULL, h.context_block_id, NULL,
            cb.start_ayah, cb.end_ayah,
            'Context note revision preserved from pre-v1.3 history',
            json_object('body', h.prior_body), h.changed_utc
        FROM context_note_history h
        JOIN context_blocks cb ON cb.id=h.context_block_id;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'ContextBoundaryLegacy', 'ContextBoundaryHistory', h.id,
            cb.surah_number, NULL, h.context_block_id, NULL,
            h.new_start_ayah, h.new_end_ayah,
            COALESCE(h.owner_note, 'Boundary edit') || ' · ' ||
            h.old_start_ayah || CASE WHEN h.old_end_ayah=h.old_start_ayah THEN '' ELSE '–' || h.old_end_ayah END ||
            ' → ' ||
            h.new_start_ayah || CASE WHEN h.new_end_ayah=h.new_start_ayah THEN '' ELSE '–' || h.new_end_ayah END,
            '{}', h.changed_utc
        FROM context_boundary_history h
        JOIN context_blocks cb ON cb.id=h.context_block_id
        WHERE COALESCE(h.owner_note, '') NOT LIKE 'Created by proposal import #%'
          AND COALESCE(h.owner_note, '') NOT LIKE 'Superseded by proposal import #%';

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            CASE WHEN discarded_utc IS NULL
                 THEN 'ContextProposalImported'
                 ELSE 'ContextProposalDiscarded' END,
            'ContextProposal', id,
            surah_number, NULL, NULL, NULL,
            1, 1,
            CASE WHEN discarded_utc IS NULL
                 THEN 'Imported Context proposal #' || id || ' · ' || normalized_ranges
                 ELSE 'Discarded Context proposal #' || id || ' · ' || normalized_ranges END,
            '{}', COALESCE(discarded_utc, created_utc)
        FROM context_proposal_imports;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'BookmarkSaved', 'Bookmark', id,
            surah_number, ayah_number, NULL, NULL,
            ayah_number, ayah_number,
            CASE WHEN trim(title)='' THEN 'Bookmark ' || surah_number || ':' || ayah_number
                 ELSE 'Bookmark · ' || title END,
            json_object('title', title, 'note', note), updated_utc
        FROM bookmarks;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            CASE WHEN is_active=1 THEN 'WorkingSliceSaved' ELSE 'WorkingSliceRemoved' END,
            'WorkingSlice', id,
            surah_number, NULL, context_block_id, id,
            start_ayah, end_ayah,
            CASE WHEN is_active=1
                 THEN 'Working Slice · ' || title || ' · ' || status
                 ELSE 'Working Slice removed · ' || title || ' · ' || status END,
            json_object(
                'title', title,
                'status', status,
                'researchNotes', research_notes,
                'conclusion', conclusion), updated_utc
        FROM working_slices;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'WorkingSliceRevision', 'WorkingSliceRevision', wr.id,
            ws.surah_number, NULL, ws.context_block_id, ws.id,
            ws.start_ayah, ws.end_ayah,
            'Working Slice revision preserved · ' || wr.prior_title || ' · ' || wr.prior_status,
            json_object(
                'title', wr.prior_title,
                'status', wr.prior_status,
                'researchNotes', wr.prior_research_notes,
                'conclusion', wr.prior_conclusion), wr.changed_utc
        FROM working_slice_revisions wr
        JOIN working_slices ws ON ws.id=wr.working_slice_id;

        INSERT INTO research_activity_events(
            event_type, entity_type, entity_id,
            surah_number, ayah_number, context_block_id, working_slice_id,
            start_ayah, end_ayah, summary, detail_json, occurred_utc)
        SELECT
            'ContextStateMigrated', 'ContextBlock', id,
            surah_number, NULL, id, NULL,
            start_ayah, end_ayah,
            'Context state migrated · ' || status || ' · ' ||
            start_ayah || CASE WHEN end_ayah=start_ayah THEN '' ELSE '–' || end_ayah END,
            '{}', updated_utc
        FROM context_blocks
        WHERE is_active=1;
        """;
        command.ExecuteNonQuery();
    }

    private static void RejectNewerSchema(
        string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        using var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false
                }.ToString());

        connection.Open();

        using var table = connection.CreateCommand();
        table.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name='meta';
            """;

        if (Convert.ToInt64(table.ExecuteScalar()) == 0)
        {
            return;
        }

        using var schema = connection.CreateCommand();
        schema.CommandText =
            "SELECT value FROM meta WHERE key='schema_version';";

        string? value =
            schema.ExecuteScalar()?.ToString();

        if (int.TryParse(value, out int version) &&
            version > SchemaVersion)
        {
            throw new InvalidDataException(
                $"This research database uses schema {version}, but this app supports through schema {SchemaVersion}. Refusing to open newer owner state with an older build.");
        }
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        SqliteTransaction tx,
        string table,
        string column,
        string definition)
    {
        using var pragma = connection.CreateCommand();
        pragma.Transaction = tx;
        pragma.CommandText = $"PRAGMA table_info({table});";

        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(
                reader.GetString(1),
                column,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();

        using var alter = connection.CreateCommand();
        alter.Transaction = tx;
        alter.CommandText =
            $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
}
