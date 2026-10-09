using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

string root =
    Path.Combine(
        Path.GetTempPath(),
        "QuranReconciliation-R11SafetyProbe-" +
        Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(root);

try
{
    TestInstanceIsolation(root);
    TestFutureSchemaRefusal(root);
    TestLegacyMigration(root);
    TestInterruptedRecovery(root);
    TestCommittedRestoreRecovery(root);
    TestCommittedResetRecovery(root);
    TestCommittedResetLedgerAnomalies(root);
    TestAmbiguousRecoveryFailsClosed(root);
    TestBackupShapeValidation(root);

    Console.WriteLine(
        "Build 10 R11 post-ANTARCTICA safety-closure probe: PASS");

    return 0;
}
finally
{
    try
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(root))
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }
    catch
    {
    }
}

static void TestInstanceIsolation(string root)
{
    string a =
        Path.Combine(root, "instance-a");

    string b =
        Path.Combine(root, "instance-b");

    using PortableInstanceGuard first =
        PortableInstanceGuard.Acquire(a);

    bool rejectedSameFolder = false;

    try
    {
        using PortableInstanceGuard duplicate =
            PortableInstanceGuard.Acquire(a);
    }
    catch (InvalidOperationException)
    {
        rejectedSameFolder = true;
    }

    Require(
        rejectedSameFolder,
        "Second process/guard for the same portable folder was not rejected.");

    using PortableInstanceGuard independent =
        PortableInstanceGuard.Acquire(b);

    Require(
        Directory.Exists(
            Path.Combine(
                b,
                "Data")),
        "Independent portable folder did not acquire its own lock.");
}

static void TestFutureSchemaRefusal(string root)
{
    string database =
        Path.Combine(
            root,
            "future-schema",
            "Data",
            "research.sqlite");

    ResearchDatabase.InitializeAt(database);

    using (var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database
            }.ToString()))
    {
        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            "UPDATE meta SET value='8' WHERE key='schema_version';";

        command.ExecuteNonQuery();
    }

    SqliteConnection.ClearAllPools();

    bool rejected = false;

    try
    {
        ResearchDatabase.InitializeAt(database);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }

    Require(
        rejected,
        "R11 did not fail closed on a newer research schema.");

    using var verify =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());

    verify.Open();

    using var read =
        verify.CreateCommand();

    read.CommandText =
        "SELECT value FROM meta WHERE key='schema_version';";

    Require(
        read.ExecuteScalar()?.ToString() == "8",
        "Newer schema metadata was rewritten despite fail-closed refusal.");
}

