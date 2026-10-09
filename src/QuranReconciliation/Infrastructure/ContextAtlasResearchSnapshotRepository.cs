using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class ContextAtlasResearchSnapshotRepository
{
    private readonly string _databasePath;

    internal ContextAtlasResearchSnapshotRepository(
        string? databasePath = null)
    {
        _databasePath =
            Path.GetFullPath(
                databasePath ??
                AppPaths.ResearchDatabase);
    }

    internal ContextAtlasResearchSnapshot ReadRange(
        int surahNumber,
        int startAyah,
        int endAyah)
    {
        using SqliteConnection connection =
            OpenReadOnly();

        IReadOnlyList<ContextAtlasLiveContext> contexts =
            ReadContexts(
                connection,
                surahNumber,
                startAyah,
                endAyah);

        IReadOnlyList<ContextAtlasWorkingSliceSummary> slices =
            ReadWorkingSlices(
                connection,
                surahNumber,
                startAyah,
                endAyah);

        int ayahNoteCount =
            ReadScalarCount(
                connection,
                """
                SELECT COUNT(*)
                FROM ayah_notes
                WHERE surah_number=$surah
                  AND ayah_number BETWEEN $start AND $end;
                """,
                surahNumber,
                startAyah,
                endAyah);

        int ayahRevisionCount =
            ReadScalarCount(
                connection,
                """
                SELECT COUNT(*)
                FROM ayah_note_history
                WHERE surah_number=$surah
                  AND ayah_number BETWEEN $start AND $end;
                """,
                surahNumber,
                startAyah,
                endAyah);

        int contextNoteCount =
            ReadScalarCount(
                connection,
                """
                SELECT COUNT(*)
                FROM context_notes n
                JOIN context_blocks c
                  ON c.id=n.context_block_id
                WHERE c.surah_number=$surah
                  AND c.is_active=1
                  AND c.start_ayah <= $end
                  AND c.end_ayah >= $start;
                """,
                surahNumber,
                startAyah,
                endAyah);

        int contextRevisionCount =
            ReadScalarCount(
                connection,
                """
                SELECT COUNT(*)
                FROM context_note_history h
                JOIN context_blocks c
                  ON c.id=h.context_block_id
                WHERE c.surah_number=$surah
                  AND c.is_active=1
                  AND c.start_ayah <= $end
                  AND c.end_ayah >= $start;
                """,
                surahNumber,
                startAyah,
                endAyah);

        DateTimeOffset? lastUpdated =
            ReadLastUpdated(
                connection,
                surahNumber,
                startAyah,
                endAyah);

        return new ContextAtlasResearchSnapshot(
            surahNumber,
            startAyah,
            endAyah,
            contexts,
            slices,
            ayahNoteCount,
            ayahRevisionCount,
            contextNoteCount,
            contextRevisionCount,
            lastUpdated);
    }

    private SqliteConnection OpenReadOnly()
    {
        if (!File.Exists(
                _databasePath))
        {
            throw new FileNotFoundException(
                "Research snapshot database is unavailable.",
                _databasePath);
        }

        var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource =
                        _databasePath,
                    Mode =
                        SqliteOpenMode.ReadOnly,
                    Cache =
                        SqliteCacheMode.Private,
                    Pooling =
                        false
                }.ToString());

        connection.Open();

        return connection;
    }

    private static IReadOnlyList<ContextAtlasLiveContext>
        ReadContexts(
            SqliteConnection connection,
            int surahNumber,
            int startAyah,
            int endAyah)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
        """
        SELECT id, start_ayah, end_ayah, status, updated_utc
        FROM context_blocks
        WHERE surah_number=$surah
          AND is_active=1
          AND start_ayah <= $end
          AND end_ayah >= $start
        ORDER BY start_ayah, end_ayah, id;
        """;

        AddRangeParameters(
            command,
            surahNumber,
            startAyah,
            endAyah);

        using SqliteDataReader reader =
            command.ExecuteReader();

        var rows =
            new List<ContextAtlasLiveContext>();

        while (reader.Read())
        {
            rows.Add(
                new ContextAtlasLiveContext(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    DateTimeOffset.Parse(
                        reader.GetString(4),
                        System.Globalization.CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static IReadOnlyList<ContextAtlasWorkingSliceSummary>
        ReadWorkingSlices(
            SqliteConnection connection,
            int surahNumber,
            int startAyah,
            int endAyah)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
        """
        SELECT
            w.id,
            w.title,
            w.status,
            w.start_ayah,
            w.end_ayah,
            (
                SELECT COUNT(*)
                FROM working_slice_revisions r
                WHERE r.working_slice_id=w.id
            ),
            w.updated_utc
        FROM working_slices w
        WHERE w.surah_number=$surah
          AND w.is_active=1
          AND w.start_ayah <= $end
          AND w.end_ayah >= $start
        ORDER BY w.start_ayah, w.end_ayah, w.id;
        """;

        AddRangeParameters(
            command,
            surahNumber,
            startAyah,
            endAyah);

        using SqliteDataReader reader =
            command.ExecuteReader();

        var rows =
            new List<ContextAtlasWorkingSliceSummary>();

        while (reader.Read())
        {
            rows.Add(
                new ContextAtlasWorkingSliceSummary(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    DateTimeOffset.Parse(
                        reader.GetString(6),
                        System.Globalization.CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static int ReadScalarCount(
        SqliteConnection connection,
        string sql,
        int surahNumber,
        int startAyah,
        int endAyah)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            sql;

        AddRangeParameters(
            command,
            surahNumber,
            startAyah,
            endAyah);

        return Convert.ToInt32(
            command.ExecuteScalar(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadLastUpdated(
        SqliteConnection connection,
        int surahNumber,
        int startAyah,
        int endAyah)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
        """
        SELECT MAX(updated_utc)
        FROM (
            SELECT updated_utc
            FROM context_blocks
            WHERE surah_number=$surah
              AND is_active=1
              AND start_ayah <= $end
              AND end_ayah >= $start

            UNION ALL

            SELECT updated_utc
            FROM working_slices
            WHERE surah_number=$surah
              AND is_active=1
              AND start_ayah <= $end
              AND end_ayah >= $start

            UNION ALL

            SELECT updated_utc
            FROM ayah_notes
            WHERE surah_number=$surah
              AND ayah_number BETWEEN $start AND $end
        );
        """;

        AddRangeParameters(
            command,
            surahNumber,
            startAyah,
            endAyah);

        object? value =
            command.ExecuteScalar();

        if (value is null ||
            value is DBNull)
        {
            return null;
        }

        return DateTimeOffset.Parse(
            Convert.ToString(
                value,
                System.Globalization.CultureInfo.InvariantCulture)!,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddRangeParameters(
        SqliteCommand command,
        int surahNumber,
        int startAyah,
        int endAyah)
    {
        command.Parameters.AddWithValue(
            "$surah",
            surahNumber);

        command.Parameters.AddWithValue(
            "$start",
            startAyah);

        command.Parameters.AddWithValue(
            "$end",
            endAyah);
    }
}
