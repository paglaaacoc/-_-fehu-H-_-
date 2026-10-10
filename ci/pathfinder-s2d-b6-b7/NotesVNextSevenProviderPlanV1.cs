using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NivareQ.Step2Pathfinder.Models;

namespace NivareQ.Step2Pathfinder.Services;

// S2-D B7 INTERNAL: an IN-MEMORY candidate dictionary for the *existing*
// seven-target RestoreTransactionService journal. No Commit(), live hooks,
// file writes, runtime state substitution, or Notes pointer activation here.
public sealed record NotesVNextSevenProviderCandidateV1(
    IReadOnlyDictionary<string, string> Targets, string SourceGenerationId,
    string StagedGenerationId, long RestoreEpoch);

public static class NotesVNextSevenProviderPlanV1
{
    public static NotesVNextSevenProviderCandidateV1 Prepare(
        NotesVNextUncommittedRestorePlan staged, string notesRoot,
        NotesVNextMutationCoordinatorV1.RestoreLease heldFence,
        CockpitStateService cockpitSerializer, UWorldSelectorService selectorSerializer,
        BackupExtensionStore extensionSerializer, JournalState migratedJournalTarget)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(heldFence);
        ArgumentNullException.ThrowIfNull(cockpitSerializer);
        ArgumentNullException.ThrowIfNull(selectorSerializer);
        ArgumentNullException.ThrowIfNull(extensionSerializer);
        ArgumentNullException.ThrowIfNull(migratedJournalTarget);
        heldFence.RequirePreCommitOwnership();
        var plan = staged.ProviderPlan;
        if (plan.SourceSchemaVersion != BackupContractService.CanonicalSchemaVersion ||
            !plan.ReplaceCockpitState || !plan.ReplaceJournal ||
            !plan.ReplaceUWorldSelector || !plan.ReplacePreservedExtensions)
            throw new InvalidDataException("The B7 seven-provider candidate requires full schema-7 replacement semantics.");
        NotesVNextProviderAuthorityV1.ValidatePointerRebase(staged.SourceGenerationId,
            staged.StagedGenerationId, null);

        // Reverify the generation at the final provider-planning boundary. B5
        // verification alone is not a license to trust a modified staged path.
        using (var verified = NotesStoreV1.OpenStagedGeneration(notesRoot, staged.StagedGenerationId))
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = verified.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key='stage_state'";
            if ((string?)command.ExecuteScalar() != "sealed")
                throw new InvalidDataException("The staged Notes generation is no longer sealed.");
        }

        var originalExtensions = plan.PreservedExtensions.Values ??
            throw new InvalidDataException("The original preserved-extension provider is unavailable.");
        var rewritten = staged.CandidateExtensions.Values ??
            throw new InvalidDataException("The Notes pointer candidate is unavailable.");
        if (originalExtensions.Count != rewritten.Count ||
            !rewritten.TryGetValue(NotesStoreV1.GenerationPointerKey, out var replacement) ||
            replacement.ValueKind != JsonValueKind.String ||
            replacement.GetString() != staged.StagedGenerationId ||
            !originalExtensions.TryGetValue(NotesStoreV1.GenerationPointerKey, out var source) ||
            source.ValueKind != JsonValueKind.String ||
            source.GetString() != staged.SourceGenerationId)
            throw new InvalidDataException("The Notes generation pointer was not rebased from the verified source.");
        foreach (var item in originalExtensions)
        {
            if (!rewritten.TryGetValue(item.Key, out var candidate))
                throw new InvalidDataException("A preserved-extension key was removed from the restore candidate.");
            if (item.Key != NotesStoreV1.GenerationPointerKey &&
                item.Value.GetRawText() != candidate.GetRawText())
                throw new InvalidDataException("A non-Notes preserved extension was modified during staging.");
        }

        // The original JSON restore path migrates Personal notes into the
        // Journal before serialization. B7 requires the caller's real
        // BuildJournalRestoreTarget result, and rejects a missing marker so
        // compatibility evidence cannot be silently omitted.
        foreach (var pair in plan.PersonalStudy.SubjectNotes)
            if (!string.IsNullOrWhiteSpace(pair.Value) &&
                !migratedJournalTarget.LegacyPersonalMigrationSlugs.Contains(pair.Key.Trim(), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Missing legacy Personal-to-Journal migration marker.");

        var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PathfinderPaths.SettingsPath] = SettingsService.Serialize(plan.ShellSettings),
            [PathfinderPaths.PersonalStudyPath] = PersonalStudyService.Serialize(plan.PersonalStudy),
            [PathfinderPaths.BackupExtensionsPath] = extensionSerializer.Serialize(staged.CandidateExtensions),
            [PathfinderPaths.CockpitStatePath] = cockpitSerializer.Serialize(plan.CockpitState),
            [PathfinderPaths.JournalSnapshotPath] = JournalService.SerializeState(migratedJournalTarget),
            [PathfinderPaths.JournalLogPath] = JournalService.EmptyOperationLog,
            [PathfinderPaths.UWorldSelectorStatePath] = selectorSerializer.SerializeState(plan.UWorldSelector)
        };
        if (target.Count != 7)
            throw new InvalidOperationException("The canonical restore target set must have exactly seven distinct files.");
        foreach (var path in target.Keys)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new InvalidDataException("An inherited restore target was not a fully qualified path.");
            // The authority to WRITE these paths still belongs exclusively
            // to RestoreTransactionService.Commit and its own path validator.
        }
        heldFence.RequirePreCommitOwnership();
        return new NotesVNextSevenProviderCandidateV1(
            new ReadOnlyDictionary<string, string>(target), staged.SourceGenerationId,
            staged.StagedGenerationId, heldFence.Epoch);
    }
}
