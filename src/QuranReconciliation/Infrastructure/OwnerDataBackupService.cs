using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuranReconciliation.Infrastructure;

internal sealed record BackupManifest(
    int FormatVersion,
    string BackupId,
    DateTimeOffset CreatedUtc,
    string Reason,
    string AppBuild,
    int ResearchSchemaVersion,
    int SettingsSchemaVersion,
    string ResearchSha256,
    long ResearchLength,
    string SettingsSha256,
    long SettingsLength,
    string? SourceRevision = null);

internal sealed record BackupSummary(
    string Path,
    BackupManifest Manifest)
{
    internal string DisplayLabel =>
        $"{Manifest.CreatedUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss} · " +
        $"{Manifest.Reason} · {Manifest.AppBuild} · {System.IO.Path.GetFileName(Path)}";
}

internal sealed record VerifiedOwnerBackup(
    string Path,
    BackupManifest Manifest,
    DateTimeOffset VerifiedUtc);

internal sealed record RestoreOwnerStateResult(
    VerifiedOwnerBackup RestoredBackup,
    VerifiedOwnerBackup PreRestoreBackup);

internal sealed record OwnerStateRecoveryResult(
    bool Recovered,
    string Message);

internal sealed record OwnerStateOperationJournal(
    string OperationId,
    string Kind,
    string Stage,
    string AppBuild,
    string SourceRevision,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string PreOperationBackupPath,
    string PreOperationBackupId,
    string PreResearchSha256,
    string PreSettingsSha256,
    string? TargetBackupPath,
    string? TargetBackupId,
    string? NewResearchSha256,
    string? NewSettingsSha256,
    string QuarantinePath);

internal sealed class OwnerStateRecoveryRequiredException
    : InvalidOperationException
{
    internal OwnerStateRecoveryRequiredException(
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
    }
}

internal sealed class OwnerDataBackupService
{
    internal const int BackupFormatVersion = 1;
    internal const string ManualReason = "Manual";
    internal const string PreRestoreReason = "PreRestore";
    internal const string PreResetReason = "PreReset";

