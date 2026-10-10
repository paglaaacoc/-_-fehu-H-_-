using System.Collections.ObjectModel;

namespace NivareQ.Step2Pathfinder.Services;

// S2-D B8 INTERNAL / UNACCEPTED: a dormant bridge to the EXISTING
// RestoreTransactionService.Commit journal. No MainWindow/backup/reset/Notes
// caller is allowed until B9 full mutation coverage + native CI/fault gates.
// In particular this class does not make the uninstalled B6 coordinator global.
internal static class NotesVNextCommitBoundaryV1
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    // Exactly the seven inherited transaction targets; Notes binaries are NOT
    // part of the text journal. Their fresh sealed generation was checked by B7.
    private static HashSet<string> CanonicalPaths() => new(PathComparer)
    {
        PathfinderPaths.SettingsPath,
        PathfinderPaths.PersonalStudyPath,
        PathfinderPaths.BackupExtensionsPath,
        PathfinderPaths.CockpitStatePath,
        PathfinderPaths.JournalSnapshotPath,
        PathfinderPaths.JournalLogPath,
        PathfinderPaths.UWorldSelectorStatePath
    };

    // This method is intentionally unreferenced from the product. When enabled
    // in a later checkpoint it MUST share the coordinator used by EVERY writer.
    internal static PersistenceWriteResult CommitDormant(
        NotesVNextSevenProviderCandidateV1 candidate,
        NotesVNextMutationCoordinatorV1.RestoreLease heldFence,
        string observedCurrentGenerationId)
    {
        return CommitCore(candidate, heldFence, observedCurrentGenerationId,
            RestoreTransactionService.Commit,
            () => RestoreTransactionService.BlockMutationsUntilRestart(
                "Backup VNext restored a Notes generation. Restart Pathfinder before any further state mutations."));
    }

    // The native harness uses a fake journal delegate only: no disk, registry,
    // AppData or real user state. The callback is invoked ONLY after success.
    internal static PersistenceWriteResult CommitCore(
        NotesVNextSevenProviderCandidateV1 candidate,
        NotesVNextMutationCoordinatorV1.RestoreLease heldFence,
        string observedCurrentGenerationId,
        Func<IReadOnlyDictionary<string, string>, PersistenceWriteResult> journalCommit,
        Action blockExistingWritersAfterSuccess)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(heldFence);
        ArgumentNullException.ThrowIfNull(journalCommit);
        ArgumentNullException.ThrowIfNull(blockExistingWritersAfterSuccess);
        heldFence.RequirePreCommitOwnership();

        if (candidate.RestoreEpoch != heldFence.Epoch)
            throw new InvalidOperationException("Restore plan belongs to a different mutation epoch.");
        if (!NotesStoreV1.IsValidGenerationId(candidate.SourceGenerationId) ||
            !NotesStoreV1.IsValidGenerationId(candidate.StagedGenerationId) ||
            string.Equals(candidate.SourceGenerationId, candidate.StagedGenerationId, StringComparison.Ordinal) ||
            !string.Equals(candidate.SourceGenerationId, observedCurrentGenerationId, StringComparison.Ordinal))
            throw new InvalidDataException("Current Notes pointer changed or staged destination is not distinct.");

        var allowedPaths = CanonicalPaths();
        if (allowedPaths.Count != 7 || candidate.Targets is null || candidate.Targets.Count != 7 ||
            !allowedPaths.SetEquals(candidate.Targets.Keys))
            throw new InvalidDataException("Restore target set must match the seven existing journal providers exactly.");
        if (candidate.Targets.Any(p => p.Value is null))
            throw new InvalidDataException("A journal candidate has no provider payload.");

        // Snapshot the dictionary before invoking any external code. The B9
        // gate must additionally prove that each provider snapshot is frozen.
        var frozen = new ReadOnlyDictionary<string, string>(
            candidate.Targets.ToDictionary(p => p.Key, p => p.Value, PathComparer));
        heldFence.RequirePreCommitOwnership();

        // After this point, failure/cancellation/exception must NEVER reopen
        // the B6 coordinator. Only a clean restart is permitted.
        heldFence.MarkCommitAttempted();
        var result = journalCommit(frozen);
        if (result is null)
            throw new IOException("Journal commit returned no persistence result; restart is required.");
        if (!result.Success)
            return result; // Failed commits still leave B6 blocked until restart.
        heldFence.MarkCommitSucceeded();
        blockExistingWritersAfterSuccess();
        return result;
    }
}
