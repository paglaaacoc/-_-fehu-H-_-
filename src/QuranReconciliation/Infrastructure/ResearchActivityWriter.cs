using Microsoft.Data.Sqlite;

namespace QuranReconciliation.Infrastructure;

internal static class ResearchActivityWriter
{
    internal static long Append(
        SqliteConnection connection,
        SqliteTransaction tx,
        string eventType,
        string entityType,
        long? entityId,
        int surahNumber,
        int? ayahNumber,
        long? contextBlockId,
        long? workingSliceId,
        int? startAyah,
        int? endAyah,
        string summary,
        string occurredUtc,
        string detailJson = "{}")
    {
        using var command = connection.CreateCommand();
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
            $eventType,
            $entityType,
            $entityId,
            $surah,
            $ayah,
            $context,
            $slice,
            $start,
            $end,
            $summary,
            $detail,
            $occurred);
        SELECT last_insert_rowid();
        """;

        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$entityType", entityType);
        command.Parameters.AddWithValue("$entityId", DbValue(entityId));
        command.Parameters.AddWithValue("$surah", surahNumber);
        command.Parameters.AddWithValue("$ayah", DbValue(ayahNumber));
        command.Parameters.AddWithValue("$context", DbValue(contextBlockId));
        command.Parameters.AddWithValue("$slice", DbValue(workingSliceId));
        command.Parameters.AddWithValue("$start", DbValue(startAyah));
        command.Parameters.AddWithValue("$end", DbValue(endAyah));
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$detail", detailJson);
        command.Parameters.AddWithValue("$occurred", occurredUtc);

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static object DbValue<T>(T? value)
        where T : struct =>
        value.HasValue
            ? value.Value
            : DBNull.Value;
}