    private const string RestoreOperation = "Restore";
    private const string ResetOperation = "Reset";
    private const string PreparedStage = "Prepared";
    private const string QuarantinedStage = "Quarantined";
    private const string CommittedStage = "Committed";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    private static readonly IReadOnlyDictionary<string, string[]>
        RequiredResearchColumns =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["meta"] = ["key", "value"],
                ["context_blocks"] =
                    ["id", "surah_number", "start_ayah", "end_ayah",
                     "status", "owner_note", "created_utc", "updated_utc",
                     "origin", "is_active", "proposal_import_id"],
                ["context_boundary_history"] =
                    ["id", "context_block_id", "old_start_ayah", "old_end_ayah",
                     "new_start_ayah", "new_end_ayah", "owner_note", "changed_utc"],
                ["ayah_notes"] =
                    ["surah_number", "ayah_number", "body", "updated_utc"],
                ["ayah_note_history"] =
                    ["id", "surah_number", "ayah_number", "prior_body", "changed_utc"],
                ["context_notes"] =
                    ["context_block_id", "body", "updated_utc"],
                ["context_note_history"] =
                    ["id", "context_block_id", "prior_body", "changed_utc"],
                ["bookmarks"] =
                    ["id", "surah_number", "ayah_number", "title", "note",
                     "created_utc", "updated_utc"],
                ["context_proposal_imports"] =
                    ["id", "surah_number", "raw_payload", "normalized_ranges",
                     "created_utc", "discarded_utc"],
                ["working_slices"] =
                    ["id", "surah_number", "context_block_id", "start_ayah",
                     "end_ayah", "title", "status", "research_notes",
                     "conclusion", "created_utc", "updated_utc", "is_active"],
                ["working_slice_revisions"] =
                    ["id", "working_slice_id", "prior_title", "prior_status",
                     "prior_research_notes", "prior_conclusion", "changed_utc"],
                ["context_proposal_replaced_blocks"] =
                    ["import_id", "context_block_id", "position", "prior_status",
                     "prior_origin", "prior_updated_utc"]
            };

    private static readonly IReadOnlyDictionary<string, string[]>
        Schema7ResearchColumns =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["working_slice_revisions"] =
                    ["changed_fields_json", "change_summary"],
                ["context_operations"] =
                    ["id", "surah_number", "operation_type", "summary",
                     "created_utc", "proposal_import_id"],
                ["context_operation_blocks"] =
                    ["id", "operation_id", "context_block_id", "role",
                     "old_start_ayah", "old_end_ayah",
                     "new_start_ayah", "new_end_ayah",
                     "old_status", "new_status"],
                ["research_activity_events"] =
                    ["id", "event_type", "entity_type", "entity_id",
                     "surah_number", "ayah_number", "context_block_id",
                     "working_slice_id", "start_ayah", "end_ayah",
                     "summary", "detail_json", "occurred_utc"]
            };

    private readonly string _rootDirectory;
    private readonly string _dataDirectory;
    private readonly string _backupsDirectory;
    private readonly string _diagnosticsDirectory;
    private readonly string _researchDatabase;
    private readonly string _settingsFile;
    private readonly string _operationJournal;
    private readonly string _appBuild;
    private readonly string _sourceRevision;

    internal OwnerDataBackupService(
        string appBuild,
        string? baseDirectory = null,
        string? sourceRevision = null)
    {
        _appBuild = appBuild;
        _sourceRevision =
            string.IsNullOrWhiteSpace(sourceRevision)
                ? BuildIdentity.SourceRevision
                : sourceRevision;

        _rootDirectory =
            Path.GetFullPath(
                baseDirectory ??
                AppPaths.BaseDirectory);

        _dataDirectory =
            Path.Combine(
                _rootDirectory,
                "Data");

        _backupsDirectory =
            Path.Combine(
                _rootDirectory,
                "Backups");

        _diagnosticsDirectory =
            Path.Combine(
                _rootDirectory,
                "Diagnostics");

        _researchDatabase =
            Path.Combine(
                _dataDirectory,
                "research.sqlite");

        _settingsFile =
            Path.Combine(
                _dataDirectory,
                "settings.json");

        _operationJournal =
            Path.Combine(
                _dataDirectory,
                "owner-state-operation.json");
    }

    internal IReadOnlyList<BackupSummary> ListBackups()
    {
        EnsureDirectories();

        var result =
            new List<BackupSummary>();

        foreach (string path in
            Directory.EnumerateFiles(
                _backupsDirectory,
                "QuranReconciliation-Backup-*.zip",
                SearchOption.TopDirectoryOnly))
        {
            try
            {
                BackupManifest manifest =
                    ReadManifest(path);

                ValidateManifest(manifest);

                result.Add(
                    new BackupSummary(
                        path,
                        manifest));
            }
            catch
            {
                // Invalid/incomplete archives are deliberately hidden from
                // the restore picker. Full verification still runs before
                // every restore.
            }
        }

        return result
            .OrderByDescending(
                item =>
                    item.Manifest.CreatedUtc)
            .ToList();
    }

    internal VerifiedOwnerBackup CreateVerifiedBackup(
        string reason)
    {
        if (reason != ManualReason &&
            reason != PreRestoreReason &&
            reason != PreResetReason)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason));
        }

        EnsureDirectories();

        if (!File.Exists(_researchDatabase))
        {
            throw new FileNotFoundException(
                "research.sqlite is missing.",
                _researchDatabase);
        }

        if (!File.Exists(_settingsFile))
        {
            throw new FileNotFoundException(
                "settings.json is missing.",
                _settingsFile);
        }

        string staging =
            Path.Combine(
                _backupsDirectory,
                ".backup-stage-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);

        string stagedResearch =
            Path.Combine(staging, "research.sqlite");

        string stagedSettings =
            Path.Combine(staging, "settings.json");

        string stagedManifest =
            Path.Combine(staging, "manifest.json");

        string? temporaryArchive = null;

        try
        {
            SnapshotResearchDatabase(
                _researchDatabase,
                stagedResearch);

            File.Copy(
                _settingsFile,
                stagedSettings,
                overwrite: true);

            int researchSchema =
                VerifyResearchDatabase(
                    stagedResearch,
                    requireCurrentSchema: true);

            AppSettings settings =
                VerifySettings(stagedSettings);

            var manifest =
                new BackupManifest(
                    BackupFormatVersion,
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow,
                    reason,
                    _appBuild,
                    researchSchema,
                    settings.SchemaVersion,
                    ComputeSha256(stagedResearch),
                    new FileInfo(stagedResearch).Length,
                    ComputeSha256(stagedSettings),
                    new FileInfo(stagedSettings).Length,
                    _sourceRevision);

            File.WriteAllText(
                stagedManifest,
                JsonSerializer.Serialize(
                    manifest,
                    JsonOptions));

            string fileName =
                $"QuranReconciliation-Backup-" +
                $"{manifest.CreatedUtc:yyyyMMdd-HHmmssfff}-" +
                $"{reason.ToLowerInvariant()}-" +
                $"{manifest.BackupId[..8]}.zip";

            string finalArchive =
                Path.Combine(
                    _backupsDirectory,
                    fileName);

            temporaryArchive =
                finalArchive + ".tmp";

            using (var archive =
                ZipFile.Open(
                    temporaryArchive,
                    ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(
                    stagedManifest,
                    "manifest.json",
                    CompressionLevel.Optimal);

                archive.CreateEntryFromFile(
                    stagedResearch,
                    "research.sqlite",
                    CompressionLevel.Optimal);

                archive.CreateEntryFromFile(
                    stagedSettings,
                    "settings.json",
                    CompressionLevel.Optimal);
            }

            File.Move(
                temporaryArchive,
                finalArchive,
                overwrite: false);

            temporaryArchive = null;

            return VerifyBackup(finalArchive);
        }
        finally
        {
            TryDeleteDirectory(staging);

            if (temporaryArchive is not null)
            {
                TryDeleteFile(temporaryArchive);
            }
        }
    }

    internal VerifiedOwnerBackup VerifyBackup(
        string backupPath)
    {
        string fullPath =
            Path.GetFullPath(backupPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Backup archive is missing.",
                fullPath);
        }

        using var archive =
            ZipFile.OpenRead(fullPath);

        string[] expected =
        [
            "manifest.json",
            "research.sqlite",
            "settings.json"
        ];

        string[] actual =
            archive.Entries
                .Select(entry => entry.FullName)
                .OrderBy(
                    name => name,
                    StringComparer.Ordinal)
                .ToArray();

        string[] orderedExpected =
            expected
                .OrderBy(
                    name => name,
                    StringComparer.Ordinal)
                .ToArray();

        if (!actual.SequenceEqual(
                orderedExpected,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Backup archive contains an unexpected file set.");
        }

        ZipArchiveEntry manifestEntry =
            archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException(
                "Backup manifest is missing.");

        BackupManifest manifest;

        using (Stream stream = manifestEntry.Open())
        {
            manifest =
                JsonSerializer.Deserialize<BackupManifest>(
                    stream,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    "Backup manifest is invalid.");
        }

        ValidateManifest(manifest);

        ZipArchiveEntry researchEntry =
            archive.GetEntry("research.sqlite")
            ?? throw new InvalidDataException(
                "Backup research database is missing.");

        ZipArchiveEntry settingsEntry =
            archive.GetEntry("settings.json")
            ?? throw new InvalidDataException(
                "Backup settings are missing.");

        if (researchEntry.Length != manifest.ResearchLength ||
            !string.Equals(
                ComputeSha256(researchEntry),
                manifest.ResearchSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Backup research database digest does not match its manifest.");
        }

        if (settingsEntry.Length != manifest.SettingsLength ||
            !string.Equals(
                ComputeSha256(settingsEntry),
                manifest.SettingsSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Backup settings digest does not match its manifest.");
        }

        string verification =
            Path.Combine(
                _backupsDirectory,
                ".verify-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(verification);

        try
        {
            string research =
                Path.Combine(
                    verification,
                    "research.sqlite");

            string settings =
                Path.Combine(
                    verification,
                    "settings.json");

            researchEntry.ExtractToFile(research);
            settingsEntry.ExtractToFile(settings);

            int researchSchema =
                VerifyResearchDatabase(
                    research,
                    requireCurrentSchema: false);

            AppSettings verifiedSettings =
                VerifySettings(settings);

            if (researchSchema !=
                manifest.ResearchSchemaVersion)
            {
                throw new InvalidDataException(
                    "Backup research schema does not match its manifest.");
            }

            if (verifiedSettings.SchemaVersion !=
                manifest.SettingsSchemaVersion)
            {
                throw new InvalidDataException(
                    "Backup settings schema does not match its manifest.");
            }
        }
        finally
        {
            TryDeleteDirectory(verification);
        }

        return new VerifiedOwnerBackup(
            fullPath,
            manifest,
            DateTimeOffset.UtcNow);
    }

    internal RestoreOwnerStateResult RestoreOwnerState(
        string backupPath)
    {
        VerifiedOwnerBackup target =
            VerifyBackup(backupPath);

        VerifiedOwnerBackup preRestore =
            CreateVerifiedBackup(
                PreRestoreReason);

        string staging =
            ExtractVerifiedPayload(target);

        OwnerStateOperationJournal journal =
            BeginOperation(
                RestoreOperation,
                preRestore,
                target,
                target.Manifest.ResearchSha256,
                target.Manifest.SettingsSha256);

        try
        {
            Directory.CreateDirectory(
                journal.QuarantinePath);

            SqliteConnection.ClearAllPools();

            MoveResearchFilesTo(
                journal.QuarantinePath);

            MoveIfExists(
                _settingsFile,
                Path.Combine(
                    journal.QuarantinePath,
                    "settings.json"));

            journal =
                UpdateJournal(
                    journal,
                    QuarantinedStage);

            File.Move(
                Path.Combine(
                    staging,
                    "research.sqlite"),
                _researchDatabase,
                overwrite: false);

            File.Move(
                Path.Combine(
                    staging,
                    "settings.json"),
                _settingsFile,
                overwrite: false);

            VerifyInstalledState(
                target.Manifest.ResearchSha256,
                target.Manifest.SettingsSha256,
                requireCurrentSchema: false);

            journal =
                UpdateJournal(
                    journal,
                    CommittedStage);

            WriteRecoveryReceipt(
                journal,
                "verified complete");

            TryDeleteDirectory(
                journal.QuarantinePath);

            DeleteFileStrict(
                _operationJournal);

            return new RestoreOwnerStateResult(
                target,
                preRestore);
        }
        catch (Exception ex)
        {
            WriteRecoveryReceipt(
                journal,
                "operation failed; rollback started");

            try
            {
                RecoverPreOperationState(
                    journal,
                    "restore failure rollback");
            }
            catch (Exception rollbackEx)
            {
                throw new OwnerStateRecoveryRequiredException(
                    "Restore failed and automatic rollback could not be proven. Recovery material and the operation record were preserved. Do not continue research in this folder until recovery is completed.",
                    new AggregateException(
                        ex,
                        rollbackEx));
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    internal void ResetResearchData(
        VerifiedOwnerBackup safetyBackup)
    {
        VerifiedOwnerBackup verified =
            VerifyBackup(safetyBackup.Path);

        if (!string.Equals(
                verified.Manifest.BackupId,
                safetyBackup.Manifest.BackupId,
                StringComparison.Ordinal) ||
            !string.Equals(
                verified.Manifest.Reason,
                PreResetReason,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Reset requires the exact verified PreReset backup created by this reset flow.");
        }

        if (DateTimeOffset.UtcNow -
            verified.Manifest.CreatedUtc >
            TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException(
                "The PreReset backup is no longer fresh. Create and verify a new backup before resetting.");
        }

        VerifyBackupMatchesCurrentState(verified);

        OwnerStateOperationJournal journal =
            BeginOperation(
                ResetOperation,
                verified,
                targetBackup: null,
                newResearchSha256: null,
                newSettingsSha256: null);

        try
        {
            Directory.CreateDirectory(
                journal.QuarantinePath);

            SqliteConnection.ClearAllPools();

            MoveResearchFilesTo(
                journal.QuarantinePath);

            journal =
                UpdateJournal(
                    journal,
                    QuarantinedStage);

            ResearchDatabase.InitializeAt(
                _researchDatabase);

            VerifyResearchDatabase(
                _researchDatabase,
                requireCurrentSchema: true);

            (string researchHash, string settingsHash) =
                ComputeLiveStateHashes();

            journal =
                UpdateJournal(
                    journal,
                    CommittedStage,
                    researchHash,
                    settingsHash);

            VerifyInstalledState(
                researchHash,
                settingsHash,
                requireCurrentSchema: true);

            WriteRecoveryReceipt(
                journal,
                "verified complete");

            TryDeleteDirectory(
                journal.QuarantinePath);

            DeleteFileStrict(
                _operationJournal);
        }
        catch (Exception ex)
        {
            WriteRecoveryReceipt(
                journal,
                "operation failed; rollback started");

            try
            {
                RecoverPreOperationState(
                    journal,
                    "reset failure rollback");
            }
            catch (Exception rollbackEx)
            {
                throw new OwnerStateRecoveryRequiredException(
                    "Reset failed and automatic rollback could not be proven. Recovery material and the operation record were preserved. Do not continue research in this folder until recovery is completed.",
                    new AggregateException(
                        ex,
                        rollbackEx));
            }

            throw;
        }
    }

    internal OwnerStateRecoveryResult RecoverInterruptedOperation()
    {
        EnsureDirectories();

        if (!File.Exists(_operationJournal))
        {
            return new OwnerStateRecoveryResult(
                false,
                string.Empty);
        }

        OwnerStateOperationJournal journal =
            ReadJournal();

        WriteRecoveryReceipt(
            journal,
            $"startup recovery entered at stage {journal.Stage}");

        try
        {
            if (journal.Stage ==
                CommittedStage)
            {
                return RecoverCommittedOperation(
                    journal);
            }

            RecoverPreOperationState(
                journal,
                "startup rollback");

            return new OwnerStateRecoveryResult(
                true,
                $"Recovered interrupted {journal.Kind.ToLowerInvariant()} {journal.OperationId}: the exact verified pre-operation owner state was restored before normal database initialization.");
        }
        catch (Exception ex)
        {
            WriteRecoveryReceipt(
                journal,
                "recovery required");

            throw new OwnerStateRecoveryRequiredException(
                $"An interrupted {journal.Kind.ToLowerInvariant()} requires manual recovery. The app refused to initialize a replacement research database. Operation ID: {journal.OperationId}. Operation record: {_operationJournal}.",
                ex);
        }
    }

    private OwnerStateRecoveryResult RecoverCommittedOperation(
        OwnerStateOperationJournal journal)
    {
        if (journal.Kind ==
            RestoreOperation)
        {
            if (string.IsNullOrWhiteSpace(
                    journal.TargetBackupPath) ||
                string.IsNullOrWhiteSpace(
                    journal.TargetBackupId))
            {
                throw new InvalidDataException(
                    "Committed restore journal is missing its verified target backup identity.");
            }

            VerifiedOwnerBackup target =
                VerifyBackup(
                    journal.TargetBackupPath);

            if (!string.Equals(
                    target.Manifest.BackupId,
                    journal.TargetBackupId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Committed restore target no longer matches the operation journal.");
            }

            bool exactLiveTarget =
                File.Exists(
                    _researchDatabase) &&
                File.Exists(
                    _settingsFile) &&
                !File.Exists(
                    _researchDatabase + "-wal") &&
                !File.Exists(
                    _researchDatabase + "-shm") &&
                string.Equals(
                    ComputeSha256(
                        _researchDatabase),
                    target.Manifest.ResearchSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    ComputeSha256(
                        _settingsFile),
                    target.Manifest.SettingsSha256,
                    StringComparison.OrdinalIgnoreCase);

            string? preserved = null;

            if (!exactLiveTarget)
            {
                preserved =
                    PreserveLiveAsUnverified(
                        journal);

                InstallVerifiedBackupWithoutPreBackup(
                    target);
            }
            else
            {
                VerifyInstalledState(
                    target.Manifest.ResearchSha256,
                    target.Manifest.SettingsSha256,
                    requireCurrentSchema: false);
            }

            CompleteRecoveredOperation(
                journal,
                preserved is null
                    ? "committed restore independently re-verified on startup"
                    : $"committed restore reinstalled from its exact verified target; displaced live material preserved at {preserved}");

            return new OwnerStateRecoveryResult(
                true,
                $"Recovered committed restore {journal.OperationId}: the exact verified restore target is authoritative and cleanup completed.");
        }

        if (journal.Kind ==
            ResetOperation)
        {
            if (!CommittedResetStateIsValid(
                    journal))
            {
                string? preserved =
                    PreserveLiveAsUnverified(
                        journal);

                RebuildCommittedResetState(
                    journal);

                CompleteRecoveredOperation(
                    journal,
                    preserved is null
                        ? "committed reset reconstructed and verified on startup"
                        : $"committed reset reconstructed and verified; displaced live material preserved at {preserved}");
            }
            else
            {
                CompleteRecoveredOperation(
                    journal,
                    "committed reset independently re-verified on startup");
            }

            return new OwnerStateRecoveryResult(
                true,
                $"Recovered committed reset {journal.OperationId}: fresh research state and preserved settings were verified before normal initialization.");
        }

        throw new InvalidDataException(
            $"Unsupported committed operation kind: {journal.Kind}.");
    }

    private bool CommittedResetStateIsValid(
        OwnerStateOperationJournal journal)
    {
        if (!File.Exists(
                _researchDatabase) ||
            !File.Exists(
                _settingsFile))
        {
            return false;
        }

        try
        {
            VerifyResearchDatabase(
                _researchDatabase,
                requireCurrentSchema: true);

            VerifySettings(
                _settingsFile);

            string expectedSettings =
                journal.NewSettingsSha256 ??
                journal.PreSettingsSha256;

            if (!string.Equals(
                    ComputeSha256(
                        _settingsFile),
                    expectedSettings,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var connection =
                new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource =
                            Path.GetFullPath(
                                _researchDatabase),
                        Mode =
                            SqliteOpenMode.ReadOnly,
                        Cache =
                            SqliteCacheMode.Private,
                        Pooling =
                            false
                    }.ToString());

            connection.Open();

            string[] mutableTables =
            [
                "context_blocks",
                "context_boundary_history",
                "ayah_notes",
                "ayah_note_history",
                "context_notes",
                "context_note_history",
                "bookmarks",
                "context_proposal_imports",
                "context_proposal_replaced_blocks",
                "working_slices",
                "working_slice_revisions",
                "research_activity_events",
                "context_operations",
                "context_operation_blocks"
            ];

            foreach (string table
                     in mutableTables)
            {
                using var command =
                    connection.CreateCommand();

                command.CommandText =
                    $"SELECT COUNT(*) FROM {table};";

                if (Convert.ToInt64(
                        command.ExecuteScalar()) !=
                    0)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RebuildCommittedResetState(
        OwnerStateOperationJournal journal)
    {
        VerifiedOwnerBackup preOperation =
            VerifyBackup(
                journal.PreOperationBackupPath);

        if (!string.Equals(
                preOperation.Manifest.BackupId,
                journal.PreOperationBackupId,
                StringComparison.Ordinal) ||
            !string.Equals(
                preOperation.Manifest.SettingsSha256,
                journal.PreSettingsSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Committed reset recovery cannot verify the recorded pre-reset settings authority.");
        }

        string staging =
            ExtractVerifiedPayload(
                preOperation);

        try
        {
            SqliteConnection.ClearAllPools();

            DeleteLiveOwnerStateFilesStrict(
                includeSettings: true);

            ResearchDatabase.InitializeAt(
                _researchDatabase);

            File.Move(
                Path.Combine(
                    staging,
                    "settings.json"),
                _settingsFile,
                overwrite: false);

            if (!CommittedResetStateIsValid(
                    journal))
            {
                throw new InvalidDataException(
                    "Committed reset recovery rebuilt fresh research state but verification did not pass.");
            }
        }
        finally
        {
            TryDeleteDirectory(
                staging);
        }
    }

    private void CompleteRecoveredOperation(
        OwnerStateOperationJournal journal,
        string outcome)
    {
        TryDeleteDirectory(
            journal.QuarantinePath);

        DeleteFileStrict(
            _operationJournal);

        WriteRecoveryReceipt(
            journal,
            outcome);
    }

    private OwnerStateOperationJournal BeginOperation(
        string kind,
        VerifiedOwnerBackup preOperation,
        VerifiedOwnerBackup? targetBackup,
        string? newResearchSha256,
        string? newSettingsSha256)
    {
        EnsureDirectories();

        if (File.Exists(_operationJournal))
        {
            throw new OwnerStateRecoveryRequiredException(
                $"An existing owner-state operation record must be recovered before starting another destructive operation: {_operationJournal}");
        }

        string operationId =
            Guid.NewGuid().ToString("N");

        string quarantine =
            Path.Combine(
                _dataDirectory,
                $".{kind.ToLowerInvariant()}-quarantine-" +
                operationId);

        var journal =
            new OwnerStateOperationJournal(
                operationId,
                kind,
                PreparedStage,
                _appBuild,
                _sourceRevision,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                preOperation.Path,
                preOperation.Manifest.BackupId,
                preOperation.Manifest.ResearchSha256,
                preOperation.Manifest.SettingsSha256,
                targetBackup?.Path,
                targetBackup?.Manifest.BackupId,
                newResearchSha256,
                newSettingsSha256,
                quarantine);

        WriteJournal(journal);
        WriteRecoveryReceipt(
            journal,
            "prepared");

        return journal;
    }

    private OwnerStateOperationJournal UpdateJournal(
        OwnerStateOperationJournal journal,
        string stage,
        string? newResearchSha256 = null,
        string? newSettingsSha256 = null)
    {
        OwnerStateOperationJournal updated =
            journal with
            {
                Stage = stage,
                UpdatedUtc = DateTimeOffset.UtcNow,
                NewResearchSha256 =
                    newResearchSha256 ??
                    journal.NewResearchSha256,
                NewSettingsSha256 =
                    newSettingsSha256 ??
                    journal.NewSettingsSha256
            };

        WriteJournal(updated);
        WriteRecoveryReceipt(
            updated,
            $"stage {stage}");

        return updated;
    }

    private void RecoverPreOperationState(
        OwnerStateOperationJournal journal,
        string reason)
    {
        VerifiedOwnerBackup preOperation =
            VerifyBackup(
                journal.PreOperationBackupPath);

        if (!string.Equals(
                preOperation.Manifest.BackupId,
                journal.PreOperationBackupId,
                StringComparison.Ordinal) ||
            !string.Equals(
                preOperation.Manifest.ResearchSha256,
                journal.PreResearchSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                preOperation.Manifest.SettingsSha256,
                journal.PreSettingsSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The recorded pre-operation backup identity no longer matches the recovery journal.");
        }

        if (LiveStateMatches(
                journal.PreResearchSha256,
                journal.PreSettingsSha256))
        {
            TryDeleteDirectory(
                journal.QuarantinePath);

            DeleteFileStrict(
                _operationJournal);

            WriteRecoveryReceipt(
                journal,
                $"{reason}: live state already matched verified pre-operation backup");

            return;
        }

        string? unverified =
            PreserveLiveAsUnverified(
                journal);

        // Installation performs exact digest + schema verification before
        // returning. Do not immediately re-backup that SQLite snapshot and
        // require byte identity with the first snapshot: SQLite backup output
        // is a logical snapshot and a second round-trip is not a stable
        // file-byte identity check.
        InstallVerifiedBackupWithoutPreBackup(
            preOperation);

        TryDeleteDirectory(
            journal.QuarantinePath);

        DeleteFileStrict(
            _operationJournal);

        WriteRecoveryReceipt(
            journal,
            unverified is null
                ? $"{reason}: exact verified pre-operation backup restored"
                : $"{reason}: exact verified pre-operation backup restored; displaced live material preserved at {unverified}");
    }

    private string? PreserveLiveAsUnverified(
        OwnerStateOperationJournal journal)
    {
        string recoveryRoot =
            Path.Combine(
                _dataDirectory,
                "Recovery-Unverified");

        string destination =
            Path.Combine(
                recoveryRoot,
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-" +
                journal.OperationId);

        bool hasAny =
            File.Exists(_researchDatabase) ||
            File.Exists(_researchDatabase + "-wal") ||
            File.Exists(_researchDatabase + "-shm") ||
            File.Exists(_settingsFile);

        if (!hasAny)
        {
            return null;
        }

        Directory.CreateDirectory(
            destination);

        SqliteConnection.ClearAllPools();

        MoveIfExists(
            _researchDatabase,
            Path.Combine(
                destination,
                "research.sqlite"));

        MoveIfExists(
            _researchDatabase + "-wal",
            Path.Combine(
                destination,
                "research.sqlite-wal"));

        MoveIfExists(
            _researchDatabase + "-shm",
            Path.Combine(
                destination,
                "research.sqlite-shm"));

        MoveIfExists(
            _settingsFile,
            Path.Combine(
                destination,
                "settings.json"));

        File.WriteAllText(
            Path.Combine(
                destination,
                "UNVERIFIED-RECOVERY-MATERIAL.txt"),
            "This folder contains displaced live files preserved during owner-state recovery. " +
            "They were NOT accepted as a verified backup. Do not replace verified backup archives with these files.\n");

        return destination;
    }

    private void InstallVerifiedBackupWithoutPreBackup(
        VerifiedOwnerBackup backup)
    {
        string staging =
            ExtractVerifiedPayload(
                backup);

        try
        {
            SqliteConnection.ClearAllPools();

            DeleteLiveOwnerStateFilesStrict(
                includeSettings: true);

            File.Move(
                Path.Combine(
                    staging,
                    "research.sqlite"),
                _researchDatabase,
                overwrite: false);

            File.Move(
                Path.Combine(
                    staging,
                    "settings.json"),
                _settingsFile,
                overwrite: false);

            VerifyInstalledState(
                backup.Manifest.ResearchSha256,
                backup.Manifest.SettingsSha256,
                requireCurrentSchema: false);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private void VerifyInstalledState(
        string expectedResearchSha256,
        string expectedSettingsSha256,
        bool requireCurrentSchema)
    {
        VerifyResearchDatabase(
            _researchDatabase,
            requireCurrentSchema);

        VerifySettings(
            _settingsFile);

        string researchHash;

        if (File.Exists(
                _researchDatabase + "-wal"))
        {
            string staging =
                Path.Combine(
                    _backupsDirectory,
                    ".installed-check-" +
                    Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(staging);

            try
            {
                string snapshot =
                    Path.Combine(
                        staging,
                        "research.sqlite");

                SnapshotResearchDatabase(
                    _researchDatabase,
                    snapshot);

                researchHash =
                    ComputeSha256(snapshot);
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }
        else
        {
            researchHash =
                ComputeSha256(
                    _researchDatabase);
        }

        string settingsHash =
            ComputeSha256(
                _settingsFile);

        if (!string.Equals(
                researchHash,
                expectedResearchSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                settingsHash,
                expectedSettingsSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Installed owner state does not match the expected verified digests. " +
                $"research expected={expectedResearchSha256}, actual={researchHash}; " +
                $"settings expected={expectedSettingsSha256}, actual={settingsHash}.");
        }
    }

    private bool LiveStateMatches(
        string expectedResearchSha256,
        string expectedSettingsSha256)
    {
        if (!File.Exists(_researchDatabase) ||
            !File.Exists(_settingsFile))
        {
            return false;
        }

        try
        {
            (string researchHash, string settingsHash) =
                ComputeLiveStateHashes();

            return
                string.Equals(
                    researchHash,
                    expectedResearchSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    settingsHash,
                    expectedSettingsSha256,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private (string ResearchSha256, string SettingsSha256)
        ComputeLiveStateHashes()
    {
        string staging =
            Path.Combine(
                _backupsDirectory,
                ".live-state-check-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);

        try
        {
            string snapshot =
                Path.Combine(
                    staging,
                    "research.sqlite");

            SnapshotResearchDatabase(
                _researchDatabase,
                snapshot);

            return (
                ComputeSha256(snapshot),
                ComputeSha256(_settingsFile));
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private string ExtractVerifiedPayload(
        VerifiedOwnerBackup backup)
    {
        VerifiedOwnerBackup verified =
            VerifyBackup(backup.Path);

        if (!string.Equals(
                verified.Manifest.BackupId,
                backup.Manifest.BackupId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Backup identity changed after verification.");
        }

        string staging =
            Path.Combine(
                _backupsDirectory,
                ".restore-stage-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);

        try
        {
            using var archive =
                ZipFile.OpenRead(verified.Path);

            archive.GetEntry("research.sqlite")!
                .ExtractToFile(
                    Path.Combine(
                        staging,
                        "research.sqlite"));

            archive.GetEntry("settings.json")!
                .ExtractToFile(
                    Path.Combine(
                        staging,
                        "settings.json"));

            VerifyResearchDatabase(
                Path.Combine(
                    staging,
                    "research.sqlite"),
                requireCurrentSchema: false);

            VerifySettings(
                Path.Combine(
                    staging,
                    "settings.json"));

            return staging;
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    private void VerifyBackupMatchesCurrentState(
        VerifiedOwnerBackup backup)
    {
        (string researchHash, string settingsHash) =
            ComputeLiveStateHashes();

        if (!string.Equals(
                researchHash,
                backup.Manifest.ResearchSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                settingsHash,
                backup.Manifest.SettingsSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Owner state changed after the PreReset backup. Reset is blocked until a new matching backup is created and verified.");
        }
    }

    private static void SnapshotResearchDatabase(
        string sourcePath,
        string destinationPath)
    {
        TryDeleteFile(destinationPath);
        TryDeleteFile(destinationPath + "-wal");
        TryDeleteFile(destinationPath + "-shm");

        {
            using var source =
                new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource =
                            Path.GetFullPath(sourcePath),
                        Mode =
                            SqliteOpenMode.ReadOnly,
                        Cache =
                            SqliteCacheMode.Private,
                        Pooling =
                            false
                    }.ToString());

            using var destination =
                new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource =
                            Path.GetFullPath(destinationPath),
                        Mode =
                            SqliteOpenMode.ReadWriteCreate,
                        Cache =
                            SqliteCacheMode.Private,
                        Pooling =
                            false
                    }.ToString());

            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        using (var normalized =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource =
                        Path.GetFullPath(destinationPath),
                    Mode =
                        SqliteOpenMode.ReadWrite,
                    Cache =
                        SqliteCacheMode.Private,
                    Pooling =
                        false
                }.ToString()))
        {
            normalized.Open();

            using var command =
                normalized.CreateCommand();

            command.CommandText =
                """
                PRAGMA wal_checkpoint(TRUNCATE);
                PRAGMA journal_mode = DELETE;
                """;

            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        TryDeleteFile(destinationPath + "-wal");
        TryDeleteFile(destinationPath + "-shm");
    }

    private static int VerifyResearchDatabase(
        string databasePath,
        bool requireCurrentSchema)
    {
        using var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource =
                        Path.GetFullPath(databasePath),
                    Mode =
                        SqliteOpenMode.ReadOnly,
                    Cache =
                        SqliteCacheMode.Private,
                    Pooling =
                        false
                }.ToString());

        connection.Open();

        using (var integrity =
            connection.CreateCommand())
        {
            integrity.CommandText =
                "PRAGMA integrity_check;";

            string? result =
                integrity.ExecuteScalar()
                    ?.ToString();

            if (!string.Equals(
                    result,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SQLite integrity_check failed: {result ?? "no result"}");
            }
        }

        using (var foreignKeys =
            connection.CreateCommand())
        {
            foreignKeys.CommandText =
                "PRAGMA foreign_key_check;";

            using var reader =
                foreignKeys.ExecuteReader();

            if (reader.Read())
            {
                throw new InvalidDataException(
                    "SQLite foreign_key_check reported a violation.");
            }
        }

        foreach ((string table, string[] requiredColumns)
                 in RequiredResearchColumns)
        {
            var columns =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            using var tableInfo =
                connection.CreateCommand();

            tableInfo.CommandText =
                $"PRAGMA table_info({table});";

            using var reader =
                tableInfo.ExecuteReader();

            while (reader.Read())
            {
                columns.Add(
                    reader.GetString(1));
            }

            if (columns.Count == 0)
            {
                throw new InvalidDataException(
                    $"Research database is missing required table: {table}");
            }

            foreach (string requiredColumn
                     in requiredColumns)
            {
                if (!columns.Contains(
                        requiredColumn))
                {
                    throw new InvalidDataException(
                        $"Research database table {table} is missing required column: {requiredColumn}");
                }
            }
        }

        using var schema =
            connection.CreateCommand();

        schema.CommandText =
            "SELECT value FROM meta WHERE key='schema_version';";

        string? value =
            schema.ExecuteScalar()
                ?.ToString();

        if (!int.TryParse(
                value,
                out int version) ||
            version < 1 ||
            version >
                ResearchDatabase.SchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported research schema version: {value ?? "missing"}.");
        }

        if (version >= 7)
        {
            foreach ((string table, string[] requiredColumns)
                     in Schema7ResearchColumns)
            {
                var columns =
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                using var tableInfo =
                    connection.CreateCommand();

                tableInfo.CommandText =
                    $"PRAGMA table_info({table});";

                using var reader =
                    tableInfo.ExecuteReader();

                while (reader.Read())
                {
                    columns.Add(
                        reader.GetString(1));
                }

                if (columns.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Research database is missing schema-7 table: {table}");
                }

                foreach (string requiredColumn
                         in requiredColumns)
                {
                    if (!columns.Contains(
                            requiredColumn))
                    {
                        throw new InvalidDataException(
                            $"Research database table {table} is missing schema-7 column: {requiredColumn}");
                    }
                }
            }
        }

        if (requireCurrentSchema &&
            version !=
                ResearchDatabase.SchemaVersion)
        {
            throw new InvalidDataException(
                $"Research schema must be {ResearchDatabase.SchemaVersion}, got {version}.");
        }

        return version;
    }

    private static AppSettings VerifySettings(
        string settingsPath)
    {
        string json =
            File.ReadAllText(
                settingsPath);

        using (JsonDocument document =
            JsonDocument.Parse(json))
        {
            if (document.RootElement.ValueKind !=
                    JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(
                    "SchemaVersion",
                    out _))
            {
                throw new InvalidDataException(
                    "settings.json is missing its schema identity.");
            }
        }

        AppSettings settings =
            JsonSerializer.Deserialize<AppSettings>(
                json,
                JsonOptions)
            ?? throw new InvalidDataException(
                "settings.json is invalid.");

        if (settings.SchemaVersion < 1 ||
            settings.SchemaVersion >
                AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported settings schema version: {settings.SchemaVersion}.");
        }

        return settings;
    }

    private static void ValidateManifest(
        BackupManifest manifest)
    {
        if (manifest.FormatVersion !=
            BackupFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported backup format: {manifest.FormatVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.BackupId) ||
            manifest.CreatedUtc == default ||
            (manifest.Reason != ManualReason &&
             manifest.Reason != PreRestoreReason &&
             manifest.Reason != PreResetReason) ||
            string.IsNullOrWhiteSpace(manifest.AppBuild) ||
            string.IsNullOrWhiteSpace(manifest.ResearchSha256) ||
            string.IsNullOrWhiteSpace(manifest.SettingsSha256) ||
            manifest.ResearchLength <= 0 ||
            manifest.SettingsLength <= 0)
        {
            throw new InvalidDataException(
                "Backup manifest is incomplete.");
        }

        if (manifest.ResearchSchemaVersion < 1 ||
            manifest.ResearchSchemaVersion >
                ResearchDatabase.SchemaVersion ||
            manifest.SettingsSchemaVersion < 1 ||
            manifest.SettingsSchemaVersion >
                AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "Backup manifest requires a newer schema than this app supports.");
        }
    }

    private BackupManifest ReadManifest(
        string backupPath)
    {
        using var archive =
            ZipFile.OpenRead(backupPath);

        ZipArchiveEntry entry =
            archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException(
                "Backup manifest is missing.");

        using Stream stream =
            entry.Open();

        return
            JsonSerializer.Deserialize<BackupManifest>(
                stream,
                JsonOptions)
            ?? throw new InvalidDataException(
                "Backup manifest is invalid.");
    }

    private OwnerStateOperationJournal ReadJournal()
    {
        OwnerStateOperationJournal journal =
            JsonSerializer.Deserialize<OwnerStateOperationJournal>(
                File.ReadAllText(
                    _operationJournal),
                JsonOptions)
            ?? throw new InvalidDataException(
                "Owner-state operation journal is invalid.");

        if (string.IsNullOrWhiteSpace(
                journal.OperationId) ||
            (journal.Kind != RestoreOperation &&
             journal.Kind != ResetOperation) ||
            (journal.Stage != PreparedStage &&
             journal.Stage != QuarantinedStage &&
             journal.Stage != CommittedStage) ||
            string.IsNullOrWhiteSpace(
                journal.PreOperationBackupPath) ||
            string.IsNullOrWhiteSpace(
                journal.PreOperationBackupId) ||
            string.IsNullOrWhiteSpace(
                journal.PreResearchSha256) ||
            string.IsNullOrWhiteSpace(
                journal.PreSettingsSha256) ||
            string.IsNullOrWhiteSpace(
                journal.QuarantinePath))
        {
            throw new InvalidDataException(
                "Owner-state operation journal is incomplete.");
        }

        return journal;
    }

    private void WriteJournal(
        OwnerStateOperationJournal journal)
    {
        EnsureDirectories();

        string temp =
            _operationJournal + ".tmp";

        string json =
            JsonSerializer.Serialize(
                journal,
                JsonOptions);

        byte[] bytes =
            Encoding.UTF8.GetBytes(json);

        using (var stream =
            new FileStream(
                temp,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
        {
            stream.Write(
                bytes,
                0,
                bytes.Length);

            stream.Flush(
                flushToDisk: true);
        }

        File.Move(
            temp,
            _operationJournal,
            overwrite: true);
    }

    private void WriteRecoveryReceipt(
        OwnerStateOperationJournal journal,
        string outcome)
    {
        try
        {
            Directory.CreateDirectory(
                _diagnosticsDirectory);

            string path =
                Path.Combine(
                    _diagnosticsDirectory,
                    "owner-state-recovery.log");

            File.AppendAllText(
                path,
                $"{DateTimeOffset.UtcNow:O} · " +
                $"operation={journal.OperationId} · " +
                $"kind={journal.Kind} · " +
                $"stage={journal.Stage} · " +
                $"build={journal.AppBuild} · " +
                $"source={journal.SourceRevision} · " +
                $"preBackup={journal.PreOperationBackupId} · " +
                $"targetBackup={journal.TargetBackupId ?? "none"} · " +
                $"outcome={outcome}{Environment.NewLine}");
        }
        catch
        {
            // Recovery receipts are diagnostic only. They never determine
            // authority or success of an owner-state operation.
        }
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(
            _dataDirectory);

        Directory.CreateDirectory(
            _backupsDirectory);

        Directory.CreateDirectory(
            _diagnosticsDirectory);
    }

    private void MoveResearchFilesTo(
        string destination)
    {
        MoveIfExists(
            _researchDatabase,
            Path.Combine(
                destination,
                "research.sqlite"));

        MoveIfExists(
            _researchDatabase + "-wal",
            Path.Combine(
                destination,
                "research.sqlite-wal"));

        MoveIfExists(
            _researchDatabase + "-shm",
            Path.Combine(
                destination,
                "research.sqlite-shm"));
    }

    private void DeleteLiveOwnerStateFilesStrict(
        bool includeSettings)
    {
        DeleteFileStrict(
            _researchDatabase);

        DeleteFileStrict(
            _researchDatabase + "-wal");

        DeleteFileStrict(
            _researchDatabase + "-shm");

        if (includeSettings)
        {
            DeleteFileStrict(
                _settingsFile);
        }
    }

    private static void MoveIfExists(
        string source,
        string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        string? directory =
            Path.GetDirectoryName(
                destination);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.Move(
            source,
            destination,
            overwrite: true);
    }

    private static void DeleteFileStrict(
        string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string ComputeSha256(
        string path)
    {
        using FileStream stream =
            File.OpenRead(path);

        return Convert.ToHexString(
                SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static string ComputeSha256(
        ZipArchiveEntry entry)
    {
        using Stream stream =
            entry.Open();

        return Convert.ToHexString(
                SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary-file cleanup is best effort only.
        }
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch
        {
            // Completed-operation cleanup is best effort. Authority comes
            // from the durable journal stage and verified state digests.
        }
    }
}