static void TestLegacyMigration(string root)
{
    string database =
        Path.Combine(
            root,
            "legacy-migration",
            "research.sqlite");

    Directory.CreateDirectory(
        Path.GetDirectoryName(database)!);

    using (var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Pooling = false
            }.ToString()))
    {
        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA foreign_keys=ON;

            CREATE TABLE meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT INTO meta(key, value)
            VALUES ('schema_version', '1');

            CREATE TABLE context_blocks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                surah_number INTEGER NOT NULL,
                start_ayah INTEGER NOT NULL,
                end_ayah INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Proposed',
                owner_note TEXT,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE context_boundary_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                context_block_id INTEGER NOT NULL,
                old_start_ayah INTEGER NOT NULL,
                old_end_ayah INTEGER NOT NULL,
                new_start_ayah INTEGER NOT NULL,
                new_end_ayah INTEGER NOT NULL,
                owner_note TEXT,
                changed_utc TEXT NOT NULL
            );

            CREATE TABLE ayah_notes (
                surah_number INTEGER NOT NULL,
                ayah_number INTEGER NOT NULL,
                body TEXT NOT NULL DEFAULT '',
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(surah_number, ayah_number)
            );

            CREATE TABLE ayah_note_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                surah_number INTEGER NOT NULL,
                ayah_number INTEGER NOT NULL,
                prior_body TEXT NOT NULL,
                changed_utc TEXT NOT NULL
            );

            CREATE TABLE context_notes (
                context_block_id INTEGER PRIMARY KEY,
                body TEXT NOT NULL DEFAULT '',
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE context_note_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                context_block_id INTEGER NOT NULL,
                prior_body TEXT NOT NULL,
                changed_utc TEXT NOT NULL
            );
            """;

        command.ExecuteNonQuery();
    }

    ResearchDatabase.InitializeAt(
        database);

    using var verify =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

    verify.Open();

    using var schema =
        verify.CreateCommand();

    schema.CommandText =
        "SELECT value FROM meta WHERE key='schema_version';";

    Require(
        schema.ExecuteScalar()?.ToString() ==
            ResearchDatabase.SchemaVersion.ToString(),
        "Legacy schema did not migrate to the current research schema.");

    using var columns =
        verify.CreateCommand();

    columns.CommandText =
        "SELECT COUNT(*) FROM pragma_table_info('context_blocks') WHERE name IN ('origin','is_active','proposal_import_id');";

    Require(
        Convert.ToInt32(
            columns.ExecuteScalar()) == 3,
        "Legacy Context schema did not receive all current columns.");

    using var tables =
        verify.CreateCommand();

    tables.CommandText =
        """
        SELECT COUNT(*)
        FROM sqlite_master
        WHERE type='table'
          AND name IN (
              'bookmarks',
              'context_proposal_imports',
              'context_proposal_replaced_blocks',
              'working_slices',
              'working_slice_revisions',
              'context_operations',
              'context_operation_blocks',
              'research_activity_events');
        """;

    Require(
        Convert.ToInt32(
            tables.ExecuteScalar()) == 8,
        "Legacy research database did not receive all current tables.");

    using var revisionColumns =
        verify.CreateCommand();

    revisionColumns.CommandText =
        """
        SELECT COUNT(*)
        FROM pragma_table_info('working_slice_revisions')
        WHERE name IN ('changed_fields_json','change_summary');
        """;

    Require(
        Convert.ToInt32(
            revisionColumns.ExecuteScalar()) == 2,
        "Schema-7 Working Slice revision metadata columns were not migrated.");
}

static void TestInterruptedRecovery(string root)
{
    string appRoot =
        Path.Combine(
            root,
            "interrupted-recovery");

    string data =
        Path.Combine(
            appRoot,
            "Data");

    Directory.CreateDirectory(data);

    string database =
        Path.Combine(
            data,
            "research.sqlite");

    string settings =
        Path.Combine(
            data,
            "settings.json");

    ResearchDatabase.InitializeAt(database);

    WriteAyahNote(
        database,
        "recovery-anchor-note");

    WriteSettings(
        settings,
        "OLED Cyan");

    var service =
        new OwnerDataBackupService(
            "Build 10 R11 probe",
            appRoot,
            "probe-source");

    VerifiedOwnerBackup pre =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.PreRestoreReason);

    SqliteConnection.ClearAllPools();

    DeleteIfExists(database);
    DeleteIfExists(database + "-wal");
    DeleteIfExists(database + "-shm");
    DeleteIfExists(settings);

    string operationId =
        Guid.NewGuid().ToString("N");

    string quarantine =
        Path.Combine(
            data,
            ".restore-quarantine-" +
            operationId);

    Directory.CreateDirectory(quarantine);

    var journal =
        new OwnerStateOperationJournal(
            operationId,
            "Restore",
            "Quarantined",
            "Build 10 R11 probe",
            "probe-source",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            pre.Path,
            pre.Manifest.BackupId,
            pre.Manifest.ResearchSha256,
            pre.Manifest.SettingsSha256,
            null,
            null,
            null,
            null,
            quarantine);

    File.WriteAllText(
        Path.Combine(
            data,
            "owner-state-operation.json"),
        JsonSerializer.Serialize(
            journal,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));

    OwnerStateRecoveryResult recovered =
        service.RecoverInterruptedOperation();

    Require(
        recovered.Recovered,
        "Interrupted operation was not reported as recovered.");

    Require(
        ReadAyahNote(database) ==
            "recovery-anchor-note",
        "Startup recovery did not restore the exact pre-operation research state.");

    Require(
        ReadSettingsTheme(settings) ==
            "OLED Cyan",
        "Startup recovery did not restore the exact pre-operation settings.");

    Require(
        !File.Exists(
            Path.Combine(
                data,
                "owner-state-operation.json")),
        "Recovery journal remained after verified recovery.");
}

static void TestCommittedRestoreRecovery(string root)
{
    string appRoot =
        Path.Combine(
            root,
            "committed-restore");

    string data =
        Path.Combine(
            appRoot,
            "Data");

    Directory.CreateDirectory(data);

    string database =
        Path.Combine(
            data,
            "research.sqlite");

    string settings =
        Path.Combine(
            data,
            "settings.json");

    ResearchDatabase.InitializeAt(database);

    WriteAyahNote(
        database,
        "pre-restore-state");

    WriteSettings(
        settings,
        "Daylight");

    var service =
        new OwnerDataBackupService(
            "Build 10 R11 probe",
            appRoot,
            "probe-source");

    VerifiedOwnerBackup pre =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.PreRestoreReason);

    using (var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Pooling = false
            }.ToString()))
    {
        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            UPDATE ayah_notes
            SET body='committed-restore-target',
                updated_utc=$now
            WHERE surah_number=2
              AND ayah_number=255;
            """;

        command.Parameters.AddWithValue(
            "$now",
            DateTimeOffset.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }

    WriteSettings(
        settings,
        "OLED Amber");

    VerifiedOwnerBackup target =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.ManualReason);

    SqliteConnection.ClearAllPools();

    DeleteIfExists(database);
    DeleteIfExists(database + "-wal");
    DeleteIfExists(database + "-shm");
    DeleteIfExists(settings);

    using (var archive =
        ZipFile.OpenRead(
            target.Path))
    {
        archive.GetEntry(
                "research.sqlite")!
            .ExtractToFile(
                database);

        archive.GetEntry(
                "settings.json")!
            .ExtractToFile(
                settings);
    }

    string operationId =
        Guid.NewGuid().ToString("N");

    var journal =
        new OwnerStateOperationJournal(
            operationId,
            "Restore",
            "Committed",
            "Build 10 R11 probe",
            "probe-source",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            pre.Path,
            pre.Manifest.BackupId,
            pre.Manifest.ResearchSha256,
            pre.Manifest.SettingsSha256,
            target.Path,
            target.Manifest.BackupId,
            target.Manifest.ResearchSha256,
            target.Manifest.SettingsSha256,
            Path.Combine(
                data,
                ".restore-quarantine-" +
                operationId));

    File.WriteAllText(
        Path.Combine(
            data,
            "owner-state-operation.json"),
        JsonSerializer.Serialize(
            journal,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));

    OwnerStateRecoveryResult recovered =
        service.RecoverInterruptedOperation();

    Require(
        recovered.Recovered,
        "Committed restore was not recovered.");

    Require(
        ReadAyahNote(database) ==
            "committed-restore-target",
        "Committed restore recovery rolled back the intended restore target.");

    Require(
        ReadSettingsTheme(settings) ==
            "OLED Amber",
        "Committed restore recovery did not preserve the intended restored settings.");

    Require(
        !File.Exists(
            Path.Combine(
                data,
                "owner-state-operation.json")),
        "Committed restore journal was not cleared after verification.");
}

