using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using System.Text.Json;

string root =
    Path.Combine(
        Path.GetTempPath(),
        "quran-reconciliation-backup-probe-" +
        Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(Path.Combine(root, "Data"));
Directory.CreateDirectory(Path.Combine(root, "Backups"));

string db = Path.Combine(root, "Data", "research.sqlite");
string settingsPath = Path.Combine(root, "Data", "settings.json");

try
{
    ResearchDatabase.InitializeAt(db);

    WriteAyahNote(db, "original-note");
    WriteSettings(settingsPath, "OLED Cyan", 1.10f);

    var service =
        new OwnerDataBackupService(
            "BackupWorkflowProbe",
            root);

    VerifiedOwnerBackup manual =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.ManualReason);

    service.VerifyBackup(manual.Path);

    Require(
        File.Exists(manual.Path),
        "Manual backup archive was not created.");

    WriteAyahNote(db, "mutated-note");
    WriteSettings(settingsPath, "Warm Sand", 0.90f);

    RestoreOwnerStateResult restored =
        service.RestoreOwnerState(manual.Path);

    Require(
        ReadAyahNote(db) == "original-note",
        "Restore did not recover research data.");

    AppSettings restoredSettings =
        ReadSettings(settingsPath);

    Require(
        restoredSettings.Theme == "OLED Cyan" &&
        Math.Abs(restoredSettings.Zoom - 1.10f) < 0.001f,
        "Restore did not recover settings.");

    service.VerifyBackup(
        restored.PreRestoreBackup.Path);

    bool rejectedManualReset = false;

    try
    {
        service.ResetResearchData(manual);
    }
    catch (InvalidOperationException)
    {
        rejectedManualReset = true;
    }

    Require(
        rejectedManualReset,
        "Reset accepted a non-PreReset backup.");

    WriteAyahNote(db, "state-before-reset");

    VerifiedOwnerBackup stalePreReset =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.PreResetReason);

    WriteAyahNote(db, "changed-after-backup");

    bool rejectedStaleReset = false;

    try
    {
        service.ResetResearchData(stalePreReset);
    }
    catch (InvalidOperationException)
    {
        rejectedStaleReset = true;
    }

    Require(
        rejectedStaleReset,
        "Reset did not reject a PreReset backup that no longer matches live state.");

    Require(
        ReadAyahNote(db) == "changed-after-backup",
        "Rejected reset changed live research state.");

    VerifiedOwnerBackup freshPreReset =
        service.CreateVerifiedBackup(
            OwnerDataBackupService.PreResetReason);

    service.ResetResearchData(freshPreReset);

    Require(
        CountAyahNotes(db) == 0,
        "Reset did not create fresh research state.");

    AppSettings preservedSettings =
        ReadSettings(settingsPath);

    Require(
        preservedSettings.Theme == "OLED Cyan",
        "Research reset changed app preferences.");

    RestoreOwnerStateResult restoredReset =
        service.RestoreOwnerState(
            freshPreReset.Path);

    Require(
        ReadAyahNote(db) == "changed-after-backup",
        "Verified PreReset backup could not restore pre-reset research state.");

    service.VerifyBackup(
        restoredReset.PreRestoreBackup.Path);

    string tampered =
        Path.Combine(
            root,
            "Backups",
            "QuranReconciliation-Backup-tampered.zip");

    File.Copy(manual.Path, tampered);

    using (var stream =
        new FileStream(
            tampered,
            FileMode.Open,
            FileAccess.Write))
    {
        stream.SetLength(
            Math.Max(1, stream.Length / 2));
    }

    bool tamperRejected = false;

    try
    {
        service.VerifyBackup(tampered);
    }
    catch
    {
        tamperRejected = true;
    }

    Require(
        tamperRejected,
        "Tampered backup archive was accepted.");

    IReadOnlyList<BackupSummary> backups =
        service.ListBackups();

    Require(
        backups.Count >= 4,
        "Verified backup list did not retain safety archives.");

    Console.WriteLine(
        "Verified backup / restore / reset contract: PASS");

    return 0;
}
finally
{
    try
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
    catch
    {
    }
}

static void WriteAyahNote(string db, string body)
{
    using var connection =
        new SqliteConnection($"Data Source={db}");

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
    VALUES(2, 255, $body, $now)
    ON CONFLICT(surah_number, ayah_number)
    DO UPDATE SET
        body=excluded.body,
        updated_utc=excluded.updated_utc;
    """;

    command.Parameters.AddWithValue("$body", body);
    command.Parameters.AddWithValue(
        "$now",
        DateTime.UtcNow.ToString("O"));
    command.ExecuteNonQuery();
}

static string? ReadAyahNote(string db)
{
    using var connection =
        new SqliteConnection($"Data Source={db}");

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

static int CountAyahNotes(string db)
{
    using var connection =
        new SqliteConnection($"Data Source={db}");

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
    string theme,
    float zoom)
{
    var settings =
        new AppSettings
        {
            Theme = theme,
            Zoom = zoom,
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

static AppSettings ReadSettings(string path)
{
    return
        JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(path))
        ?? throw new InvalidDataException(
            "Probe settings are invalid.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
