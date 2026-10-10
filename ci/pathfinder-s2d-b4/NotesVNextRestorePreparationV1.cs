using System.Text.Json;
using Microsoft.Data.Sqlite;
using NivareQ.Step2Pathfinder.Models;

namespace NivareQ.Step2Pathfinder.Services;

// S2-D B5: in-memory-only, UNCOMMITTED candidate for a future single journaled
// provider transaction. No disk writes, Commit() call or live feature hook.
public sealed record NotesVNextUncommittedRestorePlan(
    BackupImportPlan ProviderPlan, string SourceGenerationId,
    string StagedGenerationId, PreservedBackupExtensionState CandidateExtensions);

public static class NotesVNextRestorePreparationV1
{
    public static NotesVNextUncommittedRestorePlan Prepare(
        BackupImportPlan verifiedProviderPlan, string notesRoot, string stagedGenerationId,
        string? currentlyActiveGenerationId)
    {
        ArgumentNullException.ThrowIfNull(verifiedProviderPlan);
        if (verifiedProviderPlan.SourceSchemaVersion != BackupContractService.CanonicalSchemaVersion ||
            !verifiedProviderPlan.ReplacePreservedExtensions ||
            !verifiedProviderPlan.ReplaceCockpitState ||
            !verifiedProviderPlan.ReplaceJournal ||
            !verifiedProviderPlan.ReplaceUWorldSelector)
            throw new InvalidDataException("Cannot prepare a partial or legacy provider replacement for Notes restore.");
        var candidate = BackupExtensionStore.Clone(verifiedProviderPlan.PreservedExtensions);
        if (!candidate.Values.TryGetValue(NotesStoreV1.GenerationPointerKey, out var sourcePointer) ||
            sourcePointer.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Cannot restore a Notes archive with no canonical Notes source pointer.");
        var original = sourcePointer.GetString() ?? "";
        NotesVNextProviderAuthorityV1.ValidatePointerRebase(original, stagedGenerationId, currentlyActiveGenerationId);
        // No pointer plan may name a non-existent, partially extracted, mutable
        // or corrupt database. OpenStagedGeneration verifies full relational
        // and referenced-attachment integrity without creating missing files.
        using (var staged = NotesStoreV1.OpenStagedGeneration(notesRoot, stagedGenerationId))
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = staged.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            db.Open();
            using var query = db.CreateCommand();
            query.CommandText = "SELECT value FROM meta WHERE key='stage_state'";
            if ((string?)query.ExecuteScalar() != "sealed")
                throw new InvalidDataException("Unsealed Notes generation cannot enter a restore pointer plan.");
        }
        // Only the pointer is rebased: every other preserved extension (including
        // any existing S1 recovery data) remains an untouched cloned JsonElement.
        candidate.Values[NotesStoreV1.GenerationPointerKey] = JsonSerializer.SerializeToElement(stagedGenerationId);
        return new NotesVNextUncommittedRestorePlan(verifiedProviderPlan, original,
            stagedGenerationId, candidate);
    }
}