static void TestCommittedResetRecovery(string root)
{
    string appRoot =
        Path.Combine(
            root,
            "committed-reset");

    string data =
        Path.Combine(
            appRoot,
            "Data");

    Directory.CreateDirectory(data);

    string database =
        Path.Combine(
            data,
            "research.sqlite");

    string settings =
        Path.Combine(
            data,
            "settings.json");

    ResearchDatabase.InitializeAt(database);

    WriteAyahNote(
        database,
        "state-before-reset");

    WriteSettings(
        settings,
        "OLED Cyan");

    var service =
        new OwnerDataBackupService(
            "Build 10 R11 probe",
            appRoot,
            "probe-source");

    VerifiedOwnerBackup pre =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.PreResetReason);

    SqliteConnection.ClearAllPools();

    DeleteIfExists(database);
    DeleteIfExists(database + "-wal");
    DeleteIfExists(database + "-shm");

    ResearchDatabase.InitializeAt(
        database);

    string operationId =
        Guid.NewGuid().ToString("N");

    var journal =
        new OwnerStateOperationJournal(
            operationId,
            "Reset",
            "Committed",
            "Build 10 R11 probe",
            "probe-source",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            pre.Path,
            pre.Manifest.BackupId,
            pre.Manifest.ResearchSha256,
            pre.Manifest.SettingsSha256,
            null,
            null,
            null,
            pre.Manifest.SettingsSha256,
            Path.Combine(
                data,
                ".reset-quarantine-" +
                operationId));

    File.WriteAllText(
        Path.Combine(
            data,
            "owner-state-operation.json"),
        JsonSerializer.Serialize(
            journal,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));

    OwnerStateRecoveryResult recovered =
        service.RecoverInterruptedOperation();

    Require(
        recovered.Recovered,
        "Committed reset was not recovered.");

    Require(
        CountAyahNotes(database) == 0,
        "Committed reset recovery rolled back to pre-reset research data.");

    Require(
        ReadSettingsTheme(settings) ==
            "OLED Cyan",
        "Committed reset recovery did not preserve preferences.");

    Require(
        !File.Exists(
            Path.Combine(
                data,
                "owner-state-operation.json")),
        "Committed reset journal was not cleared after verification.");
}

