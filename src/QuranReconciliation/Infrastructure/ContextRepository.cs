using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class ContextRepository
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.Ordinal)
        {
            "Proposed",
            "Owner Reviewed",
            "Accepted"
        };

    private readonly string _databasePath;

    internal ContextRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.ResearchDatabase;
    }

    internal IReadOnlyList<ContextBlock> EnsureSeeded(
        int surahNumber,
        int versesCount)
    {
        IReadOnlyList<ContextBlock> existing = GetBlocks(surahNumber);
        if (existing.Count > 0)
        {
            return existing;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO context_blocks(
            surah_number, start_ayah, end_ayah, status,
            owner_note, created_utc, updated_utc, origin, is_active)
        VALUES(
            $surah, 1, $end, 'Proposed',
            NULL, $now, $now, 'Unsegmented seed', 1);
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);
        command.Parameters.AddWithValue("$end", versesCount);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();

        return GetBlocks(surahNumber);
    }

    internal IReadOnlyList<ContextBlock> GetBlocks(int surahNumber)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, surah_number, start_ayah, end_ayah,
               status, origin, updated_utc
        FROM context_blocks
        WHERE surah_number=$surah AND is_active=1
        ORDER BY start_ayah, id;
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);

        using var reader = command.ExecuteReader();
        var result = new List<ContextBlock>();

        while (reader.Read())
        {
            result.Add(new ContextBlock(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6))));
        }

        return result;
    }

    internal ContextBlock GetBlock(long id)
    {
        using var connection = Open();
        return GetBlock(connection, id)
            ?? throw new InvalidOperationException("Context block no longer exists.");
    }

    internal ContextBlock GetHistoricalBlock(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, surah_number, start_ayah, end_ayah,
               status, origin, updated_utc
        FROM context_blocks
        WHERE id=$id
        LIMIT 1;
        """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException(
                "Original Context Block no longer exists.");
        }

        return new ContextBlock(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    internal bool IsBlockActive(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT is_active FROM context_blocks WHERE id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        object? value = command.ExecuteScalar();
        if (value is null || value is DBNull)
        {
            throw new InvalidOperationException(
                "Original Context Block no longer exists.");
        }

        return Convert.ToInt64(value) != 0;
    }

    internal IReadOnlyList<ContextBoundaryEvent> GetHistory(
        long blockId,
        int limit = 12)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, context_block_id,
               old_start_ayah, old_end_ayah,
               new_start_ayah, new_end_ayah,
               owner_note, changed_utc
        FROM context_boundary_history
        WHERE context_block_id=$id
        ORDER BY id DESC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$id", blockId);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var result = new List<ContextBoundaryEvent>();

        while (reader.Read())
        {
            result.Add(new ContextBoundaryEvent(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7))));
        }

        return result;
    }

    internal void SetStatus(long id, string status)
    {
        if (!AllowedStatuses.Contains(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        using var connection = Open();
        using var tx = connection.BeginTransaction();

        ContextBlock block =
            GetBlock(connection, id, tx)
            ?? throw new InvalidOperationException(
                "Context block no longer exists.");

        if (string.Equals(
                block.Status,
                status,
                StringComparison.Ordinal))
        {
            tx.Commit();
            return;
        }

        string now =
            DateTimeOffset.UtcNow.ToString("O");

        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
            UPDATE context_blocks
            SET status=$status, updated_utc=$now
            WHERE id=$id AND is_active=1;
            """;
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$id", id);

            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "Context block no longer exists.");
            }
        }

        string summary =
            $"Context status · {block.RangeLabel} · {block.Status} → {status}";

        long operationId =
            AddOperation(
                connection,
                tx,
                block.SurahNumber,
                "StatusChange",
                summary,
                now);

        AddOperationBlock(
            connection,
            tx,
            operationId,
            block.Id,
            "Target",
            block.StartAyah,
            block.EndAyah,
            block.StartAyah,
            block.EndAyah,
            block.Status,
            status);

        AddOperationActivity(
            connection,
            tx,
            operationId,
            "ContextStatusChanged",
            block.SurahNumber,
            block.Id,
            block.StartAyah,
            block.EndAyah,
            summary,
            now);

        tx.Commit();
    }

    internal long SplitAfter(long id, int ayah)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        ContextBlock block = GetBlock(connection, id, tx)
            ?? throw new InvalidOperationException("Context block no longer exists.");

        EnsureEditable(block);

        if (ayah < block.StartAyah || ayah >= block.EndAyah)
        {
            throw new InvalidOperationException(
                $"Split point must be between {block.StartAyah} and {block.EndAyah - 1}.");
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        UpdateBlock(
            connection, tx, block.Id,
            block.StartAyah, ayah,
            "Proposed", "Manual edit", now);

        AddHistory(
            connection, tx, block.Id,
            block.StartAyah, block.EndAyah,
            block.StartAyah, ayah,
            $"Split after ayah {ayah}", now);

        using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
        INSERT INTO context_blocks(
            surah_number, start_ayah, end_ayah, status,
            owner_note, created_utc, updated_utc, origin, is_active)
        VALUES(
            $surah, $start, $end, 'Proposed',
            NULL, $now, $now, 'Manual split', 1);
        SELECT last_insert_rowid();
        """;
        insert.Parameters.AddWithValue("$surah", block.SurahNumber);
        insert.Parameters.AddWithValue("$start", ayah + 1);
        insert.Parameters.AddWithValue("$end", block.EndAyah);
        insert.Parameters.AddWithValue("$now", now);

        long newId = Convert.ToInt64(insert.ExecuteScalar());

        AddHistory(
            connection, tx, newId,
            ayah + 1, block.EndAyah,
            ayah + 1, block.EndAyah,
            $"Created by split from block {block.Id}", now);

        string summary =
            $"Split Context {block.RangeLabel} after ayah {ayah} → " +
            $"{block.StartAyah}–{ayah} + {ayah + 1}–{block.EndAyah}";

        long operationId =
            AddOperation(
                connection,
                tx,
                block.SurahNumber,
                "Split",
                summary,
                now);

        AddOperationBlock(
            connection,
            tx,
            operationId,
            block.Id,
            "Retained",
            block.StartAyah,
            block.EndAyah,
            block.StartAyah,
            ayah,
            block.Status,
            "Proposed");

        AddOperationBlock(
            connection,
            tx,
            operationId,
            newId,
            "Created",
            null,
            null,
            ayah + 1,
            block.EndAyah,
            null,
            "Proposed");

        AddOperationActivity(
            connection,
            tx,
            operationId,
            "ContextSplit",
            block.SurahNumber,
            block.Id,
            block.StartAyah,
            block.EndAyah,
            summary,
            now);

        tx.Commit();
        return newId;
    }

    internal long MergeWithNext(long id)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        ContextBlock current = GetBlock(connection, id, tx)
            ?? throw new InvalidOperationException("Context block no longer exists.");
        ContextBlock next = GetNeighbor(connection, current, next: true, tx)
            ?? throw new InvalidOperationException("There is no next context block.");

        EnsureEditable(current);
        EnsureEditable(next);

        if (current.EndAyah + 1 != next.StartAyah)
        {
            throw new InvalidDataException("Context map is not contiguous.");
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        UpdateBlock(
            connection, tx, current.Id,
            current.StartAyah, next.EndAyah,
            "Proposed", "Manual edit", now);

        AddHistory(
            connection, tx, current.Id,
            current.StartAyah, current.EndAyah,
            current.StartAyah, next.EndAyah,
            $"Merged with next block {next.Id}", now);

        DeactivateBlock(connection, tx, next.Id, now);

        AddHistory(
            connection, tx, next.Id,
            next.StartAyah, next.EndAyah,
            next.StartAyah, next.EndAyah,
            $"Merged into block {current.Id}", now);

        string summary =
            $"Merged Context {current.RangeLabel} + {next.RangeLabel} → " +
            $"{current.StartAyah}–{next.EndAyah}";

        long operationId =
            AddOperation(
                connection,
                tx,
                current.SurahNumber,
                "Merge",
                summary,
                now);

        AddOperationBlock(
            connection,
            tx,
            operationId,
            current.Id,
            "Retained",
            current.StartAyah,
            current.EndAyah,
            current.StartAyah,
            next.EndAyah,
            current.Status,
            "Proposed");

        AddOperationBlock(
            connection,
            tx,
            operationId,
            next.Id,
            "Merged",
            next.StartAyah,
            next.EndAyah,
            null,
            null,
            next.Status,
            null);

        AddOperationActivity(
            connection,
            tx,
            operationId,
            "ContextMerged",
            current.SurahNumber,
            current.Id,
            current.StartAyah,
            next.EndAyah,
            summary,
            now);

        tx.Commit();
        return current.Id;
    }

    internal long MergeWithPrevious(long id)
    {
        using var connection = Open();
        ContextBlock current = GetBlock(connection, id)
            ?? throw new InvalidOperationException("Context block no longer exists.");
        ContextBlock previous = GetNeighbor(connection, current, next: false)
            ?? throw new InvalidOperationException("There is no previous context block.");

        return MergeWithNext(previous.Id);
    }

    internal void ExtendStart(long id) =>
        ShiftStartBoundary(id, earlier: true);

    internal void ShrinkStart(long id) =>
        ShiftStartBoundary(id, earlier: false);

    internal void ExtendEnd(long id) =>
        ShiftEndBoundary(id, later: true);

    internal void ShrinkEnd(long id) =>
        ShiftEndBoundary(id, later: false);

    internal ContextProposalImportResult ImportProposal(
        int surahNumber,
        int versesCount,
        string rawPayload,
        IReadOnlyList<ContextProposalRange> ranges)
    {
        ValidateProposalRanges(
            ranges,
            versesCount);

        using var connection = Open();
        using var tx = connection.BeginTransaction();

        IReadOnlyList<ContextBlock> current =
            GetBlocks(
                connection,
                surahNumber,
                tx);

        if (current.Count == 0)
        {
            throw new InvalidDataException(
                "The current Context Map is unavailable.");
        }

        if (current.Any(
            block =>
                string.Equals(
                    block.Status,
                    "Accepted",
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Proposal import is blocked because this Surah contains an Accepted / Locked context block. Change accepted decisions deliberately before replacing the map.");
        }

        string now =
            DateTimeOffset.UtcNow.ToString("O");

        string normalized =
            ContextProposalParser.Normalize(
                ranges);

        using var batch = connection.CreateCommand();
        batch.Transaction = tx;
        batch.CommandText = """
        INSERT INTO context_proposal_imports(
            surah_number,
            raw_payload,
            normalized_ranges,
            created_utc)
        VALUES(
            $surah,
            $raw,
            $normalized,
            $now);
        SELECT last_insert_rowid();
        """;
        batch.Parameters.AddWithValue(
            "$surah",
            surahNumber);
        batch.Parameters.AddWithValue(
            "$raw",
            rawPayload.Trim());
        batch.Parameters.AddWithValue(
            "$normalized",
            normalized);
        batch.Parameters.AddWithValue(
            "$now",
            now);

        long importId =
            Convert.ToInt64(
                batch.ExecuteScalar());

        string operationSummary =
            $"Imported Context proposal #{importId} · {ranges.Count} block(s) · {normalized}";

        long operationId =
            AddOperation(
                connection,
                tx,
                surahNumber,
                "ProposalImport",
                operationSummary,
                now,
                importId);

        int priorPosition = 0;

        foreach (ContextBlock block in current)
        {
            using (var remember =
                connection.CreateCommand())
            {
                remember.Transaction = tx;
                remember.CommandText = """
                INSERT INTO context_proposal_replaced_blocks(
                    import_id,
                    context_block_id,
                    position,
                    prior_status,
                    prior_origin,
                    prior_updated_utc)
                VALUES(
                    $importId,
                    $contextId,
                    $position,
                    $status,
                    $origin,
                    $updatedUtc);
                """;
                remember.Parameters.AddWithValue(
                    "$importId",
                    importId);
                remember.Parameters.AddWithValue(
                    "$contextId",
                    block.Id);
                remember.Parameters.AddWithValue(
                    "$position",
                    priorPosition++);
                remember.Parameters.AddWithValue(
                    "$status",
                    block.Status);
                remember.Parameters.AddWithValue(
                    "$origin",
                    block.Origin);
                remember.Parameters.AddWithValue(
                    "$updatedUtc",
                    block.UpdatedUtc.ToString("O"));
                remember.ExecuteNonQuery();
            }

            using var deactivate =
                connection.CreateCommand();

            deactivate.Transaction = tx;
            deactivate.CommandText = """
            UPDATE context_blocks
            SET is_active=0,
                origin=origin || $suffix,
                updated_utc=$now
            WHERE id=$id AND is_active=1;
            """;
            deactivate.Parameters.AddWithValue(
                "$suffix",
                $" · superseded by proposal import #{importId}");
            deactivate.Parameters.AddWithValue(
                "$now",
                now);
            deactivate.Parameters.AddWithValue(
                "$id",
                block.Id);

            if (deactivate.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "The existing Context Map changed before the proposal could be imported.");
            }

            AddHistory(
                connection,
                tx,
                block.Id,
                block.StartAyah,
                block.EndAyah,
                block.StartAyah,
                block.EndAyah,
                $"Superseded by proposal import #{importId}",
                now);

            AddOperationBlock(
                connection,
                tx,
                operationId,
                block.Id,
                "Replaced",
                block.StartAyah,
                block.EndAyah,
                null,
                null,
                block.Status,
                null);
        }

        long firstId = 0;

        foreach (ContextProposalRange range in ranges)
        {
            using var insert =
                connection.CreateCommand();

            insert.Transaction = tx;
            insert.CommandText = """
            INSERT INTO context_blocks(
                surah_number,
                start_ayah,
                end_ayah,
                status,
                owner_note,
                created_utc,
                updated_utc,
                origin,
                is_active,
                proposal_import_id)
            VALUES(
                $surah,
                $start,
                $end,
                'Proposed',
                NULL,
                $now,
                $now,
                $origin,
                1,
                $importId);
            SELECT last_insert_rowid();
            """;
            insert.Parameters.AddWithValue(
                "$surah",
                surahNumber);
            insert.Parameters.AddWithValue(
                "$start",
                range.StartAyah);
            insert.Parameters.AddWithValue(
                "$end",
                range.EndAyah);
            insert.Parameters.AddWithValue(
                "$now",
                now);
            insert.Parameters.AddWithValue(
                "$origin",
                $"Proposal import #{importId}");
            insert.Parameters.AddWithValue(
                "$importId",
                importId);

            long newId =
                Convert.ToInt64(
                    insert.ExecuteScalar());

            if (firstId == 0)
            {
                firstId = newId;
            }

            AddHistory(
                connection,
                tx,
                newId,
                range.StartAyah,
                range.EndAyah,
                range.StartAyah,
                range.EndAyah,
                $"Created by proposal import #{importId}",
                now);

            AddOperationBlock(
                connection,
                tx,
                operationId,
                newId,
                "Created",
                null,
                null,
                range.StartAyah,
                range.EndAyah,
                null,
                "Proposed");
        }

        AddOperationActivity(
            connection,
            tx,
            operationId,
            "ContextProposalImported",
            surahNumber,
            firstId,
            1,
            versesCount,
            operationSummary,
            now);

        tx.Commit();

        return new ContextProposalImportResult(
            importId,
            firstId,
            ranges.Count,
            normalized);
    }

    internal ContextProposalDiscardState? GetDiscardableProposal(
        int surahNumber)
    {
        using var connection = Open();

        using var current = connection.CreateCommand();
        current.CommandText = """
        SELECT DISTINCT proposal_import_id
        FROM context_blocks
        WHERE surah_number=$surah
          AND is_active=1;
        """;
        current.Parameters.AddWithValue(
            "$surah",
            surahNumber);

        using var reader = current.ExecuteReader();
        var importIds = new List<long?>();

        while (reader.Read())
        {
            importIds.Add(
                reader.IsDBNull(0)
                    ? null
                    : reader.GetInt64(0));
        }

        reader.Close();

        if (importIds.Count != 1 ||
            importIds[0] is not long importId)
        {
            return null;
        }

        using var batch = connection.CreateCommand();
        batch.CommandText = """
        SELECT normalized_ranges,
               discarded_utc
        FROM context_proposal_imports
        WHERE id=$id
          AND surah_number=$surah;
        """;
        batch.Parameters.AddWithValue(
            "$id",
            importId);
        batch.Parameters.AddWithValue(
            "$surah",
            surahNumber);

        using var batchReader =
            batch.ExecuteReader();

        if (!batchReader.Read())
        {
            return null;
        }

        string normalized =
            batchReader.GetString(0);

        bool alreadyDiscarded =
            !batchReader.IsDBNull(1);

        batchReader.Close();

        if (alreadyDiscarded)
        {
            return new ContextProposalDiscardState(
                importId,
                0,
                normalized,
                false,
                "This proposal was already discarded.");
        }

        using var check = connection.CreateCommand();
        check.CommandText = """
        SELECT
            COUNT(*) AS active_count,
            SUM(
                CASE
                    WHEN proposal_import_id=$id
                     AND status='Proposed'
                     AND created_utc=updated_utc
                    THEN 1
                    ELSE 0
                END
            ) AS pristine_count,
            SUM(
                CASE
                    WHEN proposal_import_id=$id
                    THEN 1
                    ELSE 0
                END
            ) AS imported_count
        FROM context_blocks
        WHERE surah_number=$surah
          AND is_active=1;
        """;
        check.Parameters.AddWithValue(
            "$id",
            importId);
        check.Parameters.AddWithValue(
            "$surah",
            surahNumber);

        using var checkReader =
            check.ExecuteReader();

        checkReader.Read();

        int activeCount =
            checkReader.GetInt32(0);
        int pristineCount =
            checkReader.IsDBNull(1)
                ? 0
                : checkReader.GetInt32(1);
        int importedCount =
            checkReader.IsDBNull(2)
                ? 0
                : checkReader.GetInt32(2);

        checkReader.Close();

        if (activeCount == 0 ||
            importedCount != activeCount ||
            pristineCount != activeCount)
        {
            return new ContextProposalDiscardState(
                importId,
                importedCount,
                normalized,
                false,
                "This proposal has already been edited, split/merged, or moved out of Proposed state.");
        }

        using var notes = connection.CreateCommand();
        notes.CommandText = """
        SELECT COUNT(*)
        FROM context_notes cn
        JOIN context_blocks cb
          ON cb.id=cn.context_block_id
        WHERE cb.proposal_import_id=$id;
        """;
        notes.Parameters.AddWithValue(
            "$id",
            importId);

        if (Convert.ToInt32(
                notes.ExecuteScalar()) > 0)
        {
            return new ContextProposalDiscardState(
                importId,
                importedCount,
                normalized,
                false,
                "This proposal has Context notes attached and is no longer considered unworked.");
        }

        using var slices = connection.CreateCommand();
        slices.CommandText = """
        SELECT COUNT(*)
        FROM working_slices ws
        JOIN context_blocks cb
          ON cb.id=ws.context_block_id
        WHERE cb.proposal_import_id=$id
          AND ws.is_active=1;
        """;
        slices.Parameters.AddWithValue(
            "$id",
            importId);

        if (Convert.ToInt32(
                slices.ExecuteScalar()) > 0)
        {
            return new ContextProposalDiscardState(
                importId,
                importedCount,
                normalized,
                false,
                "This proposal is referenced by a Working Slice and cannot be discarded.");
        }

        if (!TryValidateProposalRestore(
                connection,
                transaction: null,
                importId,
                surahNumber,
                out _,
                out string restoreReason))
        {
            return new ContextProposalDiscardState(
                importId,
                importedCount,
                normalized,
                false,
                restoreReason);
        }

        return new ContextProposalDiscardState(
            importId,
            importedCount,
            normalized,
            true,
            "Pristine imported proposal; the previous Context Map can be restored.");
    }

    internal void DiscardUnworkedProposal(
        long importId,
        int surahNumber)
    {
        ContextProposalDiscardState? state =
            GetDiscardableProposal(
                surahNumber);

        if (state is null ||
            state.ImportId != importId ||
            !state.CanDiscard)
        {
            throw new InvalidOperationException(
                state?.Reason ??
                "There is no discardable proposal for this Surah.");
        }

        using var connection = Open();
        using var tx = connection.BeginTransaction();

        if (!TryValidateProposalRestore(
                connection,
                tx,
                importId,
                surahNumber,
                out int expectedEndAyah,
                out string restoreReason))
        {
            throw new InvalidOperationException(
                restoreReason);
        }

        string now =
            DateTimeOffset.UtcNow.ToString("O");

        string operationSummary =
            $"Discarded Context proposal #{importId} · restored previous Context Map";

        long operationId =
            AddOperation(
                connection,
                tx,
                surahNumber,
                "ProposalDiscard",
                operationSummary,
                now,
                importId);

        using (var rememberDiscarded = connection.CreateCommand())
        {
            rememberDiscarded.Transaction = tx;
            rememberDiscarded.CommandText = """
            INSERT INTO context_operation_blocks(
                operation_id,
                context_block_id,
                role,
                old_start_ayah,
                old_end_ayah,
                new_start_ayah,
                new_end_ayah,
                old_status,
                new_status)
            SELECT
                $operation,
                id,
                'Discarded',
                start_ayah,
                end_ayah,
                NULL,
                NULL,
                status,
                NULL
            FROM context_blocks
            WHERE surah_number=$surah
              AND is_active=1
              AND proposal_import_id=$id;
            """;
            rememberDiscarded.Parameters.AddWithValue(
                "$operation",
                operationId);
            rememberDiscarded.Parameters.AddWithValue(
                "$surah",
                surahNumber);
            rememberDiscarded.Parameters.AddWithValue(
                "$id",
                importId);
            rememberDiscarded.ExecuteNonQuery();
        }

        using (var deactivate =
            connection.CreateCommand())
        {
            deactivate.Transaction = tx;
            deactivate.CommandText = """
            UPDATE context_blocks
            SET is_active=0,
                origin=origin || $suffix,
                updated_utc=$now
            WHERE surah_number=$surah
              AND is_active=1
              AND proposal_import_id=$id
              AND status='Proposed'
              AND created_utc=updated_utc;
            """;
            deactivate.Parameters.AddWithValue(
                "$suffix",
                $" · discarded proposal import #{importId}");
            deactivate.Parameters.AddWithValue(
                "$now",
                now);
            deactivate.Parameters.AddWithValue(
                "$surah",
                surahNumber);
            deactivate.Parameters.AddWithValue(
                "$id",
                importId);

            if (deactivate.ExecuteNonQuery() !=
                state.BlockCount)
            {
                throw new InvalidOperationException(
                    "The proposal changed before it could be discarded.");
            }
        }

        using (var restore =
            connection.CreateCommand())
        {
            restore.Transaction = tx;
            restore.CommandText = """
            UPDATE context_blocks
            SET is_active=1,
                status=(
                    SELECT prior_status
                    FROM context_proposal_replaced_blocks r
                    WHERE r.import_id=$id
                      AND r.context_block_id=context_blocks.id
                ),
                origin=(
                    SELECT prior_origin
                    FROM context_proposal_replaced_blocks r
                    WHERE r.import_id=$id
                      AND r.context_block_id=context_blocks.id
                ),
                updated_utc=(
                    SELECT prior_updated_utc
                    FROM context_proposal_replaced_blocks r
                    WHERE r.import_id=$id
                      AND r.context_block_id=context_blocks.id
                )
            WHERE id IN (
                SELECT context_block_id
                FROM context_proposal_replaced_blocks
                WHERE import_id=$id
            );
            """;
            restore.Parameters.AddWithValue(
                "$id",
                importId);

            if (restore.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException(
                    "The previous Context Map could not be restored.");
            }
        }

        ValidateMapShape(
            GetBlocks(
                connection,
                surahNumber,
                tx),
            expectedEndAyah);

        using (var rememberRestored = connection.CreateCommand())
        {
            rememberRestored.Transaction = tx;
            rememberRestored.CommandText = """
            INSERT INTO context_operation_blocks(
                operation_id,
                context_block_id,
                role,
                old_start_ayah,
                old_end_ayah,
                new_start_ayah,
                new_end_ayah,
                old_status,
                new_status)
            SELECT
                $operation,
                cb.id,
                'Restored',
                NULL,
                NULL,
                cb.start_ayah,
                cb.end_ayah,
                NULL,
                cb.status
            FROM context_blocks cb
            JOIN context_proposal_replaced_blocks r
              ON r.context_block_id=cb.id
            WHERE r.import_id=$id;
            """;
            rememberRestored.Parameters.AddWithValue(
                "$operation",
                operationId);
            rememberRestored.Parameters.AddWithValue(
                "$id",
                importId);
            rememberRestored.ExecuteNonQuery();
        }

        using (var mark =
            connection.CreateCommand())
        {
            mark.Transaction = tx;
            mark.CommandText = """
            UPDATE context_proposal_imports
            SET discarded_utc=$now
            WHERE id=$id
              AND discarded_utc IS NULL;
            """;
            mark.Parameters.AddWithValue(
                "$now",
                now);
            mark.Parameters.AddWithValue(
                "$id",
                importId);

            if (mark.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "Proposal discard could not be recorded.");
            }
        }

        AddOperationActivity(
            connection,
            tx,
            operationId,
            "ContextProposalDiscarded",
            surahNumber,
            null,
            1,
            expectedEndAyah,
            operationSummary,
            now);

        tx.Commit();
    }

    private static bool TryValidateProposalRestore(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long importId,
        int surahNumber,
        out int expectedEndAyah,
        out string reason)
    {
        expectedEndAyah = 0;
        reason =
            "The prior Context Map cannot be restored safely.";

        var current =
            new List<(int Start, int End)>();

        using (var active =
            connection.CreateCommand())
        {
            active.Transaction = transaction;
            active.CommandText = """
            SELECT start_ayah, end_ayah
            FROM context_blocks
            WHERE surah_number=$surah
              AND is_active=1
              AND proposal_import_id=$id
            ORDER BY start_ayah, id;
            """;
            active.Parameters.AddWithValue(
                "$surah",
                surahNumber);
            active.Parameters.AddWithValue(
                "$id",
                importId);

            using var reader =
                active.ExecuteReader();

            while (reader.Read())
            {
                current.Add(
                    (reader.GetInt32(0),
                     reader.GetInt32(1)));
            }
        }

        if (!TryValidateRangeMap(
                current,
                out expectedEndAyah))
        {
            reason =
                "The current proposal map is not a complete contiguous map and cannot be discarded safely.";
            return false;
        }

        var prior =
            new List<(int Start, int End)>();

        using (var remembered =
            connection.CreateCommand())
        {
            remembered.Transaction = transaction;
            remembered.CommandText = """
            SELECT
                cb.surah_number,
                cb.start_ayah,
                cb.end_ayah
            FROM context_proposal_replaced_blocks r
            JOIN context_blocks cb
              ON cb.id=r.context_block_id
            WHERE r.import_id=$id
            ORDER BY r.position, cb.start_ayah, cb.id;
            """;
            remembered.Parameters.AddWithValue(
                "$id",
                importId);

            using var reader =
                remembered.ExecuteReader();

            while (reader.Read())
            {
                if (reader.GetInt32(0) !=
                    surahNumber)
                {
                    reason =
                        "The remembered prior Context Map contains a block from another Surah.";
                    return false;
                }

                prior.Add(
                    (reader.GetInt32(1),
                     reader.GetInt32(2)));
            }
        }

        if (!TryValidateRangeMap(
                prior,
                out int priorEndAyah) ||
            priorEndAyah != expectedEndAyah)
        {
            reason =
                "The remembered prior Context Map is incomplete or non-contiguous and cannot be restored safely.";
            return false;
        }

        return true;
    }

    private static bool TryValidateRangeMap(
        IReadOnlyList<(int Start, int End)> ranges,
        out int endAyah)
    {
        endAyah = 0;

        if (ranges.Count == 0 ||
            ranges[0].Start != 1)
        {
            return false;
        }

        for (int i = 0; i < ranges.Count; i++)
        {
            (int start, int end) =
                ranges[i];

            if (start < 1 ||
                end < start)
            {
                return false;
            }

            if (i > 0 &&
                ranges[i - 1].End + 1 !=
                start)
            {
                return false;
            }
        }

        endAyah =
            ranges[^1].End;

        return true;
    }

    private static void ValidateProposalRanges(
        IReadOnlyList<ContextProposalRange> ranges,
        int versesCount)
    {
        if (ranges.Count == 0)
        {
            throw new InvalidDataException(
                "A proposal must contain at least one context block.");
        }

        if (ranges[0].StartAyah != 1)
        {
            throw new InvalidDataException(
                "A proposal must begin at ayah 1.");
        }

        for (int i = 0; i < ranges.Count; i++)
        {
            ContextProposalRange range =
                ranges[i];

            if (range.StartAyah < 1 ||
                range.EndAyah < range.StartAyah ||
                range.EndAyah > versesCount)
            {
                throw new InvalidDataException(
                    $"Invalid proposal range {range.RangeLabel}.");
            }

            if (i > 0 &&
                ranges[i - 1].EndAyah + 1 !=
                range.StartAyah)
            {
                throw new InvalidDataException(
                    "Proposal ranges must form one contiguous map with no gaps or overlaps.");
            }
        }

        if (ranges[^1].EndAyah != versesCount)
        {
            throw new InvalidDataException(
                $"A proposal must end at ayah {versesCount}.");
        }
    }

    private static IReadOnlyList<ContextBlock> GetBlocks(
        SqliteConnection connection,
        int surahNumber,
        SqliteTransaction transaction)
    {
        using var command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
        SELECT id, surah_number, start_ayah, end_ayah,
               status, origin, updated_utc
        FROM context_blocks
        WHERE surah_number=$surah AND is_active=1
        ORDER BY start_ayah, id;
        """;
        command.Parameters.AddWithValue(
            "$surah",
            surahNumber);

        using var reader =
            command.ExecuteReader();

        var result =
            new List<ContextBlock>();

        while (reader.Read())
        {
            result.Add(
                new ContextBlock(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(
                        reader.GetString(6))));
        }

        return result;
    }

    internal void ValidateMap(int surahNumber, int versesCount)
    {
        ValidateMapShape(
            GetBlocks(surahNumber),
            versesCount);
    }

    private static void ValidateMapShape(
        IReadOnlyList<ContextBlock> blocks,
        int versesCount)
    {
        if (blocks.Count == 0)
        {
            throw new InvalidDataException("Context map contains no active blocks.");
        }

        if (blocks[0].StartAyah != 1)
        {
            throw new InvalidDataException("Context map must begin at ayah 1.");
        }

        for (int i = 1; i < blocks.Count; i++)
        {
            if (blocks[i - 1].EndAyah + 1 != blocks[i].StartAyah)
            {
                throw new InvalidDataException(
                    $"Context map gap/overlap between {blocks[i - 1].DisplayLabel} and {blocks[i].DisplayLabel}.");
            }
        }

        if (blocks[^1].EndAyah != versesCount)
        {
            throw new InvalidDataException(
                $"Context map must end at ayah {versesCount}.");
        }
    }

    private void ShiftStartBoundary(long id, bool earlier)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        ContextBlock current = GetBlock(connection, id, tx)
            ?? throw new InvalidOperationException("Context block no longer exists.");
        ContextBlock previous = GetNeighbor(connection, current, next: false, tx)
            ?? throw new InvalidOperationException("There is no previous context block.");

        EnsureEditable(current);
        EnsureEditable(previous);

        int currentStart;
        int previousEnd;

        if (earlier)
        {
            if (previous.StartAyah == previous.EndAyah)
            {
                throw new InvalidOperationException(
                    "The previous block cannot give up its only ayah.");
            }

            currentStart = current.StartAyah - 1;
            previousEnd = previous.EndAyah - 1;
        }
        else
        {
            if (current.StartAyah == current.EndAyah)
            {
                throw new InvalidOperationException(
                    "This block cannot give up its only ayah.");
            }

            currentStart = current.StartAyah + 1;
            previousEnd = previous.EndAyah + 1;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        UpdateBlock(connection, tx, current.Id, currentStart, current.EndAyah, "Proposed", "Manual edit", now);
        UpdateBlock(connection, tx, previous.Id, previous.StartAyah, previousEnd, "Proposed", "Manual edit", now);

        AddHistory(connection, tx, current.Id,
            current.StartAyah, current.EndAyah,
            currentStart, current.EndAyah,
            earlier ? "Extended start earlier" : "Shrank start later", now);

        AddHistory(connection, tx, previous.Id,
            previous.StartAyah, previous.EndAyah,
            previous.StartAyah, previousEnd,
            earlier ? $"Gave end ayah to block {current.Id}" : $"Received start ayah from block {current.Id}", now);

        string operationType =
            earlier
                ? "ExtendStart"
                : "ShrinkStart";

        string summary =
            earlier
                ? $"Extended Context start · {current.StartAyah} → {currentStart}"
                : $"Shrank Context start · {current.StartAyah} → {currentStart}";

        long operationId =
            AddOperation(
                connection,
                tx,
                current.SurahNumber,
                operationType,
                summary,
                now);

        AddOperationBlock(
            connection, tx, operationId, current.Id, "Target",
            current.StartAyah, current.EndAyah,
            currentStart, current.EndAyah,
            current.Status, "Proposed");

        AddOperationBlock(
            connection, tx, operationId, previous.Id, "Neighbor",
            previous.StartAyah, previous.EndAyah,
            previous.StartAyah, previousEnd,
            previous.Status, "Proposed");

        AddOperationActivity(
            connection, tx, operationId,
            $"Context{operationType}",
            current.SurahNumber,
            current.Id,
            currentStart,
            current.EndAyah,
            summary,
            now);

        tx.Commit();
    }

    private void ShiftEndBoundary(long id, bool later)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        ContextBlock current = GetBlock(connection, id, tx)
            ?? throw new InvalidOperationException("Context block no longer exists.");
        ContextBlock next = GetNeighbor(connection, current, next: true, tx)
            ?? throw new InvalidOperationException("There is no next context block.");

        EnsureEditable(current);
        EnsureEditable(next);

        int currentEnd;
        int nextStart;

        if (later)
        {
            if (next.StartAyah == next.EndAyah)
            {
                throw new InvalidOperationException(
                    "The next block cannot give up its only ayah.");
            }

            currentEnd = current.EndAyah + 1;
            nextStart = next.StartAyah + 1;
        }
        else
        {
            if (current.StartAyah == current.EndAyah)
            {
                throw new InvalidOperationException(
                    "This block cannot give up its only ayah.");
            }

            currentEnd = current.EndAyah - 1;
            nextStart = next.StartAyah - 1;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        UpdateBlock(connection, tx, current.Id, current.StartAyah, currentEnd, "Proposed", "Manual edit", now);
        UpdateBlock(connection, tx, next.Id, nextStart, next.EndAyah, "Proposed", "Manual edit", now);

        AddHistory(connection, tx, current.Id,
            current.StartAyah, current.EndAyah,
            current.StartAyah, currentEnd,
            later ? "Extended end later" : "Shrank end earlier", now);

        AddHistory(connection, tx, next.Id,
            next.StartAyah, next.EndAyah,
            nextStart, next.EndAyah,
            later ? $"Gave start ayah to block {current.Id}" : $"Received end ayah from block {current.Id}", now);

        string operationType =
            later
                ? "ExtendEnd"
                : "ShrinkEnd";

        string summary =
            later
                ? $"Extended Context end · {current.EndAyah} → {currentEnd}"
                : $"Shrank Context end · {current.EndAyah} → {currentEnd}";

        long operationId =
            AddOperation(
                connection,
                tx,
                current.SurahNumber,
                operationType,
                summary,
                now);

        AddOperationBlock(
            connection, tx, operationId, current.Id, "Target",
            current.StartAyah, current.EndAyah,
            current.StartAyah, currentEnd,
            current.Status, "Proposed");

        AddOperationBlock(
            connection, tx, operationId, next.Id, "Neighbor",
            next.StartAyah, next.EndAyah,
            nextStart, next.EndAyah,
            next.Status, "Proposed");

        AddOperationActivity(
            connection, tx, operationId,
            $"Context{operationType}",
            current.SurahNumber,
            current.Id,
            current.StartAyah,
            currentEnd,
            summary,
            now);

        tx.Commit();
    }

    private SqliteConnection Open()
    {
        var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.GetFullPath(_databasePath),
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static ContextBlock? GetBlock(
        SqliteConnection connection,
        long id,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
        SELECT id, surah_number, start_ayah, end_ayah,
               status, origin, updated_utc
        FROM context_blocks
        WHERE id=$id AND is_active=1;
        """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ContextBlock(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    private static ContextBlock? GetNeighbor(
        SqliteConnection connection,
        ContextBlock current,
        bool next,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = next
            ? """
              SELECT id, surah_number, start_ayah, end_ayah,
                     status, origin, updated_utc
              FROM context_blocks
              WHERE surah_number=$surah
                AND is_active=1
                AND start_ayah>$start
              ORDER BY start_ayah
              LIMIT 1;
              """
            : """
              SELECT id, surah_number, start_ayah, end_ayah,
                     status, origin, updated_utc
              FROM context_blocks
              WHERE surah_number=$surah
                AND is_active=1
                AND start_ayah<$start
              ORDER BY start_ayah DESC
              LIMIT 1;
              """;
        command.Parameters.AddWithValue("$surah", current.SurahNumber);
        command.Parameters.AddWithValue("$start", current.StartAyah);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ContextBlock(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    private static void UpdateBlock(
        SqliteConnection connection,
        SqliteTransaction tx,
        long id,
        int start,
        int end,
        string status,
        string origin,
        string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        UPDATE context_blocks
        SET start_ayah=$start,
            end_ayah=$end,
            status=$status,
            origin=$origin,
            updated_utc=$now
        WHERE id=$id AND is_active=1;
        """;
        command.Parameters.AddWithValue("$start", start);
        command.Parameters.AddWithValue("$end", end);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$origin", origin);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$id", id);

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Context block update failed.");
        }
    }

    private static void DeactivateBlock(
        SqliteConnection connection,
        SqliteTransaction tx,
        long id,
        string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        UPDATE context_blocks
        SET is_active=0,
            status='Proposed',
            origin='Merged',
            updated_utc=$now
        WHERE id=$id AND is_active=1;
        """;
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static long AddOperation(
        SqliteConnection connection,
        SqliteTransaction tx,
        int surahNumber,
        string operationType,
        string summary,
        string now,
        long? proposalImportId = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        INSERT INTO context_operations(
            surah_number,
            operation_type,
            summary,
            created_utc,
            proposal_import_id)
        VALUES(
            $surah,
            $type,
            $summary,
            $now,
            $proposal);
        SELECT last_insert_rowid();
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);
        command.Parameters.AddWithValue("$type", operationType);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue(
            "$proposal",
            proposalImportId is long proposal
                ? proposal
                : DBNull.Value);

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void AddOperationBlock(
        SqliteConnection connection,
        SqliteTransaction tx,
        long operationId,
        long contextBlockId,
        string role,
        int? oldStart,
        int? oldEnd,
        int? newStart,
        int? newEnd,
        string? oldStatus,
        string? newStatus)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        INSERT INTO context_operation_blocks(
            operation_id,
            context_block_id,
            role,
            old_start_ayah,
            old_end_ayah,
            new_start_ayah,
            new_end_ayah,
            old_status,
            new_status)
        VALUES(
            $operation,
            $context,
            $role,
            $oldStart,
            $oldEnd,
            $newStart,
            $newEnd,
            $oldStatus,
            $newStatus);
        """;

        command.Parameters.AddWithValue("$operation", operationId);
        command.Parameters.AddWithValue("$context", contextBlockId);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$oldStart", oldStart is int value1 ? value1 : DBNull.Value);
        command.Parameters.AddWithValue("$oldEnd", oldEnd is int value2 ? value2 : DBNull.Value);
        command.Parameters.AddWithValue("$newStart", newStart is int value3 ? value3 : DBNull.Value);
        command.Parameters.AddWithValue("$newEnd", newEnd is int value4 ? value4 : DBNull.Value);
        command.Parameters.AddWithValue("$oldStatus", oldStatus is not null ? oldStatus : DBNull.Value);
        command.Parameters.AddWithValue("$newStatus", newStatus is not null ? newStatus : DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void AddOperationActivity(
        SqliteConnection connection,
        SqliteTransaction tx,
        long operationId,
        string eventType,
        int surahNumber,
        long? contextBlockId,
        int? startAyah,
        int? endAyah,
        string summary,
        string now)
    {
        ResearchActivityWriter.Append(
            connection,
            tx,
            eventType,
            "ContextOperation",
            operationId,
            surahNumber,
            null,
            contextBlockId,
            null,
            startAyah,
            endAyah,
            summary,
            now);
    }

    private static void AddHistory(
        SqliteConnection connection,
        SqliteTransaction tx,
        long blockId,
        int oldStart,
        int oldEnd,
        int newStart,
        int newEnd,
        string action,
        string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        INSERT INTO context_boundary_history(
            context_block_id,
            old_start_ayah, old_end_ayah,
            new_start_ayah, new_end_ayah,
            owner_note, changed_utc)
        VALUES(
            $id, $oldStart, $oldEnd,
            $newStart, $newEnd,
            $action, $now);
        """;
        command.Parameters.AddWithValue("$id", blockId);
        command.Parameters.AddWithValue("$oldStart", oldStart);
        command.Parameters.AddWithValue("$oldEnd", oldEnd);
        command.Parameters.AddWithValue("$newStart", newStart);
        command.Parameters.AddWithValue("$newEnd", newEnd);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static void EnsureEditable(ContextBlock block)
    {
        if (block.Status == "Accepted")
        {
            throw new InvalidOperationException(
                "Accepted context blocks are locked. Change the status to Owner Reviewed before editing the boundary.");
        }
    }
}
