using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class WorkingSliceRepository
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.Ordinal)
        {
            "Draft",
            "In Review",
            "Resolved"
        };

    private readonly string _databasePath;

    internal WorkingSliceRepository(
        string? databasePath = null)
    {
        _databasePath =
            databasePath ??
            AppPaths.ResearchDatabase;
    }

    internal int CountActiveForSurah(int surahNumber)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT COUNT(*)
        FROM working_slices ws
        WHERE ws.is_active=1 AND ws.surah_number=$surah;
        """;
        command.Parameters.AddWithValue("$surah", surahNumber);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    internal IReadOnlyList<WorkingSlice> List(
        string status = "All",
        string? query = null,
        string sort = "Research")
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
        SELECT id,
               surah_number,
               context_block_id,
               start_ayah,
               end_ayah,
               title,
               status,
               research_notes,
               conclusion,
               created_utc,
               updated_utc
        FROM working_slices
        WHERE is_active=1
          AND ($status='All' OR status=$status)
          AND (
                $query=''
             OR title LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR research_notes LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR conclusion LIKE $pattern ESCAPE '\' COLLATE NOCASE
             OR CAST(surah_number AS TEXT) LIKE $pattern ESCAPE '\'
             OR CAST(start_ayah AS TEXT) LIKE $pattern ESCAPE '\'
             OR CAST(end_ayah AS TEXT) LIKE $pattern ESCAPE '\'
          )
        ORDER BY
            CASE WHEN $sort='Research' THEN surah_number END ASC,
            CASE WHEN $sort='Research' THEN start_ayah END ASC,
            CASE WHEN $sort='Research' THEN end_ayah END ASC,
            CASE WHEN $sort='Research' THEN title END COLLATE NOCASE ASC,
            CASE WHEN $sort='Recent' THEN updated_utc END DESC,
            CASE WHEN $sort='Status' THEN
                CASE status
                    WHEN 'In Review' THEN 0
                    WHEN 'Draft' THEN 1
                    WHEN 'Resolved' THEN 2
                    ELSE 3
                END
            END ASC,
            CASE WHEN $sort='Status' THEN surah_number END ASC,
            CASE WHEN $sort='Status' THEN start_ayah END ASC,
            id DESC;
        """;
        command.Parameters.AddWithValue(
            "$status",
            NormalizeFilter(status));

        string needle =
            query?.Trim() ?? string.Empty;

        command.Parameters.AddWithValue(
            "$query",
            needle);

        command.Parameters.AddWithValue(
            "$pattern",
            SqliteLiteralLike.Pattern(needle));

        command.Parameters.AddWithValue(
            "$sort",
            NormalizeSort(sort));

        using var reader = command.ExecuteReader();
        var result = new List<WorkingSlice>();

        while (reader.Read())
        {
            result.Add(Read(reader));
        }

        return result;
    }

    internal WorkingSlice Get(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
        SELECT id,
               surah_number,
               context_block_id,
               start_ayah,
               end_ayah,
               title,
               status,
               research_notes,
               conclusion,
               created_utc,
               updated_utc
        FROM working_slices
        WHERE id=$id AND is_active=1;
        """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException(
                "Working Slice no longer exists.");
        }

        return Read(reader);
    }

    internal WorkingSlice Create(
        WorkingSliceCreateRequest request)
    {
        string title =
            CleanRequired(
                request.Title,
                "Working Slice title");

        using var connection = Open();
        using var tx = connection.BeginTransaction();

        ContextBlock parent =
            ReadActiveContext(
                connection,
                tx,
                request.ContextBlockId)
            ?? throw new InvalidOperationException(
                "The parent Context Block is no longer active.");

        if (parent.SurahNumber !=
            request.SurahNumber)
        {
            throw new InvalidDataException(
                "Working Slice Surah does not match its parent Context Block.");
        }

        if (request.StartAyah <
                parent.StartAyah ||
            request.EndAyah >
                parent.EndAyah ||
            request.EndAyah <
                request.StartAyah)
        {
            throw new InvalidDataException(
                $"Working Slice range must stay inside parent {parent.RangeLabel}.");
        }

        string now =
            DateTimeOffset.UtcNow.ToString("O");

        using var command =
            connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        INSERT INTO working_slices(
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
            $surah,
            $context,
            $start,
            $end,
            $title,
            'Draft',
            '',
            '',
            $now,
            $now,
            1);
        SELECT last_insert_rowid();
        """;
        command.Parameters.AddWithValue(
            "$surah",
            request.SurahNumber);
        command.Parameters.AddWithValue(
            "$context",
            request.ContextBlockId);
        command.Parameters.AddWithValue(
            "$start",
            request.StartAyah);
        command.Parameters.AddWithValue(
            "$end",
            request.EndAyah);
        command.Parameters.AddWithValue(
            "$title",
            title);
        command.Parameters.AddWithValue(
            "$now",
            now);

        long id =
            Convert.ToInt64(
                command.ExecuteScalar());

        ResearchActivityWriter.Append(
            connection,
            tx,
            "WorkingSliceCreated",
            "WorkingSlice",
            id,
            request.SurahNumber,
            null,
            request.ContextBlockId,
            id,
            request.StartAyah,
            request.EndAyah,
            $"Created Working Slice · {title} · Draft",
            now,
            JsonSerializer.Serialize(
                new
                {
                    title,
                    status = "Draft",
                    researchNotes = string.Empty,
                    conclusion = string.Empty
                }));

        tx.Commit();

        return Get(id);
    }

    internal bool Save(
        WorkingSliceSaveRequest request)
    {
        string title =
            CleanRequired(
                request.Title,
                "Working Slice title");

        if (!AllowedStatuses.Contains(
                request.Status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Status));
        }

        using var connection = Open();
        using var tx = connection.BeginTransaction();

        WorkingSlice prior =
            Read(
                connection,
                tx,
                request.Id)
            ?? throw new InvalidOperationException(
                "Working Slice no longer exists.");

        string research =
            request.ResearchNotes ?? string.Empty;
        string conclusion =
            request.Conclusion ?? string.Empty;

        bool changed =
            !string.Equals(
                prior.Title,
                title,
                StringComparison.Ordinal) ||
            !string.Equals(
                prior.Status,
                request.Status,
                StringComparison.Ordinal) ||
            !string.Equals(
                prior.ResearchNotes,
                research,
                StringComparison.Ordinal) ||
            !string.Equals(
                prior.Conclusion,
                conclusion,
                StringComparison.Ordinal);

        if (!changed)
        {
            tx.Commit();
            return false;
        }

        string now =
            DateTimeOffset.UtcNow.ToString("O");

        var changedFields = new List<string>();
        var summaryParts = new List<string>();

        if (!string.Equals(prior.Title, title, StringComparison.Ordinal))
        {
            changedFields.Add("title");
            summaryParts.Add("title changed");
        }

        if (!string.Equals(prior.Status, request.Status, StringComparison.Ordinal))
        {
            changedFields.Add("status");
            summaryParts.Add($"{prior.Status} → {request.Status}");
        }

        if (!string.Equals(prior.ResearchNotes, research, StringComparison.Ordinal))
        {
            changedFields.Add("research_notes");
            summaryParts.Add(
                string.IsNullOrWhiteSpace(prior.ResearchNotes)
                    ? "notes added"
                    : string.IsNullOrWhiteSpace(research)
                        ? "notes cleared"
                        : "notes changed");
        }

        if (!string.Equals(prior.Conclusion, conclusion, StringComparison.Ordinal))
        {
            changedFields.Add("conclusion");
            summaryParts.Add(
                string.IsNullOrWhiteSpace(prior.Conclusion)
                    ? "conclusion added"
                    : string.IsNullOrWhiteSpace(conclusion)
                        ? "conclusion cleared"
                        : "conclusion changed");
        }

        string changeSummary =
            string.Join(" · ", summaryParts);

        string changedFieldsJson =
            JsonSerializer.Serialize(changedFields);

        using (var history =
            connection.CreateCommand())
        {
            history.Transaction = tx;
            history.CommandText = """
            INSERT INTO working_slice_revisions(
                working_slice_id,
                prior_title,
                prior_status,
                prior_research_notes,
                prior_conclusion,
                changed_utc,
                changed_fields_json,
                change_summary)
            VALUES(
                $id,
                $title,
                $status,
                $research,
                $conclusion,
                $now,
                $changedFields,
                $changeSummary);
            """;
            history.Parameters.AddWithValue(
                "$id",
                prior.Id);
            history.Parameters.AddWithValue(
                "$title",
                prior.Title);
            history.Parameters.AddWithValue(
                "$status",
                prior.Status);
            history.Parameters.AddWithValue(
                "$research",
                prior.ResearchNotes);
            history.Parameters.AddWithValue(
                "$conclusion",
                prior.Conclusion);
            history.Parameters.AddWithValue(
                "$now",
                now);
            history.Parameters.AddWithValue(
                "$changedFields",
                changedFieldsJson);
            history.Parameters.AddWithValue(
                "$changeSummary",
                changeSummary);
            history.ExecuteNonQuery();
        }

        using var save =
            connection.CreateCommand();
        save.Transaction = tx;
        save.CommandText = """
        UPDATE working_slices
        SET title=$title,
            status=$status,
            research_notes=$research,
            conclusion=$conclusion,
            updated_utc=$now
        WHERE id=$id AND is_active=1;
        """;
        save.Parameters.AddWithValue(
            "$title",
            title);
        save.Parameters.AddWithValue(
            "$status",
            request.Status);
        save.Parameters.AddWithValue(
            "$research",
            research);
        save.Parameters.AddWithValue(
            "$conclusion",
            conclusion);
        save.Parameters.AddWithValue(
            "$now",
            now);
        save.Parameters.AddWithValue(
            "$id",
            request.Id);

        if (save.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Working Slice save failed.");
        }

        ResearchActivityWriter.Append(
            connection,
            tx,
            "WorkingSliceRevised",
            "WorkingSlice",
            prior.Id,
            prior.SurahNumber,
            null,
            prior.ContextBlockId,
            prior.Id,
            prior.StartAyah,
            prior.EndAyah,
            $"Revised Working Slice · {title} · {changeSummary}",
            now,
            JsonSerializer.Serialize(
                new
                {
                    changedFields,
                    priorStatus = prior.Status,
                    newStatus = request.Status,
                    title,
                    researchNotes = research,
                    conclusion
                }));

        tx.Commit();
        return true;
    }

    internal WorkingSlice Remove(
        long id)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        WorkingSlice prior =
            Read(
                connection,
                tx,
                id)
            ?? throw new InvalidOperationException(
                "Working Slice no longer exists.");

        string now =
            DateTimeOffset.UtcNow.ToString("O");

        using var remove =
            connection.CreateCommand();

        remove.Transaction = tx;
        remove.CommandText =
            """
            UPDATE working_slices
            SET is_active=0,
                updated_utc=$now
            WHERE id=$id
              AND is_active=1;
            """;

        remove.Parameters.AddWithValue(
            "$now",
            now);

        remove.Parameters.AddWithValue(
            "$id",
            id);

        if (remove.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Working Slice removal failed.");
        }

        ResearchActivityWriter.Append(
            connection,
            tx,
            "WorkingSliceRemoved",
            "WorkingSlice",
            prior.Id,
            prior.SurahNumber,
            null,
            prior.ContextBlockId,
            prior.Id,
            prior.StartAyah,
            prior.EndAyah,
            $"Removed Working Slice · {prior.Title} · saved history preserved",
            now,
            JsonSerializer.Serialize(
                new
                {
                    title = prior.Title,
                    status = prior.Status,
                    researchNotes = prior.ResearchNotes,
                    conclusion = prior.Conclusion
                }));

        tx.Commit();

        return prior;
    }

    internal IReadOnlyList<WorkingSliceRevision>
        GetRevisions(
            long id,
            int limit = 20)
    {
        using var connection = Open();
        using var command =
            connection.CreateCommand();

        command.CommandText = """
        SELECT id,
               working_slice_id,
               prior_title,
               prior_status,
               prior_research_notes,
               prior_conclusion,
               changed_utc,
               changed_fields_json,
               change_summary
        FROM working_slice_revisions
        WHERE working_slice_id=$id
        ORDER BY id DESC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue(
            "$id",
            id);
        command.Parameters.AddWithValue(
            "$limit",
            Math.Clamp(limit, 1, 200));

        using var reader =
            command.ExecuteReader();

        var result =
            new List<WorkingSliceRevision>();

        while (reader.Read())
        {
            result.Add(
                new WorkingSliceRevision(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(
                        reader.GetString(6)),
                    reader.GetString(7),
                    reader.GetString(8)));
        }

        return result;
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

        using var pragma =
            connection.CreateCommand();
        pragma.CommandText =
            "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static WorkingSlice Read(
        SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            DateTimeOffset.Parse(
                reader.GetString(9)),
            DateTimeOffset.Parse(
                reader.GetString(10)));

    private static WorkingSlice? Read(
        SqliteConnection connection,
        SqliteTransaction tx,
        long id)
    {
        using var command =
            connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        SELECT id,
               surah_number,
               context_block_id,
               start_ayah,
               end_ayah,
               title,
               status,
               research_notes,
               conclusion,
               created_utc,
               updated_utc
        FROM working_slices
        WHERE id=$id AND is_active=1;
        """;
        command.Parameters.AddWithValue(
            "$id",
            id);

        using var reader =
            command.ExecuteReader();

        return reader.Read()
            ? Read(reader)
            : null;
    }

    private static ContextBlock? ReadActiveContext(
        SqliteConnection connection,
        SqliteTransaction tx,
        long id)
    {
        using var command =
            connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
        SELECT id,
               surah_number,
               start_ayah,
               end_ayah,
               status,
               origin,
               updated_utc
        FROM context_blocks
        WHERE id=$id AND is_active=1;
        """;
        command.Parameters.AddWithValue(
            "$id",
            id);

        using var reader =
            command.ExecuteReader();

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
            DateTimeOffset.Parse(
                reader.GetString(6)));
    }

    private static string CleanRequired(
        string? value,
        string label)
    {
        string clean =
            value?.Trim() ??
            string.Empty;

        if (clean.Length == 0)
        {
            throw new InvalidDataException(
                $"{label} is required.");
        }

        return clean;
    }

    private static string NormalizeFilter(
        string value) =>
        value is "Draft" or
            "In Review" or
            "Resolved"
            ? value
            : "All";

    private static string NormalizeSort(
        string value) =>
        value is "Recent" or "Status"
            ? value
            : "Research";
}