static void TestCommittedResetLedgerAnomalies(string root)
{
    foreach (string anomaly in new[]
        { "research_activity_events", "context_operations" })
    {
        string appRoot = Path.Combine(root, "reset-ledger-" + anomaly);
        string data = Path.Combine(appRoot, "Data");
        Directory.CreateDirectory(data);
        string db = Path.Combine(data, "research.sqlite");
        string settings = Path.Combine(data, "settings.json");

        ResearchDatabase.InitializeAt(db);
        WriteAyahNote(db, "preserved-before-reset");
        WriteSettings(settings, "OLED Cyan");
        var service = new OwnerDataBackupService(
            "Build 1.9 synthetic reset probe", appRoot, "probe-source");

        VerifiedOwnerBackup pre =
            service.CreateVerifiedBackup(OwnerDataBackupService.PreResetReason);
        SqliteConnection.ClearAllPools();
        DeleteIfExists(db);
        DeleteIfExists(db + "-wal");
        DeleteIfExists(db + "-shm");
        ResearchDatabase.InitializeAt(db);

        using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = anomaly == "research_activity_events"
                ? """
                  INSERT INTO research_activity_events(
                      event_type,entity_type,surah_number,summary,occurred_utc)
                  VALUES('Injected','ContextOperation',2,'synthetic anomaly','2026-10-09T00:00:00Z');
                  """
                : """
                  INSERT INTO context_operations(
                      surah_number,operation_type,summary,created_utc)
                  VALUES(2,'Injected','synthetic anomaly','2026-10-09T00:00:00Z');
                  """;
            cmd.ExecuteNonQuery();
        }

        // Deliberately forge a matching installed-state SHA for the polluted
        // research file. Digest-only checks cannot replace ledger validation.
        VerifiedOwnerBackup tainted =
            service.CreateVerifiedBackup(OwnerDataBackupService.ManualReason);
        string id = Guid.NewGuid().ToString("N");
        var journal = new OwnerStateOperationJournal(
            id, "Reset", "Committed", "Build 1.9 synthetic reset probe",
            "probe-source", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            pre.Path, pre.Manifest.BackupId,
            pre.Manifest.ResearchSha256, pre.Manifest.SettingsSha256,
            null, null, tainted.Manifest.ResearchSha256,
            pre.Manifest.SettingsSha256,
            Path.Combine(data, ".reset-quarantine-" + id));

        File.WriteAllText(
            Path.Combine(data, "owner-state-operation.json"),
            JsonSerializer.Serialize(journal));

        OwnerStateRecoveryResult recovered =
            service.RecoverInterruptedOperation();

        using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM " + anomaly + ";";
            Require(Convert.ToInt64(cmd.ExecuteScalar()) == 0,
                "Committed-reset recovery accepted populated " + anomaly);
        }

        Require(recovered.Recovered && CountAyahNotes(db) == 0,
            "Committed-reset ledger fault was not repaired safely.");
        Require(!File.Exists(Path.Combine(data, "owner-state-operation.json")),
            "Committed-reset ledger recovery did not finish journal cleanup.");
    }
}

static void TestAmbiguousRecoveryFailsClosed(string root)
{
    string appRoot =
        Path.Combine(
            root,
            "ambiguous-recovery");

    string data =
        Path.Combine(
            appRoot,
            "Data");

    Directory.CreateDirectory(data);

    string operationId =
        Guid.NewGuid().ToString("N");

    string missingDatabase =
        Path.Combine(
            data,
            "research.sqlite");

    var journal =
        new OwnerStateOperationJournal(
            operationId,
            "Restore",
            "Quarantined",
            "Build 10 R11 probe",
            "probe-source",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Path.Combine(
                appRoot,
                "Backups",
                "missing.zip"),
            "missing-backup-id",
            new string('a', 64),
            new string('b', 64),
            null,
            null,
            null,
            null,
            Path.Combine(
                data,
                ".restore-quarantine-" +
                operationId));

    File.WriteAllText(
        Path.Combine(
            data,
            "owner-state-operation.json"),
        JsonSerializer.Serialize(
            journal,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));

    var service =
        new OwnerDataBackupService(
            "Build 10 R11 probe",
            appRoot,
            "probe-source");

    bool blocked = false;

    try
    {
        service.RecoverInterruptedOperation();
    }
    catch (OwnerStateRecoveryRequiredException)
    {
        blocked = true;
    }

    Require(
        blocked,
        "Ambiguous recovery did not fail closed.");

    Require(
        !File.Exists(missingDatabase),
        "Ambiguous recovery created a replacement research database.");
}

