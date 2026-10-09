using System.Text.Json;

namespace QuranReconciliation.Infrastructure;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static AppSettings Load()
    {
        AppPaths.EnsurePortableDirectories();

        if (!File.Exists(AppPaths.SettingsFile))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(AppPaths.SettingsFile);
            AppSettings settings =
                JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("settings.json is invalid.");

            if (settings.SchemaVersion >
                AppSettings.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"This settings file uses schema {settings.SchemaVersion}, but this app supports through schema {AppSettings.CurrentSchemaVersion}. Refusing to overwrite newer owner settings with an older build.");
            }

            if (settings.SchemaVersion < 1)
            {
                throw new InvalidDataException(
                    $"Unsupported settings schema version: {settings.SchemaVersion}.");
            }

            return settings;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidDataException)
        {
            // Only invalid settings content qualifies for recovery.
            // IO/access errors fail closed; they must not silently reset
            // preferences or overwrite the original owner's settings.
            PreserveCorruptSettings();

            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }
    }

    internal static void Save(AppSettings settings)
    {
        AppPaths.EnsurePortableDirectories();

        settings.SchemaVersion =
            AppSettings.CurrentSchemaVersion;

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        string temp = AppPaths.SettingsFile + ".tmp";

        File.WriteAllText(temp, json);
        File.Move(temp, AppPaths.SettingsFile, true);
    }

    private static void PreserveCorruptSettings()
    {
        if (!File.Exists(AppPaths.SettingsFile))
        {
            return;
        }

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string destination = Path.Combine(
            AppPaths.DataDirectory,
            $"settings.corrupt-{stamp}-{Guid.NewGuid():N}.json");

        // If preservation fails, refuse to replace the original file.
        File.Move(AppPaths.SettingsFile, destination, overwrite: false);
    }
}