static void TestBackupShapeValidation(string root)
{
    string appRoot =
        Path.Combine(
            root,
            "backup-shape");

    string data =
        Path.Combine(
            appRoot,
            "Data");

    Directory.CreateDirectory(data);

    string database =
        Path.Combine(
            data,
            "research.sqlite");

    string settings =
        Path.Combine(
            data,
            "settings.json");

    ResearchDatabase.InitializeAt(database);

    WriteSettings(
        settings,
        "Daylight");

    var service =
        new OwnerDataBackupService(
            "Build 10 R11 probe",
            appRoot,
            "probe-source");

    VerifiedOwnerBackup original =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.ManualReason);

    Require(
        original.Manifest.AppBuild ==
            "Build 10 R11 probe" &&
        original.Manifest.SourceRevision ==
            "probe-source",
        "Backup provenance is incomplete.");

    string staging =
        Path.Combine(
            root,
            "shape-stage");

    Directory.CreateDirectory(staging);

    ZipFile.ExtractToDirectory(
        original.Path,
        staging);

    string stagedDb =
        Path.Combine(
            staging,
            "research.sqlite");

    using (var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = stagedDb,
                Pooling = false
            }.ToString()))
    {
        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            "ALTER TABLE bookmarks DROP COLUMN note;";

        command.ExecuteNonQuery();
    }

    string manifestPath =
        Path.Combine(
            staging,
            "manifest.json");

    BackupManifest manifest =
        JsonSerializer.Deserialize<BackupManifest>(
            File.ReadAllText(manifestPath))
        ?? throw new InvalidDataException(
            "Probe manifest could not be read.");

    manifest =
        manifest with
        {
            ResearchSha256 =
                Sha256(stagedDb),
            ResearchLength =
                new FileInfo(stagedDb).Length
        };

    File.WriteAllText(
        manifestPath,
        JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));

    string tampered =
        Path.Combine(
            appRoot,
            "Backups",
            "QuranReconciliation-Backup-shape-invalid.zip");

    ZipFile.CreateFromDirectory(
        staging,
        tampered,
        CompressionLevel.Optimal,
        includeBaseDirectory: false);

    bool rejected = false;

    try
    {
        service.VerifyBackup(tampered);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }

    Require(
        rejected,
        "Backup verification accepted a database missing a runtime-required column.");
}

static void WriteAyahNote(
    string database,
    string body)
{
    using var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database
            }.ToString());

    connection.Open();

    using var command =
        connection.CreateCommand();

    command.CommandText =
        """
        INSERT INTO ayah_notes(
            surah_number,
            ayah_number,
            body,
            updated_utc)
        VALUES(2, 255, $body, $now);
        """;

    command.Parameters.AddWithValue(
        "$body",
        body);

    command.Parameters.AddWithValue(
        "$now",
        DateTimeOffset.UtcNow.ToString("O"));

    command.ExecuteNonQuery();
}

static string? ReadAyahNote(
    string database)
{
    using var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());

    connection.Open();

    using var command =
        connection.CreateCommand();

    command.CommandText =
        """
        SELECT body
        FROM ayah_notes
        WHERE surah_number=2
          AND ayah_number=255;
        """;

    return command.ExecuteScalar()?.ToString();
}

static int CountAyahNotes(
    string database)
{
    using var connection =
        new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

    connection.Open();

    using var command =
        connection.CreateCommand();

    command.CommandText =
        "SELECT COUNT(*) FROM ayah_notes;";

    return Convert.ToInt32(
        command.ExecuteScalar());
}

static void WriteSettings(
    string path,
    string theme)
{
    Directory.CreateDirectory(
        Path.GetDirectoryName(path)!);

    var settings =
        new AppSettings
        {
            Theme = theme,
            Zoom = 1.1f,
            LastSurahNumber = 2,
            SourceSelectionInitialized = true
        };

    File.WriteAllText(
        path,
        JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));
}

static string? ReadSettingsTheme(
    string path)
{
    AppSettings? settings =
        JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(path));

    return settings?.Theme;
}

static void DeleteIfExists(
    string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static string Sha256(
    string path)
{
    using FileStream stream =
        File.OpenRead(path);

    return Convert.ToHexString(
            SHA256.HashData(stream))
        .ToLowerInvariant();
}

static void Require(
    bool condition,
    string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            message);
    }
}
